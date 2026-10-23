using System.Collections.Generic;
using System.Linq;
using Comet.Client;
using Comet.Content;
using Comet.Simulation;
using Comet.Unity;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// The join screen (UI Toolkit, <c>Assets/UI/JoinScreen.uxml</c>): a card over the orbiting island where the
    /// player picks a name, shape and colors, sees the shape turning with its speed and jump height, and joins.
    /// The server's refusals and connection failures show on the card, which stays up for another try; it hides
    /// once the player spawns. When the server shuts down (as a restart does), the card tries joining again for a
    /// minute; if the server came back with new content, a web page reloads to load it and joins again by itself.
    /// The choices are remembered for next time. Desktop builds also have a server field; a web page joins the
    /// server it came from.
    /// </summary>
    [RequireComponent(typeof(UIDocument), typeof(JoinClient), typeof(GameView))]
    public sealed class JoinScreen : MonoBehaviour
    {
        [SerializeField] private ShapePreview previewPrefab;

        // PlayerPrefs keys for the choices remembered for next time.
        private const string NameKey = "remember.name", ShapeKey = "remember.shape", ColorKey = "remember.color", EyesKey = "remember.eyes", ServerKey = "remember.server";

        private JoinClient _join;
        private GameView _view;
        private VisualElement _root;
        private TextField _name;
        private TextField _server;
        private VisualElement _shapes;
        private VisualElement _colors;
        private VisualElement _eyes;
        private VisualElement _stats;
        private Label _error;
        private Button _joinButton;
        private Button _cancelButton;
        private VisualElement _serverField;
        private ShapePreview _preview;
        private float? _lostAt;

        // How long after a lost connection a taken name is probably the player's own old session: the server notices a
        // dead connection within about 30 s (keep-alive).
        private const float OldSessionSeconds = 40;

        // After the server shuts down (a restart sends that), the card tries joining again once a second for this long.
        private const float RejoinSeconds = 60;
        private float? _rejoinUntil;
        private float _nextRejoin;
        private ShapeLandContent _content;
        private Shape _shape;
        private uint _color;
        private uint _eyeColor;

        /// <summary>Whether to remember the choices in PlayerPrefs for next time (off in tests).</summary>
        public bool Remember { get; set; } = true;

        /// <summary>The screen's root, for tests.</summary>
        public VisualElement Root => _root;

        /// <summary>True while the card is up.</summary>
        public bool Showing => _root != null && !_root.ClassListContains("hidden");

        /// <summary>True while the card is up to change the look of a player already in the game.</summary>
        public bool Changing { get; private set; }

        /// <summary>The message on the card, or null when there is none.</summary>
        public string Error => _error != null && _error.ClassListContains("shown") ? _error.text : null;

        /// <summary>The chosen shape's speed (m/s) and jump height (m), as the card shows them.</summary>
        public static (float Speed, float JumpHeight) Stats(Shape shape, MovementRules rules) => (shape.MaxSpeed, rules.JumpApex(shape.JumpVelocity));

        /// <summary>What the card says when the server refuses a join.</summary>
        public static string RefusalMessage(StringTable strings, JoinRejection reason, string name) => reason switch
        {
            JoinRejection.NameTaken => strings.Format("join.name_taken", ("name", name)),
            JoinRejection.InvalidName => strings.Get("join.name_rules"),
            JoinRejection.DifferentContent => strings.Get(Debug.isDebugBuild ? "join.different_content_dev" : "join.different_content"),
            _ => strings.Get("join.not_accepted"),
        };

        /// <summary>What the card says after the connection was lost for <paramref name="reason"/>.</summary>
        public static string LostMessage(StringTable strings, CloseReason reason) => strings.Get(reason switch
        {
            CloseReason.Lost => "connection.lost",
            CloseReason.ServerClosed => "connection.server_closed",
            CloseReason.ServerShutDown => "connection.server_shut_down",
            CloseReason.FellBehind => "connection.fell_behind",
            CloseReason.ProtocolError => "connection.protocol_error",
            _ => "connection.failed",
        });

        private void Awake()
        {
            _join = GetComponent<JoinClient>();
            _view = GetComponent<GameView>();
            _join.ContentLoaded += OnContentLoaded;
            _join.OwnSpawned += OnOwnSpawned;
            _join.JoinRefused += OnRefused;
            _join.Failed += OnFailed;
            _join.ConnectionLost += OnConnectionLost;
        }

        private void OnEnable()
        {
            var tree = GetComponent<UIDocument>().rootVisualElement;
            _root = tree.Q("join");
            _name = tree.Q<TextField>("name");
            _server = tree.Q<TextField>("server");
            _shapes = tree.Q("shapes");
            _colors = tree.Q("colors");
            _eyes = tree.Q("eyes");
            _stats = tree.Q("stats");
            _error = tree.Q<Label>("error");
            _joinButton = tree.Q<Button>("join-button");
            _joinButton.clicked += Submit;
            _cancelButton = tree.Q<Button>("cancel-button");
            _cancelButton.clicked += Cancel;
            _serverField = tree.Q("server-field");
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            SetChanging(false);
        }

        // Changing the look: no server to pick, and the card can be closed without changing anything.
        private void SetChanging(bool changing)
        {
            Changing = changing;
#if UNITY_WEBGL && !UNITY_EDITOR
            _serverField.AddToClassList("hidden");
#else
            _serverField.EnableInClassList("hidden", changing);
#endif
            _cancelButton.EnableInClassList("hidden", !changing);
            SetButton(true, changing ? "join.apply" : "join.join");
        }

        private void SetButton(bool enabled, string key)
        {
            _joinButton.SetEnabled(enabled);
            _joinButton.text = Text(key);
        }

        // Before joining, the picked shape's start zone shows behind the card, the camera circling its start (gliding
        // there from another shape's start in the same zone).
        private void ShowStartZone(bool glide = true)
        {
            _view.Showcase(_shape, glide);
            _join.ShowZone(ShapeLandWorld.StartOf(_content, _shape).Zone);
        }

        // Shows the card with the current choices.
        private void Show()
        {
            OpenPreview();
            Refresh();
            _root.RemoveFromClassList("hidden");
        }

        /// <summary>Brings the card up over the game, filled in with the player's current look, to change it.</summary>
        public void OpenChange()
        {
            if (Showing || !_join.Joined || _join.Spawn is not PlayerSpawn spawn)
            {
                return;
            }

            SetChanging(true);
            _name.value = spawn.Name;
            _shape = _content.Shapes[spawn.Shape];
            _color = spawn.Color;
            _eyeColor = spawn.EyeColor;
            ShowError(null);
            Show();
            FocusName();
        }

        // Ready to type a name, except on a phone, where focusing the field would bring up its keyboard over the card
        // before the player has looked at it.
        private void FocusName()
        {
            if (!WebPage.IsPhone)
            {
                _name.Focus();
            }
        }

        /// <summary>Closes the card without changing the look.</summary>
        public void Cancel()
        {
            if (Changing)
            {
                Close();
            }
        }

        private void Close()
        {
            _root.AddToClassList("hidden");
            _root.focusController?.focusedElement?.Blur(); // or Enter and the controller's submit button still reach the hidden card
            if (_preview != null)
            {
                Destroy(_preview.gameObject);
                _preview = null;
            }
            SetChanging(false);
        }

        // The server address in the card, or the default one when it's empty.
        private string ServerAddress() => string.IsNullOrWhiteSpace(_server.value) ? _join.DefaultAddress : _server.value.Trim();

        // The server last joined, else the default. In the editor, one on this computer means its local game server,
        // on whatever port that uses now (Comet › Server); one elsewhere (a teammate's) stays as it was.
        private string RememberedServer()
        {
            var remembered = PlayerPrefs.GetString(ServerKey, "");
#if UNITY_EDITOR
            if (Comet.Unity.LocalServerPort.InEditor != null && JoinClient.TryServerUrl(remembered, out var url) && url.IsLoopback)
            {
                return _join.DefaultAddress;
            }
#endif
            return string.IsNullOrWhiteSpace(remembered) ? _join.DefaultAddress : remembered;
        }

        private void OnContentLoaded(ShapeLandContent content)
        {
            _content = content;
            UiText.Fill(_root, content.Strings);
            SetChanging(false);
            _name.value = PlayerPrefs.GetString(NameKey, "");
            _server.value = RememberedServer();
            _shape = content.Shapes.All.FirstOrDefault(s => s.Id == PlayerPrefs.GetString(ShapeKey, "")) ?? content.Shapes.All[0];
            ShowStartZone(glide: false);
            _color = Remembered(ColorKey, _content.Settings.Colors.Body);
            _eyeColor = Remembered(EyesKey, _content.Settings.Colors.Eyes);

            foreach (var shape in content.Shapes.All)
            {
                var button = new Button(() =>
                {
                    _shape = shape;
                    ShowStartZone();
                    Refresh();
                }) { userData = shape };
                button.AddToClassList("shape-button");
                button.Add(new ShapeIcon(content.BodyKind(shape), GameView.Color(_color)));
                button.Add(new Label(shape.DisplayName));
                _shapes.Add(button);
            }

            AddSwatches(_colors, _content.Settings.Colors.Body, color => _color = color);
            AddSwatches(_eyes, _content.Settings.Colors.Eyes, color => _eyeColor = color);
            Show();
            FocusName();
            if (WebPage.TakeRejoin())
            {
                Submit(); // the page reloaded for new content while rejoining; carry on with the remembered choices
            }
        }

        // The first color unless a remembered one is still in the set.
        private static uint Remembered(string key, uint[] set)
        {
            var saved = (uint)PlayerPrefs.GetInt(key, (int)set[0]);
            return set.Contains(saved) ? saved : set[0];
        }

        private void AddSwatches(VisualElement row, uint[] colors, System.Action<uint> pick)
        {
            foreach (var color in colors)
            {
                var swatch = new Button(() =>
                {
                    pick(color);
                    Refresh();
                }) { userData = color, tooltip = $"#{color:X6}" };
                swatch.AddToClassList("swatch");
                swatch.style.backgroundColor = GameView.Color(color);
                row.Add(swatch);
            }
        }

        // Marks the choices, recolors the icons, and updates the preview and the stats.
        private void Refresh()
        {
            foreach (var button in _shapes.Children())
            {
                button.EnableInClassList("on", button.userData == _shape);
                button.Q<ShapeIcon>().Set(_content.BodyKind((Shape)button.userData), GameView.Color(_color));
            }

            MarkSwatch(_colors, _color);
            MarkSwatch(_eyes, _eyeColor);

            _preview.Show(_view, _shape, _color, _eyeColor);

            var best = _content.Shapes.All.Select(shape => Stats(shape, _content.Movement)).ToList();
            var (speed, jump) = Stats(_shape, _content.Movement);
            _stats.Clear();
            var strings = _content.Strings;
            _stats.Add(Stat(strings.Get("join.speed"), strings.Format("join.speed_value", ("speed", speed.ToString("0.#"))), speed / best.Max(s => s.Speed)));
            _stats.Add(Stat(strings.Get("join.jump"), strings.Format("join.jump_value", ("height", jump.ToString("0.0"))), jump / best.Max(s => s.JumpHeight)));
        }

        private static void MarkSwatch(VisualElement row, uint color)
        {
            foreach (var swatch in row.Children())
            {
                swatch.EnableInClassList("on", (uint)swatch.userData == color);
            }
        }

        private static VisualElement Stat(string name, string value, float share)
        {
            var stat = new VisualElement();
            stat.AddToClassList("stat");
            var top = new VisualElement();
            top.AddToClassList("stat-top");
            var label = new Label(name);
            label.AddToClassList("stat-name");
            top.Add(label);
            top.Add(new Label(value));
            var bar = new VisualElement();
            bar.AddToClassList("bar");
            var fill = new VisualElement();
            fill.AddToClassList("bar-fill");
            fill.style.width = Length.Percent(Mathf.Clamp01(share) * 100);
            bar.Add(fill);
            stat.Add(top);
            stat.Add(bar);
            return stat;
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                Submit();
                e.StopPropagation();
            }
        }

        /// <summary>Checks the name and asks to join, or to change the look, with the choices on the card.</summary>
        public void Submit()
        {
            if (!_joinButton.enabledSelf || _content == null)
            {
                return;
            }

            if (!ShapeLandRules.TryNormalizeName(_name.value, out var name))
            {
                ShowError(Text("join.name_rules"));
                return;
            }

            var server = ServerAddress();
            if (Remember)
            {
                PlayerPrefs.SetString(NameKey, name);
                PlayerPrefs.SetString(ShapeKey, _shape.Id);
                PlayerPrefs.SetInt(ColorKey, (int)_color);
                PlayerPrefs.SetInt(EyesKey, (int)_eyeColor);
#if !UNITY_WEBGL || UNITY_EDITOR
                PlayerPrefs.SetString(ServerKey, server);
#endif
                PlayerPrefs.Save();
            }

            if (_rejoinUntil == null)
            {
                ShowError(null); // while rejoining, the card keeps saying so between tries
            }

            var request = new JoinRequest { Name = name, Shape = _shape.Number, Color = _color, EyeColor = _eyeColor };
            if (Changing)
            {
                var current = _join.Spawn.Value;
                if (name == current.Name && request.Shape == current.Shape && request.Color == current.Color && request.EyeColor == current.EyeColor)
                {
                    Close();
                    return;
                }

                SetButton(false, "join.applying");
                _join.Change(request);
                return;
            }

            SetButton(false, "join.joining");
            _join.Join(server, request);
        }

        private void OnRefused(JoinRejection reason)
        {
            var rejoining = _rejoinUntil != null;
            _rejoinUntil = null;
            if (rejoining && reason == JoinRejection.DifferentContent && WebPage.Reload())
            {
                return; // the server came back with new content; the reloaded page loads it and rejoins
            }

            var message = RefusalMessage(_content.Strings, reason, _join.PlayerName);
            if (reason == JoinRejection.NameTaken && _lostAt is float lostAt && Time.unscaledTime - lostAt < OldSessionSeconds)
            {
                // Likely this player's own old session, which the server hasn't noticed is gone yet.
                message = _content.Strings.Format("join.old_session", ("name", _join.PlayerName));
            }

            Retry(message);
        }

        // The card comes back with the reason, and the choices made before.
        private void OnConnectionLost(CloseReason reason)
        {
            _lostAt = Time.unscaledTime;
            SetChanging(false);
            Show();
            if (reason == CloseReason.ServerShutDown)
            {
                _rejoinUntil = Time.unscaledTime + RejoinSeconds;
                _nextRejoin = Time.unscaledTime + 1;
                Retry(Text("join.rejoining"));
                return;
            }

            Retry(LostMessage(_content.Strings, reason));
        }

        // While rejoining, tries again each second while the card waits for the player.
        private void Update()
        {
            // Kept out of sight while a phone page shows its "Tap to play" card over the game.
            var waiting = !WebPage.Playing;
            if (waiting != _root.ClassListContains("waiting"))
            {
                _root.EnableInClassList("waiting", waiting);
            }
            if (_rejoinUntil is not float until || !_joinButton.enabledSelf || Time.unscaledTime < _nextRejoin)
            {
                return;
            }

            if (Time.unscaledTime > until)
            {
                _rejoinUntil = null;
                Retry(LostMessage(_content.Strings, CloseReason.ServerShutDown));
                return;
            }

            _nextRejoin = Time.unscaledTime + 1;
            Submit();
        }

        private void OnFailed(JoinFailure failure)
        {
            if (_content == null)
            {
                return; // the content didn't load; nothing to show the card with
            }

            if (_rejoinUntil != null && failure != JoinFailure.BadAddress)
            {
                Retry(Text("join.rejoining")); // not back yet
                return;
            }

            var address = _server.value.Trim();
            Retry(failure switch
            {
                JoinFailure.BadAddress => _content.Strings.Format("join.bad_address", ("address", address)),
                JoinFailure.Unanswered => Text("join.unanswered"),
                _ when Application.platform == RuntimePlatform.WebGLPlayer => Text("join.unreachable_web"),
                _ => _content.Strings.Format("join.unreachable", ("address", address)),
            });
        }

        private void Retry(string message)
        {
            ShowError(message);
            SetButton(true, Changing ? "join.apply" : "join.join");
        }

        private string Text(string key) => _content?.Strings.Get(key) ?? "";

        private void ShowError(string message)
        {
            _error.text = message ?? "";
            _error.EnableInClassList("shown", message != null);
        }

        private void OnOwnSpawned(PlayerSpawn spawn)
        {
            _rejoinUntil = null;
            Close();
        }

        private void OpenPreview()
        {
            if (_preview == null)
            {
                _preview = Instantiate(previewPrefab);
                _preview.ShowIn(_root.Q("preview"));
            }
        }

        private void OnDestroy()
        {
            _join.ContentLoaded -= OnContentLoaded;
            _join.OwnSpawned -= OnOwnSpawned;
            _join.JoinRefused -= OnRefused;
            _join.Failed -= OnFailed;
            _join.ConnectionLost -= OnConnectionLost;
            if (_preview != null)
            {
                Destroy(_preview.gameObject); // its own object, so it doesn't go with the card
            }
        }
    }
}

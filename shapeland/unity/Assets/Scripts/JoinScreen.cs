using System.Collections.Generic;
using System.Linq;
using Comet.Client;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// The join screen (UI Toolkit, <c>Assets/UI/JoinScreen.uxml</c>): a card over the orbiting island where the
    /// player picks a name, shape and colours, sees the shape turning with its speed and jump height, and joins.
    /// The server's refusals and connection failures show on the card, which stays up for another try; it hides
    /// once the player spawns. The choices are remembered for next time. Desktop builds also have a server field;
    /// a web page joins the server it came from.
    /// </summary>
    [RequireComponent(typeof(UIDocument), typeof(JoinClient), typeof(GameView))]
    public sealed class JoinScreen : MonoBehaviour
    {
        private const string NameKey = "join.name", ShapeKey = "join.shape", ColourKey = "join.colour", EyesKey = "join.eyes", ServerKey = "join.server";

        private JoinClient _join;
        private GameView _view;
        private VisualElement _root;
        private TextField _name;
        private TextField _server;
        private VisualElement _shapes;
        private VisualElement _colours;
        private VisualElement _eyes;
        private VisualElement _stats;
        private Label _error;
        private Button _joinButton;
        private ShapePreview _preview;
        private float? _lostAt;

        // How long after a lost connection a taken name is probably the player's own old session: the server notices a
        // dead connection within about 30 s (keep-alive).
        private const float OldSessionSeconds = 40;
        private ShapeLandContent _content;
        private Shape _shape;
        private uint _colour;
        private uint _eyeColour;

        /// <summary>Whether to remember the choices in PlayerPrefs for next time (off in tests).</summary>
        public bool Remember { get; set; } = true;

        /// <summary>The screen's root, for tests.</summary>
        public VisualElement Root => _root;

        /// <summary>True while the card is up.</summary>
        public bool Showing => _root != null && !_root.ClassListContains("hidden");

        /// <summary>The message on the card, or null when there is none.</summary>
        public string Error => _error != null && _error.ClassListContains("shown") ? _error.text : null;

        /// <summary>The chosen shape's speed (m/s) and jump height (m), as the card shows them.</summary>
        public static (float Speed, float JumpHeight) Stats(Shape shape) => (shape.MaxSpeed, ShapeLandWorld.Rules.JumpApex(shape.JumpVelocity));

        /// <summary>What the card says when the server refuses a join.</summary>
        public static string RefusalMessage(JoinRejection reason, string name) => reason switch
        {
            JoinRejection.NameTaken => $"Someone online is already called {name}. Pick another name.",
            JoinRejection.InvalidName => NameRules,
            _ => "The server didn't accept that shape or colour. It may be running a different version of ShapeLand.",
        };

        private const string NameRules = "Names use letters, digits, spaces, - and _, up to 16 characters.";

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
            _colours = tree.Q("colours");
            _eyes = tree.Q("eyes");
            _stats = tree.Q("stats");
            _error = tree.Q<Label>("error");
            _joinButton = tree.Q<Button>("join-button");
            _joinButton.clicked += Submit;
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
#if UNITY_WEBGL && !UNITY_EDITOR
            tree.Q("server-field").AddToClassList("hidden");
#endif
        }

        private void OnContentLoaded(ShapeLandContent content)
        {
            _content = content;
            _name.value = PlayerPrefs.GetString(NameKey, "");
            _server.value = PlayerPrefs.GetString(ServerKey, _join.DefaultAddress);
            _shape = content.Shapes.All.FirstOrDefault(s => s.Id == PlayerPrefs.GetString(ShapeKey, "")) ?? content.Shapes.All[0];
            _colour = Remembered(ColourKey, ShapeLandRules.BodyColours);
            _eyeColour = Remembered(EyesKey, ShapeLandRules.EyeColours);

            foreach (var shape in content.Shapes.All)
            {
                var button = new Button(() => Pick(shape)) { userData = shape };
                button.AddToClassList("shape-button");
                button.Add(new ShapeIcon(shape.Mesh, GameView.Colour(_colour)));
                button.Add(new Label(shape.DisplayName));
                _shapes.Add(button);
            }

            AddSwatches(_colours, ShapeLandRules.BodyColours, colour => _colour = colour);
            AddSwatches(_eyes, ShapeLandRules.EyeColours, colour => _eyeColour = colour);
            _preview = new ShapePreview(_view, _root.Q("preview"));
            Refresh();
            _root.RemoveFromClassList("hidden");
            _name.Focus();
        }

        // The first colour unless a remembered one is still in the set.
        private static uint Remembered(string key, uint[] set)
        {
            var saved = (uint)PlayerPrefs.GetInt(key, (int)set[0]);
            return set.Contains(saved) ? saved : set[0];
        }

        private void AddSwatches(VisualElement row, uint[] colours, System.Action<uint> pick)
        {
            foreach (var colour in colours)
            {
                var swatch = new Button(() =>
                {
                    pick(colour);
                    Refresh();
                }) { userData = colour, tooltip = $"#{colour:X6}" };
                swatch.AddToClassList("swatch");
                swatch.style.backgroundColor = GameView.Colour(colour);
                row.Add(swatch);
            }
        }

        private void Pick(Shape shape)
        {
            _shape = shape;
            Refresh();
        }

        // Marks the choices, recolours the icons, and updates the preview and the stats.
        private void Refresh()
        {
            foreach (var button in _shapes.Children())
            {
                button.EnableInClassList("on", button.userData == _shape);
                button.Q<ShapeIcon>().Set(((Shape)button.userData).Mesh, GameView.Colour(_colour));
            }

            foreach (var swatch in _colours.Children())
            {
                swatch.EnableInClassList("on", (uint)swatch.userData == _colour);
            }

            foreach (var swatch in _eyes.Children())
            {
                swatch.EnableInClassList("on", (uint)swatch.userData == _eyeColour);
            }

            _preview.Show(_shape, _colour, _eyeColour);

            var best = _content.Shapes.All.Select(Stats).ToList();
            var (speed, jump) = Stats(_shape);
            _stats.Clear();
            _stats.Add(Stat("Speed", $"{speed:0.#} m/s", speed / best.Max(s => s.Speed)));
            _stats.Add(Stat("Jump", $"{jump:0.0} m", jump / best.Max(s => s.JumpHeight)));
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

        /// <summary>Checks the name and asks to join with the choices on the card.</summary>
        public void Submit()
        {
            if (!_joinButton.enabledSelf || _content == null)
            {
                return;
            }

            if (!ShapeLandRules.TryNormaliseName(_name.value, out var name))
            {
                ShowError(NameRules);
                return;
            }

            var server = string.IsNullOrWhiteSpace(_server.value) ? _join.DefaultAddress : _server.value.Trim();
            if (Remember)
            {
                PlayerPrefs.SetString(NameKey, name);
                PlayerPrefs.SetString(ShapeKey, _shape.Id);
                PlayerPrefs.SetInt(ColourKey, (int)_colour);
                PlayerPrefs.SetInt(EyesKey, (int)_eyeColour);
#if !UNITY_WEBGL || UNITY_EDITOR
                PlayerPrefs.SetString(ServerKey, server);
#endif
                PlayerPrefs.Save();
            }

            ShowError(null);
            _joinButton.SetEnabled(false);
            _joinButton.text = "Joining…";
            _join.Join(server, new JoinRequest { Name = name, Shape = _shape.Number, Colour = _colour, EyeColour = _eyeColour });
        }

        private void OnRefused(JoinRejection reason)
        {
            var message = RefusalMessage(reason, _join.PlayerName);
            if (reason == JoinRejection.NameTaken && _lostAt is float lostAt && Time.unscaledTime - lostAt < OldSessionSeconds)
            {
                // Likely this player's own old session, which the server hasn't noticed is gone yet.
                message = $"The server still has your last session as {_join.PlayerName}. Try again in a few seconds.";
            }

            Retry(message);
        }

        // The card comes back with the reason, and the choices made before.
        private void OnConnectionLost(string reason)
        {
            _lostAt = Time.unscaledTime;
            _preview ??= new ShapePreview(_view, _root.Q("preview"));
            Refresh();
            _root.RemoveFromClassList("hidden");
            Retry(LostMessage(reason));
        }

        /// <summary>What the card says after the connection was lost for <paramref name="reason"/>.</summary>
        public static string LostMessage(string reason) =>
            reason == ClientSession.LostMessage ? reason : $"{ClientSession.LostMessage} {reason}";

        private void OnFailed(string error)
        {
            if (_content == null)
            {
                return; // the content didn't load; nothing to show the card with
            }

            if (_join.BadAddress || error == JoinClient.JoinUnanswered)
            {
                Retry(error);
                return;
            }

            Retry(Application.platform == RuntimePlatform.WebGLPlayer
                ? "Couldn't reach the game server. It may not be running right now."
                : $"Couldn't reach the server at {_server.value.Trim()}. Check the address, or whether it's running.");
        }

        private void Retry(string message)
        {
            ShowError(message);
            _joinButton.SetEnabled(true);
            _joinButton.text = "Join";
        }

        private void ShowError(string message)
        {
            _error.text = message ?? "";
            _error.EnableInClassList("shown", message != null);
        }

        private void OnOwnSpawned(PlayerSpawn spawn)
        {
            _root.AddToClassList("hidden");
            _preview?.Dispose();
            _preview = null;
        }

        private void Update() => _preview?.Turn(Time.unscaledDeltaTime);

        private void OnDestroy()
        {
            _join.ContentLoaded -= OnContentLoaded;
            _join.OwnSpawned -= OnOwnSpawned;
            _join.JoinRefused -= OnRefused;
            _join.Failed -= OnFailed;
            _join.ConnectionLost -= OnConnectionLost;
            _preview?.Dispose();
        }

        /// <summary>
        /// The chosen shape, built like any player's, turning in front of its own camera on a layer the main
        /// camera doesn't draw, far below the island, and shown on the card through a render texture.
        /// </summary>
        private sealed class ShapePreview : System.IDisposable
        {
            private const float DegreesPerSecond = 40;
            private static readonly Vector3 Place = new Vector3(0, -500, 0);

            private readonly GameView _view;
            private readonly GameObject _stand;
            private readonly RenderTexture _texture;
            private GameObject _shape;
            private ShapeFade _materials;

            public ShapePreview(GameView view, VisualElement target)
            {
                _view = view;
                _texture = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32) { name = "Shape preview" };
                _stand = new GameObject("Shape preview") { layer = GameView.PreviewLayer };
                _stand.transform.position = Place;
                var camera = new GameObject("Preview camera", typeof(Camera)).GetComponent<Camera>();
                camera.gameObject.layer = GameView.PreviewLayer;
                camera.transform.SetParent(_stand.transform, false);
                camera.transform.SetLocalPositionAndRotation(new Vector3(0, 1.3f, -3.4f), Quaternion.Euler(14, 0, 0));
                camera.fieldOfView = 28;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0, 0, 0, 0);
                camera.cullingMask = 1 << GameView.PreviewLayer;
                camera.targetTexture = _texture;
                target.style.backgroundImage = Background.FromRenderTexture(_texture);
            }

            public void Show(Shape shape, uint colour, uint eyeColour)
            {
                var facing = _shape != null ? _shape.transform.localRotation : Quaternion.Euler(0, 200, 0);
                Clear();
                _shape = _view.BuildShape(shape, colour, eyeColour, "Previewed shape", out _materials);
                _shape.transform.SetParent(_stand.transform, false);
                _shape.transform.localRotation = facing;
                foreach (var renderer in _shape.GetComponentsInChildren<Renderer>())
                {
                    renderer.gameObject.layer = GameView.PreviewLayer;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            public void Turn(float seconds)
            {
                if (_shape != null)
                {
                    _shape.transform.Rotate(0, DegreesPerSecond * seconds, 0);
                }
            }

            private void Clear()
            {
                if (_shape != null)
                {
                    _materials.Destroy();
                    Object.Destroy(_shape);
                    _shape = null;
                }
            }

            public void Dispose()
            {
                Clear();
                Object.Destroy(_stand);
                _texture.Release();
                Object.Destroy(_texture);
            }
        }
    }
}

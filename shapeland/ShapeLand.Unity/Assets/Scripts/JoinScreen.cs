using System;
using System.Collections.Generic;
using System.Linq;
using Comet.Client;
using Comet.Content;
using Comet.Protocol.Login;
using Comet.Simulation;
using Comet.Unity;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// The join screen (UI Toolkit, <c>Assets/UI/JoinScreen.uxml</c>): a card over the orbiting island where a new
    /// player picks a name, shape and colors, sees the shape turning with its speed and jump height, and joins, which
    /// makes a guest account. A returning player (a kept session token) skips it and goes straight into the game; the
    /// card's Sign in link signs in to an account made elsewhere, with a password or a recovery code. A signed-in
    /// player not playing (after leaving, a drop or a lost connection) gets a Play button instead of the fields.
    /// Refusals and connection failures show on the card, which stays up for another try; it hides once the player
    /// spawns. When the server shuts down (as a restart does), the card tries joining again for a minute; if the
    /// server came back with new content, a web page reloads to load it and joins again by itself. Desktop builds
    /// also have a server field (the Login server); a web page uses the one its config names. The sign-in panel and
    /// the signed-in state are temporary interface, to be redone.
    /// </summary>
    [RequireComponent(typeof(UIDocument), typeof(JoinClient), typeof(GameView))]
    public sealed class JoinScreen : MonoBehaviour
    {
        [SerializeField] private ShapePreview previewPrefab;

        // PlayerPrefs keys for the choices remembered for next time.
        private const string NameKey = "remember.name", ShapeKey = "remember.shape", ColorKey = "remember.color.number", EyesKey = "remember.eyes.number", ServerKey = "remember.login";

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
        private Button _backButton;
        // Where Back goes, when the card was opened from the Esc menu.
        private Action _back;
        private VisualElement _serverField;
        private VisualElement _signIn;
        private TextField _signInUsername;
        private TextField _signInPassword;
        private TextField _signInCode;
        private VisualElement _signInPasswordField;
        private VisualElement _signInCodeField;
        private Button _useCode;
        private Button _signInLink;
        private Label _signedInText;
        private bool _withCode;
        // A rename on the look card: the card stays up until a spawn shows the new name (the look's own spawn may come first).
        private string _awaitName;
        private bool _busy;
        private ShapePreview _preview;
        // Keys and gamepad: the rows they move through (name, shape, color, eyes, then the bottom line of server field
        // and buttons), the selected one, the selected item on the bottom line, and the held direction's repeat.
        private VisualElement[] _rows;
        private ScrollView _scroll;
        private VisualElement _buttonsRow;
        private int _row;
        private int _buttonIndex;
        // Up from the name row reaches Back, when the card has it (opened from the Esc menu).
        private bool _onBack;
        private Vector2 _held;
        private float _repeatAt;
        private int _shownOnFrame;
        private const float RepeatDelay = 0.4f, RepeatInterval = 0.1f;
        // What each shape and color button does, for left and right to pick with.
        private readonly Dictionary<VisualElement, Action> _choose = new();

        // After the server shuts down (a restart sends that), the card tries joining again once a second for this long.
        private const float RejoinSeconds = 60;
        private float? _rejoinUntil;
        private bool _focusNamePending;
        private float _nextRejoin;
        private ShapeLandContent _content;
        private Shape _shape;
        private int _color;
        private int _eyeColor;

        /// <summary>Whether to remember the choices in PlayerPrefs for next time (off in tests).</summary>
        public bool Remember { get; set; } = true;

        /// <summary>The screen's root, for tests.</summary>
        public VisualElement Root => _root;

        /// <summary>True while the card is up.</summary>
        public bool Showing => _root != null && !_root.ClassListContains("hidden");

        /// <summary>True while the card is up to change the look of a player already in the game.</summary>
        public bool Changing { get; private set; }

        /// <summary>True while the card shows the sign-in panel instead of the new player's fields.</summary>
        public bool SigningIn => _root != null && _root.ClassListContains("signing-in");

        /// <summary>True while the card is up for a signed-in player who isn't playing: a message and Play.</summary>
        public bool SignedInCard => _root != null && _root.ClassListContains("signed-in");

        /// <summary>True while a text field on the card has the keyboard.</summary>
        public bool Typing => _root?.focusController?.focusedElement is VisualElement focused
            && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);

        /// <summary>The message on the card, or null when there is none.</summary>
        public string Error => _error != null && _error.ClassListContains("shown") ? _error.text : null;

        /// <summary>The chosen shape's speed (m/s) and jump height (m), as the card shows them.</summary>
        public static (float Speed, float JumpHeight) Stats(Shape shape, MovementRules rules) => (shape.MaxSpeed, rules.JumpApex(shape.JumpVelocity));

        /// <summary>What the card says when the server refuses a join.</summary>
        public static string RefusalMessage(StringTable strings, JoinRejection reason) => reason switch
        {
            JoinRejection.DifferentContent => strings.Get(Debug.isDebugBuild ? "join.different_content_dev" : "join.different_content"),
            JoinRejection.Busy => strings.Get("join.busy"),
            JoinRejection.Replaced => strings.Get("drop.replaced"),
            JoinRejection.NoCharacter => strings.Get("account.no_character"),
            _ => strings.Get("join.not_accepted"),
        };

        /// <summary>What the card says for a Login server error code (<see cref="LoginErrors"/>).</summary>
        public static string LoginMessage(StringTable strings, string code, string name = "") => code switch
        {
            LoginErrors.NameTaken => strings.Format("join.name_taken", ("name", name)),
            LoginErrors.InvalidName => strings.Get("join.name_rules"),
            LoginErrors.WrongPassword => strings.Get("account.wrong_password"),
            LoginErrors.WrongCode => strings.Get("account.wrong_code"),
            LoginErrors.LockedOut => strings.Get("account.locked_out"),
            LoginErrors.RateLimited => strings.Get("account.rate_limited"),
            LoginErrors.WeakPassword => strings.Get("account.weak_password"),
            LoginErrors.SignInAgain => strings.Get("account.sign_in_again"),
            LoginErrors.Banned => strings.Get("account.banned"),
            LoginErrors.AccountDeleted => strings.Get("account.deleted"),
            LoginErrors.NoCharacter => strings.Get("account.no_character"),
            LoginErrors.SignedOut => strings.Get("account.signed_out"),
            Comet.Client.Login.LoginClient<CharacterView>.Unreachable => strings.Get("account.unreachable"),
            _ => strings.Get("account.failed"),
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
            _join.LookRefused += OnLookRefused;
            _join.Failed += OnFailed;
            _join.ConnectionLost += OnConnectionLost;
            _join.SignedOut += OnSignedOut;
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
            _backButton = tree.Q<Button>("look-back");
            _backButton.clicked += Back;
            _serverField = tree.Q("server-field");
            _signIn = tree.Q("sign-in");
            _signInUsername = tree.Q<TextField>("sign-in-username");
            _signInPassword = tree.Q<TextField>("sign-in-password");
            _signInCode = tree.Q<TextField>("sign-in-code");
            _signInPasswordField = tree.Q("sign-in-password-field");
            _signInCodeField = tree.Q("sign-in-code-field");
            _useCode = tree.Q<Button>("sign-in-use-code");
            _useCode.clicked += () => SetWithCode(!_withCode);
            _signInLink = tree.Q<Button>("sign-in-link");
            _signInLink.clicked += () => SetSigningIn(!SigningIn);
            _signedInText = tree.Q<Label>("signed-in-text");
            _buttonsRow = _joinButton.parent;
            // The server field shares the bottom line with the buttons, and is one of its items.
            _scroll = tree.Q<ScrollView>("card-scroll");
            _rows = _root.Query(className: "nav-row").Where(row => row != _serverField).ToList().Append(_buttonsRow).ToArray();
            for (var i = 0; i < _rows.Length - 1; i++)
            {
                var row = i;
                _rows[i].RegisterCallback<PointerDownEvent>(_ => SelectRow(row), TrickleDown.TrickleDown);
            }

            foreach (var item in new[] { _serverField, _cancelButton, _joinButton })
            {
                item.RegisterCallback<PointerDownEvent>(_ => SelectItem(item), TrickleDown.TrickleDown);
            }

            _root.EnableInClassList("phone", WebPage.IsPhone);
            GameMenu.TakeOverNavigation(_root);
            _root.RegisterCallback<PointerDownEvent>(_ => Navigating = false, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerMoveEvent>(_ => Navigating = false, TrickleDown.TrickleDown);
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            SetChanging(false);
        }

        // The sign-in panel in place of the new player's fields, or back.
        private void SetSigningIn(bool signingIn)
        {
            _root.EnableInClassList("signing-in", signingIn);
            _signIn.EnableInClassList("hidden", !signingIn);
            MarkFieldRows();
            _signInLink.text = Text(signingIn ? "account.new_player_link" : "account.sign_in_link");
            SetButton(true, signingIn ? "account.sign_in" : "join.join");
            ShowError(null);
            if (signingIn)
            {
                SetWithCode(false);
            }
        }

        // A recovery code in place of the password, or back.
        private void SetWithCode(bool withCode)
        {
            _withCode = withCode;
            _signInPasswordField.EnableInClassList("hidden", withCode);
            _signInCodeField.EnableInClassList("hidden", !withCode);
            _signInUsername.parent.EnableInClassList("hidden", withCode); // a code signs in on its own
            _useCode.text = Text(withCode ? "account.use_password" : "account.use_code");
        }

        // A signed-in player who isn't playing: a message and Play, in place of the fields.
        private void SetSignedIn(bool signedIn, string message = null)
        {
            _root.EnableInClassList("signed-in", signedIn);
            _signedInText.EnableInClassList("hidden", !signedIn);
            _signedInText.text = !signedIn ? "" : message ?? (_join.Account != null
                ? _content.Strings.Format("account.signed_in_as", ("name", _join.PlayerName))
                : Text("account.signed_in"));
            MarkFieldRows();
            if (signedIn)
            {
                SetButton(true, "join.play");
            }
        }

        // Keys and gamepad skip the new player's rows while they're out of sight.
        private void MarkFieldRows()
        {
            var away = SigningIn || SignedInCard;
            foreach (var row in _rows.Take(_rows.Length - 1))
            {
                row.EnableInClassList("hidden", away);
            }

            _signInLink.EnableInClassList("hidden", Changing || SignedInCard);
        }

        // Changing the look: no server to pick, and the card can be closed without changing anything.
        private void SetChanging(bool changing)
        {
            Changing = changing;
            if (changing)
            {
                _root.RemoveFromClassList("signing-in");
                _signIn.AddToClassList("hidden");
                SetSignedIn(false);
            }
            _root.EnableInClassList("changing", changing);
            _root.pickingMode = changing ? PickingMode.Ignore : PickingMode.Position;
#if UNITY_WEBGL && !UNITY_EDITOR
            _serverField.AddToClassList("hidden");
#else
            _serverField.EnableInClassList("hidden", changing);
#endif
            if (!changing)
            {
                _back = null;
            }

            // Opened from the Esc menu, Back stands in for Cancel.
            _cancelButton.EnableInClassList("hidden", !changing || _back != null);
            _backButton.EnableInClassList("hidden", _back == null);
            MarkFieldRows();
            SetButton(true, changing ? "join.apply" : SignedInCard ? "join.play" : SigningIn ? "account.sign_in" : "join.join");
        }

        private void SetButton(bool enabled, string key)
        {
            _busy = !enabled;
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

        // Shows the card with the current choices, the name row selected.
        private void Show()
        {
            OpenPreview();
            Refresh();
            _root.RemoveFromClassList("hidden");
            _shownOnFrame = Time.frameCount; // the press that brought the card up (from the Esc menu) isn't the card's
            SelectRow(0);
        }

        /// <summary>True while keys or a gamepad move the card's selection, which shows only then.</summary>
        public bool Navigating
        {
            get => _root.ClassListContains("navigating");
            private set => _root.EnableInClassList("navigating", value);
        }

        /// <summary>True while the card is up to change the look and was opened from the Esc menu.</summary>
        public bool FromMenu => Changing && _back != null;

        /// <summary>
        /// Brings the card up over the game, filled in with the player's current look, to change it. Given
        /// <paramref name="back"/> (opened from the Esc menu), the card has Back, which closes it and calls that.
        /// </summary>
        public void OpenChange(Action back = null, bool navigating = false)
        {
            if (Showing || !_join.Joined || _join.Spawn is not PlayerSpawn spawn)
            {
                return;
            }

            Navigating = navigating;

            _back = back;
            SetChanging(true);
            _name.value = spawn.Name;
            _shape = _content.Shapes.TryGet(spawn.Shape, out var shape) ? shape : _content.Shapes.All[0];
            _color = spawn.Color;
            _eyeColor = spawn.EyeColor;
            ShowError(null);
            Show();
        }

        // Ready to type a name when joining, except on a phone, where focusing the field would bring up its keyboard
        // over the card before the player has looked at it. Focused once no key is down, or the key that brought the
        // card up would type.
        private void FocusName() => _focusNamePending = !WebPage.IsPhone;

        /// <summary>Closes the card without changing the look and goes back to the Esc menu, if it came from there.</summary>
        public void Back()
        {
            var back = FromMenu ? _back : null;
            Cancel();
            back?.Invoke();
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
            _awaitName = null;
            _root.AddToClassList("hidden");
            _root.focusController?.focusedElement?.Blur(); // or Enter and the controller's submit button still reach the hidden card
            if (_preview != null)
            {
                Destroy(_preview.gameObject);
                _preview = null;
            }
            SetChanging(false);
        }

        // The Login server in the card, or the default one when it's empty.
        private string ServerAddress() => string.IsNullOrWhiteSpace(_server.value) ? _join.DefaultLoginAddress : _server.value.Trim();

        // The Login server last used, else the default. In the editor, one on this computer means its local Login
        // server, on whatever port that uses now (Comet › Server); one elsewhere (a teammate's) stays as it was.
        private string RememberedServer()
        {
            var remembered = PlayerPrefs.GetString(ServerKey, "");
#if UNITY_EDITOR
            if (Comet.Unity.LocalServerPort.LoginInEditor != null && Uri.TryCreate(JoinClient.LoginUrl(remembered), UriKind.Absolute, out var url) && url.IsLoopback)
            {
                return _join.DefaultLoginAddress;
            }
#endif
            return string.IsNullOrWhiteSpace(remembered) ? _join.DefaultLoginAddress : remembered;
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
            _color = Remembered(ColorKey);
            _eyeColor = Remembered(EyesKey);

            foreach (var shape in content.Shapes.All)
            {
                void Choose()
                {
                    _shape = shape;
                    ShowStartZone();
                    Refresh();
                }

                var button = new Button(Choose) { userData = shape };
                _choose[button] = Choose;
                button.AddToClassList("shape-button");
                button.Add(new ShapeIcon(content.BodyKind(shape), GameView.Color(content.ColorRgb(_color))));
                button.Add(new Label(shape.DisplayName));
                _shapes.Add(button);
            }

            AddSwatches(_colors, color => _color = color);
            AddSwatches(_eyes, color => _eyeColor = color);
            WebPage.TakeRejoin(); // a returning player resumes below anyway
            if (_join.SignedIn)
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                _join.UseLoginServer(_server.value); // the one remembered from the card, as the session's
#endif
                Resume(); // a returning player goes straight into the game
                return;
            }

            Show();
            FocusName();
        }

        // A returning player: the card stays down while the account loads and the character joins; it comes up only
        // if that fails (a refused token shows the new player's card, through SignedOut).
        private async void Resume()
        {
            var error = await _join.ResumeAsync();
            if (error == null || error == LoginErrors.SignedOut)
            {
                return;
            }

            ShowSignedIn(LoginMessage(_content.Strings, error));
        }

        // The card for a signed-in player who isn't playing, with a message.
        private void ShowSignedIn(string message)
        {
            if (Changing)
            {
                Close();
            }

            SetSignedIn(true);
            Show();
            ShowError(message);
        }

        // The token was refused (expired, revoked, a deleted guest) or the player signed out: the new player's card.
        private void OnSignedOut()
        {
            if (_content == null)
            {
                return;
            }

            if (Changing)
            {
                Close();
            }

            SetSignedIn(false);
            SetSigningIn(false);
            Show();
        }

        // The first color unless a remembered one (by number) is still in the content.
        private int Remembered(string key)
        {
            var saved = PlayerPrefs.GetInt(key, 0);
            return _content.Colors.TryGet(saved, out _) ? saved : _content.Colors.All[0].Number;
        }

        private void AddSwatches(VisualElement row, Action<int> pick)
        {
            foreach (var entry in _content.Colors.All)
            {
                var color = entry.Number;
                void Choose()
                {
                    pick(color);
                    Refresh();
                }

                var swatch = new Button(Choose) { userData = color, tooltip = $"#{entry.Rgb:X6}" };
                _choose[swatch] = Choose;
                swatch.AddToClassList("swatch");
                var fill = new VisualElement { pickingMode = PickingMode.Ignore };
                fill.AddToClassList("swatch-fill");
                fill.style.backgroundColor = GameView.Color(entry.Rgb);
                swatch.Add(fill);
                row.Add(swatch);
            }
        }

        // Marks the choices, recolors the icons, and updates the preview and the stats.
        private void Refresh()
        {
            foreach (var button in _shapes.Children())
            {
                button.EnableInClassList("on", button.userData == _shape);
                button.Q<ShapeIcon>().Set(_content.BodyKind((Shape)button.userData), GameView.Color(_content.ColorRgb(_color)));
            }

            MarkSwatch(_colors, _color);
            MarkSwatch(_eyes, _eyeColor);

            _preview.Show(_view, _shape, _content.ColorRgb(_color), _content.ColorRgb(_eyeColor));

            var best = _content.Shapes.All.Select(shape => Stats(shape, _content.Movement)).ToList();
            var (speed, jump) = Stats(_shape, _content.Movement);
            _stats.Clear();
            var strings = _content.Strings;
            _stats.Add(Stat(strings.Get("join.speed"), strings.Format("join.speed_value", ("speed", speed.ToString("0.#"))), speed / best.Max(s => s.Speed)));
            _stats.Add(Stat(strings.Get("join.jump"), strings.Format("join.jump_value", ("height", jump.ToString("0.0"))), jump / best.Max(s => s.JumpHeight)));
        }

        private static void MarkSwatch(VisualElement row, int color)
        {
            foreach (var swatch in row.Children())
            {
                swatch.EnableInClassList("on", (int)swatch.userData == color);
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

        // Enter in a field joins (applies); elsewhere on the card Enter is the Submit action's (Navigate).
        private void OnKeyDown(KeyDownEvent e)
        {
            if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && Typing)
            {
                Submit();
                e.StopPropagation();
            }
        }

        /// <summary>
        /// The card's main button: joins as a new guest with the choices on the card, signs in (the sign-in panel),
        /// plays (a signed-in player), or applies a change of look and name.
        /// </summary>
        public async void Submit()
        {
            if (!_joinButton.enabledSelf || _content == null || _busy)
            {
                return;
            }

            if (SigningIn)
            {
                await SignIn();
                return;
            }

            if (SignedInCard)
            {
                SetButton(false, "join.joining");
                var playError = await _join.PlayAsync();
                if (playError != null)
                {
                    Retry(LoginMessage(_content.Strings, playError));
                }

                return;
            }

            if (!ShapeLandRules.TryNormalizeName(_name.value, out var name))
            {
                ShowError(Text("join.name_rules"));
                return;
            }

            if (Remember)
            {
                PlayerPrefs.SetString(NameKey, name);
                PlayerPrefs.SetString(ShapeKey, _shape.Id);
                PlayerPrefs.SetInt(ColorKey, _color);
                PlayerPrefs.SetInt(EyesKey, _eyeColor);
#if !UNITY_WEBGL || UNITY_EDITOR
                PlayerPrefs.SetString(ServerKey, ServerAddress());
#endif
                PlayerPrefs.Save();
            }

            if (_rejoinUntil == null)
            {
                ShowError(null); // while rejoining, the card keeps saying so between tries
            }

            if (Changing)
            {
                await Apply(name);
                return;
            }

            SetButton(false, "join.joining");
#if !UNITY_WEBGL || UNITY_EDITOR
            _join.UseLoginServer(ServerAddress());
#endif
            var error = await _join.CreateGuestAsync(name, new NewCharacter { Shape = _shape.Number, Color = _color, EyeColor = _eyeColor });
            if (error != null)
            {
                Retry(LoginMessage(_content.Strings, error, name));
            }
        }

        // The Change Shape card: the look goes to the game server, a new name to the Login server first. The card
        // closes on the new spawn either brings.
        private async System.Threading.Tasks.Task Apply(string name)
        {
            var current = _join.Spawn.Value;
            var lookChanged = _shape.Number != current.Shape || _color != current.Color || _eyeColor != current.EyeColor;
            var nameChanged = name != current.Name;
            if (!lookChanged && !nameChanged)
            {
                Close();
                return;
            }

            SetButton(false, "join.applying");
            if (nameChanged)
            {
                _awaitName = name;
                var error = await _join.RenameAsync(name);
                if (error != null)
                {
                    _awaitName = null;
                    Retry(LoginMessage(_content.Strings, error, name));
                    return;
                }
            }

            if (lookChanged)
            {
                _join.ChangeLook(new LookChange { Shape = _shape.Number, Color = _color, EyeColor = _eyeColor });
            }
        }

        // The sign-in panel: a username and password, or a recovery code alone.
        private async System.Threading.Tasks.Task SignIn()
        {
            SetButton(false, "account.signing_in");
            ShowError(null);
#if !UNITY_WEBGL || UNITY_EDITOR
            _join.UseLoginServer(ServerAddress());
#endif
            var error = _withCode
                ? await _join.SignInWithCodeAsync(_signInCode.value)
                : await _join.SignInAsync(_signInUsername.value.Trim(), _signInPassword.value);
            _signInPassword.value = "";
            if (error != null)
            {
                ShowError(LoginMessage(_content.Strings, error));
                SetButton(true, "account.sign_in");
                return;
            }

            _signInCode.value = "";
        }

        private void OnRefused(JoinRejection reason)
        {
            var rejoining = _rejoinUntil != null;
            _rejoinUntil = null;
            if (rejoining && reason == JoinRejection.DifferentContent && WebPage.Reload())
            {
                return; // the server came back with new content; the reloaded page loads it and rejoins
            }

            ShowSignedIn(RefusalMessage(_content.Strings, reason));
        }

        private void OnLookRefused(LookRejection reason)
        {
            if (Changing)
            {
                Retry(Text(reason == LookRejection.TooFast ? "join.too_fast" : "join.not_accepted"));
            }
        }

        // The card comes back with the reason (none after leaving), and the choices made before.
        private void OnConnectionLost(CloseReason reason)
        {
            if (!_join.SignedIn)
            {
                return; // signed out: OnSignedOut brought up the new player's card
            }

            SetChanging(false);
            if (_join.DroppedFor is DropReason dropped)
            {
                // Taken over by another tab or device, or removed: no rejoining by itself.
                ShowSignedIn(Text(dropped == DropReason.Replaced ? "drop.replaced" : "drop.removed"));
                return;
            }

            if (reason == CloseReason.None)
            {
                ShowSignedIn(null);
                return;
            }

            if (reason == CloseReason.ServerShutDown)
            {
                _rejoinUntil = Time.unscaledTime + RejoinSeconds;
                _nextRejoin = Time.unscaledTime + 1;
                ShowSignedIn(Text("join.rejoining"));
                return;
            }

            ShowSignedIn(LostMessage(_content.Strings, reason));
        }

        // While rejoining, tries again each second while the card waits for the player.
        private void Update()
        {
            if (Showing && WebPage.Playing && _content != null && Time.frameCount != _shownOnFrame)
            {
                Navigate(_view.Controls.Menu);
                // A gamepad's Start joins, or applies the change, from anywhere on the card (Esc is the Hud's).
                var start = _view.Controls.Game.Menu;
                if (start.WasPressedThisFrame() && start.activeControl?.device is Gamepad)
                {
                    Navigating = true;
                    Submit();
                }
            }

            if (_focusNamePending && (Keyboard.current == null || !Keyboard.current.anyKey.isPressed))
            {
                _focusNamePending = false;
                if (Showing)
                {
                    _name.Focus();
                }
            }

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
                ShowSignedIn(LostMessage(_content.Strings, CloseReason.ServerShutDown));
                return;
            }

            _nextRejoin = Time.unscaledTime + 1;
            if (SignedInCard)
            {
                Submit();
            }
        }

        // Keys and gamepad on the card, with the Esc menu's Menu actions: up and down move between rows, left and right
        // pick within one (or between the buttons), Submit types into a field, presses a button or joins (applies) from
        // the other rows, and Back stops typing, else goes back or cancels. Typing needs a keyboard; while a field has
        // it, only the gamepad moves on (and the keys type).
        private void Navigate(ShapeLandControls.MenuActions actions)
        {
            var typing = Typing;
            var fromPad = actions.Navigate.activeControl?.device is Gamepad;
            var step = Step(typing && !fromPad ? Vector2.zero : actions.Navigate.ReadValue<Vector2>());
            if (!Navigating && step != Vector2Int.zero)
            {
                Navigating = true; // the first press only shows where the selection is
                return;
            }

            if (actions.Back.WasPressedThisFrame() || actions.Submit.WasPressedThisFrame() && !typing)
            {
                Navigating = true;
            }

            if (step != Vector2Int.zero && typing)
            {
                _root.focusController?.focusedElement?.Blur();
            }

            if (step.y != 0)
            {
                MoveRow(step.y);
            }
            else if (step.x != 0)
            {
                Pick(step.x);
            }

            if (actions.Back.WasPressedThisFrame())
            {
                if (typing)
                {
                    _root.focusController?.focusedElement?.Blur();
                }
                else if (FromMenu)
                {
                    Back();
                }
                else
                {
                    Cancel();
                }
            }
            // Space and Enter while typing are the field's (Enter joins through OnKeyDown); a gamepad's A ends typing.
            else if (actions.Submit.WasPressedThisFrame() && (!typing || actions.Submit.activeControl?.device is Gamepad))
            {
                SubmitRow(typing);
            }
        }

        // A held direction steps once, then repeats after a pause; down and right are positive.
        private Vector2Int Step(Vector2 value)
        {
            var held = new Vector2(Mathf.Abs(value.x) > 0.5f ? Mathf.Sign(value.x) : 0, Mathf.Abs(value.y) > 0.5f ? -Mathf.Sign(value.y) : 0);
            if (held.y != 0)
            {
                held.x = 0;
            }

            var now = Time.unscaledTime;
            if (held != _held)
            {
                _held = held;
                _repeatAt = now + RepeatDelay;
                return Vector2Int.RoundToInt(held);
            }

            if (held != Vector2.zero && now >= _repeatAt)
            {
                _repeatAt = now + RepeatInterval;
                return Vector2Int.RoundToInt(held);
            }

            return Vector2Int.zero;
        }

        private void MoveRow(int by)
        {
            if (_onBack)
            {
                if (by > 0)
                {
                    SelectRow(0);
                }

                return;
            }

            if (by < 0 && _row == 0 && FromMenu)
            {
                SelectBack();
                return;
            }

            var row = _row;
            do
            {
                row += by;
            } while (row >= 0 && row < _rows.Length && !Usable(_rows[row]));

            if (row >= 0 && row < _rows.Length)
            {
                SelectRow(row);
                if (_scroll.contentContainer.Contains(_rows[row]))
                {
                    _scroll.ScrollTo(_rows[row]); // when the card is too tall for a phone's screen
                }
            }
        }

        private static bool Usable(VisualElement row) => !row.ClassListContains("hidden");

        private void SelectBack()
        {
            SelectRow(0);
            _onBack = true;
            _rows[0].RemoveFromClassList("selected");
            _backButton.AddToClassList("selected");
        }

        private void SelectRow(int row)
        {
            _onBack = false;
            _backButton.RemoveFromClassList("selected");
            _row = row;
            for (var i = 0; i < _rows.Length; i++)
            {
                _rows[i].EnableInClassList("selected", i == row && _rows[i] != _buttonsRow);
            }

            _buttonIndex = Items().Count - 1; // Join or Apply
            MarkItem();
        }

        // The bottom line's visible items, left to right: the server field (desktop builds), Cancel and Join or Apply.
        private List<VisualElement> Items() => new[] { _serverField, _cancelButton, _joinButton }.Where(Usable).ToList();

        private void SelectItem(VisualElement item)
        {
            _row = _rows.Length - 1;
            foreach (var row in _rows)
            {
                row.RemoveFromClassList("selected");
            }

            _buttonIndex = Mathf.Max(0, Items().IndexOf(item));
            MarkItem();
        }

        private void MarkItem()
        {
            var items = Items();
            foreach (var item in new[] { _serverField, _cancelButton, _joinButton })
            {
                item.EnableInClassList("selected", _rows[_row] == _buttonsRow && items.IndexOf(item) == _buttonIndex);
            }
        }

        // Left or right on the selected row: the next shape or color (picked at once), or the next button.
        private void Pick(int by)
        {
            var row = _rows[_row];
            if (_onBack)
            {
                return;
            }

            if (row == _buttonsRow)
            {
                _buttonIndex = Mathf.Clamp(_buttonIndex + by, 0, Items().Count - 1);
                MarkItem();
                return;
            }

            var choices = row.Q(className: "row");
            if (choices == null || choices.childCount == 0)
            {
                return;
            }

            var current = choices.Children().ToList().FindIndex(choice => choice.ClassListContains("on"));
            var next = Mathf.Clamp(current + by, 0, choices.childCount - 1);
            if (next != current && _choose.TryGetValue(choices[next], out var choose))
            {
                choose();
            }
        }

        private void SubmitRow(bool typing)
        {
            var row = _rows[_row];
            if (typing)
            {
                _root.focusController?.focusedElement?.Blur();
            }
            else if (_onBack)
            {
                Back();
            }
            else if (row == _buttonsRow)
            {
                var item = Items()[_buttonIndex];
                if (item == _serverField)
                {
                    _server.Focus();
                }
                else if (item == _cancelButton)
                {
                    Cancel();
                }
                else
                {
                    Submit();
                }
            }
            else if (row.Q<TextField>() is { } field)
            {
                field.Focus();
            }
            else
            {
                Submit();
            }
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

            var message = failure switch
            {
                JoinFailure.BadAddress => _content.Strings.Format("join.bad_address", ("address", _join.Error ?? "")),
                JoinFailure.Unanswered => Text("join.unanswered"),
                _ when Application.platform == RuntimePlatform.WebGLPlayer => Text("join.unreachable_web"),
                _ => _content.Strings.Format("join.unreachable", ("address", _join.LoginAddress)),
            };
            if (_join.SignedIn)
            {
                ShowSignedIn(message);
            }
            else
            {
                Retry(message);
            }
        }

        private void Retry(string message)
        {
            ShowError(message);
            SetButton(true, Changing ? "join.apply" : SignedInCard ? "join.play" : SigningIn ? "account.sign_in" : "join.join");
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
            if (_awaitName != null && Changing && spawn.Name != _awaitName)
            {
                return; // the look's spawn; the rename's comes next
            }

            _awaitName = null;
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
            _join.LookRefused -= OnLookRefused;
            _join.Failed -= OnFailed;
            _join.ConnectionLost -= OnConnectionLost;
            _join.SignedOut -= OnSignedOut;
            if (_preview != null)
            {
                Destroy(_preview.gameObject); // its own object, so it doesn't go with the card
            }
        }
    }
}

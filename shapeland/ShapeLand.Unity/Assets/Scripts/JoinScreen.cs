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
    [RequireComponent(typeof(UIDocument), typeof(JoinClient), typeof(GameView))]
    public sealed partial class JoinScreen : MonoBehaviour
    {
        [SerializeField]
        private ShapePreview previewPrefab;

        private const string ServerKey = "remember.login";

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

        private Action _back;
        private VisualElement _serverField;
        private VisualElement _serverLine;
        private Button _serverChange;
        private Button _serverCancel;
        private Button _pageBack;
        private Button _signOutButton;
        private VisualElement _nameRow;
        private VisualElement[] _pageRows;
        private Label _serverText;

        private string _serverBefore;
        private Label _pageName;
        private VisualElement _signIn;

        private string _devName;
        private NewCharacter _devLook;
        private TextField _signInUsername;
        private TextField _signInPassword;
        private TextField _signInCode;
        private VisualElement _signInUsernameField;
        private VisualElement _signInPasswordField;
        private VisualElement _signInCodeField;
        private Button _useCode;
        private Button _signInButton;
        private TextField _setPassword;
        private TextField _setPasswordAgain;
        private Label _setPasswordUsername;
        private Label _whoName;
        private VisualElement _whoGuest;
        private VisualElement _whoGuestNote;
        private ConfirmPanel _confirm;
        private bool _withCode;

        private string _takenName;
        private bool _busy;

        private VisualElement _pop;
        private Label _popMain;
        private Label _popSub;
        private VisualElement[] _popDots;
        private int _popAttempt;
        private bool _popBusy;
        private int _popDot;
        private float _popDotAt;
        private ShapePreview _preview;

        private VisualElement[] _rows;
        private ScrollView _scroll;
        private VisualElement _buttonsRow;
        private int _row;
        private int _buttonIndex;

        private bool _onBack;
        private readonly MenuNavigation _navigation = new();
        private int _shownOnFrame;

        private readonly Dictionary<VisualElement, Action> _choose = new();

        private const float RejoinSeconds = 60;
        private float? _rejoinUntil;
        private bool _focusNamePending;
        private float _nextRejoin;
        private ShapeLandContent _content;
        private Shape _shape;
        private int _color;
        private int _eyeColor;

        public VisualElement Root => _root;

        public bool Showing => _root != null && !_root.ClassListContains("hidden");

        public bool Changing { get; private set; }

        public bool SigningIn => _root != null && _root.ClassListContains("signing-in");

        public bool SignedInCard => _root != null && _root.ClassListContains("signed-in");

        public bool SettingPassword => _root != null && _root.ClassListContains("setting-password");

        public bool Connecting => _pop != null && !_pop.ClassListContains("hidden");

        public string ConnectingError => Connecting && _pop.ClassListContains("failed") ? _popMain.text : null;

        public bool Typing =>
            _root?.focusController?.focusedElement is VisualElement focused
            && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);

        public string Error => _error != null && _error.ClassListContains("shown") ? _error.text : null;

        public static (float Speed, float JumpHeight) Stats(Shape shape, MovementRules rules) => (shape.MaxSpeed, rules.JumpApex(shape.JumpVelocity));

        public static string RefusalMessage(StringTable strings, JoinRejection reason) =>
            reason switch
            {
                JoinRejection.DifferentContent => strings.Get(Debug.isDebugBuild ? "join.different_content_dev" : "join.different_content"),
                JoinRejection.Busy => strings.Get("join.busy"),
                JoinRejection.Replaced => strings.Get("drop.replaced"),
                JoinRejection.NoCharacter => strings.Get("account.no_character"),
                JoinRejection.AccountsNeeded => strings.Get("join.accounts_needed"),
                JoinRejection.BadName => strings.Get("join.name_rules"),
                JoinRejection.NameOnline => strings.Get("join.name_online"),
                JoinRejection.NameTaken => strings.Get("join.bot_name"),
                _ => strings.Get("join.not_accepted"),
            };

        public static string LoginMessage(StringTable strings, string code, string name = "") =>
            code switch
            {
                LoginErrors.NameTaken => strings.Format("join.name_taken", ("username", name)),
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

        public static string LostMessage(StringTable strings, CloseReason reason) =>
            strings.Get(
                reason switch
                {
                    CloseReason.Lost => "connection.lost",
                    CloseReason.ServerClosed => "connection.server_closed",
                    CloseReason.ServerShutDown => "connection.server_shut_down",
                    CloseReason.FellBehind => "connection.fell_behind",
                    CloseReason.ProtocolError => "connection.protocol_error",
                    _ => "connection.failed",
                }
            );

        private void Awake()
        {
            _join = GetComponent<JoinClient>();
            _view = GetComponent<GameView>();
            _join.ContentLoaded += OnContentLoaded;
            _join.OwnSpawned += OnOwnSpawned;
            _join.JoinRefused += OnRefused;
            _join.LookRefused += OnLookRefused;
            _join.Failed += OnFailed;
            _join.RetryFailed += OnRetryFailed;
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
            _serverLine = tree.Q("server-line");
            _serverText = tree.Q<Label>("server-text");
            _serverChange = tree.Q<Button>("server-change");
            _serverChange.clicked += OpenServerField;
            _serverCancel = tree.Q<Button>("server-cancel");
            _serverCancel.clicked += CancelServerField;
            _server.RegisterValueChangedCallback(_ => ShowServer());
            _pageName = tree.Q<Label>("page-name");
            _pageBack = tree.Q<Button>("page-back");
            _pageBack.clicked += PageBack;
            _signIn = tree.Q("sign-in");
            _signInUsername = tree.Q<TextField>("sign-in-username");
            _signInPassword = tree.Q<TextField>("sign-in-password");
            _signInCode = tree.Q<TextField>("sign-in-code");
            _signInUsernameField = tree.Q("sign-in-username-field");
            _signInPasswordField = tree.Q("sign-in-password-field");
            _signInCodeField = tree.Q("sign-in-code-field");
            _useCode = tree.Q<Button>("sign-in-use-code");
            _useCode.clicked += () => SetWithCode(!_withCode);
            _signInButton = tree.Q<Button>("sign-in-button");
            _signInButton.clicked += () => SetSigningIn(true);
            _signOutButton = tree.Q<Button>("sign-out-button");
            _signOutButton.clicked += AskToSignOut;
            _setPassword = tree.Q<TextField>("set-password-field");
            _setPasswordAgain = tree.Q<TextField>("set-password-again");
            _setPasswordUsername = tree.Q<Label>("set-password-username");
            _whoName = tree.Q<Label>("who-name");
            _whoGuest = tree.Q("who-guest");
            _whoGuestNote = tree.Q("who-guest-note");
            _confirm = new ConfirmPanel(_root);
            _pop = tree.Q("join-pop");
            _popMain = tree.Q<Label>("pop-main");
            _popSub = tree.Q<Label>("pop-sub");
            _popDots = tree.Q("pop-dots").Children().ToArray();
            tree.Q<Button>("pop-cancel").clicked += CancelPop;
            tree.Q<Button>("pop-retry").clicked += () => JoinFromPop();
            _buttonsRow = _joinButton.parent;
            _scroll = tree.Q<ScrollView>("card-scroll");
            _nameRow = tree.Q("name-row");
            _pageRows = new VisualElement[]
            {
                _signInUsernameField,
                _signInPasswordField,
                _signInCodeField,
                _useCode,
                tree.Q("set-password-row"),
                tree.Q("set-password-again-row"),
            };
            _rows = _pageRows.Concat(_scroll.Query(className: "nav-row").ToList()).Append(_buttonsRow).Append(_serverLine).ToArray();
            for (var i = 0; i < _rows.Length; i++)
            {
                var row = i;
                if (_rows[i] != _buttonsRow && _rows[i] != _serverLine)
                {
                    _rows[i].RegisterCallback<PointerDownEvent>(_ => SelectRow(row), TrickleDown.TrickleDown);
                }
            }

            foreach (var item in AllItems())
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

        private void SetChanging(bool changing)
        {
            Changing = changing;
            if (changing)
            {
                _root.RemoveFromClassList("signing-in");
                SetSignedIn(false);
            }
            _root.EnableInClassList("changing", changing);
            _root.pickingMode = changing ? PickingMode.Ignore : PickingMode.Position;
            if (changing)
            {
                CloseServerField();
            }
            if (!changing)
            {
                _back = null;
            }

            _cancelButton.EnableInClassList("hidden", !changing || _back != null);
            _backButton.EnableInClassList("hidden", _back == null);
            MarkFieldRows();
            SetButton(true, MainButtonKey());
        }

        private string MainButtonKey() =>
            Changing ? "join.apply"
            : SigningIn ? "account.sign_in"
            : SettingPassword ? "account.save_password"
            : "join.play";

        private void SetButton(bool enabled, string key)
        {
            _busy = !enabled;
            _joinButton.SetEnabled(enabled);
            _joinButton.text = Text(key);
        }

        private void ShowStartZone(bool glide = true)
        {
            _view.Showcase(_shape, glide);
            _join.ShowZone(ShapeLandWorld.StartOf(_content, _shape).Zone);
        }

        private void Show()
        {
            OpenPreview();
            Refresh();
            _root.RemoveFromClassList("hidden");
            _shownOnFrame = Time.frameCount;
            SelectRow(FirstRow());
        }

        public bool Navigating
        {
            get => _root.ClassListContains("navigating");
            private set => _root.EnableInClassList("navigating", value);
        }

        public bool FromMenu => Changing && _back != null;

        public void OpenChange(Action back = null, bool navigating = false)
        {
            if (Showing || !_join.Joined || _join.Spawn is not PlayerSpawn spawn)
            {
                return;
            }

            Navigating = navigating;

            _back = back;
            SetChanging(true);
            _shape = _content.Shapes.TryGet(spawn.Shape, out var shape) ? shape : _content.Shapes.All[0];
            _color = spawn.Color;
            _eyeColor = spawn.EyeColor;
            ShowError(null);
            Show();
        }

        private void FocusName() => _focusNamePending = !WebPage.IsPhone;

        public void Back()
        {
            var back = FromMenu ? _back : null;
            Cancel();
            back?.Invoke();
        }

        public void Cancel()
        {
            if (Changing)
            {
                Close();
            }
        }

        private void Close()
        {
            _confirm.Close();
            _root.AddToClassList("hidden");
            _root.focusController?.focusedElement?.Blur();
            if (_preview != null)
            {
                Destroy(_preview.gameObject);
                _preview = null;
            }
            SetChanging(false);
        }

        private void OnContentLoaded(ShapeLandContent content)
        {
            _content = content;
            UiText.Fill(_root, content.Strings);
            UiText.Fill(_pop, content.Strings);
            SetChanging(false);
            _server.value = RememberedServer();
            ShowServer();
            StartFresh(glide: false);

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
                button.Add(new ShapeIcon(content.BodyKind(shape), Conversions.FromRgb(content.ColorRgb(_color))));
                button.Add(new Label(shape.DisplayName));
                _shapes.Add(button);
            }

            AddSwatches(_colors, color => _color = color);
            AddSwatches(_eyes, color => _eyeColor = color);
            if (!JoinClient.Accounts)
            {
                _root.Q<Label>(className: "have-account-text").text = Text("account.off_note");
                _signInButton.SetEnabled(false);
            }

            WebPage.TakeRejoin();
#if !UNITY_WEBGL || UNITY_EDITOR
            _join.UseLoginServer(ServerAddress());
#endif
            if (_join.SignedIn)
            {
                if (GameSettings.AutoJoin)
                {
                    JoinFromPop();
                }
                else
                {
                    ShowSignedInOnLaunch();
                }

                return;
            }

#if UNITY_EDITOR
            if (!JoinClient.Accounts && LastDevCharacter() is { } last)
            {
                int Known(int color) => _content.Colors.TryGet(color, out _) ? color : _content.Colors.All[0].Number;
                var (color, eyeColor) = (Known(last.Color), Known(last.EyeColor));
                var lastShape = _content.Shapes.TryGet(last.Shape, out var shape) ? shape : _content.Shapes.All[0];
                (_devName, _devLook) = (
                    last.Name,
                    new NewCharacter
                    {
                        Shape = lastShape.Number,
                        Color = color,
                        EyeColor = eyeColor,
                    }
                );
                if (GameSettings.AutoJoin)
                {
                    JoinFromPop();
                    return;
                }

                _name.value = last.Name;
                _shape = lastShape;
                (_color, _eyeColor) = (color, eyeColor);
                ShowStartZone(glide: false);
            }
#endif

            Show();
            FocusName();
        }

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

            if (SettingPassword)
            {
                await SavePassword();
                return;
            }

            if (Changing)
            {
                Apply();
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

            ShowError(null);
            _signInButton.RemoveFromClassList("pointed");
            SetButton(false, "join.joining");
            if (!JoinClient.Accounts)
            {
                (_devName, _devLook) = (
                    name,
                    new NewCharacter
                    {
                        Shape = _shape.Number,
                        Color = _color,
                        EyeColor = _eyeColor,
                    }
                );
                _join.DevJoin(_devName, _devLook);
                return;
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            _join.UseLoginServer(ServerAddress());
#endif
            var error = await _join.CreateGuestAsync(
                name,
                new NewCharacter
                {
                    Shape = _shape.Number,
                    Color = _color,
                    EyeColor = _eyeColor,
                }
            );
            if (error == LoginErrors.NameTaken)
            {
                _takenName = name;
                _signInButton.AddToClassList("pointed");
                Retry(_content.Strings.Format("join.name_taken_new", ("username", name)));
            }
            else if (error != null)
            {
                Retry(LoginMessage(_content.Strings, error, name));
            }
#if !UNITY_WEBGL || UNITY_EDITOR
            else
            {
                PlayerPrefs.SetString(ServerKey, ServerAddress());
                PlayerPrefs.Save();
            }
#endif
        }

        private void Apply()
        {
            var current = _join.Spawn.Value;
            if (_shape.Number == current.Shape && _color == current.Color && _eyeColor == current.EyeColor)
            {
                Close();
                return;
            }

            ShowError(null);
            SetButton(false, "join.applying");
            _join.ChangeLook(
                new LookChange
                {
                    Shape = _shape.Number,
                    Color = _color,
                    EyeColor = _eyeColor,
                }
            );
        }

        private void OnRefused(JoinRejection reason)
        {
            var rejoining = _rejoinUntil != null;
            _rejoinUntil = null;
            if (rejoining && reason == JoinRejection.DifferentContent && WebPage.Reload())
            {
                return;
            }

            ShowCardAgain(RefusalMessage(_content.Strings, reason));
        }

        private void OnLookRefused(LookRejection reason)
        {
            if (Changing)
            {
                Retry(Text(reason == LookRejection.TooFast ? "join.too_fast" : "join.not_accepted"));
            }
        }

        private void OnConnectionLost(CloseReason reason)
        {
            if (!JoinClient.Accounts)
            {
                SetChanging(false);
                if (reason == CloseReason.ServerShutDown)
                {
                    _rejoinUntil = Time.unscaledTime + RejoinSeconds;
                    _nextRejoin = Time.unscaledTime + 1;
                    JoinFromPop();
                    return;
                }

                if (reason == CloseReason.None)
                {
                    StartFresh();
                }

                ShowCardAgain(reason == CloseReason.None ? null : LostMessage(_content.Strings, reason));
                return;
            }

            if (!_join.SignedIn)
            {
                return;
            }

            SetChanging(false);
            if (_join.DroppedFor is DropReason dropped)
            {
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
                JoinFromPop();
                return;
            }

            ShowSignedIn(LostMessage(_content.Strings, reason));
        }

        private void Update()
        {
            if (Showing && WebPage.Playing && _content != null && Time.frameCount != _shownOnFrame)
            {
                Navigate(_view.Controls.Menu);
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

            var waiting = !WebPage.Playing;
            if (waiting != _root.ClassListContains("waiting"))
            {
                _root.EnableInClassList("waiting", waiting);
            }
            if (Connecting && WebPage.Playing && _content != null)
            {
                var menu = _view.Controls.Menu;
                if (menu.Back.WasPressedThisFrame())
                {
                    CancelPop();
                }
                else if (menu.Submit.WasPressedThisFrame() && _pop.ClassListContains("failed"))
                {
                    JoinFromPop();
                }
            }

            if (Connecting && !_pop.ClassListContains("failed") && Time.unscaledTime >= _popDotAt)
            {
                _popDotAt = Time.unscaledTime + 0.4f;
                _popDot = (_popDot + 1) % _popDots.Length;
                for (var i = 0; i < _popDots.Length; i++)
                {
                    _popDots[i].EnableInClassList("lit", i == _popDot);
                }
            }

            if (_rejoinUntil is not float until || !Connecting || _popBusy || Time.unscaledTime < _nextRejoin)
            {
                return;
            }

            if (Time.unscaledTime > until)
            {
                _rejoinUntil = null;
                FailPop(LostMessage(_content.Strings, CloseReason.ServerShutDown));
                return;
            }

            _nextRejoin = Time.unscaledTime + 1;
            JoinFromPop();
        }

        private void OnRetryFailed(string error)
        {
            if (_rejoinUntil != null)
            {
                _popBusy = false;
                return;
            }

            var message = LoginMessage(_content.Strings, error);
            if (Connecting)
            {
                FailPop(message);
            }
            else if (_join.SignedIn)
            {
                ShowSignedIn(message);
            }
            else
            {
                Retry(message);
            }
        }

        private void OnFailed(JoinFailure failure)
        {
            if (_content == null)
            {
                return;
            }

            if (_rejoinUntil != null && failure != JoinFailure.BadAddress)
            {
                _popBusy = false;
                return;
            }

            var message = failure switch
            {
                JoinFailure.BadAddress => _content.Strings.Format("join.bad_address", ("address", _join.Error ?? "")),
                JoinFailure.Unanswered => Text("join.unanswered"),
                _ when Application.platform == RuntimePlatform.WebGLPlayer => Text("join.unreachable_web"),
                _ => _content.Strings.Format(
                    "join.unreachable",
                    ("address", JoinClient.Accounts ? _join.LoginAddress : $"localhost:{Comet.Unity.LocalServerPort.InEditor ?? 5080}")
                ),
            };
            if (Connecting)
            {
                FailPop(message);
            }
            else if (_join.SignedIn)
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
            SetButton(true, MainButtonKey());
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
            HidePop();
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
            _join.RetryFailed -= OnRetryFailed;
            _join.ConnectionLost -= OnConnectionLost;
            _join.SignedOut -= OnSignedOut;
            if (_preview != null)
            {
                Destroy(_preview.gameObject);
            }
        }
    }
}

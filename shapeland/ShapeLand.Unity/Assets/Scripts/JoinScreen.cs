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
    /// makes a guest account. The card's Sign in page signs in to an account made elsewhere, with a password or a
    /// recovery code. A returning player (a kept session token) goes straight into the game behind a small connecting
    /// pop-up, unless Join on launch is off. A signed-in player not playing (after leaving, a drop, a cancelled join)
    /// gets the signed-in card: who they are beside their shape, Play, and Sign out (a guest is warned, and can set a
    /// password on the card first). Refusals and connection failures show on the card, which stays up for another
    /// try; it hides once the player spawns. When the server shuts down (as a restart does), the pop-up tries joining
    /// again for a minute; if the server came back with new content, a web page reloads to load it and joins again by
    /// itself. Desktop builds also have a server field (the Login server); a web page uses the one its config names.
    /// </summary>
    [RequireComponent(typeof(UIDocument), typeof(JoinClient), typeof(GameView))]
    public sealed class JoinScreen : MonoBehaviour
    {
        [SerializeField]
        private ShapePreview previewPrefab;

        // The PlayerPrefs key for the server remembered for next time.
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

        // Where Back goes, when the card was opened from the Esc menu.
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

        // The address when Change was pressed, for Cancel to put back.
        private string _serverBefore;
        private Label _pageName;
        private VisualElement _signIn;

        // With accounts off, the last join's name and look, for joining again after the server restarts.
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

        // A new player's name that's taken: the card points them to Sign in, filled in with it.
        private string _takenName;
        private bool _busy;

        // The connecting pop-up: each join from it counts, so a cancelled one's answer is ignored; the dots step.
        private VisualElement _pop;
        private Label _popMain;
        private Label _popSub;
        private VisualElement[] _popDots;
        private int _popAttempt;
        private bool _popBusy;
        private int _popDot;
        private float _popDotAt;
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
        private readonly MenuNavigation _navigation = new();
        private int _shownOnFrame;

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

        /// <summary>The screen's root, for tests.</summary>
        public VisualElement Root => _root;

        /// <summary>True while the card is up.</summary>
        public bool Showing => _root != null && !_root.ClassListContains("hidden");

        /// <summary>True while the card is up to change the look of a player already in the game.</summary>
        public bool Changing { get; private set; }

        /// <summary>True while the card shows the sign-in panel instead of the new player's fields.</summary>
        public bool SigningIn => _root != null && _root.ClassListContains("signing-in");

        /// <summary>True while the card is up for a signed-in player who isn't playing: who they are, and Play.</summary>
        public bool SignedInCard => _root != null && _root.ClassListContains("signed-in");

        /// <summary>True while the card shows its Set password page (from the signed-in card).</summary>
        public bool SettingPassword => _root != null && _root.ClassListContains("setting-password");

        /// <summary>True while the connecting pop-up is up (joining on launch or after a restart).</summary>
        public bool Connecting => _pop != null && !_pop.ClassListContains("hidden");

        /// <summary>The pop-up's message after a join from it failed, or null.</summary>
        public string ConnectingError => Connecting && _pop.ClassListContains("failed") ? _popMain.text : null;

        /// <summary>True while a text field on the card has the keyboard.</summary>
        public bool Typing =>
            _root?.focusController?.focusedElement is VisualElement focused
            && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);

        /// <summary>The message on the card, or null when there is none.</summary>
        public string Error => _error != null && _error.ClassListContains("shown") ? _error.text : null;

        /// <summary>The chosen shape's speed (m/s) and jump height (m), as the card shows them.</summary>
        public static (float Speed, float JumpHeight) Stats(Shape shape, MovementRules rules) => (shape.MaxSpeed, rules.JumpApex(shape.JumpVelocity));

        /// <summary>What the card says when the server refuses a join.</summary>
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

        /// <summary>What the card says for a Login server error code (<see cref="LoginErrors"/>).</summary>
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

        /// <summary>What the card says after the connection was lost for <paramref name="reason"/>.</summary>
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
            // Rows for keys and a gamepad, top to bottom: the Sign In and Set Password pages' rows, the new player's
            // rows (name, shape, color, eyes), the buttons, and the server line; only those in sight are reached.
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

        // The Sign in page in place of the new player's rows, or back; filled in with a taken name it was pointed to from.
        private void SetSigningIn(bool signingIn)
        {
            _root.EnableInClassList("signing-in", signingIn);
            _signInButton.RemoveFromClassList("pointed");
            MarkFieldRows();
            SetButton(true, signingIn ? "account.sign_in" : "join.play");
            ShowError(null);
            if (signingIn)
            {
                _signInUsername.value = _takenName ?? _signInUsername.value;
                _takenName = null;
                SetWithCode(false);
                (_signInUsername.value.Length > 0 ? _signInPassword : _signInUsername).Focus();
            }
        }

        // A recovery code in place of the username and password, or back.
        private void SetWithCode(bool withCode)
        {
            _withCode = withCode;
            _signIn.EnableInClassList("with-code", withCode);
            MarkFieldRows();
            _useCode.text = Text(withCode ? "account.use_password" : "account.use_code");
            _pageName.text = Text(withCode ? "account.sign_in_code_page" : "account.sign_in");
        }

        // The signed-in card's Set password page (a guest keeping their character), or back to the card.
        private void SetSettingPassword(bool setting)
        {
            _root.EnableInClassList("setting-password", setting);
            if (setting)
            {
                _root.RemoveFromClassList("signed-in");
                _pageName.text = Text("account.set_password");
                _setPasswordUsername.text = _join.PlayerName;
                _setPassword.value = "";
                _setPasswordAgain.value = "";
                CloseServerField();
                MarkFieldRows();
                SetButton(true, "account.save_password");
                ShowError(null);
                _setPassword.Focus();
                return;
            }

            SetSignedIn(true);
        }

        // Back on the card's own pages.
        private void PageBack()
        {
            if (SigningIn)
            {
                SetSigningIn(false);
            }
            else if (SettingPassword)
            {
                SetSettingPassword(false);
            }
        }

        // The signed-in card: who the player is, beside their shape as it was last saved, in place of the new player's rows.
        private void SetSignedIn(bool signedIn)
        {
            _root.EnableInClassList("signed-in", signedIn);
            _root.RemoveFromClassList("setting-password");
            if (signedIn)
            {
                var account = _join.Account;
                _whoName.text = account?.Username ?? "";
                var guest = account?.Guest ?? false;
                _whoGuest.EnableInClassList("hidden", !guest);
                _whoGuestNote.EnableInClassList("hidden", !guest);
                if (account?.Character is CharacterView character && _content.Shapes.TryGet(character.Shape, out var shape))
                {
                    _shape = shape;
                    _color = character.Color;
                    _eyeColor = character.EyeColor;
                }

                SetButton(true, "join.play");
            }

            MarkFieldRows();
        }

        // Keys and gamepad reach only the rows in sight, so each row is hidden or shown here with its page: the new
        // player's rows (Change Shape has no name row), the Sign In page's (a code signs in on its own, with no username
        // or password), the Set Password page's, and the server line (desktop builds, not while changing the look or
        // setting a password). Then the selection moves to the top row in sight.
        private void MarkFieldRows()
        {
            var page = SigningIn || SettingPassword;
            foreach (var row in _scroll.Query(className: "nav-row").ToList())
            {
                row.EnableInClassList("hidden", page || SignedInCard || (row == _nameRow && Changing));
            }

            _signInUsernameField.EnableInClassList("hidden", !SigningIn || _withCode);
            _signInPasswordField.EnableInClassList("hidden", !SigningIn || _withCode);
            _signInCodeField.EnableInClassList("hidden", !SigningIn || !_withCode);
            _useCode.EnableInClassList("hidden", !SigningIn);
            _pageRows[4].EnableInClassList("hidden", !SettingPassword);
            _pageRows[5].EnableInClassList("hidden", !SettingPassword);
#if UNITY_WEBGL && !UNITY_EDITOR
            _serverLine.AddToClassList("hidden");
#else
            _serverLine.EnableInClassList("hidden", Changing || SettingPassword || !JoinClient.Accounts);
#endif
            if (_rows != null && !Usable(_rows[_row]))
            {
                SelectRow(FirstRow());
            }
        }

        // Changing the look: no server to pick, and the card can be closed without changing anything.
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

            // Opened from the Esc menu, Back stands in for Cancel.
            _cancelButton.EnableInClassList("hidden", !changing || _back != null);
            _backButton.EnableInClassList("hidden", _back == null);
            MarkFieldRows();
            SetButton(true, MainButtonKey());
        }

        // What the card's main button says when it's ready.
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
            SelectRow(FirstRow());
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
            _confirm.Close();
            _root.AddToClassList("hidden");
            _root.focusController?.focusedElement?.Blur(); // or Enter and the controller's submit button still reach the hidden card
            if (_preview != null)
            {
                Destroy(_preview.gameObject);
                _preview = null;
            }
            SetChanging(false);
        }

        // The server line's Change: the field in place of the line's text, for keys and gamepad too.
        private void OpenServerField()
        {
            _serverBefore = _server.value;
            _serverLine.AddToClassList("open");
            _serverField.RemoveFromClassList("hidden");
            _server.Focus();
        }

        private void CancelServerField()
        {
            _server.value = _serverBefore;
            CloseServerField();
        }

        private void CloseServerField()
        {
            _serverLine.RemoveFromClassList("open");
            _serverField.AddToClassList("hidden");
        }

        private void ShowServer()
        {
            if (_content != null)
            {
                _serverText.text = _content.Strings.Format("join.server_line", ("address", ServerAddress()));
            }
        }

        // The Login server in the card, or the default one when it's empty.
        private string ServerAddress() => string.IsNullOrWhiteSpace(_server.value) ? _join.DefaultLoginAddress : _server.value.Trim();

        // The Login server last used, else the default. In the editor, one on this computer means its local Login
        // server, on whatever port that uses now (Comet › Server); one elsewhere (a teammate's) stays as it was.
        private string RememberedServer()
        {
            var remembered = PlayerPrefs.GetString(ServerKey, "");
#if UNITY_EDITOR
            if (
                Comet.Unity.LocalServerPort.LoginInEditor != null
                && Uri.TryCreate(JoinClient.LoginUrl(remembered), UriKind.Absolute, out var url)
                && url.IsLoopback
            )
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
                button.Add(new ShapeIcon(content.BodyKind(shape), GameView.Color(content.ColorRgb(_color))));
                button.Add(new Label(shape.DisplayName));
                _shapes.Add(button);
            }

            AddSwatches(_colors, color => _color = color);
            AddSwatches(_eyes, color => _eyeColor = color);
            if (!JoinClient.Accounts)
            {
                // The editor's own server, joined by name: no Login server to sign in to or pick.
                _root.Q<Label>(className: "have-account-text").text = Text("account.off_note");
                _signInButton.SetEnabled(false);
            }

            WebPage.TakeRejoin(); // a returning player resumes below anyway
#if !UNITY_WEBGL || UNITY_EDITOR
            _join.UseLoginServer(ServerAddress()); // the one remembered in the card, with the token kept for it
#endif
            if (_join.SignedIn)
            {
                if (GameSettings.AutoJoin)
                {
                    JoinFromPop(); // a returning player goes straight into the game
                }
                else
                {
                    ShowSignedInOnLaunch();
                }

                return;
            }

#if UNITY_EDITOR
            // Without accounts, a designer comes back as the character they played last on the editor's server.
            if (!JoinClient.Accounts && LastDevCharacter() is { } last)
            {
                // A shape or color since removed from the content falls back to the first.
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
                ShowStartZone(glide: false); // Show, below, draws the card with them
            }
#endif

            Show();
            FocusName();
        }

#if UNITY_EDITOR
        /// <summary>The editor server's character file to come back from without accounts; null for its own, "" for none (tests).</summary>
        public static string DevCharactersFile { get; set; }

        // The editor's server's characters without accounts, as it keeps them (ShapeLand.GameServer's DevCharacters).
        [Serializable]
        private sealed class DevFile
        {
            public DevCharacter[] Characters = new DevCharacter[0];
            public DevStatus[] Statuses = new DevStatus[0];
        }

        [Serializable]
        private sealed class DevCharacter
        {
            public long Id;
            public long AccountId;
            public string Name = "";
            public int Shape,
                Color,
                EyeColor;
            public bool IsDeleted;
        }

        [Serializable]
        private sealed class DevStatus
        {
            public long CharacterId;
            public string UpdatedAt = "";
        }

        // The character last played on the editor's server (the latest status saved), or null for none or no file.
        // The editor server's account for people's characters (ShapeLand.GameServer's DevCharacters.AccountId).
        private const long PeopleAccount = 1;

        private static DevCharacter LastDevCharacter()
        {
            try
            {
                var path = DevCharactersFile ?? Comet.Unity.LocalServerPort.DevCharactersFile;
                if (path.Length == 0 || !System.IO.File.Exists(path))
                {
                    return null;
                }

                var file = JsonUtility.FromJson<DevFile>(System.IO.File.ReadAllText(path));
                // People's characters only: the editor's bots have their own account.
                var live = file?.Characters?.Where(c => !c.IsDeleted && c.Name.Length > 0 && c.AccountId == PeopleAccount).ToList();
                if (live == null || live.Count == 0)
                {
                    return null;
                }

                var latest = file
                    .Statuses?.Where(st => live.Any(c => c.Id == st.CharacterId))
                    .OrderByDescending(st =>
                        DateTime.TryParse(st.UpdatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : DateTime.MinValue
                    )
                    .FirstOrDefault();
                return latest != null ? live.First(c => c.Id == latest.CharacterId) : live[live.Count - 1];
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Couldn't read the editor server's characters ({e.Message}); starting with a new one.");
                return null;
            }
        }
#endif

        // A returning player, joining behind the small pop-up (the card stays down); on failure the pop-up says why, with
        // Retry, and Cancel brings up the signed-in card. A refused token shows the new player's card (through SignedOut).
        private async void JoinFromPop()
        {
            var attempt = ++_popAttempt;
            _popBusy = true;
            if (!JoinClient.Accounts)
            {
                _join.DevJoin(_devName, _devLook);
                ShowPop();
                return;
            }

            ShowPop();
            var error = _join.Account == null ? await _join.LoadAccountAsync() : null;
            if (attempt != _popAttempt)
            {
                return;
            }

            if (error == null)
            {
                ShowPop(); // with the name, now that the account has loaded
                error = await _join.PlayAsync();
                if (attempt != _popAttempt)
                {
                    return;
                }
            }

            if (error == null)
            {
                return; // connecting; the spawn, a refusal or a failure comes next
            }

            _popBusy = false;
            if (error == LoginErrors.SignedOut)
            {
                HidePop();
            }
            else if (_rejoinUntil == null)
            {
                FailPop(LoginMessage(_content.Strings, error));
            }
        }

        // Join on launch off: the signed-in card, once the account has loaded.
        private async void ShowSignedInOnLaunch()
        {
            var error = await _join.LoadAccountAsync();
            if (error != LoginErrors.SignedOut)
            {
                ShowSignedIn(error == null ? null : LoginMessage(_content.Strings, error));
            }
        }

        private void ShowPop()
        {
            var name = _join.PlayerName;
            _popMain.text = Text(name.Length > 0 ? "account.joining_as" : "account.joining");
            _popSub.text = Text("account.connecting");
            _pop.RemoveFromClassList("failed");
            _pop.RemoveFromClassList("hidden");
        }

        private void FailPop(string message)
        {
            _popBusy = false;
            _popMain.text = message;
            _popSub.text = "";
            _pop.AddToClassList("failed");
            _pop.RemoveFromClassList("hidden");
        }

        private void HidePop()
        {
            _popAttempt++;
            _popBusy = false;
            _pop.AddToClassList("hidden");
        }

        // The pop-up's Cancel: stops the join and brings up the signed-in card.
        private void CancelPop()
        {
            _rejoinUntil = null;
            HidePop();
            _join.CancelJoin();
            ShowCardAgain(null);
        }

        // Signing out from the signed-in card asks first; a guest would lose the character, so Set password comes first.
        private void AskToSignOut()
        {
            var strings = _content.Strings;
            if (_join.Account?.Guest ?? true)
            {
                _confirm.Show(
                    strings.Get("account.sign_out_guest_title"),
                    strings.Get("account.sign_out_guest_note"),
                    null,
                    (Text("account.set_password"), false, () => SetSettingPassword(true)),
                    (Text("account.stay"), false, null),
                    (Text("account.sign_out"), true, SignOut)
                );
            }
            else
            {
                _confirm.Show(
                    Text("account.sign_out_title"),
                    Text("account.sign_out_note"),
                    null,
                    (Text("account.sign_out"), true, SignOut),
                    (Text("account.stay"), false, null)
                );
            }
        }

        private async void SignOut() => await _join.SignOutAsync(); // SignedOut brings up the new player's card

        // After a join that didn't happen or a game that ended: the signed-in card, or with accounts off (no account to
        // show), the new player's card with its choices kept.
        private void ShowCardAgain(string message)
        {
            if (JoinClient.Accounts)
            {
                ShowSignedIn(message);
                return;
            }

            if (Changing)
            {
                Close();
            }

            HidePop();
            SetSignedIn(false);
            Show();
            Retry(message);
        }

        // The card for a signed-in player who isn't playing, with a message: off the Sign In page, if it was last on it.
        private void ShowSignedIn(string message)
        {
            if (Changing)
            {
                Close();
            }

            HidePop();
            SetSigningIn(false);
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

            HidePop();
            _confirm.Close();
            StartFresh();
            SetSignedIn(false);
            SetSigningIn(false);
            Show();
        }

        // The new player's card as for someone new: no name, the first shape and colors.
        private void StartFresh(bool glide = true)
        {
            _name.value = "";
            _shape = _content.Shapes.All[0];
            _color = _eyeColor = _content.Colors.All[0].Number;
            ShowStartZone(glide);
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
            _stats.Add(
                Stat(strings.Get("join.speed"), strings.Format("join.speed_value", ("speed", speed.ToString("0.#"))), speed / best.Max(s => s.Speed))
            );
            _stats.Add(
                Stat(
                    strings.Get("join.jump"),
                    strings.Format("join.jump_value", ("height", jump.ToString("0.0"))),
                    jump / best.Max(s => s.JumpHeight)
                )
            );
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

        // Enter in a field joins (applies); elsewhere on the card Enter is the Submit action's (Navigate). Tab and
        // Shift+Tab move to the next or last row, from a field too.
        private void OnKeyDown(KeyDownEvent e)
        {
            if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && Typing)
            {
                Submit();
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Tab && !_confirm.Open)
            {
                Navigating = true;
                _root.focusController?.focusedElement?.Blur();
                MoveRow(e.shiftKey ? -1 : 1);
                FocusRowField();
                e.StopPropagation();
            }
        }

        // With a keyboard, landing on a text field's row goes straight into typing; Up, Down and Tab still leave it.
        private void FocusRowField()
        {
            if (!_onBack && _rows[_row].Q<TextField>() is { } field && Usable(_rows[_row]))
            {
                field.Focus();
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
                _join.DevJoin(_devName, _devLook); // the spawn, a refusal or a failure comes next
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
                // Probably the player's own account from another device: Sign in, filled in with the name.
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
                PlayerPrefs.SetString(ServerKey, ServerAddress()); // as for a sign-in: the guest's token is kept under this server
                PlayerPrefs.Save();
            }
#endif
        }

        // The Change Shape card: the look goes to the game server, and the card closes on the spawn it brings.
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

        // The signed-in card's Set password page; back to the card once it's saved.
        private async System.Threading.Tasks.Task SavePassword()
        {
            if (_setPassword.value != _setPasswordAgain.value)
            {
                ShowError(Text("account.passwords_differ"));
                return;
            }

            SetButton(false, "account.saving");
            ShowError(null);
            var error = await _join.SetPasswordAsync(_setPassword.value);
            _setPassword.value = "";
            _setPasswordAgain.value = "";
            if (error != null)
            {
                ShowError(LoginMessage(_content.Strings, error));
                SetButton(true, "account.save_password");
                return;
            }

            SetSettingPassword(false);
        }

        // The Sign in page: a username and password, or a recovery code alone.
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

#if !UNITY_WEBGL || UNITY_EDITOR
            PlayerPrefs.SetString(ServerKey, ServerAddress()); // the next launch goes back to the server signed in to
            PlayerPrefs.Save();
#endif
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

            ShowCardAgain(RefusalMessage(_content.Strings, reason)); // the pop-up gives way to the card, which says why
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
            if (!JoinClient.Accounts)
            {
                SetChanging(false);
                if (reason == CloseReason.ServerShutDown)
                {
                    // The editor's server restarting (for new content, say): back in as the same name, where it stood.
                    _rejoinUntil = Time.unscaledTime + RejoinSeconds;
                    _nextRejoin = Time.unscaledTime + 1;
                    JoinFromPop();
                    return;
                }

                if (reason == CloseReason.None)
                {
                    StartFresh(); // left the game; a lost connection keeps the choices, to join again
                }

                ShowCardAgain(reason == CloseReason.None ? null : LostMessage(_content.Strings, reason));
                return;
            }

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
                JoinFromPop();
                return;
            }

            ShowSignedIn(LostMessage(_content.Strings, reason));
        }

        // While rejoining, the pop-up tries again each second; its dots step meanwhile.
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
            // The pop-up with keys or a gamepad: Back cancels, Submit retries after a failure.
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

        // Keys and gamepad on the card, with the Esc menu's Menu actions: up and down move between rows, left and right
        // pick within one (or between the buttons), Submit types into a field, presses a button or joins (applies) from
        // the other rows, and Back stops typing, else goes back or cancels. Typing needs a keyboard; while a field has
        // it, only the gamepad moves on (and the keys type).
        private void Navigate(ShapeLandControls.MenuActions actions)
        {
            if (_confirm.Open)
            {
                NavigateConfirm(actions);
                return;
            }

            var typing = Typing;
            var fromPad = actions.Navigate.activeControl?.device is Gamepad;
            var step = _navigation.Step(typing && !fromPad ? MenuNavigation.TypingArrows() : actions.Navigate.ReadValue<Vector2>());
            if (!Navigating && step != Vector2Int.zero && !typing)
            {
                Navigating = true; // the first press only shows where the selection is (leaving a field moves at once)
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
                if (!fromPad)
                {
                    FocusRowField();
                }
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
                else if (SigningIn || SettingPassword)
                {
                    PageBack();
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

        // The sign-out question: up and down choose, Submit picks, Back stays.
        private void NavigateConfirm(ShapeLandControls.MenuActions actions)
        {
            var step = _navigation.Step(actions.Navigate.ReadValue<Vector2>());
            if (step.y != 0)
            {
                Navigating = true;
                _confirm.Move(step.y);
            }

            if (actions.Back.WasPressedThisFrame())
            {
                _confirm.Cancel();
            }
            else if (actions.Submit.WasPressedThisFrame())
            {
                Navigating = true;
                _confirm.Choose();
            }
        }

        private void MoveRow(int by)
        {
            if (_onBack)
            {
                if (by > 0)
                {
                    SelectRow(FirstRow());
                }

                return;
            }

            if (by < 0 && _row == FirstRow() && CurrentBack != null)
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

        // The top row in sight: the name, or the shape when changing the look.
        private int FirstRow() => Math.Max(0, Array.FindIndex(_rows, Usable));

        // Back above the top row: the card's own pages', or the look card's when it came from the Esc menu.
        private Button CurrentBack =>
            SigningIn || SettingPassword ? _pageBack
            : FromMenu ? _backButton
            : null;

        private void SelectBack()
        {
            SelectRow(FirstRow());
            _onBack = true;
            _rows[_row].RemoveFromClassList("selected");
            MarkItem();
            CurrentBack?.AddToClassList("selected");
        }

        private void SelectRow(int row)
        {
            _onBack = false;
            _backButton.RemoveFromClassList("selected");
            _pageBack.RemoveFromClassList("selected");
            _row = row;
            for (var i = 0; i < _rows.Length; i++)
            {
                _rows[i].EnableInClassList("selected", i == row && Items(_rows[i]).Count == 0);
            }

            // The buttons start on the main one (Play, Apply, Sign In); the server line on its first item.
            _buttonIndex = _rows[row] == _buttonsRow ? Items(_buttonsRow).IndexOf(_joinButton) : 0;
            MarkItem();
        }

        // A row's items, left to right, for left and right to move between: the buttons in sight (Sign In or Sign Out,
        // Cancel, then the main one), and the server line's Change, or its field and Cancel once open. None for other rows.
        private List<VisualElement> Items(VisualElement row)
        {
            if (row == _buttonsRow)
            {
                var signIn = !Changing && !SigningIn && !SignedInCard && !SettingPassword;
                return new VisualElement[] { signIn ? _signInButton : null, SignedInCard ? _signOutButton : null, _cancelButton, _joinButton }
                    .Where(item => item != null && Usable(item))
                    .ToList();
            }

            if (row == _serverLine)
            {
                return _serverLine.ClassListContains("open")
                    ? new List<VisualElement> { _serverField, _serverCancel }
                    : new List<VisualElement> { _serverChange };
            }

            return new List<VisualElement>();
        }

        private VisualElement[] AllItems() =>
            new VisualElement[] { _signInButton, _signOutButton, _cancelButton, _joinButton, _serverChange, _serverField, _serverCancel };

        private void SelectItem(VisualElement item)
        {
            var row = item == _serverChange || item == _serverField || item == _serverCancel ? _serverLine : _buttonsRow;
            _onBack = false;
            _row = Array.IndexOf(_rows, row);
            foreach (var other in _rows)
            {
                other.RemoveFromClassList("selected");
            }

            _buttonIndex = Mathf.Max(0, Items(row).IndexOf(item));
            MarkItem();
        }

        private void MarkItem()
        {
            var items = _onBack ? new List<VisualElement>() : Items(_rows[_row]);
            foreach (var item in AllItems())
            {
                item.EnableInClassList("selected", items.IndexOf(item) == _buttonIndex && items.Contains(item));
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

            var items = Items(row);
            if (items.Count > 0)
            {
                _buttonIndex = Mathf.Clamp(_buttonIndex + by, 0, items.Count - 1);
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
                if (SigningIn || SettingPassword)
                {
                    PageBack();
                }
                else
                {
                    Back();
                }
            }
            else if (Items(row) is { Count: > 0 } items)
            {
                var item = items[Mathf.Clamp(_buttonIndex, 0, items.Count - 1)];
                if (item == _serverField)
                {
                    _server.Focus();
                }
                else if (item == _joinButton)
                {
                    Submit();
                }
                else if (item == _cancelButton)
                {
                    Cancel();
                }
                else if (item == _signInButton)
                {
                    SetSigningIn(true);
                }
                else if (item == _signOutButton)
                {
                    AskToSignOut();
                }
                else if (item == _serverChange)
                {
                    OpenServerField();
                }
                else if (item == _serverCancel)
                {
                    CancelServerField();
                }
            }
            else if (row == _useCode)
            {
                SetWithCode(!_withCode);
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

        // The join's retry with a fresh ticket couldn't get one: said where the join was asked from.
        private void OnRetryFailed(string error)
        {
            if (_rejoinUntil != null)
            {
                _popBusy = false; // not back yet: the pop-up tries again
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
                return; // the content didn't load; nothing to show the card with
            }

            if (_rejoinUntil != null && failure != JoinFailure.BadAddress)
            {
                _popBusy = false; // not back yet: the pop-up tries again
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
                Destroy(_preview.gameObject); // its own object, so it doesn't go with the card
            }
        }
    }
}

using Comet.Client;
using Comet.Unity;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class Hud : MonoBehaviour
    {
        [SerializeField]
        private JoinClient join;

        [SerializeField]
        private GameView view;

        [SerializeField]
        private JoinScreen card;

        private VisualElement _root;
        private Chat _chat;
        private TouchControls _touch;
        private NetStats _stats;
        private CollisionDebug _collisions;
        private GameMenu _menu;
        private AccountPage _account;
        private VisualElement _guestReminder;
        private float _guestReminderUntil;
        private VisualElement _lookButton;
        private VisualElement _menuButton;
        private bool _menuTipSeen;

        private bool _reopenChat;
        private string _chatDraft;

        private VisualElement[] _safe;
        private VisualElement[] _centeredSafe;
        private Vector4 _insets = new Vector4(-1, -1, -1, -1);

        private Vector4 _safeFor;

        public JoinClient Join => join;

        public NetStats Stats => _stats;

        public CollisionDebug Collisions => _collisions;

        public GameView View => view;

        public VisualElement Root => _root;

        public bool Shown => _root != null && !_root.ClassListContains("hidden");

        public bool CardShowing => card.Showing;

        public bool MenuBlocking => _menu != null && (_menu.Blocking || _account.Open);

        public void OpenAccount() => _account.Show(() => _menu.Show(GameMenu.AccountEntry, navigating: _account.Navigating), _menu.Navigating);

        private static readonly Vector2Int PhoneReferenceResolution = new Vector2Int(800, 500);

        // The panel settings' own design size, put back on disabling: in the editor a change to the asset would outlast
        // play mode.
        private Vector2Int? _referenceResolution;

        private void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            if (WebPage.IsPhone && document.panelSettings != null)
            {
                _referenceResolution = document.panelSettings.referenceResolution;
                document.panelSettings.referenceResolution = PhoneReferenceResolution;
            }

            var tree = document.rootVisualElement;
            _root = tree.Q("hud");
            _chat = GetComponent<Chat>();
            _stats = GetComponent<NetStats>();
            _collisions = GetComponent<CollisionDebug>();
            _lookButton = tree.Q("look-button");
            ((Button)_lookButton).clicked += ToggleLook;
            _menuButton = tree.Q("menu-button");
            ((Button)_menuButton).clicked += ToggleMenu;
            _root.EnableInClassList("phone", WebPage.IsPhone);
            tree.Q<Button>("return-button").clicked += () =>
            {
                CloseLook();
                ReturnToSpawn();
            };
            tree.Q<Button>("chat-button").clicked += () =>
            {
                CloseLook();
                _chat.Toggle();
            };
            _menuTipSeen = GameSettings.MenuTipSeen;
            tree.Query(className: "hud-tip").ForEach(tip => tip.parent.RegisterCallback<PointerEnterEvent>(_ => MarkMenuTipSeen()));
            var cardSafe = card.GetComponent<UIDocument>().rootVisualElement.Q("card-safe");
            _safe = new[] { tree.Q("safe"), tree.Q("touch-safe") };
            _centeredSafe = new[] { tree.Q("menu-safe"), tree.Q("account-safe"), cardSafe };
            _touch = new TouchControls(_root, view.OrbitCamera);
            _menu = new GameMenu(this, tree);
            _account = new AccountPage(this, tree);
            _guestReminder = tree.Q("guest-reminder");
            tree.Q<Button>("guest-reminder-set").clicked += () =>
            {
                HideGuestReminder();
                _menu.Close();
                _account.ShowSetPassword(() => _menu.Show(GameMenu.AccountEntry, navigating: false));
            };
            tree.Q<Button>("guest-reminder-dismiss").clicked += HideGuestReminder;
            GameMenu.TakeOverNavigation(_root);

            join.ContentLoaded += OnContentLoaded;
            join.OwnSpawned += OnOwnSpawned;
            join.ConnectionLost += OnConnectionLost;
            if (join.Content != null)
            {
                OnContentLoaded(join.Content);
            }
        }

        private void OnDisable()
        {
            join.ContentLoaded -= OnContentLoaded;
            join.OwnSpawned -= OnOwnSpawned;
            join.ConnectionLost -= OnConnectionLost;
            _touch?.Dispose();
            _touch = null;
            if (_referenceResolution is { } reference && GetComponent<UIDocument>().panelSettings is { } panel)
            {
                panel.referenceResolution = reference;
                _referenceResolution = null;
            }
        }

        private void OnContentLoaded(ShapeLandContent content) => UiText.Fill(_root, content.Strings);

        private void OnOwnSpawned(PlayerSpawn spawn)
        {
            _root.RemoveFromClassList("hidden");
            if (GuestReminder.Due(join.Account))
            {
                _guestReminder.Q<Label>("guest-reminder-text").text = join.Content.Strings.Get("account.reminder");
                _guestReminder.RemoveFromClassList("hidden");
                _guestReminderUntil = Time.unscaledTime + GuestReminder.ShowSeconds;
            }
        }

        private void HideGuestReminder()
        {
            _guestReminderUntil = 0;
            _guestReminder.AddToClassList("hidden");
        }

        private void OnConnectionLost(CloseReason reason)
        {
            _menu.Close();
            _account.Close();
            HideGuestReminder();
            _root.AddToClassList("hidden");
        }

        private void Update()
        {
            _touch?.Update(Shown && view.Player != null && view.Player.InputEnabled, _menu.Open);
            if (_touch != null && card.Root != null)
            {
                card.Root.EnableInClassList("touch-input", _touch.Input == InputKind.Touch);
                card.Root.EnableInClassList("pad-input", _touch.Input == InputKind.Gamepad);
            }

            if (WebPage.IsPhone)
            {
                KeepToSafeArea();
            }

            if (!Shown)
            {
                return;
            }

            _lookButton.EnableInClassList("on", card.Changing);
            _menuButton.EnableInClassList("on", _menu.Open);
            _root.EnableInClassList("look-open", card.Changing);
            _root.EnableInClassList("account-open", _account.Open);
            _menuButton.EnableInClassList("first-time", !_menuTipSeen && !_menu.Open && !CardShowing);
            _chat.Box.EnableInClassList("hidden", card.Changing);
            _stats.Panel.EnableInClassList("hidden", !_stats.Shown || card.Changing);
            ReopenChat();

            if (_guestReminderUntil > 0 && Time.unscaledTime > _guestReminderUntil)
            {
                HideGuestReminder();
            }

            var menu = view.Controls.Game.Menu;
            if (menu.WasPressedThisFrame() && _account.Open)
            {
                _account.Back();
            }
            else if (menu.WasPressedThisFrame() && !_chat.Open && !_chat.JustToggled)
            {
                var startAppliesLook = card.Changing && menu.activeControl is not KeyControl;
                if (!startAppliesLook)
                {
                    if (card.FromMenu)
                    {
                        _reopenChat = false;
                        card.Back();
                    }
                    else if (card.Changing)
                    {
                        CloseLook();
                    }
                    else if (!_menu.Open)
                    {
                        _menu.Show();
                    }
                    else if (menu.activeControl is KeyControl)
                    {
                        _menu.Back();
                    }
                    else
                    {
                        _menu.Close();
                    }
                }
            }
            else if (_account.Open)
            {
                _account.Update(view.Controls.Menu);
            }
            else
            {
                _menu.Update(view.Controls.Menu, _touch.Input);
            }

            if (!_menu.Blocking)
            {
                var changeLook = view.Controls.Game.ChangeLook;
                if (changeLook.WasPressedThisFrame())
                {
                    if (card.Changing)
                    {
                        if (!card.Typing)
                        {
                            card.Cancel();
                        }
                    }
                    else if (!_chat.Open && !_chat.JustToggled)
                    {
                        OpenLook();
                    }
                }

                if (view.Controls.Game.ReturnToSpawn.WasPressedThisFrame())
                {
                    ReturnToSpawn();
                }

                if (view.Controls.Game.ShowThroughWalls.WasPressedThisFrame() && !_chat.Open && !card.Typing)
                {
                    view.ShowThroughWalls = !view.ShowThroughWalls;
                }
            }

            UpdateMoving();
        }

        private void KeepToSafeArea()
        {
            var size = _root.parent.layout.size;
            var sizes = new Vector4(Screen.width, Screen.height, size.x, size.y);
            if (float.IsNaN(size.x) || size.x <= 0 || sizes == _safeFor)
            {
                return;
            }

            _safeFor = sizes;
            var (top, right, bottom, left) = WebPage.SafeArea();
            var insets = new Vector4(top * size.y, right * size.x, bottom * size.y, left * size.x);
            if (insets == _insets)
            {
                return;
            }

            _insets = insets;
            foreach (var safe in _safe)
            {
                Inset(safe, insets);
            }

            var vertical = Mathf.Max(insets.x, insets.z);
            foreach (var safe in _centeredSafe)
            {
                Inset(safe, new Vector4(vertical, insets.y, vertical, insets.w));
            }
        }

        private static void Inset(VisualElement safe, Vector4 insets)
        {
            safe.style.top = insets.x;
            safe.style.right = insets.y;
            safe.style.bottom = insets.z;
            safe.style.left = insets.w;
        }

        public void ReturnToSpawn()
        {
            if (!_chat.Open && !card.Changing)
            {
                Join.ReturnToSpawn();
            }
        }

        public void OpenLook(bool fromMenu = false)
        {
            if (_chat.Open)
            {
                _reopenChat = true;
                _chatDraft = _chat.Typed;
                _chat.CloseInput();
            }

            card.OpenChange(fromMenu ? () => _menu.ShowAtChangeLook(card.Navigating) : null, fromMenu && _menu.Navigating);
            UpdateMoving();
        }

        public void MarkMenuTipSeen()
        {
            if (!_menuTipSeen)
            {
                _menuTipSeen = true;
                GameSettings.MenuTipSeen = true;
            }
        }

        private void ToggleMenu()
        {
            if (_menu.Open)
            {
                _menu.Close();
                return;
            }

            CloseLook();
            _menu.Show(navigating: false);
            UpdateMoving();
        }

        private void ToggleLook()
        {
            if (card.Changing)
            {
                CloseLook();
            }
            else
            {
                OpenLook();
            }
        }

        private void CloseLook()
        {
            if (card.Changing)
            {
                _reopenChat = false;
                card.Cancel();
                UpdateMoving();
            }
        }

        private void ReopenChat()
        {
            if (_reopenChat && !CardShowing && (Keyboard.current == null || !Keyboard.current.anyKey.isPressed))
            {
                _reopenChat = false;
                _chat.OpenInput();
                _chat.Typed = _chatDraft;
            }
        }

        public void UpdateMoving()
        {
            if (view.Player != null)
            {
                view.Player.InputEnabled = !_chat.Open && !CardShowing && !MenuBlocking;
            }
        }
    }
}

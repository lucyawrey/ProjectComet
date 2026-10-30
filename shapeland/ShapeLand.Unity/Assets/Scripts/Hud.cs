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
    /// <summary>
    /// The in-game overlay (UI Toolkit, <c>Assets/UI/Hud.uxml</c>), shown once the player has joined: the menu, Change
    /// Look, Return to Spawn, chat and network stats buttons (Change Look, or C, brings up the join card), the Esc menu
    /// (<see cref="GameMenu"/>, its button's tip showing until it's first opened), and the respawn fade over the whole screen. Esc or gamepad Start
    /// closes the chat input first, then the look card, else opens the menu (in it, Esc goes back a step and Start
    /// closes it). The chat
    /// (<see cref="Chat"/>), name tags (<see cref="NameTags"/>) and network numbers (<see cref="NetStats"/>) are
    /// components beside it on the same object.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class Hud : MonoBehaviour
    {
        [SerializeField] private JoinClient join;
        [SerializeField] private GameView view;
        [SerializeField] private JoinScreen card;

        private VisualElement _root;
        private Chat _chat;
        private TouchControls _touch;
        private NetStats _stats;
        private CollisionDebug _collisions;
        private GameMenu _menu;
        private VisualElement _lookButton;
        private VisualElement _menuButton;
        // Chat that was open when the look card came up, and what was typed in it: it reopens once the card closes.
        private bool _reopenChat;
        private string _chatDraft;
        // The overlay's parts kept inside the screen's safe area, and the insets they have now.
        private VisualElement[] _safe;
        private Vector4 _insets = new Vector4(-1, -1, -1, -1);
        // The screen and overlay sizes the safe area was last applied for: it's asked for again only when they change.
        private Vector4 _safeFor;

        public JoinClient Join => join;

        /// <summary>The network numbers (F3), beside the overlay.</summary>
        public NetStats Stats => _stats;

        /// <summary>The collision view (F4), beside the overlay.</summary>
        public CollisionDebug Collisions => _collisions;

        public GameView View => view;

        /// <summary>The overlay's root.</summary>
        public VisualElement Root => _root;

        /// <summary>True while the player is in the game and the overlay is up.</summary>
        public bool Shown => _root != null && !_root.ClassListContains("hidden");

        /// <summary>True while the join card is up over the game.</summary>
        public bool CardShowing => card.Showing;

        /// <summary>True while the Esc menu is up, and on the frame it closed (<see cref="GameMenu.Blocking"/>).</summary>
        public bool MenuBlocking => _menu != null && _menu.Blocking;

        // On a phone the interface scales from a smaller design size than the panel settings' (960 x 600), so a panel
        // pixel is about a CSS pixel there and text stays readable; the join card still fits a small phone held
        // landscape.
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
            // The other buttons close the look card first.
            tree.Q<Button>("return-button").clicked += () => { CloseLook(); ReturnToSpawn(); };
            tree.Q<Button>("chat-button").clicked += () => { CloseLook(); _chat.Toggle(); };
            tree.Q<Button>("stats-button").clicked += () => { CloseLook(); _stats.Toggle(); };
            _safe = new[] { tree.Q("safe"), tree.Q("touch-safe"), tree.Q("menu-safe") };
            _touch = new TouchControls(_root, view.OrbitCamera);
            _menu = new GameMenu(this, tree);
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

        private void OnOwnSpawned(PlayerSpawn spawn) => _root.RemoveFromClassList("hidden");

        private void OnConnectionLost(CloseReason reason)
        {
            _menu.Close();
            _root.AddToClassList("hidden");
        }

        private void Update()
        {
            _touch?.Update(Shown && view.Player != null && view.Player.InputEnabled, _menu.Open);

            if (!Shown)
            {
                return;
            }

            if (WebPage.IsPhone)
            {
                KeepToSafeArea();
            }

            // The look card hides chat and the network numbers while it's up.
            _lookButton.EnableInClassList("on", card.Changing);
            _menuButton.EnableInClassList("on", _menu.Open);
            _root.EnableInClassList("look-open", card.Changing);
            _menuButton.EnableInClassList("first-time", !_menu.EverOpened && !_menu.Open && !CardShowing);
            _chat.Box.EnableInClassList("hidden", card.Changing);
            _stats.Panel.EnableInClassList("hidden", !_stats.Shown || card.Changing);
            ReopenChat();

            // Esc closes the chat input itself (Chat), so a press just after that is left alone.
            var menu = view.Controls.Game.Menu;
            if (menu.WasPressedThisFrame() && !_chat.Open && !_chat.JustToggled)
            {
                if (card.Changing && menu.activeControl is not KeyControl)
                {
                    // Start on the look card applies the change (JoinScreen).
                }
                else if (card.FromMenu)
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
            else
            {
                _menu.Update(view.Controls.Menu, _touch.Input);
            }

            if (!_menu.Blocking)
            {
                // C typed in the card's name field is just a letter.
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

        // Insets the safe parts by the screen's safe area (notches, rounded corners, the home bar), which changes as the
        // phone turns.
        private void KeepToSafeArea()
        {
            var size = _root.layout.size;
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
                safe.style.top = insets.x;
                safe.style.right = insets.y;
                safe.style.bottom = insets.z;
                safe.style.left = insets.w;
            }
        }

        /// <summary>Back to the spawn point, unless typing or on the look card. Becomes an action once Comet has them.</summary>
        public void ReturnToSpawn()
        {
            if (!_chat.Open && !card.Changing)
            {
                Join.ReturnToSpawn();
            }
        }

        /// <summary>
        /// Brings up the card to change name, shape and colors, putting an open chat away until it closes. From the Esc
        /// menu (<paramref name="fromMenu"/>), the card's Back returns to the menu.
        /// </summary>
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

        // The menu button: opens the menu, closing the look card first, or closes it.
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

        // The look button: brings the card up, or closes it.
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

        // Closes the look card without changing anything, leaving chat closed.
        private void CloseLook()
        {
            if (card.Changing)
            {
                _reopenChat = false;
                card.Cancel();
                UpdateMoving();
            }
        }

        // Chat put away by the look card opens again once the card is gone, after the key that closed it is let go
        // (focusing the input while it's down would type it).
        private void ReopenChat()
        {
            if (_reopenChat && !CardShowing && (Keyboard.current == null || !Keyboard.current.anyKey.isPressed))
            {
                _reopenChat = false;
                _chat.OpenInput();
                _chat.Typed = _chatDraft;
            }
        }

        /// <summary>Stops the shape while the player types, chooses a look or is in the menu.</summary>
        public void UpdateMoving()
        {
            if (view.Player != null)
            {
                view.Player.InputEnabled = !_chat.Open && !CardShowing && !MenuBlocking;
            }
        }
    }
}

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
    /// The in-game overlay (UI Toolkit, <c>Assets/UI/Hud.uxml</c>), shown once the player has joined: the Change
    /// Look, Return to Spawn, chat and network stats buttons (Change Look, or Esc, brings up the join card), and the
    /// respawn fade over the whole screen. The chat
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
        private VisualElement _lookButton;
        // Chat that was open when the look card came up, and what was typed in it: it reopens once the card closes.
        private bool _reopenChat;
        private string _chatDraft;
        // The overlay's parts kept inside the screen's safe area, and the insets they have now.
        private VisualElement[] _safe;
        private Vector4 _insets = new Vector4(-1, -1, -1, -1);
        // The screen and overlay sizes the safe area was last applied for: it's asked for again only when they change.
        private Vector4 _safeFor;

        public JoinClient Join => join;

        public GameView View => view;

        /// <summary>The overlay's root.</summary>
        public VisualElement Root => _root;

        /// <summary>True while the player is in the game and the overlay is up.</summary>
        public bool Shown => _root != null && !_root.ClassListContains("hidden");

        /// <summary>True while the join card is up over the game.</summary>
        public bool CardShowing => card.Showing;

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
            _lookButton = tree.Q("look-button");
            ((Button)_lookButton).clicked += ToggleLook;
            // The other buttons close the look card first.
            tree.Q<Button>("return-button").clicked += () => { CloseLook(); ReturnToSpawn(); };
            tree.Q<Button>("chat-button").clicked += () => { CloseLook(); _chat.Toggle(); };
            tree.Q<Button>("stats-button").clicked += () => { CloseLook(); _stats.Toggle(); };
            _safe = new[] { tree.Q("safe"), tree.Q("touch-safe") };
            _touch = new TouchControls(_root, view.OrbitCamera);

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
            _root.AddToClassList("hidden");
        }

        private void Update()
        {
            _touch?.Update(Shown && view.Player != null && view.Player.InputEnabled);

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
            _chat.Box.EnableInClassList("hidden", card.Changing);
            _stats.Panel.EnableInClassList("hidden", !_stats.Shown || card.Changing);
            ReopenChat();

            // Esc closes the chat input first (Chat); only a press clear of that opens or closes the card. C typed in
            // the card's name field is just a letter.
            var changeLook = view.Controls.Game.ChangeLook;
            if (changeLook.WasPressedThisFrame())
            {
                if (card.Changing)
                {
                    if (!(card.Typing && changeLook.activeControl is KeyControl { keyCode: Key.C }))
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

        /// <summary>Brings up the card to change name, shape and colors, putting an open chat away until it closes.</summary>
        public void OpenLook()
        {
            if (_chat.Open)
            {
                _reopenChat = true;
                _chatDraft = _chat.Typed;
                _chat.CloseInput();
            }

            card.OpenChange();
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

        /// <summary>Stops the shape while the player types or chooses a look.</summary>
        public void UpdateMoving()
        {
            if (view.Player != null)
            {
                view.Player.InputEnabled = !_chat.Open && !CardShowing;
            }
        }
    }
}

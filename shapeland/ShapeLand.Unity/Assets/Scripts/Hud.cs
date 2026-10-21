using Comet.Client;
using Comet.Unity;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// The in-game overlay (UI Toolkit, <c>Assets/UI/Hud.uxml</c>), shown once the player has joined: the Change
    /// Look button (or Esc) that brings up the join card, and the respawn fade over the whole screen. The chat
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
        private VisualElement _fade;
        private Chat _chat;
        private TouchControls _touch;

        public JoinClient Join => join;

        public GameView View => view;

        /// <summary>The overlay's root.</summary>
        public VisualElement Root => _root;

        /// <summary>True while the player is in the game and the overlay is up.</summary>
        public bool Shown => _root != null && !_root.ClassListContains("hidden");

        /// <summary>True while the join card is up over the game.</summary>
        public bool CardShowing => card.Showing;

        /// <summary>How dark the respawn fade has the screen now: 0 clear, 1 black.</summary>
        public float Darkness { get; private set; }

        private void OnEnable()
        {
            var tree = GetComponent<UIDocument>().rootVisualElement;
            _root = tree.Q("hud");
            _fade = tree.Q("screen-fade");
            _chat = GetComponent<Chat>();
            tree.Q<Button>("look-button").clicked += OpenLook;
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
        }

        private void OnContentLoaded(ShapeLandContent content) => UiText.Fill(_root, content.Strings);

        private void OnOwnSpawned(PlayerSpawn spawn) => _root.RemoveFromClassList("hidden");

        private void OnConnectionLost(CloseReason reason)
        {
            _root.AddToClassList("hidden");
            Darkness = 0;
            _fade.style.opacity = 0;
        }

        private void Update()
        {
            _touch?.Update(Shown && view.Player != null && view.Player.InputEnabled);

            if (!Shown)
            {
                return;
            }

            // Esc closes the chat input first (Chat); only a press clear of that opens or closes the card.
            if (view.Controls.Game.ChangeLook.WasPressedThisFrame())
            {
                if (card.Changing)
                {
                    card.Cancel();
                }
                else if (!_chat.Open && !_chat.JustToggled)
                {
                    OpenLook();
                }
            }

            // Back to the spawn point, unless typing or on the look card. Becomes an action once Comet has them.
            if (view.Controls.Game.ReturnToSpawn.WasPressedThisFrame() && !_chat.Open && !card.Changing)
            {
                Join.ReturnToSpawn();
            }

            UpdateMoving();
            Darkness = view.Player != null ? view.Player.Darkness : 0;
            _fade.style.opacity = Darkness;
        }

        /// <summary>Brings up the card to change name, shape and colors, closing the chat input first.</summary>
        public void OpenLook()
        {
            _chat.CloseInput();
            card.OpenChange();
            UpdateMoving();
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

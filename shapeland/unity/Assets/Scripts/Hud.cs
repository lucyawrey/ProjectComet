using System.Collections.Generic;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// The in-game overlay (UI Toolkit, <c>Assets/UI/Hud.uxml</c>), shown once the player has joined: the chat log
    /// in a lower corner, which fades after a while without messages, with an input that Enter opens (and the
    /// shape stops moving while it's open); and over each shape a name tag (not over the player's own) and a
    /// speech bubble with their latest line. Tags and bubbles are placed over the shapes every frame in screen
    /// space, so they keep one readable size.
    /// </summary>
    [DefaultExecutionOrder(100)] // after the camera has moved
    [RequireComponent(typeof(UIDocument))]
    public sealed class Hud : MonoBehaviour
    {
        /// <summary>Seconds without a message before the log fades out.</summary>
        public const float LogSeconds = 10;

        /// <summary>How many lines the log keeps.</summary>
        public const int MaxLines = 8;

        /// <summary>Tags and bubbles further away than this, in metres, aren't shown.</summary>
        public const float TagDistance = 45;

        // How far above a shape's top its name tag sits, in metres, and the bubble above the tag, in panel pixels.
        private const float TagLift = 0.25f;
        private const float BubbleGap = 26;

        // Enter that opened or closed the chat may reach the other handler too; ignore it for this long.
        private const float KeyDebounce = 0.2f;

        // How long the log and the hint take to fade in or out, in seconds, and the hint's opacity when shown.
        private const float FadeSeconds = 0.4f;
        private const float HintOpacity = 0.85f;

        [SerializeField] private JoinClient join;
        [SerializeField] private GameView view;

        private readonly Dictionary<uint, Tag> _tags = new Dictionary<uint, Tag>();
        private VisualElement _root;
        private VisualElement _tagLayer;
        private VisualElement _chat;
        private VisualElement _log;
        private VisualElement _inputRow;
        private TextField _input;
        private Label _hint;
        private float _lastLine = float.NegativeInfinity;
        private float _toggledAt = float.NegativeInfinity;
        private float _chatOpacity;
        private bool _openOnRelease;

        /// <summary>True while the chat input is open.</summary>
        public bool ChatOpen { get; private set; }

        /// <summary>How visible the chat box is now, from 0 (faded out) to 1.</summary>
        public float ChatOpacity => _chatOpacity;

        /// <summary>The overlay's root, for tests.</summary>
        public VisualElement Root => _root;

        /// <summary>Sets the join client and game view, for scenes built in code (tests); set before the component is enabled.</summary>
        public void Attach(JoinClient joinClient, GameView gameView)
        {
            join = joinClient;
            view = gameView;
        }

        private void OnEnable()
        {
            var tree = GetComponent<UIDocument>().rootVisualElement;
            _root = tree.Q("hud");
            _tagLayer = tree.Q("tags");
            _chat = tree.Q("chat");
            _log = tree.Q("log");
            _inputRow = tree.Q("chat-input-row");
            _input = tree.Q<TextField>("chat-input");
            _hint = tree.Q<Label>("chat-hint");
            _input.RegisterCallback<KeyDownEvent>(OnInputKey, TrickleDown.TrickleDown);

            join.OwnSpawned += OnOwnSpawned;
            join.OtherLeft += OnLeft;
            join.ChatReceived += OnChat;
            join.ChatRefused += OnRefused;
        }

        private void OnDisable()
        {
            join.OwnSpawned -= OnOwnSpawned;
            join.OtherLeft -= OnLeft;
            join.ChatReceived -= OnChat;
            join.ChatRefused -= OnRefused;
        }

        private void OnOwnSpawned(PlayerSpawn spawn) => _root.RemoveFromClassList("hidden");

        private void Update()
        {
            if (_root.ClassListContains("hidden"))
            {
                return;
            }

            // Chat opens when Enter is released: focusing the input while Enter is still down lets the rest of
            // that key press reach it, which in web builds leaves it ignoring typed characters.
            var openChat = view.Controls.OpenChat;
            if (!ChatOpen && openChat.WasPressedThisFrame() && Time.unscaledTime - _toggledAt > KeyDebounce)
            {
                _openOnRelease = true;
            }

            if (_openOnRelease && openChat.WasReleasedThisFrame())
            {
                _openOnRelease = false;
                OpenChat();
            }

            var logShown = ChatOpen || Time.unscaledTime - _lastLine < LogSeconds;
            _chatOpacity = Mathf.MoveTowards(_chatOpacity, logShown ? 1 : 0, Time.unscaledDeltaTime / FadeSeconds);
            if (ChatOpen)
            {
                _chatOpacity = 1; // the input shows at once
            }

            _chat.style.opacity = _chatOpacity;
            _hint.style.opacity = (1 - _chatOpacity) * HintOpacity;
        }

        private void LateUpdate()
        {
            if (!_root.ClassListContains("hidden"))
            {
                PlaceTags();
            }
        }

        /// <summary>Opens the chat input and stops the shape moving until it closes.</summary>
        public void OpenChat()
        {
            _openOnRelease = false;
            ChatOpen = true;
            _toggledAt = Time.unscaledTime;
            _inputRow.RemoveFromClassList("hidden");
            _input.value = "";
            _input.Focus();
            SetMoving(false);
        }

        /// <summary>Closes the chat input without sending.</summary>
        public void CloseChat()
        {
            ChatOpen = false;
            _toggledAt = Time.unscaledTime;
            _input.Blur();
            _inputRow.AddToClassList("hidden");
            SetMoving(true);
        }

        /// <summary>Sends what's typed (if anything) and closes the input.</summary>
        public void SendChat()
        {
            if (ShapeLandRules.TryNormaliseChat(_input.value, out var text))
            {
                join.Say(text);
            }

            CloseChat();
        }

        /// <summary>The text typed into the chat input, for tests.</summary>
        public string Typed
        {
            get => _input.value;
            set => _input.value = value;
        }

        private void SetMoving(bool moving)
        {
            if (view.Player != null)
            {
                view.Player.InputEnabled = moving;
            }
        }

        private void OnInputKey(KeyDownEvent e)
        {
            if (e.character == '\n' || e.character == '\r')
            {
                e.StopPropagation(); // Enter's character: the input is single-line, and it would submit the field
                return;
            }

            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                if (Time.unscaledTime - _toggledAt > KeyDebounce)
                {
                    SendChat();
                }

                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                CloseChat();
                e.StopPropagation();
            }
        }

        private void OnChat(ChatMessage message)
        {
            view.Spawns.TryGetValue(message.EntityId, out var speaker);
            var known = speaker.Name != null;
            var line = new VisualElement();
            line.AddToClassList("chat-line");
            if (known)
            {
                line.Add(new ShapeIcon(join.Content.Shapes[speaker.Shape].Mesh, GameView.Colour(speaker.Colour)));
            }

            var name = new Label(known ? speaker.Name : "Someone");
            name.AddToClassList("chat-name");
            var text = new Label(message.Text);
            text.AddToClassList("chat-text");
            line.Add(name);
            line.Add(text);
            AddLine(line);

            if (known)
            {
                TagFor(message.EntityId, speaker).Say(message.Text, Time.unscaledTime);
            }
        }

        private void OnRefused(ChatRejection reason)
        {
            var note = new Label(reason == ChatRejection.TooFast
                ? "You're sending messages too quickly. Wait a moment, then try again."
                : "Messages can't be empty or longer than 200 characters.");
            note.AddToClassList("chat-note");
            AddLine(note);
        }

        private void AddLine(VisualElement line)
        {
            _log.Add(line);
            while (_log.childCount > MaxLines)
            {
                _log.RemoveAt(0);
            }

            _log.RemoveFromClassList("empty");
            _lastLine = Time.unscaledTime;
        }

        private void OnLeft(uint entityId)
        {
            if (_tags.TryGetValue(entityId, out var tag))
            {
                tag.Remove();
                _tags.Remove(entityId);
            }
        }

        private Tag TagFor(uint entityId, PlayerSpawn spawn)
        {
            if (!_tags.TryGetValue(entityId, out var tag))
            {
                var own = entityId == join.Session.EntityId;
                tag = new Tag(_tagLayer, own ? null : spawn.Name);
                _tags[entityId] = tag;
            }

            return tag;
        }

        // Puts each player's tag and bubble over their shape, or hides them when behind the camera, too far or faded out.
        private void PlaceTags()
        {
            var camera = view.Camera;
            var panel = _root.panel;
            foreach (var pair in view.Spawns)
            {
                var tag = TagFor(pair.Key, pair.Value);
                var shape = view.ShapeOf(pair.Key);
                if (shape == null || panel == null)
                {
                    tag.Hide();
                    continue;
                }

                var look = join.Content.Shapes[pair.Value.Shape].Look;
                var head = shape.position + Vector3.up * (look.Hover + look.Height + TagLift);
                var screen = camera.WorldToScreenPoint(head);
                var faded = view.OtherFades.TryGetValue(pair.Key, out var fade) ? fade.Faded : 0;
                if (screen.z <= 0 || screen.z > TagDistance || faded >= 1)
                {
                    tag.Hide();
                    continue;
                }

                var at = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
                tag.Place(at, 1 - faded, Time.unscaledTime);
            }
        }

        /// <summary>A player's name tag (none for the player's own shape) and speech bubble.</summary>
        private sealed class Tag
        {
            private readonly Label _name;
            private readonly VisualElement _bubble;
            private readonly Label _text;
            private float _bubbleUntil = float.NegativeInfinity;

            public Tag(VisualElement layer, string name)
            {
                if (name != null)
                {
                    _name = new Label(name) { pickingMode = PickingMode.Ignore };
                    _name.AddToClassList("name-tag");
                    layer.Add(_name);
                }

                _bubble = new VisualElement { pickingMode = PickingMode.Ignore };
                _bubble.AddToClassList("bubble");
                _text = new Label { pickingMode = PickingMode.Ignore };
                var tail = new VisualElement { pickingMode = PickingMode.Ignore };
                tail.AddToClassList("bubble-tail");
                _bubble.Add(_text);
                _bubble.Add(tail);
                _bubble.style.display = DisplayStyle.None;
                layer.Add(_bubble);
            }

            /// <summary>Shows <paramref name="text"/> in the bubble, longer for longer lines.</summary>
            public void Say(string text, float now)
            {
                _text.text = text;
                _bubbleUntil = now + Mathf.Min(10, 4 + 0.06f * text.Length);
            }

            public bool NameShown => _name != null && _name.style.display.value == DisplayStyle.Flex;

            /// <summary>The bubble's text while it shows, else null.</summary>
            public string Bubble(float now) => now < _bubbleUntil ? _text.text : null;

            public void Place(Vector2 at, float opacity, float now)
            {
                if (_name != null)
                {
                    _name.style.display = DisplayStyle.Flex;
                    _name.style.left = at.x;
                    _name.style.top = at.y;
                    _name.style.opacity = opacity;
                }

                var talking = now < _bubbleUntil;
                _bubble.style.display = talking ? DisplayStyle.Flex : DisplayStyle.None;
                if (talking)
                {
                    _bubble.style.left = at.x;
                    _bubble.style.top = _name != null ? at.y - BubbleGap : at.y;
                    _bubble.style.opacity = opacity;
                }
            }

            public void Hide()
            {
                if (_name != null)
                {
                    _name.style.display = DisplayStyle.None;
                }

                _bubble.style.display = DisplayStyle.None;
            }

            public void Remove()
            {
                _name?.RemoveFromHierarchy();
                _bubble.RemoveFromHierarchy();
            }
        }

        /// <summary>The bubble text showing over a player now, or null, for tests.</summary>
        public string BubbleOf(uint entityId) => _tags.TryGetValue(entityId, out var tag) ? tag.Bubble(Time.unscaledTime) : null;

        /// <summary>Whether a player's name tag is on screen now, for tests.</summary>
        public bool TagShown(uint entityId) =>
            _tags.TryGetValue(entityId, out var tag) && tag.NameShown;

        /// <summary>The log's lines as text, oldest first, for tests.</summary>
        public List<string> LogLines()
        {
            var lines = new List<string>();
            foreach (var line in _log.Children())
            {
                if (line is Label note)
                {
                    lines.Add(note.text);
                    continue;
                }

                var parts = new List<string>();
                foreach (var child in line.Children())
                {
                    if (child is Label label)
                    {
                        parts.Add(label.text);
                    }
                }

                lines.Add(string.Join(" ", parts));
            }

            return lines;
        }
    }
}

using System.Collections.Generic;
using Comet.Client;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// The chat box in the HUD's lower corner: the log, which fades after a while without messages, and an input
    /// that Enter opens (the shape stops moving while it's open) and a click outside the box closes. The log scrolls
    /// back through older lines. Speech bubbles are <see cref="NameTags"/>' part.
    /// </summary>
    [RequireComponent(typeof(Hud))]
    public sealed class Chat : MonoBehaviour
    {
        /// <summary>Seconds without a message before the log fades out.</summary>
        private const float LogSeconds = 10;

        /// <summary>How many lines the log keeps to scroll back through.</summary>
        private const int MaxLines = 100;

        // Enter or Esc that opened or closed the chat may reach another handler too; ignore it for this long.
        private const float KeyDebounce = 0.2f;

        // How long the log and the hint take to fade in or out, in seconds, and the hint's opacity when shown.
        private const float FadeSeconds = 0.4f;
        private const float HintOpacity = 0.85f;

        private Hud _hud;
        private VisualElement _box;
        private ScrollView _log;
        private VisualElement _inputRow;
        private TextField _input;
        private Label _hint;
        private float _lastLine = float.NegativeInfinity;
        private float _toggledAt = float.NegativeInfinity;
        private bool _openOnRelease;

        /// <summary>True while the input is open.</summary>
        public bool Open { get; private set; }

        /// <summary>True just after the input opened or closed, while the key that did it may still reach other handlers.</summary>
        public bool JustToggled => Time.unscaledTime - _toggledAt <= KeyDebounce;

        /// <summary>How visible the chat box is now, from 0 (faded out) to 1.</summary>
        public float Opacity { get; private set; }

        /// <summary>The text typed into the input.</summary>
        public string Typed
        {
            get => _input.value;
            set => _input.value = value;
        }

        private void OnEnable()
        {
            _hud = GetComponent<Hud>();
            var tree = GetComponent<UIDocument>().rootVisualElement;
            _box = tree.Q("chat");
            _log = tree.Q<ScrollView>("log");
            _inputRow = tree.Q("chat-input-row");
            _input = tree.Q<TextField>("chat-input");
            _hint = tree.Q<Label>("chat-hint");
            _input.RegisterCallback<KeyDownEvent>(OnInputKey, TrickleDown.TrickleDown);
            tree.Q<Button>("chat-send").clicked += Send;
            SetPicking(_log, false);

            _hud.Join.ChatReceived += OnChat;
            _hud.Join.ChatRefused += OnRefused;
            _hud.Join.SpawnSet += OnSpawnSet;
            _hud.Join.ConnectionLost += OnConnectionLost;
        }

        private void OnDisable()
        {
            _hud.Join.ChatReceived -= OnChat;
            _hud.Join.ChatRefused -= OnRefused;
            _hud.Join.SpawnSet -= OnSpawnSet;
            _hud.Join.ConnectionLost -= OnConnectionLost;
        }

        // Back to the join screen: the log is for a world that's gone.
        private void OnConnectionLost(CloseReason reason)
        {
            CloseInput();
            _log.Clear();
            _log.AddToClassList("empty");
        }

        private void Update()
        {
            if (!_hud.Shown)
            {
                return;
            }

            // The input opens when Enter is released: focusing it while Enter is still down lets the rest of that
            // key press reach it, which in web builds leaves it ignoring typed characters.
            var openChat = _hud.View.Controls.Game.OpenChat;
            if (!Open && !_hud.CardShowing && openChat.WasPressedThisFrame() && !JustToggled)
            {
                _openOnRelease = true;
            }

            if (_openOnRelease && openChat.WasReleasedThisFrame())
            {
                OpenInput();
            }

            // Not in the moment it opened: a quick tap on the touch chat button lands outside the box in the same frame.
            if (Open && !JustToggled && ClickedOutsideBox())
            {
                CloseInput();
            }

            var logShown = Open || Time.unscaledTime - _lastLine < LogSeconds;
            Opacity = Open ? 1 : Mathf.MoveTowards(Opacity, logShown ? 1 : 0, Time.unscaledDeltaTime / FadeSeconds);
            _box.style.opacity = Opacity;
            _hint.style.opacity = (1 - Opacity) * HintOpacity;
        }

        /// <summary>Opens the input and stops the shape moving until it closes.</summary>
        public void OpenInput()
        {
            _openOnRelease = false;
            Open = true;
            _toggledAt = Time.unscaledTime;
            _inputRow.RemoveFromClassList("hidden");
            _log.verticalScrollerVisibility = ScrollerVisibility.Auto;
            SetPicking(_log, true);
            _input.value = "";
            _input.Focus();
            _hud.UpdateMoving();
        }

        /// <summary>Closes the input without sending.</summary>
        public void CloseInput()
        {
            if (!Open)
            {
                return;
            }

            Open = false;
            _toggledAt = Time.unscaledTime;
            _input.Blur();
            _inputRow.AddToClassList("hidden");
            _log.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            SetPicking(_log, false);
            ScrollToEnd();
            _hud.UpdateMoving();
        }

        // A click, tap or pen press this frame that landed outside the chat box (a right click too).
        private bool ClickedOutsideBox()
        {
            var pointer = Pointer.current;
            if (pointer == null || _box.panel == null
                || !(pointer.press.wasPressedThisFrame || Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame))
            {
                return false;
            }

            var screen = pointer.position.ReadValue();

            var point = RuntimePanelUtils.ScreenToPanel(_box.panel, new Vector2(screen.x, Screen.height - screen.y));
            return !_box.worldBound.Contains(point);
        }

        // Shows the newest line once the log has laid out the lines just added.
        private void ScrollToEnd() =>
            _log.schedule.Execute(() => _log.verticalScroller.value = _log.verticalScroller.highValue);

        // Whether the log takes the pointer: only while the input is open (to scroll it). Closed, it fades but stays laid
        // out, over the bottom left where a thumb works the touch stick, so it lets touches through.
        private static void SetPicking(VisualElement element, bool on) =>
            element.Query<VisualElement>().ForEach(e => e.pickingMode = on ? PickingMode.Position : PickingMode.Ignore);

        /// <summary>Typed in chat, sends the player back to their spawn point instead of saying it.</summary>
        public const string ReturnCommand = "/return";

        /// <summary>Sends what's typed (if anything) and closes the input.</summary>
        public void Send()
        {
            if (ShapeLandRules.TryNormalizeChat(_input.value, out var text))
            {
                // A command rather than chat. Becomes an action once Comet has them.
                if (text.Equals(ReturnCommand, System.StringComparison.OrdinalIgnoreCase))
                {
                    _hud.Join.ReturnToSpawn();
                }
                else
                {
                    _hud.Join.Say(text);
                }
            }

            CloseInput();
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
                if (!JustToggled)
                {
                    Send();
                }

                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                CloseInput();
                e.StopPropagation();
            }
        }

        private void OnChat(ChatMessage message)
        {
            var strings = _hud.Join.Content.Strings;
            var known = _hud.View.Spawns.TryGetValue(message.EntityId, out var speaker);
            var line = new VisualElement();
            line.AddToClassList("chat-line");
            if (known)
            {
                line.Add(new ShapeIcon(_hud.Join.Content.BodyKind(_hud.Join.Content.Shapes[speaker.Shape]), GameView.Color(speaker.Color)));
            }

            // Chat is server-wide: a speaker in another zone isn't in view, but the line names them.
            var name = new Label(!string.IsNullOrEmpty(message.Name) ? message.Name : known ? speaker.Name : strings.Get("chat.someone"));
            name.AddToClassList("chat-name");
            var text = new Label(message.Text) { enableRichText = false }; // what players type is shown as typed, never markup
            text.AddToClassList("chat-text");
            line.Add(name);
            line.Add(text);
            AddLine(line);
        }

        private void OnRefused(ChatRejection reason) => AddNote(reason == ChatRejection.TooFast ? "chat.too_fast" : "chat.invalid");

        private void OnSpawnSet(int spawn, bool claimed)
        {
            if (claimed)
            {
                AddNote("chat.spawn_set");
            }
        }

        // A line from the game rather than a player, from the strings file.
        private void AddNote(string key)
        {
            var note = new Label(_hud.Join.Content.Strings.Get(key));
            note.AddToClassList("chat-note");
            AddLine(note);
        }

        private void AddLine(VisualElement line)
        {
            // Follow new lines unless the player has scrolled back to read older ones.
            var scroller = _log.verticalScroller;
            var follow = !Open || scroller.value >= scroller.highValue - 1;
            SetPicking(line, Open);
            _log.Add(line);
            while (_log.childCount > MaxLines)
            {
                _log.RemoveAt(0);
            }

            _log.RemoveFromClassList("empty");
            _lastLine = Time.unscaledTime;
            if (follow)
            {
                ScrollToEnd();
            }
        }

        /// <summary>The log's lines as text, oldest first.</summary>
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

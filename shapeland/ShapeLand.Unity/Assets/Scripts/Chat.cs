using System.Collections.Generic;
using Comet.Client;
using Comet.Unity;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    [RequireComponent(typeof(Hud))]
    public sealed class Chat : MonoBehaviour
    {
        private const float LogSeconds = 60;

        private const int MaxLines = 100;

        private const float KeyDebounce = 0.2f;

        private const float FadeSeconds = 0.4f;

        private Hud _hud;
        private VisualElement _box;
        private ScrollView _log;
        private VisualElement _inputRow;
        private TextField _input;
        private VisualElement _button;
        private float _lastLine = float.NegativeInfinity;
        private float _toggledAt = float.NegativeInfinity;
        private bool _openOnRelease;
        private bool _keyboardWasUp;

        public bool Open { get; private set; }

        public bool JustToggled => Time.unscaledTime - _toggledAt <= KeyDebounce;

        public float Opacity { get; private set; }

        public VisualElement Box => _box;

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
            _button = tree.Q("chat-button");
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
            if (!Open && !_hud.CardShowing && !_hud.MenuBlocking && openChat.WasPressedThisFrame() && !JustToggled)
            {
                _openOnRelease = true;
            }

            if (_openOnRelease && openChat.WasReleasedThisFrame())
            {
                OpenInput();
            }

            if (Open && !JustToggled && ClickedOutside())
            {
                CloseInput();
            }

            if (Open && WebPage.IsPhone)
            {
                var keyboardUp = WebPage.KeyboardUp;
                _keyboardWasUp |= keyboardUp;
                _box.EnableInClassList("typing-top", keyboardUp || !_keyboardWasUp);
            }

            var logShown = Open || Time.unscaledTime - _lastLine < LogSeconds;
            Opacity = Open ? 1 : Mathf.MoveTowards(Opacity, logShown ? 1 : 0, Time.unscaledDeltaTime / FadeSeconds);
            _box.style.opacity = Opacity;
            _button.EnableInClassList("on", Open);
        }

        public void Toggle()
        {
            if (Open)
            {
                CloseInput();
            }
            else
            {
                OpenInput();
            }
        }

        public void OpenInput()
        {
            _openOnRelease = false;
            _keyboardWasUp = false;
            Open = true;
            _toggledAt = Time.unscaledTime;
            _inputRow.RemoveFromClassList("hidden");
            _box.EnableInClassList("typing-top", WebPage.IsPhone);
            _log.verticalScrollerVisibility = ScrollerVisibility.Auto;
            SetPicking(_log, true);
            _input.value = "";
            _input.Focus();
            _hud.UpdateMoving();
        }

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
            _box.RemoveFromClassList("typing-top");
            _log.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            SetPicking(_log, false);
            ScrollToEnd();
            _hud.UpdateMoving();
        }

        private bool ClickedOutside()
        {
            var pointer = Pointer.current;
            if (
                pointer == null
                || _box.panel == null
                || !(pointer.press.wasPressedThisFrame || Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            )
            {
                return false;
            }

            var screen = pointer.position.ReadValue();

            var point = RuntimePanelUtils.ScreenToPanel(_box.panel, new Vector2(screen.x, Screen.height - screen.y));
            return !_box.worldBound.Contains(point) && !_button.worldBound.Contains(point);
        }

        // Shows the newest line once the log has laid out. The scheduler can run before that layout, so for a moment it
        // also follows the log and its lines changing size: new lines, and closing the input (which hides the input row
        // and the scroll bar, rewrapping lines).
        private void ScrollToEnd()
        {
            _log.RegisterCallback<GeometryChangedEvent>(OnLogResized);
            _log.contentContainer.RegisterCallback<GeometryChangedEvent>(OnLogResized);
            _log.schedule.Execute(() =>
                {
                    _log.UnregisterCallback<GeometryChangedEvent>(OnLogResized);
                    _log.contentContainer.UnregisterCallback<GeometryChangedEvent>(OnLogResized);
                    ShowNewest();
                })
                .StartingIn(100);
        }

        private void OnLogResized(GeometryChangedEvent _) => ShowNewest();

        private void ShowNewest() => _log.verticalScroller.value = _log.verticalScroller.highValue;

        private static void SetPicking(VisualElement element, bool on) =>
            element.Query<VisualElement>().ForEach(e => e.pickingMode = on ? PickingMode.Position : PickingMode.Ignore);

        public const string ReturnCommand = "/return";

        public void Send()
        {
            if (string.IsNullOrWhiteSpace(_input.value))
            {
                CloseInput();
                return;
            }

            if (ShapeLandRules.TryNormalizeChat(_input.value, out var text))
            {
                if (text.Equals(ReturnCommand, System.StringComparison.OrdinalIgnoreCase))
                {
                    _hud.Join.ReturnToSpawn();
                }
                else
                {
                    _hud.Join.Say(text);
                }
            }

            _input.value = "";
            _input.Focus();
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
                line.Add(
                    new ShapeIcon(
                        _hud.Join.Content.BodyKind(_hud.Join.Content.Shapes[speaker.Shape]),
                        Conversions.FromRgb(_hud.Join.Content.ColorRgb(speaker.Color))
                    )
                );
            }

            var name = new Label(
                !string.IsNullOrEmpty(message.Name) ? message.Name
                : known ? speaker.Name
                : strings.Get("chat.someone")
            );
            name.AddToClassList("chat-name");
            var text = new Label(message.Text) { enableRichText = false };
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

        private void AddNote(string key) => Note(_hud.Join.Content.Strings.Get(key));

        public void Note(string text)
        {
            var note = new Label(text);
            note.AddToClassList("chat-note");
            AddLine(note);
        }

        private void AddLine(VisualElement line)
        {
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

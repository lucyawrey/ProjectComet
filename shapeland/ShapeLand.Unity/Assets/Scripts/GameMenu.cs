using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// The Esc menu (<c>Assets/UI/Menu.uxml</c>, inside the HUD): Resume, Controls, Settings, Change Look, Respawn and
    /// Leave game, opened with Esc, gamepad Start or the HUD's menu button (<see cref="Hud"/> decides when). Keys and
    /// the gamepad move a selection and choose with the Menu actions; a mouse or a tap chooses directly, and a tap
    /// outside closes it. Controls shows the controls for the input in use, with tabs for the others; Settings changes
    /// <see cref="GameSettings"/>. The game keeps running behind it.
    /// </summary>
    public sealed class GameMenu
    {
        private enum Page
        {
            List,
            Controls,
            Settings,
            Leave,
        }

        // Steps of the camera speed slider, and how a held direction repeats.
        private const float SpeedStep = 0.05f;
        private const float RepeatDelay = 0.4f, RepeatInterval = 0.1f;
        private const float RowHeight = 42;
        private const int ChangeLookEntry = 3;

        /// <summary>Opens the menu with Change Look selected: where Back on the look card returns to.</summary>
        public void ShowAtChangeLook() => Show(ChangeLookEntry);

        private readonly Hud _hud;
        private readonly VisualElement _menu;
        private readonly VisualElement _list;
        private readonly VisualElement _confirm;
        private readonly VisualElement _window;
        private readonly VisualElement _devices;
        private readonly ScrollView _scroll;
        private readonly VisualElement _settings;
        private readonly VisualElement _wallsSwitch;
        private readonly VisualElement _speedTrack;
        private readonly VisualElement _speedFill;
        private readonly VisualElement _speedKnob;
        private readonly Label _speedValue;
        private readonly Label _controlsName;
        private readonly Label _settingsName;
        // Per page, the rows the selection moves through (Controls has none: up and down scroll it).
        private readonly VisualElement[] _entries;
        private readonly VisualElement[] _confirmEntries;
        private readonly VisualElement[] _settingRows;
        private readonly (InputKind Kind, VisualElement Tab, VisualElement Rows)[] _tabs;
        private readonly Action[] _entryActions;

        private Page _page;
        private int _selected;
        private InputKind _tab;
        private InputKind _input = (InputKind)(-1); // none yet, so the first update marks the tab in use
        private int _closedOnFrame = -1;
        private Vector2 _held;
        private float _repeatAt;
        private int? _dragPointer;
        private float _shownSpeed = -1;

        /// <summary>The menu in <paramref name="hud"/>'s overlay.</summary>
        public GameMenu(Hud hud, VisualElement root)
        {
            _hud = hud;
            _menu = root.Q("menu");
            _list = root.Q("menu-list");
            _confirm = root.Q("menu-confirm");
            _window = root.Q("menu-window");
            _devices = root.Q("menu-devices");
            _scroll = root.Q<ScrollView>("menu-scroll");
            _settings = root.Q("settings");
            _wallsSwitch = root.Q("walls-switch");
            _speedTrack = root.Q("speed-track");
            _speedFill = root.Q("speed-fill");
            _speedKnob = root.Q("speed-knob");
            _speedValue = root.Q<Label>("speed-value");
            _controlsName = root.Q<Label>("menu-page-controls");
            _settingsName = root.Q<Label>("menu-page-settings");
            root.Q("menu-host").pickingMode = PickingMode.Ignore;

            _entries = new[] { root.Q("menu-resume"), root.Q("menu-controls"), root.Q("menu-settings"), root.Q("menu-look"), root.Q("menu-respawn"), root.Q("menu-leave") };
            _entryActions = new Action[]
            {
                Close,
                () => ShowPage(Page.Controls),
                () => ShowPage(Page.Settings),
                () => { Close(); _hud.OpenLook(fromMenu: true); },
                () => { Close(); _hud.ReturnToSpawn(); },
                () => ShowPage(Page.Leave),
            };
            _confirmEntries = new[] { root.Q("menu-stay"), root.Q("menu-leave-confirm") };
            _settingRows = new[] { root.Q("setting-walls"), root.Q("setting-speed") };
            _tabs = new[]
            {
                (InputKind.KeyboardMouse, root.Q("dev-keys"), root.Q("controls-keys")),
                (InputKind.Gamepad, root.Q("dev-pad"), root.Q("controls-pad")),
                (InputKind.Touch, root.Q("dev-touch"), root.Q("controls-touch")),
            };

            for (var i = 0; i < _entries.Length; i++)
            {
                var index = i;
                ((Button)_entries[i]).clicked += () => ChooseEntry(index);
                _entries[i].RegisterCallback<PointerEnterEvent>(_ => Select(index));
            }

            for (var i = 0; i < _confirmEntries.Length; i++)
            {
                var index = i;
                ((Button)_confirmEntries[i]).clicked += () => ChooseConfirm(index);
                _confirmEntries[i].RegisterCallback<PointerEnterEvent>(_ => Select(index));
            }

            for (var i = 0; i < _settingRows.Length; i++)
            {
                var index = i;
                _settingRows[i].RegisterCallback<PointerEnterEvent>(_ => Select(index));
            }

            _settingRows[0].RegisterCallback<ClickEvent>(_ => ToggleWalls());
            foreach (var (kind, tab, _) in _tabs)
            {
                ((Button)tab).clicked += () => ShowTab(kind);
            }

            root.Q<Button>("menu-back").clicked += Back;
            root.Q("menu-dim").RegisterCallback<PointerDownEvent>(_ => Close());
            _speedTrack.RegisterCallback<PointerDownEvent>(OnSpeedDown);
            _speedTrack.RegisterCallback<PointerMoveEvent>(OnSpeedMove);
            _speedTrack.RegisterCallback<PointerUpEvent>(e => EndSpeedDrag(e.pointerId));
            _speedTrack.RegisterCallback<PointerCaptureOutEvent>(_ => _dragPointer = null);
            EverOpened = GameSettings.MenuOpened;
        }

        /// <summary>True while the menu is up.</summary>
        public bool Open { get; private set; }

        /// <summary>
        /// True while the menu is up and on the frame it closed, so the press that closed it (Enter, Space or the
        /// gamepad's A) doesn't also open chat or jump.
        /// </summary>
        public bool Blocking => Open || Time.frameCount == _closedOnFrame;

        /// <summary>Whether the player has ever opened the menu (remembered); until then the HUD keeps the menu button's tip up.</summary>
        public bool EverOpened { get; private set; }

        /// <summary>Opens the menu at its list, with Resume (or the entry <paramref name="selected"/>) selected.</summary>
        public void Show(int selected = 0)
        {
            Open = true;
            _menu.RemoveFromClassList("hidden");
            _hud.Root.AddToClassList("menu-open");
            if (!EverOpened)
            {
                EverOpened = true;
                GameSettings.MenuOpened = true;
            }

            ShowPage(Page.List, selected);
        }

        /// <summary>Closes the menu, whatever page it's on.</summary>
        public void Close()
        {
            if (!Open)
            {
                return;
            }

            Open = false;
            _closedOnFrame = Time.frameCount;
            _dragPointer = null;
            _menu.AddToClassList("hidden");
            _hud.Root.RemoveFromClassList("menu-open");
        }

        /// <summary>One step back: from a page to the list, from the list out of the menu.</summary>
        public void Back()
        {
            switch (_page)
            {
                case Page.List:
                    Close();
                    break;
                case Page.Leave:
                    ShowPage(Page.List, 5);
                    break;
                default:
                    ShowPage(Page.List, _page == Page.Controls ? 1 : 2);
                    break;
            }
        }

        /// <summary>
        /// Moves the selection and chooses with the Menu actions, and keeps the device tabs, prompts and camera speed
        /// in step with <paramref name="input"/>, the kind of input in use.
        /// </summary>
        public void Update(ShapeLandControls.MenuActions actions, InputKind input)
        {
            if (!Open)
            {
                return;
            }

            if (input != _input)
            {
                _input = input;
                foreach (var (kind, tab, _) in _tabs)
                {
                    tab.EnableInClassList("current", kind == input);
                }
            }

            if (_page == Page.Settings)
            {
                ShowSettings();
            }

            var step = NavigationStep(actions.Navigate.ReadValue<Vector2>());
            if (step.y != 0)
            {
                Move(step.y);
            }

            if (step.x != 0)
            {
                Sideways(step.x);
            }

            if (actions.Submit.WasPressedThisFrame())
            {
                Submit();
            }
            else if (actions.Back.WasPressedThisFrame())
            {
                Back();
            }
            else if (_page == Page.Controls && actions.NextTab.WasPressedThisFrame())
            {
                ShowTab(NextKind(_tab, 1));
            }
            else if (_page == Page.Controls && actions.PreviousTab.WasPressedThisFrame())
            {
                ShowTab(NextKind(_tab, -1));
            }
        }

        // A held direction steps once, then repeats after a pause; down and right are positive.
        private Vector2Int NavigationStep(Vector2 value)
        {
            var held = new Vector2(Mathf.Abs(value.x) > 0.5f ? Mathf.Sign(value.x) : 0, Mathf.Abs(value.y) > 0.5f ? -Mathf.Sign(value.y) : 0);
            if (held.y != 0)
            {
                held.x = 0; // a diagonal moves up or down
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

        private void Move(int by)
        {
            switch (_page)
            {
                case Page.Controls:
                    _scroll.scrollOffset += new Vector2(0, by * RowHeight);
                    break;
                default:
                    var count = Rows().Length;
                    Select(Mathf.Clamp(_selected + by, 0, count - 1));
                    break;
            }
        }

        private void Sideways(int by)
        {
            if (_page == Page.Controls)
            {
                ShowTab(NextKind(_tab, by));
            }
            else if (_page == Page.Settings && _selected == 1)
            {
                SetSpeed(GameSettings.CameraSpeed(_input) + by * SpeedStep);
            }
            else if (_page == Page.Settings && _selected == 0)
            {
                ToggleWalls();
            }
        }

        private void Submit()
        {
            switch (_page)
            {
                case Page.List:
                    ChooseEntry(_selected);
                    break;
                case Page.Leave:
                    ChooseConfirm(_selected);
                    break;
                case Page.Settings when _selected == 0:
                    ToggleWalls();
                    break;
            }
        }

        private void ChooseEntry(int index) => _entryActions[index]();

        private void ChooseConfirm(int index)
        {
            if (index == 0)
            {
                Back();
            }
            else
            {
                Close();
                _hud.Join.Leave();
            }
        }

        private void ShowPage(Page page, int selected = 0)
        {
            _page = page;
            _list.EnableInClassList("hidden", page != Page.List);
            _confirm.EnableInClassList("hidden", page != Page.Leave);
            _window.EnableInClassList("hidden", page is not (Page.Controls or Page.Settings));
            _controlsName.EnableInClassList("hidden", page != Page.Controls);
            _settingsName.EnableInClassList("hidden", page != Page.Settings);
            _devices.EnableInClassList("hidden", page != Page.Controls);
            _settings.EnableInClassList("hidden", page != Page.Settings);
            _menu.EnableInClassList("on-controls", page == Page.Controls);
            if (page == Page.Controls)
            {
                ShowTab(_input);
            }
            else
            {
                foreach (var (_, _, rows) in _tabs)
                {
                    rows.AddToClassList("hidden");
                }
            }

            if (page == Page.Settings)
            {
                _shownSpeed = -1;
                ShowSettings();
            }

            _scroll.scrollOffset = Vector2.zero;
            Select(selected);
        }

        private void ShowTab(InputKind kind)
        {
            _tab = kind;
            foreach (var (tabKind, tab, rows) in _tabs)
            {
                tab.EnableInClassList("on", tabKind == kind);
                rows.EnableInClassList("hidden", tabKind != kind);
            }

            _scroll.scrollOffset = Vector2.zero;
        }

        private static InputKind NextKind(InputKind kind, int by)
        {
            var count = Enum.GetValues(typeof(InputKind)).Length;
            return (InputKind)(((int)kind + by + count) % count);
        }

        private VisualElement[] Rows() => _page switch
        {
            Page.List => _entries,
            Page.Leave => _confirmEntries,
            Page.Settings => _settingRows,
            _ => Array.Empty<VisualElement>(),
        };

        private void Select(int index)
        {
            _selected = index;
            var rows = Rows();
            foreach (var group in new[] { _entries, _confirmEntries, _settingRows })
            {
                for (var i = 0; i < group.Length; i++)
                {
                    group[i].EnableInClassList("selected", group == rows && i == index);
                }
            }
        }

        private void ToggleWalls()
        {
            _hud.View.ShowThroughWalls = !_hud.View.ShowThroughWalls;
            ShowSettings();
        }

        // The settings as they are now: show through walls can also change with V, and the speed shown is the one for
        // the input in use.
        private void ShowSettings()
        {
            _wallsSwitch.EnableInClassList("off", !_hud.View.ShowThroughWalls);
            var speed = GameSettings.CameraSpeed(_input);
            if (Mathf.Approximately(speed, _shownSpeed))
            {
                return;
            }

            _shownSpeed = speed;
            var fraction = Mathf.InverseLerp(GameSettings.MinCameraSpeed, GameSettings.MaxCameraSpeed, speed);
            _speedFill.style.width = Length.Percent(fraction * 100);
            _speedKnob.style.left = Length.Percent(fraction * 100);
            _speedValue.text = _hud.Join.Content.Strings.Format("menu.speed_value", ("percent", Mathf.RoundToInt(speed * 100)));
        }

        private void SetSpeed(float speed)
        {
            GameSettings.SetCameraSpeed(_input, Mathf.Round(speed / SpeedStep) * SpeedStep);
            ShowSettings();
        }

        private void OnSpeedDown(PointerDownEvent e)
        {
            Select(1);
            _dragPointer = e.pointerId;
            _speedTrack.CapturePointer(e.pointerId);
            SpeedAt(e.localPosition.x);
        }

        private void OnSpeedMove(PointerMoveEvent e)
        {
            if (_dragPointer == e.pointerId)
            {
                SpeedAt(e.localPosition.x);
            }
        }

        private void EndSpeedDrag(int pointerId)
        {
            if (_dragPointer == pointerId)
            {
                _dragPointer = null;
                _speedTrack.ReleasePointer(pointerId);
            }
        }

        private void SpeedAt(float x)
        {
            var width = _speedTrack.resolvedStyle.width;
            if (width > 0)
            {
                SetSpeed(Mathf.Lerp(GameSettings.MinCameraSpeed, GameSettings.MaxCameraSpeed, Mathf.Clamp01(x / width)));
            }
        }
    }
}

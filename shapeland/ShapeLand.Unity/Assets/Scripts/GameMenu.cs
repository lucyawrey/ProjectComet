using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    public sealed class GameMenu
    {
        private enum Page
        {
            List,
            Controls,
            Settings,
            Leave,
        }

        private const float SpeedStep = 0.05f;
        private const float RowHeight = 42;
        private const int ChangeLookEntry = 4;

        public const int AccountEntry = 3;

        private const int LeaveEntry = 6;

        public static void TakeOverNavigation(VisualElement root)
        {
            void Ignore(EventBase e)
            {
                e.StopPropagation();
                root.focusController?.IgnoreEvent(e);
            }

            root.RegisterCallback<NavigationMoveEvent>(Ignore, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(Ignore, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationCancelEvent>(Ignore, TrickleDown.TrickleDown);
        }

        public void ShowAtChangeLook(bool navigating) => Show(ChangeLookEntry, navigating);

        public bool Navigating
        {
            get => _menu.ClassListContains("navigating");
            private set => _menu.EnableInClassList("navigating", value);
        }

        private readonly Hud _hud;
        private readonly VisualElement _menu;
        private readonly VisualElement _list;
        private readonly VisualElement _confirm;
        private readonly VisualElement _window;
        private readonly VisualElement _backButton;
        private readonly VisualElement _devices;
        private readonly ScrollView _scroll;
        private readonly VisualElement _settings;
        private readonly VisualElement _speedTrack;
        private readonly VisualElement _speedFill;
        private readonly VisualElement _speedKnob;
        private readonly Label _speedValue;
        private readonly Label _controlsName;
        private readonly Label _settingsName;

        private readonly VisualElement[] _entries;
        private readonly VisualElement[] _confirmEntries;
        private readonly VisualElement[] _settingRows;

        private readonly (int Row, VisualElement Switch, Func<bool> On, Action Flip)[] _switches;
        private readonly (InputKind Kind, VisualElement Tab, VisualElement Rows)[] _tabs;
        private readonly Action[] _entryActions;

        private Page _page;
        private int _selected;
        private InputKind _tab;
        private InputKind _input = (InputKind)(-1);
        private int _closedOnFrame = -1;
        private int _shownOnFrame = -1;
        private readonly MenuNavigation _navigation = new();
        private int? _dragPointer;
        private float _shownSpeed = -1;

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
            _speedTrack = root.Q("speed-track");
            _speedFill = root.Q("speed-fill");
            _speedKnob = root.Q("speed-knob");
            _speedValue = root.Q<Label>("speed-value");
            _controlsName = root.Q<Label>("menu-page-controls");
            _settingsName = root.Q<Label>("menu-page-settings");
            root.Q("menu-host").pickingMode = PickingMode.Ignore;

            root.Q("menu-account").SetEnabled(JoinClient.Accounts);
            _entries = new[]
            {
                root.Q("menu-resume"),
                root.Q("menu-controls"),
                root.Q("menu-settings"),
                root.Q("menu-account"),
                root.Q("menu-look"),
                root.Q("menu-respawn"),
                root.Q("menu-leave"),
            };
            _entryActions = new Action[]
            {
                Close,
                () => ShowPage(Page.Controls),
                () => ShowPage(Page.Settings),
                () =>
                {
                    Close();
                    _hud.OpenAccount();
                },
                () =>
                {
                    Close();
                    _hud.OpenLook(fromMenu: true);
                },
                () =>
                {
                    Close();
                    _hud.ReturnToSpawn();
                },
                () => ShowPage(Page.Leave),
            };
            _confirmEntries = new[] { root.Q("menu-leave-confirm"), root.Q("menu-stay") };
            _settingRows = new[]
            {
                root.Q("setting-walls"),
                root.Q("setting-speed"),
                root.Q("setting-auto-join"),
                root.Q("setting-stats"),
                root.Q("setting-hitboxes"),
            };
            _switches = new (int, VisualElement, Func<bool>, Action)[]
            {
                (0, root.Q("walls-switch"), () => _hud.View.ShowThroughWalls, () => _hud.View.ShowThroughWalls = !_hud.View.ShowThroughWalls),
                (2, root.Q("auto-join-switch"), () => GameSettings.AutoJoin, () => GameSettings.AutoJoin = !GameSettings.AutoJoin),
                (3, root.Q("stats-switch"), () => _hud.Stats.Shown, () => _hud.Stats.Toggle()),
                (4, root.Q("hitboxes-switch"), () => _hud.Collisions.Shown, () => _hud.Collisions.Toggle()),
            };
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

            foreach (var (row, element, _, _) in _switches)
            {
                _settingRows[row].RegisterCallback<ClickEvent>(_ => Flip(row));
                element.AddToClassList("settling");
            }
            foreach (var (kind, tab, _) in _tabs)
            {
                ((Button)tab).clicked += () => ShowTab(kind);
            }

            _backButton = root.Q("menu-back");
            ((Button)_backButton).clicked += Back;
            root.Q("menu-dim").RegisterCallback<PointerDownEvent>(_ => Close());
            _menu.RegisterCallback<PointerDownEvent>(_ => Navigating = false, TrickleDown.TrickleDown);
            _menu.RegisterCallback<PointerMoveEvent>(_ => Navigating = false, TrickleDown.TrickleDown);
            _speedTrack.RegisterCallback<PointerDownEvent>(OnSpeedDown);
            _speedTrack.RegisterCallback<PointerMoveEvent>(OnSpeedMove);
            _speedTrack.RegisterCallback<PointerUpEvent>(e => EndSpeedDrag(e.pointerId));
            _speedTrack.RegisterCallback<PointerCaptureOutEvent>(_ => _dragPointer = null);
        }

        public bool Open { get; private set; }

        public bool Blocking => Open || Time.frameCount == _closedOnFrame;

        public void Show(int selected = 0, bool navigating = true)
        {
            Open = true;
            Navigating = navigating;
            _shownOnFrame = Time.frameCount;
            _menu.RemoveFromClassList("hidden");
            _hud.Root.AddToClassList("menu-open");
            _hud.MarkMenuTipSeen();

            ShowPage(Page.List, selected);
        }

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

        public void Back()
        {
            switch (_page)
            {
                case Page.List:
                    Close();
                    break;
                case Page.Leave:
                    ShowPage(Page.List, LeaveEntry);
                    break;
                default:
                    ShowPage(Page.List, _page == Page.Controls ? 1 : 2);
                    break;
            }
        }

        public void Update(ShapeLandControls.MenuActions actions, InputKind input)
        {
            if (!Open || Time.frameCount == _shownOnFrame)
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

            var step = _navigation.Step(actions.Navigate.ReadValue<Vector2>());
            var pressed =
                actions.Submit.WasPressedThisFrame()
                || actions.Back.WasPressedThisFrame()
                || actions.NextTab.WasPressedThisFrame()
                || actions.PreviousTab.WasPressedThisFrame();
            if (!Navigating && step != Vector2Int.zero)
            {
                Navigating = true;
                return;
            }

            if (pressed)
            {
                Navigating = true;
            }

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

        private void Move(int by)
        {
            switch (_page)
            {
                case Page.Controls:
                    if (_selected < 0)
                    {
                        if (by > 0)
                        {
                            Select(0);
                        }
                    }
                    else if (by < 0 && _scroll.scrollOffset.y <= 0)
                    {
                        Select(-1);
                    }
                    else
                    {
                        _scroll.scrollOffset += new Vector2(0, by * RowHeight);
                    }

                    break;
                default:
                    var rows = Rows();
                    var next = Mathf.Clamp(_selected + by, _page == Page.Settings ? -1 : 0, rows.Length - 1);
                    while (next > 0 && next < rows.Length - 1 && !rows[next].enabledSelf)
                    {
                        next += by > 0 ? 1 : -1;
                    }

                    Select(next);
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
            else if (_page == Page.Settings)
            {
                Flip(_selected);
            }
        }

        private void Submit()
        {
            if (_selected < 0)
            {
                Back();
                return;
            }

            switch (_page)
            {
                case Page.List:
                    ChooseEntry(_selected);
                    break;
                case Page.Leave:
                    ChooseConfirm(_selected);
                    break;
                case Page.Settings:
                    Flip(_selected);
                    break;
            }
        }

        private void ChooseEntry(int index)
        {
            if (_entries[index].enabledSelf)
            {
                _entryActions[index]();
            }
        }

        private void ChooseConfirm(int index)
        {
            if (index == 0)
            {
                Close();
                _hud.Join.Leave();
            }
            else
            {
                Back();
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
                _settings
                    .schedule.Execute(() =>
                    {
                        foreach (var (_, element, _, _) in _switches)
                        {
                            element.RemoveFromClassList("settling");
                        }
                    })
                    .ExecuteLater(200);
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

        private VisualElement[] Rows() =>
            _page switch
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

            _backButton.EnableInClassList("selected", index < 0);
            if (_page == Page.Settings && index >= 0 && index < rows.Length)
            {
                _scroll.ScrollTo(rows[index]);
            }
        }

        private void Flip(int row)
        {
            foreach (var (switchRow, _, _, flip) in _switches)
            {
                if (switchRow == row)
                {
                    flip();
                }
            }

            ShowSettings();
        }

        private void ShowSettings()
        {
            foreach (var (_, element, on, _) in _switches)
            {
                element.EnableInClassList("off", !on());
            }

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

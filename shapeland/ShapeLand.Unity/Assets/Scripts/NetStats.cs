using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// Network numbers in the HUD's top left corner, for tuning under simulated conditions, in every build: the
    /// round trip (latest, and the lowest of the last ten, which the clock trusts), the interpolation delay and
    /// its target, how many of the others' moves ran out of states (dead-reckoned until the late state came, then
    /// blended back to it) and how long each hold was, and the player's own snap-backs (red for any) and
    /// respawns; then the frame time (average and worst over the last second) and the
    /// graphics API and render size, for comparing builds and browsers. Each row has a dot that turns amber or red past a line.
    /// F3 shows or hides it; the choice is remembered.
    /// </summary>
    [RequireComponent(typeof(Hud), typeof(Chat))]
    public sealed class NetStats : MonoBehaviour
    {
        /// <summary>How many seconds the hold numbers cover.</summary>
        public const int WindowSeconds = 5;

        // Where the dots turn amber or red. Holds: amber past 2% of moves, the prototype's smoothness target, red
        // at twice it. Ping isn't a criterion (it's the network's); amber past the bad profile's round trip.
        private const double PingWarnMs = 250;
        private const double HoldsWarnPercent = 2;
        private const double HoldsBadPercent = 4;

        // Frames: amber when the average misses 60 fps, red below 30.
        private const double FrameWarnMs = 17.5;
        private const double FrameBadMs = 33.4;

        private const string ShownKey = "netStatsShown";

        private readonly Queue<Snapshot> _snapshots = new Queue<Snapshot>();
        private Hud _hud;
        private Chat _chat;
        private VisualElement _panel;
        private VisualElement _pingDot;
        private VisualElement _holdsDot;
        private VisualElement _snapsDot;
        private Label _ping;
        private Label _pingBest;
        private Label _delay;
        private Label _delayTarget;
        private Label _holds;
        private Label _held;
        private Label _snaps;
        private Label _respawns;
        private VisualElement _frameDot;
        private Label _frame;
        private Label _frameWorst;
        private Label _graphics;
        private float _nextSnapshot;
        private float _frameSeconds;
        private float _frameWorstSeconds;
        private int _frames;

        /// <summary>Whether showing or hiding is remembered (PlayerPrefs); off in tests.</summary>
        public bool Remember { get; set; } = true;

        /// <summary>Whether the numbers are on screen.</summary>
        public bool Shown { get; private set; }

        /// <summary>The panel, for tests.</summary>
        public VisualElement Panel => _panel;

        private void OnEnable()
        {
            _hud = GetComponent<Hud>();
            _chat = GetComponent<Chat>();
            var tree = GetComponent<UIDocument>().rootVisualElement;
            _panel = tree.Q("net-stats");
            _pingDot = tree.Q("ping-dot");
            _holdsDot = tree.Q("holds-dot");
            _snapsDot = tree.Q("snaps-dot");
            _ping = tree.Q<Label>("ping");
            _pingBest = tree.Q<Label>("ping-best");
            _delay = tree.Q<Label>("delay");
            _delayTarget = tree.Q<Label>("delay-target");
            _holds = tree.Q<Label>("holds");
            _held = tree.Q<Label>("held");
            _snaps = tree.Q<Label>("snaps");
            _respawns = tree.Q<Label>("respawns");
            _frameDot = tree.Q("frame-dot");
            _frame = tree.Q<Label>("frame");
            _frameWorst = tree.Q<Label>("frame-worst");
            _graphics = tree.Q<Label>("graphics");
            Shown = Remember && PlayerPrefs.GetInt(ShownKey, 0) == 1;
            _panel.EnableInClassList("hidden", !Shown);
        }

        /// <summary>Shows or hides the numbers, and remembers the choice.</summary>
        public void Toggle()
        {
            Shown = !Shown;
            if (Remember)
            {
                PlayerPrefs.SetInt(ShownKey, Shown ? 1 : 0);
            }

            _panel.EnableInClassList("hidden", !Shown);
        }

        private void Update()
        {
            var view = _hud.View;
            if (view.Controls.Game.NetStats.WasPressedThisFrame() && !_chat.Open)
            {
                Toggle();
            }

            CountFrame();
            var session = _hud.Join.Session;
            if (session == null || !session.Welcomed || view.Player == null)
            {
                return;
            }

            // Running totals once a second; the window runs from the oldest kept to now.
            var entities = session.Entities;
            var now = Time.unscaledTime;
            if (now >= _nextSnapshot)
            {
                _nextSnapshot = now + 1;
                _snapshots.Enqueue(new Snapshot(entities.MoveStates, entities.Holds, entities.HeldTicks));
                while (_snapshots.Count > WindowSeconds + 1)
                {
                    _snapshots.Dequeue();
                }
            }

            if (!Shown)
            {
                return;
            }

            var oldest = _snapshots.Peek();
            var moves = entities.MoveStates - oldest.MoveStates;
            var holds = entities.Holds - oldest.Holds;
            var holdsPercent = moves > 0 ? 100.0 * holds / moves : 0;
            var heldMs = holds > 0 ? (entities.HeldTicks - oldest.HeldTicks) / holds / entities.TickRate * 1000 : 0;
            var clock = session.Clock;
            var delay = session.InterpolationDelay;
            var player = view.Player;

            _ping.text = $"{clock.LastRoundTrip * 1000:0}";
            _pingBest.text = $"{clock.RoundTrip * 1000:0}";
            _delay.text = $"{delay.Seconds * 1000:0}";
            _delayTarget.text = $"{delay.Target / delay.TickRate * 1000:0}";
            _holds.text = $"{holdsPercent:0.0}";
            _held.text = $"{heldMs:0}";
            _snaps.text = player.SnapBacks.ToString();
            _respawns.text = player.Respawns.ToString();

            SetState(_pingDot, clock.LastRoundTrip * 1000 > PingWarnMs ? "warn" : null);
            SetState(_holdsDot, holdsPercent > HoldsBadPercent ? "bad" : holdsPercent > HoldsWarnPercent ? "warn" : null);
            // An honest player is never snapped back: one means a margin is too tight.
            SetState(_snapsDot, player.SnapBacks > 0 ? "bad" : null);
        }

        // Averages and the worst frame over each second, shown when the second ends.
        private void CountFrame()
        {
            var seconds = Time.unscaledDeltaTime;
            _frameSeconds += seconds;
            _frameWorstSeconds = Mathf.Max(_frameWorstSeconds, seconds);
            _frames++;
            if (_frameSeconds < 1)
            {
                return;
            }

            var averageMs = _frameSeconds / _frames * 1000;
            if (Shown)
            {
                _frame.text = $"{averageMs:0.0}";
                _frameWorst.text = $"{_frameWorstSeconds * 1000:0}";
                var strings = _hud.Join.Content?.Strings;
                _graphics.text = strings?.Format("stats.graphics_value", ("api", GraphicsName(SystemInfo.graphicsDeviceType)), ("width", Screen.width), ("height", Screen.height));
                SetState(_frameDot, averageMs > FrameBadMs ? "bad" : averageMs > FrameWarnMs ? "warn" : null);
            }

            _frameSeconds = 0;
            _frameWorstSeconds = 0;
            _frames = 0;
        }

        private static string GraphicsName(GraphicsDeviceType type) => type switch
        {
            GraphicsDeviceType.OpenGLES3 when Application.platform == RuntimePlatform.WebGLPlayer => "WebGL 2",
            _ => type.ToString(),
        };

        private static void SetState(VisualElement dot, string state)
        {
            dot.EnableInClassList("warn", state == "warn");
            dot.EnableInClassList("bad", state == "bad");
        }

        private readonly struct Snapshot
        {
            public Snapshot(long moveStates, long holds, double heldTicks)
            {
                MoveStates = moveStates;
                Holds = holds;
                HeldTicks = heldTicks;
            }

            public long MoveStates { get; }
            public long Holds { get; }
            public double HeldTicks { get; }
        }
    }
}

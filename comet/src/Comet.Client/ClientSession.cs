using System;
using System.Numerics;
using Comet.Protocol;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;
using MessagePack;

namespace Comet.Client
{
    public delegate void GameMessageHandler(ushort messageId, ReadOnlyMemory<byte> payload, uint tick);

    /// <summary> The client's side of a connection: decodes server frames, keeps the server-tick estimate and
    /// the interpolation buffer, and sends the client's frames. A game's messages arrive through <see
    /// cref="GameMessage"/>. </summary> <remarks> Polled from one thread: call <see cref="Update"/> each frame,
    /// write messages, then <see cref="Flush"/>. Events fire inside Update, in server order. Times are seconds
    /// on the caller's monotonic clock; the session never reads a clock or starts a timer, since web builds
    /// have neither threads nor timers. </remarks>
    public sealed class ClientSession
    {
        public const double LocalStallSeconds = 0.25;

        public const double ReportHoldSeconds = 2;

        public const double LostSeconds = 10;

        private readonly MessagePackSerializerOptions _options;
        private readonly MessageWriter _messages;
        private readonly MessageWriter _frame;
        private readonly double _pingInterval;
        private readonly float _teleportSpeed;
        private readonly float _gravity;
        private readonly Func<Vector3, float?>? _groundHeight;

        private ServerClock? _clock;
        private RemoteEntities? _entities;
        private InterpolationDelay? _delay;

        private double _lastUpdate;
        private bool _catchingUp;
        private uint? _reportTick;
        private (uint Tick, Vector3 Position, Vector3 Velocity, float Facing)? _heldReport;

        private double _nextPing;

        private long _lastPingSent;
        private long? _oldestUnansweredPing;

        private CloseReason _ownReason;
        private string? _ownError;

        /// <param name="pingInterval">Seconds between pings for the server-tick estimate.</param>
        /// <param name="teleportSpeed">Other entities moving faster than this jump instead of gliding (see <see cref="RemoteEntities"/>).</param>
        /// <param name="groundHeight">The highest ground under a position at or below it, so dead reckoning never goes below it.</param>
        public ClientSession(
            IClientTransport transport,
            MessagePackSerializerOptions options,
            double pingInterval = 1,
            float teleportSpeed = float.PositiveInfinity,
            float gravity = 0,
            Func<Vector3, float?>? groundHeight = null
        )
        {
            _gravity = gravity;
            _groundHeight = groundHeight;
            Transport = transport;
            _options = options;
            _messages = new MessageWriter(options: options);
            _frame = new MessageWriter(options: options);
            _pingInterval = pingInterval;
            _teleportSpeed = teleportSpeed;
        }

        public IClientTransport Transport { get; }

        public bool Welcomed => _clock != null;

        public uint EntityId { get; private set; }

        public ServerClock Clock => _clock ?? throw NotWelcomed();

        public RemoteEntities Entities => _entities ?? throw NotWelcomed();

        public InterpolationDelay InterpolationDelay => _delay ?? throw NotWelcomed();

        public uint CorrectionSequence { get; private set; }

        public int StatesReceived { get; private set; }

        public CloseReason CloseReason => _ownReason != CloseReason.None ? _ownReason : Transport.CloseReason;

        public string? Error => _ownError ?? Transport.Error;

        public bool Closed => _ownReason != CloseReason.None || Transport.State == TransportState.Closed;

        public event Action<Welcome>? WelcomeArrived;

        public event Action<PositionCorrection>? Corrected;

        public event Action<uint>? EntityDespawned;

        public event GameMessageHandler? GameMessage;

        public double RenderTick(double now) => Clock.ReceiveTick(now) - InterpolationDelay.Ticks;

        public void Update(double now)
        {
            _catchingUp = now - _lastUpdate > LocalStallSeconds;
            _lastUpdate = now;
            while (_ownReason == CloseReason.None && Transport.TryReceive(out var frame))
            {
                try
                {
                    Handle(frame, now);
                }
                catch (ProtocolException e)
                {
                    GiveUp(CloseReason.ProtocolError, e.Message);
                }
            }

            if (_clock == null)
            {
                return;
            }

            _clock.Advance(now);
            _delay!.Advance(now);
            if (now >= _nextPing)
            {
                _nextPing = now + _pingInterval;
                _lastPingSent = (long)(now * 1_000_000);
                _oldestUnansweredPing ??= _lastPingSent;
                _messages.Write(MessageIds.Ping, new Ping { ClientTime = _lastPingSent });
            }

            // Pongs handled above came first, so frames that waited through a stall of our own count as answers.
            var unanswered = _oldestUnansweredPing is { } oldest ? now - oldest / 1_000_000.0 : 0;
            ReportsHeld = unanswered > ReportHoldSeconds;
            if (unanswered > LostSeconds && _ownReason == CloseReason.None)
            {
                GiveUp(CloseReason.Lost, $"No answer from the server for {LostSeconds} s.");
                return;
            }

            if (!ReportsHeld && _heldReport is { } held)
            {
                _heldReport = null;
                ReportPosition(held.Tick, held.Position, held.Velocity, held.Facing);
            }
        }

        public bool ReportsHeld { get; private set; }

        public void Write<T>(ushort messageId, in T message) => _messages.Write(messageId, message);

        public double ServerSeconds(double now) => Clock.ServerTick(now) / Clock.TickRate;

        /// <summary>
        /// The server tick a step of a simulation at <paramref name="stepsPerSecond"/>, driven by <see cref="ServerSeconds"/>, falls on; false for
        /// a step between ticks. Report only from steps on a tick, so the stamp and the position agree exactly.
        /// </summary>
        public bool TickOfStep(long step, int stepsPerSecond, out uint tick)
        {
            var tickRate = Clock.TickRate;
            if (step < 0 || stepsPerSecond % tickRate != 0 && tickRate % stepsPerSecond != 0)
            {
                throw new ArgumentException("The simulation rate must be a multiple or a divisor of the tick rate.", nameof(stepsPerSecond));
            }

            var ticks = step * tickRate;
            tick = (uint)(ticks / stepsPerSecond);
            return ticks % stepsPerSecond == 0;
        }

        public void ReportPosition(uint tick, Vector3 position, Vector3 velocity, float facing)
        {
            if (ReportsHeld)
            {
                _heldReport = (tick, position, velocity, facing);
                return;
            }

            if (_reportTick is { } queued && queued != tick)
            {
                Flush(_lastUpdate);
            }

            _reportTick = tick;
            ReportPosition(position, velocity, facing);
        }

        public void ReportPosition(Vector3 position, Vector3 velocity, float facing) =>
            _messages.Write(
                MessageIds.PositionReport,
                new PositionReport
                {
                    X = position.X,
                    Y = position.Y,
                    Z = position.Z,
                    VelocityX = velocity.X,
                    VelocityY = velocity.Y,
                    VelocityZ = velocity.Z,
                    Facing = facing,
                    CorrectionSequence = CorrectionSequence,
                }
            );

        public void Flush(double now)
        {
            if (_messages.MessageCount == 0)
            {
                return;
            }

            _frame.BeginFrame(_reportTick ?? (_clock == null ? 0 : (uint)Math.Max(0, Math.Floor(_clock.ServerTick(now)))));
            _reportTick = null;
            _frame.Append(_messages);
            _messages.Clear();
            Transport.Send(_frame.WrittenSpan);
        }

        private void Handle(byte[] data, double now)
        {
            var reader = FrameReader.Create(data);
            var tick = reader.Tick;
            _clock?.OnFrame(tick, now);
            while (reader.TryReadNext(out var messageId, out var payload))
            {
                switch (messageId)
                {
                    case MessageIds.Welcome:
                        var welcome = FrameReader.Decode<Welcome>(payload, _options);
                        if (welcome.TickRate <= 0)
                        {
                            throw new ProtocolException("Welcome has no tick rate.");
                        }

                        EntityId = welcome.EntityId;
                        _clock = new ServerClock(welcome.TickRate);
                        _clock.OnFrame(tick, now);
                        _entities = new RemoteEntities(welcome.TickRate, teleportSpeed: _teleportSpeed, gravity: _gravity)
                        {
                            GroundHeight = _groundHeight,
                        };
                        _delay = new InterpolationDelay(welcome.TickRate);
                        _nextPing = now;
                        WelcomeArrived?.Invoke(welcome);
                        break;
                    case MessageIds.Pong:
                        var sent = FrameReader.Decode<Pong>(payload, _options).ClientTime;
                        _clock?.OnPong(tick, sent / 1_000_000.0, now);
                        if (sent >= _oldestUnansweredPing)
                        {
                            _oldestUnansweredPing = _lastPingSent > sent ? _lastPingSent : (long?)null;
                        }

                        break;
                    case MessageIds.EntityState:
                        var state = FrameReader.Decode<EntityState>(payload, _options);
                        StatesReceived++;
                        var previous = _entities?.AddState(
                            state.EntityId,
                            state.Tick,
                            new Vector3(state.X, state.Y, state.Z),
                            state.Facing,
                            new Vector3(state.VelocityX, state.VelocityY, state.VelocityZ)
                        );
                        if (previous.HasValue && _clock!.Synced && !_catchingUp)
                        {
                            _delay!.AddSample(_clock.ReceiveTick(now) - previous.Value, now);
                        }
                        break;
                    case MessageIds.EntityDespawn:
                        var despawn = FrameReader.Decode<EntityDespawn>(payload, _options);
                        _entities?.Despawn(despawn.EntityId);
                        EntityDespawned?.Invoke(despawn.EntityId);
                        break;
                    case MessageIds.PositionCorrection:
                        var correction = FrameReader.Decode<PositionCorrection>(payload, _options);
                        CorrectionSequence = correction.Sequence;
                        Corrected?.Invoke(correction);
                        break;
                    default:
                        if (messageId >= MessageIds.FirstGameMessage)
                        {
                            GameMessage?.Invoke(messageId, payload, tick);
                        }

                        break;
                }
            }
        }

        private void GiveUp(CloseReason reason, string error)
        {
            _ownReason = reason;
            _ownError = error;
            Transport.Close();
        }

        private static InvalidOperationException NotWelcomed() => new InvalidOperationException("The server hasn't welcomed this client yet.");
    }
}

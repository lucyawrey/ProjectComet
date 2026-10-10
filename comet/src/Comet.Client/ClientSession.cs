using System;
using System.Numerics;
using Comet.Protocol;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;
using MessagePack;

namespace Comet.Client
{
    /// <summary>A game message (ID from <see cref="MessageIds.FirstGameMessage"/>) in a frame stamped <paramref name="tick"/>.</summary>
    public delegate void GameMessageHandler(ushort messageId, ReadOnlyMemory<byte> payload, uint tick);

    /// <summary>
    /// The client's side of a connection: decodes server frames, keeps the server-tick estimate and the
    /// interpolation buffer, and sends the client's frames. Comet's messages are handled here; a game's
    /// arrive through <see cref="GameMessage"/>.
    /// </summary>
    /// <remarks>
    /// Polled from one thread, the same on every platform: call <see cref="Update"/> each frame, write
    /// messages, then call <see cref="Flush"/>. Events fire inside <see cref="Update"/>, in the order the
    /// server sent the messages. Times are seconds on the caller's monotonic clock; the session never reads
    /// a clock or starts a timer itself, since web builds have neither threads nor working timers.
    /// </remarks>
    public sealed class ClientSession
    {
        private readonly MessagePackSerializerOptions _options;
        private readonly MessageWriter _messages;
        private readonly MessageWriter _frame;
        private uint? _reportTick;
        private readonly float _gravity;
        private readonly Func<Vector3, float?>? _groundHeight;
        private double _lastUpdate;
        private readonly double _pingInterval;
        private readonly float _teleportSpeed;
        private ServerClock? _clock;
        private RemoteEntities? _entities;
        private InterpolationDelay? _delay;
        private bool _catchingUp;

        /// <summary>A gap between updates longer than this, in seconds, is a stall on this side rather than the network's.</summary>
        public const double LocalStallSeconds = 0.25;
        private double _nextPing;
        private string? _protocolError;

        /// <param name="options">Serializer options covering Comet's messages and the game's.</param>
        /// <param name="pingInterval">Seconds between pings for the server-tick estimate.</param>
        /// <param name="teleportSpeed">Other entities moving faster than this jump instead of gliding (see <see cref="RemoteEntities"/>).</param>
        /// <param name="gravity">For dead-reckoning other entities in the air (<see cref="RemoteEntities"/>).</param>
        /// <param name="groundHeight">The highest ground under a position at or below it, so dead reckoning never goes below it.</param>
        public ClientSession(IClientTransport transport, MessagePackSerializerOptions options, double pingInterval = 1, float teleportSpeed = float.PositiveInfinity,
            float gravity = 0, Func<Vector3, float?>? groundHeight = null)
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

        /// <summary>True once the server's <see cref="Welcome"/> has arrived.</summary>
        public bool Welcomed => _clock != null;

        /// <summary>The entity this client controls (from the welcome).</summary>
        public uint EntityId { get; private set; }

        public ServerClock Clock => _clock ?? throw NotWelcomed();

        /// <summary>Other entities, drawn through the interpolation buffer. The game spawns them from its own messages.</summary>
        public RemoteEntities Entities => _entities ?? throw NotWelcomed();

        /// <summary>How far behind the receive timeline other entities are drawn, adapted to arriving states.</summary>
        public InterpolationDelay InterpolationDelay => _delay ?? throw NotWelcomed();

        /// <summary>The last correction applied, echoed in position reports.</summary>
        public uint CorrectionSequence { get; private set; }

        /// <summary>Entity states received, for diagnostics.</summary>
        public int StatesReceived { get; private set; }

        /// <summary>Why the session ended: a protocol error here, or the transport's error.</summary>
        public string? Error => _protocolError ?? Transport.Error;

        public bool Closed => Transport.State == TransportState.Closed;

        /// <summary>The server's welcome arrived; <see cref="Clock"/> and <see cref="Entities"/> are ready.</summary>
        public event Action<Welcome>? WelcomeArrived;

        /// <summary>The server moved the player. The simulation should move there at once (see <see cref="CorrectionBlend"/>).</summary>
        public event Action<PositionCorrection>? Corrected;

        /// <summary>An entity left view; it has already been removed from <see cref="Entities"/>.</summary>
        public event Action<uint>? EntityDespawned;

        public event GameMessageHandler? GameMessage;

        /// <summary>The tick other entities are drawn at, at <paramref name="now"/>.</summary>
        public double RenderTick(double now) => Clock.ReceiveTick(now) - InterpolationDelay.Ticks;

        /// <summary>Handles received frames, moves the tick estimate and queues a ping when one is due.</summary>
        public void Update(double now)
        {
            // After a stall of our own (a hidden browser tab, a long frame), the frames waiting were received long
            // before now, so they say nothing about the network's delay; don't let them push the delay up.
            _catchingUp = now - _lastUpdate > LocalStallSeconds;
            _lastUpdate = now;
            while (_protocolError == null && Transport.TryReceive(out var frame))
            {
                try
                {
                    Handle(frame, now);
                }
                catch (ProtocolException e)
                {
                    _protocolError = e.Message;
                    Transport.Close();
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
                _messages.Write(MessageIds.Ping, new Ping { ClientTime = (long)(now * 1_000_000) });
            }
        }

        /// <summary>Queues a message for the next <see cref="Flush"/>.</summary>
        public void Write<T>(ushort messageId, in T message) => _messages.Write(messageId, message);

        /// <summary>
        /// The server's time at <paramref name="now"/>, in seconds (the tick estimate over the tick rate): drive the
        /// player's own <see cref="FixedStep"/> with it, so its steps fall on server ticks. The estimate only ever
        /// slews, so the steps stay evenly spaced.
        /// </summary>
        public double ServerSeconds(double now) => Clock.ServerTick(now) / Clock.TickRate;

        /// <summary>
        /// The server tick a step of a simulation at <paramref name="stepsPerSecond"/>, driven by
        /// <see cref="ServerSeconds"/>, falls on; false for a step between ticks. Report only from steps on a tick,
        /// stamped with it, so the stamp and the position agree exactly.
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

        /// <summary>
        /// Queues a report of where the player was at <paramref name="tick"/> (from <see cref="TickOfStep"/>),
        /// echoing the last correction. The frame it goes in is stamped with that tick.
        /// </summary>
        public void ReportPosition(uint tick, Vector3 position, Vector3 velocity, float facing)
        {
            // A frame has one tick: send what's queued for another tick first.
            if (_reportTick is { } queued && queued != tick)
            {
                Flush(_lastUpdate);
            }

            _reportTick = tick;
            ReportPosition(position, velocity, facing);
        }

        /// <summary>Queues a report of the player's own position, echoing the last correction.</summary>
        public void ReportPosition(Vector3 position, Vector3 velocity, float facing) => _messages.Write(MessageIds.PositionReport, new PositionReport
        {
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            VelocityX = velocity.X,
            VelocityY = velocity.Y,
            VelocityZ = velocity.Z,
            Facing = facing,
            CorrectionSequence = CorrectionSequence,
        });

        /// <summary>
        /// Sends the queued messages in one frame, if there are any, stamped with the tick of a report queued with
        /// one, else the estimated server tick.
        /// </summary>
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
                        _entities = new RemoteEntities(welcome.TickRate, teleportSpeed: _teleportSpeed, gravity: _gravity) { GroundHeight = _groundHeight };
                        _delay = new InterpolationDelay(welcome.TickRate);
                        _nextPing = now;
                        WelcomeArrived?.Invoke(welcome);
                        break;
                    case MessageIds.Pong:
                        _clock?.OnPong(tick, FrameReader.Decode<Pong>(payload, _options).ClientTime / 1_000_000.0, now);
                        break;
                    case MessageIds.EntityState:
                        var state = FrameReader.Decode<EntityState>(payload, _options);
                        StatesReceived++;
                        var previous = _entities?.AddState(state.EntityId, state.Tick, new Vector3(state.X, state.Y, state.Z), state.Facing, new Vector3(state.VelocityX, state.VelocityY, state.VelocityZ));
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

                        break; // an unknown Comet message, from a newer server
                }
            }
        }

        private static InvalidOperationException NotWelcomed() => new InvalidOperationException("The server hasn't welcomed this client yet.");
    }
}

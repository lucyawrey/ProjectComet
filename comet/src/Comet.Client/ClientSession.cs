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
        private readonly double _interpolationDelay;
        private readonly double _pingInterval;
        private ServerClock? _clock;
        private RemoteEntities? _entities;
        private double _nextPing;
        private string? _protocolError;

        /// <param name="options">Serializer options covering Comet's messages and the game's.</param>
        /// <param name="interpolationDelay">How far behind the newest arrivals other entities are drawn, in seconds.</param>
        /// <param name="pingInterval">Seconds between pings for the server-tick estimate.</param>
        public ClientSession(IClientTransport transport, MessagePackSerializerOptions options, double interpolationDelay = 0.1, double pingInterval = 1)
        {
            Transport = transport;
            _options = options;
            _messages = new MessageWriter(options: options);
            _frame = new MessageWriter(options: options);
            _interpolationDelay = interpolationDelay;
            _pingInterval = pingInterval;
        }

        public IClientTransport Transport { get; }

        /// <summary>True once the server's <see cref="Welcome"/> has arrived.</summary>
        public bool Welcomed => _clock != null;

        /// <summary>The entity this client controls (from the welcome).</summary>
        public uint EntityId { get; private set; }

        public ServerClock Clock => _clock ?? throw NotWelcomed();

        /// <summary>Other entities, drawn through the interpolation buffer. The game spawns them from its own messages.</summary>
        public RemoteEntities Entities => _entities ?? throw NotWelcomed();

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
        public double RenderTick(double now) => Clock.ReceiveTick(now) - _interpolationDelay * Clock.TickRate;

        /// <summary>Handles received frames, moves the tick estimate and queues a ping when one is due.</summary>
        public void Update(double now)
        {
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
            if (now >= _nextPing)
            {
                _nextPing = now + _pingInterval;
                _messages.Write(MessageIds.Ping, new Ping { ClientTime = (long)(now * 1_000_000) });
            }
        }

        /// <summary>Queues a message for the next <see cref="Flush"/>.</summary>
        public void Write<T>(ushort messageId, in T message) => _messages.Write(messageId, message);

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

        /// <summary>Sends the queued messages in one frame stamped with the estimated server tick, if there are any.</summary>
        public void Flush(double now)
        {
            if (_messages.MessageCount == 0)
            {
                return;
            }

            _frame.BeginFrame(_clock == null ? 0 : (uint)Math.Max(0, Math.Floor(_clock.ServerTick(now))));
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
                        _entities = new RemoteEntities(welcome.TickRate);
                        _nextPing = now;
                        WelcomeArrived?.Invoke(welcome);
                        break;
                    case MessageIds.Pong:
                        _clock?.OnPong(tick, FrameReader.Decode<Pong>(payload, _options).ClientTime / 1_000_000.0, now);
                        break;
                    case MessageIds.EntityState:
                        var state = FrameReader.Decode<EntityState>(payload, _options);
                        StatesReceived++;
                        _entities?.AddState(state.EntityId, tick, new Vector3(state.X, state.Y, state.Z), state.Facing);
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

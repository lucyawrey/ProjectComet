using System;
using System.Collections.Generic;
using System.Numerics;

namespace Comet.Client
{
    /// <summary>Where a remote entity is drawn.</summary>
    public readonly struct EntityPose
    {
        public EntityPose(Vector3 position, float facing)
        {
            Position = position;
            Facing = facing;
        }

        public Vector3 Position { get; }

        /// <summary>Facing as a yaw angle in radians.</summary>
        public float Facing { get; }
    }

    /// <summary>
    /// The interpolation buffer for other entities: each one's recent states, stamped with the server tick they
    /// are from (for players, when they sent them), drawn at a render tick a little behind the newest arrivals so there is
    /// usually a state on either side to blend between.
    /// </summary>
    /// <remarks>
    /// When the render tick passes an entity's newest state (late packets, or it stopped moving) the entity
    /// holds there; nothing is extrapolated. After a stall it therefore jumps to the latest state rather than
    /// replaying. The server only sends states while an entity moves, so a state left over from before an
    /// idle gap longer than <see cref="IdleGapTicks"/> is restamped to one report interval before the next
    /// state; otherwise a player who starts moving would glide slowly across the whole gap. Two states further
    /// apart than <see cref="TeleportSpeed"/> allows (a respawn, a snap-back) aren't blended: the entity holds at
    /// the first until the second's tick, then jumps.
    /// </remarks>
    public sealed class RemoteEntities
    {
        private const int MaxStates = 32;

        // Distance allowed on top of the teleport speed, so slow ticks and rounding never count as a jump.
        private const float TeleportMargin = 0.5f;

        private readonly Dictionary<uint, List<State>> _tracks = new Dictionary<uint, List<State>>();
        private double _renderTick = double.NegativeInfinity;

        /// <param name="tickRate">Server ticks per second.</param>
        /// <param name="reportInterval">How often moving entities are updated, in seconds.</param>
        /// <param name="idleGap">A gap between states longer than this, in seconds, means the entity stood still.</param>
        /// <param name="teleportSpeed">Faster than this across the ground or upwards, in metres per second, a move is drawn as a jump; falling is never one.</param>
        public RemoteEntities(int tickRate, double reportInterval = 1.0 / 15, double idleGap = 0.2, float teleportSpeed = float.PositiveInfinity)
        {
            TickRate = tickRate;
            ReportIntervalTicks = reportInterval * tickRate;
            IdleGapTicks = idleGap * tickRate;
            TeleportSpeed = teleportSpeed;
        }

        public int TickRate { get; }

        public double ReportIntervalTicks { get; }

        public double IdleGapTicks { get; }

        public float TeleportSpeed { get; }

        public int Count => _tracks.Count;

        public IEnumerable<uint> Ids => _tracks.Keys;

        public bool Contains(uint entityId) => _tracks.ContainsKey(entityId);

        /// <summary>States so far that continued a move (no idle gap before them).</summary>
        public long MoveStates { get; private set; }

        /// <summary>
        /// Of <see cref="MoveStates"/>, those that arrived after the render tick had passed the state before them:
        /// the entity ran out of states mid-move, so it was drawn standing until this one came, then jumped.
        /// </summary>
        public long Holds { get; private set; }

        /// <summary>
        /// Of <see cref="MoveStates"/>, those whose speed across the ground (distance over the stamps' gap) differs
        /// from the step before by more than 30% either way, both at walking pace: a hitch in drawn speed.
        /// </summary>
        public long SpeedHitches { get; private set; }

        /// <summary>Of <see cref="MoveStates"/>, those too far from the state before them to walk, drawn as a jump.</summary>
        public long Jumps { get; private set; }

        /// <summary>How far, in ticks, the render tick had passed the previous state when each hold ended, summed.</summary>
        public double HeldTicks { get; private set; }

        /// <summary>Starts tracking an entity at its spawn state.</summary>
        public void Spawn(uint entityId, uint tick, Vector3 position, float facing)
        {
            _tracks[entityId] = new List<State>(8) { new State(tick, position, facing) };
        }

        public bool Despawn(uint entityId) => _tracks.Remove(entityId);

        /// <summary>
        /// Adds a state; ignored for entities not spawned, and for states older than the newest. Returns the
        /// previous state's stamp when this one continues a move (no idle gap between them), else null, for
        /// <see cref="InterpolationDelay"/>.
        /// </summary>
        public double? AddState(uint entityId, uint tick, Vector3 position, float facing)
        {
            if (!_tracks.TryGetValue(entityId, out var states))
            {
                return null;
            }

            var newest = states[states.Count - 1];
            if (tick < newest.Tick)
            {
                return null;
            }

            if (tick == newest.Tick)
            {
                states[states.Count - 1] = new State(tick, position, facing);
                return null;
            }

            double? previous = newest.Tick;
            if (tick - newest.Tick > IdleGapTicks)
            {
                previous = null;
                states.Add(new State(tick - ReportIntervalTicks, newest.Position, newest.Facing));
            }
            else
            {
                MoveStates++;
                if (IsTeleport(newest, new State(tick, position, facing)))
                {
                    Jumps++;
                }

                if (states.Count >= 2)
                {
                    var before = states[states.Count - 2];
                    var was = GroundSpeed(before, newest);
                    var now = GroundSpeed(newest, new State(tick, position, facing));
                    if (was > 1 && now > 1 && (now > was * 1.3f || now < was / 1.3f))
                    {
                        SpeedHitches++;
                    }
                }

                if (_renderTick > newest.Tick)
                {
                    Holds++;
                    HeldTicks += _renderTick - newest.Tick;
                }
            }

            states.Add(new State(tick, position, facing));
            if (states.Count > MaxStates)
            {
                states.RemoveRange(0, states.Count - MaxStates);
            }

            return previous;
        }

        /// <summary>
        /// Where an entity is drawn at <paramref name="renderTick"/>, and drops states no longer needed.
        /// Returns false for an entity not spawned.
        /// </summary>
        public bool TrySample(uint entityId, double renderTick, out EntityPose pose)
        {
            _renderTick = Math.Max(_renderTick, renderTick);
            if (!_tracks.TryGetValue(entityId, out var states))
            {
                pose = default;
                return false;
            }

            // Keep one state at or before the render tick, to blend from.
            var drop = 0;
            while (drop + 1 < states.Count && states[drop + 1].Tick <= renderTick)
            {
                drop++;
            }

            if (drop > 0)
            {
                states.RemoveRange(0, drop);
            }

            var from = states[0];
            if (states.Count == 1 || renderTick <= from.Tick)
            {
                pose = new EntityPose(from.Position, from.Facing);
                return true;
            }

            var to = states[1];
            if (IsTeleport(from, to))
            {
                pose = new EntityPose(from.Position, from.Facing);
                return true;
            }

            var t = (float)((renderTick - from.Tick) / (to.Tick - from.Tick));
            pose = new EntityPose(Vector3.Lerp(from.Position, to.Position, t), LerpAngle(from.Facing, to.Facing, t));
            return true;
        }

        private float GroundSpeed(State from, State to)
        {
            var move = to.Position - from.Position;
            return new Vector2(move.X, move.Z).Length() / (float)((to.Tick - from.Tick) / TickRate);
        }

        private bool IsTeleport(State from, State to)
        {
            var allowed = TeleportSpeed * (float)((to.Tick - from.Tick) / TickRate) + TeleportMargin;
            var move = to.Position - from.Position;
            return new Vector2(move.X, move.Z).Length() > allowed || move.Y > allowed;
        }

        /// <summary>Blends two yaw angles the short way round.</summary>
        public static float LerpAngle(float from, float to, float t)
        {
            var difference = (to - from) % (2 * MathF.PI);
            if (difference > MathF.PI)
            {
                difference -= 2 * MathF.PI;
            }
            else if (difference < -MathF.PI)
            {
                difference += 2 * MathF.PI;
            }

            return from + difference * t;
        }

        private readonly struct State
        {
            public State(double tick, Vector3 position, float facing)
            {
                Tick = tick;
                Position = position;
                Facing = facing;
            }

            public double Tick { get; }

            public Vector3 Position { get; }

            public float Facing { get; }
        }
    }
}

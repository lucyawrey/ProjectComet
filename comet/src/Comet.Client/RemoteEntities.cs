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
    /// When the render tick passes an entity's newest state (a late packet: over TCP a lost one stalls the
    /// stream for a round trip or two) the entity is dead-reckoned: drawn moving on along that state's velocity
    /// (falling under <see cref="Gravity"/> while it moves vertically, never below <see cref="GroundHeight"/>)
    /// for up to <see cref="MaxExtrapolationTicks"/>, then held. When the late state arrives, the difference
    /// between where it was drawn and where it should be is eased out over <see cref="BlendTicks"/> rather than
    /// jumped. A state left from before an idle gap longer than <see cref="IdleGapTicks"/>, with the entity
    /// standing still, is restamped to one report interval before the next state; otherwise a player who starts
    /// moving would glide slowly across the whole gap. Two states further apart than <see cref="TeleportSpeed"/>
    /// allows (a respawn, a snap-back) aren't blended: the entity holds at the first until the second's tick,
    /// then jumps.
    /// </remarks>
    public sealed class RemoteEntities
    {
        private const int MaxStates = 32;

        // Distance allowed on top of the teleport speed, so slow ticks and rounding never count as a jump.
        private const float TeleportMargin = 0.5f;

        // Slower than this across the ground, in metres per second, an entity counts as standing still.
        private const float StandingSpeed = 0.1f;

        private readonly Dictionary<uint, Track> _tracks = new Dictionary<uint, Track>();
        private double _renderTick = double.NegativeInfinity;

        /// <param name="tickRate">Server ticks per second.</param>
        /// <param name="reportInterval">How often moving entities are updated, in seconds.</param>
        /// <param name="idleGap">A gap between states longer than this, in seconds, after a state standing still, means the entity stood still.</param>
        /// <param name="teleportSpeed">Faster than this across the ground or upwards, in metres per second, a move is drawn as a jump; falling is never one.</param>
        /// <param name="gravity">Downward acceleration while dead-reckoning an entity moving vertically, in metres per second squared.</param>
        /// <param name="maxExtrapolation">How long, in seconds, an entity is dead-reckoned before it holds.</param>
        /// <param name="blend">How long, in seconds, the difference is eased out when a late state arrives.</param>
        public RemoteEntities(int tickRate, double reportInterval = 1.0 / 15, double idleGap = 0.2, float teleportSpeed = float.PositiveInfinity,
            float gravity = 0, double maxExtrapolation = 0.3, double blend = 0.15)
        {
            TickRate = tickRate;
            ReportIntervalTicks = reportInterval * tickRate;
            IdleGapTicks = idleGap * tickRate;
            TeleportSpeed = teleportSpeed;
            Gravity = gravity;
            MaxExtrapolationTicks = maxExtrapolation * tickRate;
            BlendTicks = blend * tickRate;
        }

        public int TickRate { get; }

        public double ReportIntervalTicks { get; }

        public double IdleGapTicks { get; }

        public float TeleportSpeed { get; }

        public float Gravity { get; }

        public double MaxExtrapolationTicks { get; }

        public double BlendTicks { get; }

        /// <summary>
        /// The height of the highest ground under a position at or below it, or null where there is none (a hole, past
        /// the edge); dead reckoning never goes below it.
        /// </summary>
        public Func<Vector3, float?>? GroundHeight { get; set; }

        /// <summary>How far up or down an entity on the ground is followed over a short move, in metres: a step's height.</summary>
        public const float StepReach = 0.5f;

        /// <summary>A blend-back further than this, in metres, counts as a visible hitch (<see cref="VisibleBlendBacks"/>).</summary>
        public float VisibleError { get; set; } = 0.2f;

        public int Count => _tracks.Count;

        public IEnumerable<uint> Ids => _tracks.Keys;

        public bool Contains(uint entityId) => _tracks.ContainsKey(entityId);

        /// <summary>
        /// Leaves a spawned entity out of the smoothness counters (<see cref="MoveStates"/> to
        /// <see cref="VisibleBlendBacks"/>), e.g. a test's speed cheater, whose impossible moves aren't the network's.
        /// </summary>
        public void ExcludeFromCounts(uint entityId)
        {
            if (_tracks.TryGetValue(entityId, out var track))
            {
                track.Counted = false;
            }
        }

        /// <summary>States so far that continued a move (no idle gap before them).</summary>
        public long MoveStates { get; private set; }

        /// <summary>
        /// Of <see cref="MoveStates"/>, those that arrived after the render tick had passed the state before them:
        /// the entity ran out of states mid-move and was dead-reckoned until this one came.
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

        /// <summary>Times dead reckoning ran its full <see cref="MaxExtrapolationTicks"/> and the entity held: a visible stop.</summary>
        public long Overruns { get; private set; }

        /// <summary>Late states whose difference from where the entity was drawn was eased out.</summary>
        public long BlendBacks { get; private set; }

        /// <summary>Of <see cref="BlendBacks"/>, those further than <see cref="VisibleError"/>: a visible correction.</summary>
        public long VisibleBlendBacks { get; private set; }

        /// <summary>Visible hitches so far: dead reckoning running out, visible blend-backs, and jumps.</summary>
        public long VisibleHitches => Overruns + VisibleBlendBacks + Jumps;

        /// <summary>Starts tracking an entity at its spawn state.</summary>
        public void Spawn(uint entityId, uint tick, Vector3 position, float facing)
        {
            var track = new Track();
            track.States.Add(new State(tick, position, facing, Vector3.Zero));
            _tracks[entityId] = track;
        }

        public bool Despawn(uint entityId) => _tracks.Remove(entityId);

        /// <summary>
        /// Adds a state; ignored for entities not spawned, and for states older than the newest. Returns the
        /// previous state's stamp when this one continues a move (no idle gap between them), else null, for
        /// <see cref="InterpolationDelay"/>.
        /// </summary>
        public double? AddState(uint entityId, uint tick, Vector3 position, float facing, Vector3 velocity = default)
        {
            if (!_tracks.TryGetValue(entityId, out var track))
            {
                return null;
            }

            var states = track.States;
            var newest = states[states.Count - 1];
            var state = new State(tick, position, facing, velocity);
            if (tick < newest.Tick)
            {
                return null;
            }

            if (tick == newest.Tick)
            {
                states[states.Count - 1] = state;
                return null;
            }

            // A long gap after a state standing still was the entity standing still; after a moving one it was a
            // stall, and the entity was dead-reckoned through it.
            double? previous = newest.Tick;
            var standing = new Vector2(newest.Velocity.X, newest.Velocity.Z).Length() < StandingSpeed;
            if (tick - newest.Tick > IdleGapTicks && standing)
            {
                previous = null;
                states.Add(new State(tick - ReportIntervalTicks, newest.Position, newest.Facing, Vector3.Zero));
            }
            else if (track.Counted)
            {
                MoveStates++;
                if (IsTeleport(newest, state))
                {
                    Jumps++;
                }

                if (states.Count >= 2)
                {
                    var before = states[states.Count - 2];
                    var was = GroundSpeed(before, newest);
                    var now = GroundSpeed(newest, state);
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

            states.Add(state);
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
            if (!_tracks.TryGetValue(entityId, out var track))
            {
                pose = default;
                return false;
            }

            // Keep one state at or before the render tick, to blend from.
            var states = track.States;
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
            Vector3 position;
            float facing;
            var extrapolating = false;
            if (states.Count == 1 && renderTick > from.Tick)
            {
                // Ran dry: dead-reckon from the newest state, for a while.
                var ticks = renderTick - from.Tick;
                if (ticks > MaxExtrapolationTicks && track.OverrunFrom != from.Tick)
                {
                    track.OverrunFrom = from.Tick;
                    if (from.Velocity.LengthSquared() > StandingSpeed * StandingSpeed)
                    {
                        Overruns += track.Counted ? 1 : 0;
                    }
                }

                position = Extrapolate(from, Math.Min(ticks, MaxExtrapolationTicks) / TickRate);
                facing = from.Facing;
                extrapolating = true;
            }
            else if (states.Count == 1 || renderTick <= from.Tick || IsTeleport(from, states[1]))
            {
                position = from.Position;
                facing = from.Facing;
                if (states.Count > 1 && renderTick > from.Tick)
                {
                    track.Offset = Vector3.Zero; // a jump isn't eased
                }
            }
            else
            {
                var to = states[1];
                var t = (float)((renderTick - from.Tick) / (to.Tick - from.Tick));
                position = Vector3.Lerp(from.Position, to.Position, t);
                facing = LerpAngle(from.Facing, to.Facing, t);
            }

            // Leaving dead reckoning, or reckoning on from a newer state: ease out the difference from where it was drawn.
            if (track.Drawn is { } drawn && track.ExtrapolatedFrom is { } reckonedFrom && (!extrapolating || from.Tick != reckonedFrom))
            {
                var error = drawn - position;
                if (error.Length() < TeleportSpeed * (float)(BlendTicks / TickRate) + TeleportMargin)
                {
                    track.Offset = error;
                    track.BlendStart = renderTick;
                    if (track.Counted)
                    {
                        BlendBacks++;
                        if (error.Length() > VisibleError)
                        {
                            VisibleBlendBacks++;
                        }
                    }
                }
            }

            track.ExtrapolatedFrom = extrapolating ? from.Tick : (double?)null;
            var progress = BlendTicks > 0 ? Math.Min(1, Math.Max(0, (renderTick - track.BlendStart) / BlendTicks)) : 1;
            var eased = (float)(progress * progress * (3 - 2 * progress));
            var shown = position + track.Offset * (1 - eased);
            if (progress >= 1)
            {
                track.Offset = Vector3.Zero;
            }

            track.Drawn = shown;
            pose = new EntityPose(shown, facing);
            return true;
        }

        // Where an entity moving at a state's velocity is after a while: under gravity while moving vertically,
        // never below the ground; on the ground, following it up and down slopes and steps (up to 45 degrees, or
        // StepReach), and falling under gravity once it walks off a drop steeper than that.
        private Vector3 Extrapolate(State from, double seconds)
        {
            var t = (float)seconds;
            var position = from.Position + new Vector3(from.Velocity.X, 0, from.Velocity.Z) * t;
            if (MathF.Abs(from.Velocity.Y) > 0.01f)
            {
                position.Y += from.Velocity.Y * t - 0.5f * Gravity * t * t;
                var above = new Vector3(position.X, Math.Max(position.Y, from.Position.Y), position.Z);
                if (GroundHeight?.Invoke(above) is { } ground && position.Y < ground)
                {
                    position.Y = ground;
                }
            }
            else if (GroundHeight != null)
            {
                var reach = Math.Max(StepReach, new Vector2(position.X - from.Position.X, position.Z - from.Position.Z).Length());
                var ground = GroundHeight(new Vector3(position.X, from.Position.Y + reach, position.Z));
                var fallen = from.Position.Y - 0.5f * Gravity * t * t;
                position.Y = ground is { } walked && walked >= from.Position.Y - reach ? walked : Math.Max(fallen, ground ?? fallen);
            }

            return position;
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
            public State(double tick, Vector3 position, float facing, Vector3 velocity)
            {
                Tick = tick;
                Position = position;
                Facing = facing;
                Velocity = velocity;
            }

            public double Tick { get; }

            public Vector3 Position { get; }

            public float Facing { get; }

            public Vector3 Velocity { get; }
        }

        private sealed class Track
        {
            public List<State> States { get; } = new List<State>(8);

            /// <summary>Whether this entity's moves count towards the smoothness counters.</summary>
            public bool Counted { get; set; } = true;

            /// <summary>Where the entity was last drawn, and the stamp it was being dead-reckoned from (null if it wasn't).</summary>
            public Vector3? Drawn { get; set; }

            public double? ExtrapolatedFrom { get; set; }

            /// <summary>The difference being eased out, as of <see cref="BlendStart"/>.</summary>
            public Vector3 Offset { get; set; }

            public double BlendStart { get; set; }

            /// <summary>The state whose dead reckoning last ran out, so each overrun counts once.</summary>
            public double OverrunFrom { get; set; } = double.NaN;
        }
    }
}

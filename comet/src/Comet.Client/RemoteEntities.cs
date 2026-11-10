using System;
using System.Collections.Generic;
using System.Numerics;

namespace Comet.Client
{
    public readonly struct EntityPose
    {
        public EntityPose(Vector3 position, float facing)
        {
            Position = position;
            Facing = facing;
        }

        /// <summary>Where the entity's feet are, in meters.</summary>
        public Vector3 Position { get; }

        /// <summary>Facing as a yaw angle in radians.</summary>
        public float Facing { get; }
    }

    public sealed class RemoteEntities
    {
        private const int MaxStates = 32;

        private const float TeleportMargin = 0.5f;

        private const float StandingSpeed = 0.1f;

        private readonly Dictionary<uint, Track> _tracks = new Dictionary<uint, Track>();
        private double _renderTick = double.NegativeInfinity;

        /// <param name="reportInterval">How often moving entities are updated, in seconds.</param> <param
        /// name="idleGap">A gap between states longer than this, in seconds, after a state standing still,
        /// means the entity stood still.</param> <param name="teleportSpeed">Faster than this across the
        /// ground or upwards, in meters per second, a move is drawn as a jump; falling is never
        /// one.</param> <param name="gravity">Downward acceleration while dead-reckoning an entity moving
        /// vertically, in meters per second squared.</param> <param name="maxExtrapolation">How long, in
        /// seconds, an entity is dead-reckoned before it holds.</param> <param name="blend">How long, in
        /// seconds, the difference is eased out when a late state arrives.</param>
        public RemoteEntities(
            int tickRate,
            double reportInterval = 1.0 / 15,
            double idleGap = 0.2,
            float teleportSpeed = float.PositiveInfinity,
            float gravity = 0,
            double maxExtrapolation = 0.3,
            double blend = 0.15
        )
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

        /// <summary>How far up or down an entity on the ground is followed over a short move, in meters: a step's height.</summary>
        public const float StepReach = 0.5f;

        private const float WallProbe = 100f;

        /// <summary>A blend-back further than this, in meters, counts as a visible hitch (<see cref="VisibleBlendBacks"/>).</summary>
        public float VisibleError { get; set; } = 0.2f;

        public int Count => _tracks.Count;

        public IEnumerable<uint> Ids => _tracks.Keys;

        public bool Contains(uint entityId) => _tracks.ContainsKey(entityId);

        public void ExcludeFromCounts(uint entityId)
        {
            if (_tracks.TryGetValue(entityId, out var track))
            {
                track.Counted = false;
            }
        }

        public long MoveStates { get; private set; }

        public long Holds { get; private set; }

        public long SpeedHitches { get; private set; }

        public long Jumps { get; private set; }

        public double HeldTicks { get; private set; }

        public long Overruns { get; private set; }

        public long BlendBacks { get; private set; }

        public long VisibleBlendBacks { get; private set; }

        public long VisibleHitches => Overruns + VisibleBlendBacks + Jumps;

        public void Spawn(uint entityId, uint tick, Vector3 position, float facing)
        {
            var track = new Track();
            track.States.Add(new State(tick, position, facing, Vector3.Zero));
            _tracks[entityId] = track;
        }

        public bool Despawn(uint entityId) => _tracks.Remove(entityId);

        /// <summary>
        /// Adds a state (ignored for entities not spawned and states older than the newest). Returns the previous state's stamp when this one
        /// continues a move, else null, for <see cref="InterpolationDelay"/>.
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

        public bool TrySample(uint entityId, double renderTick, out EntityPose pose)
        {
            _renderTick = Math.Max(_renderTick, renderTick);
            if (!_tracks.TryGetValue(entityId, out var track))
            {
                pose = default;
                return false;
            }

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
                    track.Offset = Vector3.Zero;
                }
            }
            else
            {
                var to = states[1];
                var t = (float)((renderTick - from.Tick) / (to.Tick - from.Tick));
                position = Vector3.Lerp(from.Position, to.Position, t);
                facing = LerpAngle(from.Facing, to.Facing, t);
            }

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

        private Vector3 Extrapolate(State from, double seconds)
        {
            var t = (float)seconds;
            if (MathF.Abs(from.Velocity.Y) <= 0.01f && GroundHeight != null)
            {
                return Walk(from, t);
            }

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

            return position;
        }

        private Vector3 Walk(State from, float t)
        {
            var move = new Vector2(from.Velocity.X, from.Velocity.Z) * t;
            var pieces = Math.Max(1, (int)MathF.Ceiling(move.Length() / StepReach));
            var at = from.Position;
            for (var i = 1; i <= pieces; i++)
            {
                var along = move * ((float)i / pieces);
                var next = new Vector3(from.Position.X + along.X, at.Y, from.Position.Z + along.Y);
                if (GroundHeight!(next + new Vector3(0, WallProbe, 0)) is { } top && top > at.Y + StepReach)
                {
                    return at;
                }

                var ground = GroundHeight(next + new Vector3(0, StepReach, 0));
                if (ground is { } walked && walked >= at.Y - StepReach)
                {
                    at = new Vector3(next.X, walked, next.Z);
                    continue;
                }

                var fall = t * (1 - (float)(i - 1) / pieces);
                var end = new Vector3(from.Position.X + move.X, at.Y, from.Position.Z + move.Y);
                var fallen = at.Y - 0.5f * Gravity * fall * fall;
                var landing = GroundHeight(end);
                end.Y = Math.Max(fallen, landing ?? fallen);
                return end;
            }

            return at;
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

            public bool Counted { get; set; } = true;

            public Vector3? Drawn { get; set; }

            public double? ExtrapolatedFrom { get; set; }

            public Vector3 Offset { get; set; }

            public double BlendStart { get; set; }

            public double OverrunFrom { get; set; } = double.NaN;
        }
    }
}

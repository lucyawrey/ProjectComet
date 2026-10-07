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
    /// state; otherwise a player who starts moving would glide slowly across the whole gap.
    /// </remarks>
    public sealed class RemoteEntities
    {
        private const int MaxStates = 32;

        private readonly Dictionary<uint, List<State>> _tracks = new Dictionary<uint, List<State>>();

        /// <param name="tickRate">Server ticks per second.</param>
        /// <param name="reportInterval">How often moving entities are updated, in seconds.</param>
        /// <param name="idleGap">A gap between states longer than this, in seconds, means the entity stood still.</param>
        public RemoteEntities(int tickRate, double reportInterval = 1.0 / 15, double idleGap = 0.2)
        {
            ReportIntervalTicks = reportInterval * tickRate;
            IdleGapTicks = idleGap * tickRate;
        }

        public double ReportIntervalTicks { get; }

        public double IdleGapTicks { get; }

        public int Count => _tracks.Count;

        public IEnumerable<uint> Ids => _tracks.Keys;

        public bool Contains(uint entityId) => _tracks.ContainsKey(entityId);

        /// <summary>Starts tracking an entity at its spawn state.</summary>
        public void Spawn(uint entityId, uint tick, Vector3 position, float facing)
        {
            _tracks[entityId] = new List<State>(8) { new State(tick, position, facing) };
        }

        public bool Despawn(uint entityId) => _tracks.Remove(entityId);

        /// <summary>Adds a state; ignored for entities not spawned, and for states older than the newest.</summary>
        public void AddState(uint entityId, uint tick, Vector3 position, float facing)
        {
            if (!_tracks.TryGetValue(entityId, out var states))
            {
                return;
            }

            var newest = states[states.Count - 1];
            if (tick < newest.Tick)
            {
                return;
            }

            if (tick == newest.Tick)
            {
                states[states.Count - 1] = new State(tick, position, facing);
                return;
            }

            if (tick - newest.Tick > IdleGapTicks)
            {
                states.Add(new State(tick - ReportIntervalTicks, newest.Position, newest.Facing));
            }

            states.Add(new State(tick, position, facing));
            if (states.Count > MaxStates)
            {
                states.RemoveRange(0, states.Count - MaxStates);
            }
        }

        /// <summary>
        /// Where an entity is drawn at <paramref name="renderTick"/>, and drops states no longer needed.
        /// Returns false for an entity not spawned.
        /// </summary>
        public bool TrySample(uint entityId, double renderTick, out EntityPose pose)
        {
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
            var t = (float)((renderTick - from.Tick) / (to.Tick - from.Tick));
            pose = new EntityPose(Vector3.Lerp(from.Position, to.Position, t), LerpAngle(from.Facing, to.Facing, t));
            return true;
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

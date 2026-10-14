using System;
using System.Collections.Generic;
using System.Numerics;
using Comet.Content;

namespace Comet.Simulation
{
    /// <summary>
    /// A zone's spawn points, found by key or number, with its default; and arriving at one: a random spot within
    /// its radius with ground under it and room to stand, facing as the spawn says. When spawns are used and how
    /// players change theirs is game policy.
    /// </summary>
    public sealed class Spawns
    {
        /// <summary>Random spots tried for one with ground and room, before falling back to the point itself.</summary>
        public const int Tries = 8;

        private readonly Dictionary<string, SpawnPoint> _byKey = new Dictionary<string, SpawnPoint>();
        private readonly Dictionary<int, SpawnPoint> _byNumber = new Dictionary<int, SpawnPoint>();
        private readonly List<SpawnPoint> _all = new List<SpawnPoint>();

        /// <summary>The spawn points of <paramref name="zone"/>.</summary>
        public Spawns(Zone zone)
        {
            foreach (var chunk in zone.Chunks)
            {
                foreach (var spawn in chunk.Spawns)
                {
                    _byKey[spawn.Id] = spawn;
                    _byNumber[spawn.Number] = spawn;
                    _all.Add(spawn);
                    if (spawn.IsDefault)
                    {
                        Default = spawn;
                    }
                }
            }
        }

        public IReadOnlyList<SpawnPoint> All => _all;

        /// <summary>The zone's default spawn, or null if it has none.</summary>
        public SpawnPoint? Default { get; }

        public bool TryGet(string key, out SpawnPoint spawn) => _byKey.TryGetValue(key, out spawn!);

        public bool TryGet(int number, out SpawnPoint spawn) => _byNumber.TryGetValue(number, out spawn!);

        /// <summary>
        /// The spawn point whose radius a body stands in: its feet within the radius across the ground and no more
        /// than <paramref name="reach"/> meters above or below the point. The nearest, if several.
        /// </summary>
        public SpawnPoint? StandingIn(Vector3 feet, float reach)
        {
            SpawnPoint? nearest = null;
            var best = float.MaxValue;
            foreach (var spawn in _all)
            {
                var across = Vector2.Distance(new Vector2(feet.X, feet.Z), new Vector2(spawn.Position.X, spawn.Position.Z));
                if (across <= spawn.Radius && MathF.Abs(feet.Y - spawn.Position.Y) <= reach && across < best)
                {
                    nearest = spawn;
                    best = across;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Where a player arrives at <paramref name="spawn"/> and which way they face (radians, 0 facing +Z, as
        /// <see cref="MotorState.Facing"/>): a random spot within its radius standing on ground no more than a step
        /// above or below the point, with nothing in the way; the point itself if <see cref="Tries"/> spots don't fit.
        /// </summary>
        public static (Vector3 Feet, float Facing) Arrive(SpawnPoint spawn, CollisionWorld world, MovementRules rules, Random random)
        {
            var feet = spawn.Position;
            for (var i = 0; i < Tries; i++)
            {
                // Uniform over the disc: the square root spreads spots evenly rather than bunching them at the center.
                var angle = (float)(random.NextDouble() * 2 * Math.PI);
                var distance = spawn.Radius * MathF.Sqrt((float)random.NextDouble());
                var spot = spawn.Position + new Vector3(MathF.Sin(angle) * distance, 0, MathF.Cos(angle) * distance);
                if (world.TryGetGround(spot, rules.BodyRadius, spot.Y + rules.StepHeight, out var ground, rules.StepHeight)
                    && ground >= spot.Y - rules.StepHeight)
                {
                    var standing = new Vector3(spot.X, ground, spot.Z);
                    if (!world.IsBlocked(standing, rules.BodyRadius, rules.BodyHeight)
                        && !world.IsBlockedByTerrain(standing, rules.BodyRadius, rules.BodyHeight, rules.StepHeight))
                    {
                        feet = standing;
                        break;
                    }
                }
            }

            var facing = spawn.Facing switch
            {
                SpawnFacing.Direction => spawn.Yaw * MathF.PI / 180,
                SpawnFacing.TowardPoint when Vector2.Distance(new Vector2(feet.X, feet.Z), new Vector2(spawn.Position.X, spawn.Position.Z)) > 0.01f =>
                    MathF.Atan2(spawn.Position.X - feet.X, spawn.Position.Z - feet.Z),
                SpawnFacing.Random => (float)(random.NextDouble() * 2 * Math.PI - Math.PI),
                _ => 0f,
            };
            return (feet, facing);
        }
    }
}

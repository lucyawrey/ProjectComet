using System;
using System.Collections.Generic;
using System.Numerics;
using Comet.Content;

namespace Comet.Simulation
{
    /// <summary>
    /// A zone's spawn points, teleporters and triggers, found by key, by number or by where a player stands; whether
    /// feet are inside a trigger volume; and arriving at one: a random spot in its footprint on the ground under it,
    /// facing as it says. How each kind fires (its stand time, re-arming, what it does) is game policy.
    /// </summary>
    public sealed class Triggers
    {
        /// <summary>Random spots tried for one with ground and room, before falling back to the center.</summary>
        public const int Tries = 8;

        private readonly Dictionary<string, SpawnPoint> _spawnsByKey = new Dictionary<string, SpawnPoint>();
        private readonly Dictionary<int, SpawnPoint> _spawnsByNumber = new Dictionary<int, SpawnPoint>();
        private readonly Dictionary<string, Teleporter> _teleportersByKey = new Dictionary<string, Teleporter>();
        private readonly List<SpawnPoint> _spawns = new List<SpawnPoint>();
        private readonly List<Teleporter> _teleporters = new List<Teleporter>();
        private readonly List<Trigger> _triggers = new List<Trigger>();

        /// <summary>The spawn points, teleporters and triggers of <paramref name="zone"/>.</summary>
        public Triggers(Zone zone)
        {
            foreach (var chunk in zone.Chunks)
            {
                foreach (var spawn in chunk.Spawns)
                {
                    _spawnsByKey[spawn.Id] = spawn;
                    _spawnsByNumber[spawn.Number] = spawn;
                    _spawns.Add(spawn);
                }

                foreach (var teleporter in chunk.Teleporters)
                {
                    _teleportersByKey[teleporter.Id] = teleporter;
                    _teleporters.Add(teleporter);
                }

                _triggers.AddRange(chunk.Triggers);
            }
        }

        public IReadOnlyList<SpawnPoint> Spawns => _spawns;

        public IReadOnlyList<Teleporter> Teleporters => _teleporters;

        /// <summary>The triggers with toggle effects.</summary>
        public IReadOnlyList<Trigger> All => _triggers;

        public bool TryGetSpawn(string key, out SpawnPoint spawn) => _spawnsByKey.TryGetValue(key, out spawn!);

        public bool TryGetSpawn(int number, out SpawnPoint spawn) => _spawnsByNumber.TryGetValue(number, out spawn!);

        public bool TryGetTeleporter(string key, out Teleporter teleporter) => _teleportersByKey.TryGetValue(key, out teleporter!);

        /// <summary>The spawn point whose volume <paramref name="feet"/> are in, or null (the first, if volumes overlap).</summary>
        public SpawnPoint? SpawnAt(Vector3 feet)
        {
            foreach (var spawn in _spawns)
            {
                if (Inside(spawn.Volume, feet))
                {
                    return spawn;
                }
            }

            return null;
        }

        /// <summary>The teleporter whose volume <paramref name="feet"/> are in, or null (the first, if volumes overlap).</summary>
        public Teleporter? TeleporterAt(Vector3 feet)
        {
            foreach (var teleporter in _teleporters)
            {
                if (Inside(teleporter.Volume, feet))
                {
                    return teleporter;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether <paramref name="feet"/> are inside <paramref name="volume"/>: a box or an upright cylinder from its
        /// bottom to its top, or a sphere (an ellipsoid for an uneven size) filling its size.
        /// </summary>
        public static bool Inside(TriggerVolume volume, Vector3 feet)
        {
            var local = ToLocal(volume, feet);
            var half = volume.Size / 2;
            if (half.X <= 0 || half.Y <= 0 || half.Z <= 0)
            {
                return false;
            }

            switch (volume.Kind)
            {
                case PrimitiveKind.Sphere:
                    var d = new Vector3(local.X / half.X, (local.Y - half.Y) / half.Y, local.Z / half.Z);
                    return d.LengthSquared() <= 1;
                case PrimitiveKind.Cylinder:
                    return local.Y >= 0 && local.Y <= volume.Size.Y && Square(local.X / half.X) + Square(local.Z / half.Z) <= 1;
                default:
                    return local.Y >= 0 && local.Y <= volume.Size.Y && MathF.Abs(local.X) <= half.X && MathF.Abs(local.Z) <= half.Z;
            }
        }

        /// <summary>
        /// Where a player arrives in <paramref name="volume"/> and which way they face (radians, 0 facing +Z, as
        /// <see cref="MotorState.Facing"/>): a random spot in its footprint (an ellipse for round kinds, else a
        /// rectangle), standing on ground between a step below its bottom and its top, with nothing in the way; else
        /// its center on the ground there, else its bottom center.
        /// </summary>
        public static (Vector3 Feet, float Facing) Arrive(TriggerVolume volume, SpawnFacing facing, CollisionWorld world, MovementRules rules, Random random, ISet<int>? passThrough = null)
        {
            var top = volume.Position.Y + volume.Size.Y;
            var lowest = volume.Position.Y - rules.StepHeight;
            var feet = volume.Position;
            var found = false;
            for (var i = 0; i < Tries && !found; i++)
            {
                found = TryStand(RandomSpot(volume, random), out feet);
            }

            if (!found && !TryStand(volume.Position, out feet))
            {
                feet = volume.Position;
            }

            var center = volume.Position;
            var yaw = facing switch
            {
                SpawnFacing.Direction => volume.Yaw * MathF.PI / 180,
                SpawnFacing.TowardPoint when Vector2.Distance(new Vector2(feet.X, feet.Z), new Vector2(center.X, center.Z)) > 0.01f =>
                    MathF.Atan2(center.X - feet.X, center.Z - feet.Z),
                SpawnFacing.Random => (float)(random.NextDouble() * 2 * Math.PI - Math.PI),
                _ => 0f,
            };
            return (feet, yaw);

            bool TryStand(Vector3 spot, out Vector3 standing)
            {
                standing = spot;
                spot.Y = top;
                if (!world.TryGetGround(spot, rules.BodyRadius, top, out var ground, rules.StepHeight, passThrough) || ground < lowest)
                {
                    return false;
                }

                standing = new Vector3(spot.X, ground, spot.Z);
                return !world.IsBlocked(standing, rules.BodyRadius, rules.BodyHeight, passThrough)
                    && !world.IsBlockedByTerrain(standing, rules.BodyRadius, rules.BodyHeight, rules.StepHeight);
            }
        }

        // A uniformly random point of the volume's footprint, at its bottom.
        private static Vector3 RandomSpot(TriggerVolume volume, Random random)
        {
            var half = volume.Size / 2;
            float x, z;
            if (volume.Kind == PrimitiveKind.Box)
            {
                x = (float)(random.NextDouble() * 2 - 1) * half.X;
                z = (float)(random.NextDouble() * 2 - 1) * half.Z;
            }
            else
            {
                // Uniform over the disc: the square root spreads spots evenly rather than bunching them at the center.
                var angle = (float)(random.NextDouble() * 2 * Math.PI);
                var distance = MathF.Sqrt((float)random.NextDouble());
                x = MathF.Sin(angle) * distance * half.X;
                z = MathF.Cos(angle) * distance * half.Z;
            }

            return ToWorld(volume, new Vector3(x, 0, z));
        }

        // A world point in the volume's own space: from its bottom center, turned back by its yaw.
        private static Vector3 ToLocal(TriggerVolume volume, Vector3 point)
        {
            var d = point - volume.Position;
            var (sin, cos) = SinCos(volume.Yaw);
            return new Vector3(d.X * cos - d.Z * sin, d.Y, d.X * sin + d.Z * cos);
        }

        private static Vector3 ToWorld(TriggerVolume volume, Vector3 local)
        {
            var (sin, cos) = SinCos(volume.Yaw);
            return volume.Position + new Vector3(local.X * cos + local.Z * sin, local.Y, -local.X * sin + local.Z * cos);
        }

        private static (float Sin, float Cos) SinCos(float degrees)
        {
            var radians = degrees * MathF.PI / 180;
            return (MathF.Sin(radians), MathF.Cos(radians));
        }

        private static float Square(float x) => x * x;
    }
}

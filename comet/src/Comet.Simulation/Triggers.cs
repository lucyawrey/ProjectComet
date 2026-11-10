using System;
using System.Collections.Generic;
using System.Numerics;
using Comet.Content;

namespace Comet.Simulation
{
    public sealed class Triggers
    {
        public const int Tries = 8;

        /// <summary>
        /// How far below a box's or cylinder's bottom feet still count as inside, in meters: feet on a
        /// pad's top can come out a hair lower in floating point.
        /// </summary>
        public const float BottomSlack = 0.05f;

        private readonly Dictionary<string, SpawnPoint> _spawnsByKey = new Dictionary<string, SpawnPoint>();
        private readonly Dictionary<int, SpawnPoint> _spawnsByNumber = new Dictionary<int, SpawnPoint>();
        private readonly List<SpawnPoint> _spawns = new List<SpawnPoint>();
        private readonly List<Teleporter> _teleporters = new List<Teleporter>();
        private readonly List<Trigger> _triggers = new List<Trigger>();

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

                _teleporters.AddRange(chunk.Teleporters);
                _triggers.AddRange(chunk.Triggers);
            }
        }

        public IReadOnlyList<SpawnPoint> Spawns => _spawns;

        public IReadOnlyList<Teleporter> Teleporters => _teleporters;

        public IReadOnlyList<Trigger> All => _triggers;

        public bool TryGetSpawn(string key, out SpawnPoint spawn) => _spawnsByKey.TryGetValue(key, out spawn!);

        public bool TryGetSpawn(int number, out SpawnPoint spawn) => _spawnsByNumber.TryGetValue(number, out spawn!);

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

        public static (Zone Zone, SpawnPoint Spawn)? FindSpawn(IEnumerable<Zone> zones, string id)
        {
            foreach (var zone in zones)
            {
                foreach (var chunk in zone.Chunks)
                {
                    foreach (var spawn in chunk.Spawns)
                    {
                        if (spawn.Id == id)
                        {
                            return (zone, spawn);
                        }
                    }
                }
            }

            return null;
        }

        public static Vector3 Center(SpawnPoint spawn, CollisionWorld world)
        {
            var volume = spawn.Volume;
            var top = volume.Position.Y + volume.Size.Y;
            return world.TryGetGround(new Vector3(volume.Position.X, top, volume.Position.Z), 0, top, out var ground)
                ? new Vector3(volume.Position.X, ground, volume.Position.Z)
                : volume.Position;
        }

        /// <summary>
        /// Whether <paramref name="feet"/> are inside <paramref name="volume"/>: a sphere (ellipsoid if
        /// uneven) filling its size, else from its bottom (less <see cref="BottomSlack"/>) to its top.
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
                    return local.Y >= -BottomSlack && local.Y <= volume.Size.Y && Square(local.X / half.X) + Square(local.Z / half.Z) <= 1;
                default:
                    return local.Y >= -BottomSlack && local.Y <= volume.Size.Y && MathF.Abs(local.X) <= half.X && MathF.Abs(local.Z) <= half.Z;
            }
        }

        public static (Vector3 Feet, float Facing) Arrive(
            TriggerVolume volume,
            SpawnFacing facing,
            CollisionWorld world,
            MovementRules rules,
            Random random,
            ISet<int>? passThrough = null
        )
        {
            var top = volume.Position.Y + volume.Size.Y;
            var lowest = volume.Position.Y - rules.StepHeight;
            var feet = volume.Position;
            var found = false;
            for (var i = 0; i < Tries && !found; i++)
            {
                found = TryStand(RandomSpot(volume, rules.BodyRadius, random), out feet);
            }

            if (!found && !TryStand(volume.Position, out feet))
            {
                feet = volume.Position;
            }

            var center = volume.Position;
            var yaw = facing switch
            {
                SpawnFacing.Direction => volume.Yaw * MathF.PI / 180,
                SpawnFacing.TowardPoint when Vector2.Distance(new Vector2(feet.X, feet.Z), new Vector2(center.X, center.Z)) > 0.01f => MathF.Atan2(
                    center.X - feet.X,
                    center.Z - feet.Z
                ),
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
                return !world.IsBlocked(standing, rules.BodyRadius, rules.BodyHeight, passThrough);
            }
        }

        private static Vector3 RandomSpot(TriggerVolume volume, float margin, Random random)
        {
            var half = Vector3.Max(volume.Size / 2 - new Vector3(margin), Vector3.Zero);
            float x,
                z;
            if (volume.Kind == PrimitiveKind.Box)
            {
                x = (float)(random.NextDouble() * 2 - 1) * half.X;
                z = (float)(random.NextDouble() * 2 - 1) * half.Z;
            }
            else
            {
                var angle = (float)(random.NextDouble() * 2 * Math.PI);
                var distance = MathF.Sqrt((float)random.NextDouble());
                x = MathF.Sin(angle) * distance * half.X;
                z = MathF.Cos(angle) * distance * half.Z;
            }

            return ToWorld(volume, new Vector3(x, 0, z));
        }

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

using System;
using System.Collections.Generic;
using System.Numerics;
using Comet.Content;
using Comet.Simulation;
using ShapeLand.Shared.Content;

namespace ShapeLand.Shared.World
{
    /// <summary>
    /// ShapeLand's world: its one zone from content (terrain, placed objects and wall volumes), built the same way on
    /// client and server.
    /// </summary>
    public static class ShapeLandWorld
    {
        /// <summary>Where players appear: the zone's default spawn point, on the ground.</summary>
        public static Vector3 SpawnPoint(ShapeLandContent content, CollisionWorld world)
        {
            var spawn = DefaultSpawn(content);
            world.TryGetGround(spawn, content.Movement.BodyRadius, spawn.Y + 1000, out var ground);
            return new Vector3(spawn.X, ground, spawn.Z);
        }

        /// <summary>
        /// The highest ground under a position at or below it (terrain, or a mesh's top under the shared body), or
        /// null over a hole or past the edge: for dead-reckoning other players (Comet.Client's RemoteEntities).
        /// </summary>
        public static Func<Vector3, float?> GroundHeight(ShapeLandContent content, CollisionWorld world) =>
            feet => world.TryGetGround(feet, content.Movement.BodyRadius, feet.Y, out var height) ? height : null;

        /// <summary>
        /// Other players moving faster than this (across the ground, or upwards) are drawn jumping rather than
        /// gliding: half again the fastest shape's top speed or jump speed, so only respawns, snap-backs and speed
        /// cheats exceed it.
        /// </summary>
        public static float TeleportSpeed(ShapeLandContent content)
        {
            var fastest = 0f;
            foreach (var shape in content.Shapes.All)
            {
                fastest = System.Math.Max(fastest, System.Math.Max(shape.MaxSpeed, shape.JumpVelocity));
            }

            return 1.5f * fastest;
        }

        /// <summary>The world from <paramref name="content"/>'s zone, the same on client and server.</summary>
        public static CollisionWorld Create(ShapeLandContent content)
        {
            var zone = Zone(content);
            var meshes = new List<CollisionMesh>();
            foreach (var chunk in zone.Chunks)
            {
                foreach (var placement in chunk.Placements)
                {
                    meshes.Add(CollisionMesh.Placed(content.Assets[placement.Asset].Collision, placement.Matrix()));
                }

                foreach (var volume in chunk.Volumes)
                {
                    if (volume.Kind == VolumeKind.Wall)
                    {
                        meshes.Add(CollisionMesh.Box(volume.Size, Matrix4x4.CreateFromQuaternion(ZoneGrid.Rotation(volume.Rotation)) * Matrix4x4.CreateTranslation(volume.Position)));
                    }
                }
            }

            return new CollisionWorld(new ZoneTerrain(zone), meshes, content.Settings.World.KillHeight);
        }

        /// <summary>The zone ShapeLand plays in: the one its settings name (<c>world.zone</c>).</summary>
        public static Zone Zone(ShapeLandContent content) => content.Zones[content.Settings.World.Zone];

        private static Vector3 DefaultSpawn(ShapeLandContent content)
        {
            foreach (var chunk in Zone(content).Chunks)
            {
                foreach (var spawn in chunk.Spawns)
                {
                    if (spawn.IsDefault)
                    {
                        return spawn.Position;
                    }
                }
            }

            return Vector3.Zero;
        }
    }
}

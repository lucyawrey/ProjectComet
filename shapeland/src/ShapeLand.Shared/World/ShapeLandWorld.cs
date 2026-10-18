using System;
using System.Collections.Generic;
using System.Numerics;
using Comet.Content;
using Comet.Simulation;
using ShapeLand.Shared.Content;

namespace ShapeLand.Shared.World
{
    /// <summary>
    /// ShapeLand's world: a zone from content (terrain, placed objects and wall volumes), built the same way on client
    /// and server. A server plays one zone, the default unless it's told another, and names it in its Welcome.
    /// </summary>
    public static class ShapeLandWorld
    {
        /// <summary>Where players appear: <paramref name="zone"/>'s default spawn point, on the ground.</summary>
        public static Vector3 SpawnPoint(ShapeLandContent content, Zone zone, CollisionWorld world)
        {
            var spawn = DefaultSpawn(zone);
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

        /// <summary>The world of <paramref name="zone"/>, one of <paramref name="content"/>'s, the same on client and server.</summary>
        public static CollisionWorld Create(ShapeLandContent content, Zone zone)
        {
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

        /// <summary>The zone a server plays unless it's told another (<c>world.default_zone</c>).</summary>
        public static Zone DefaultZone(ShapeLandContent content) => content.Zones[content.Settings.World.DefaultZone];

        private static Vector3 DefaultSpawn(Zone zone)
        {
            foreach (var chunk in zone.Chunks)
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

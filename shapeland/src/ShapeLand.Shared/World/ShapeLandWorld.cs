using System;
using System.Collections.Generic;
using System.Numerics;
using Comet.Content;
using Comet.Simulation;
using ShapeLand.Shared.Content;

namespace ShapeLand.Shared.World
{
    /// <summary>
    /// ShapeLand's world: each zone from content (terrain, placed objects, trigger pads and wall volumes), built the
    /// same way on client and server. A server plays every zone; each player is in one at a time.
    /// </summary>
    public static class ShapeLandWorld
    {
        /// <summary>
        /// Where a player of <paramref name="shape"/> starts: the shape's spawn point, and the zone it's in (the
        /// content build checks it's in one).
        /// </summary>
        public static (Zone Zone, SpawnPoint Spawn) StartOf(ShapeLandContent content, Shape shape) =>
            FindSpawn(content, shape.Spawn) ?? throw new System.InvalidOperationException($"{shape.Id}'s spawn '{shape.Spawn}' isn't in any zone.");

        /// <summary>The spawn point with id <paramref name="id"/> and its zone, or null if no zone has it.</summary>
        public static (Zone Zone, SpawnPoint Spawn)? FindSpawn(ShapeLandContent content, string id)
        {
            foreach (var zone in content.Zones.All)
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

        /// <summary>The ground in the middle of a spawn point's volume (for a camera to look at), or its bottom if there's none.</summary>
        public static Vector3 Center(SpawnPoint spawn, CollisionWorld world)
        {
            var volume = spawn.Volume;
            var top = volume.Position.Y + volume.Size.Y;
            return world.TryGetGround(new Vector3(volume.Position.X, top, volume.Position.Z), 0, top, out var ground)
                ? new Vector3(volume.Position.X, ground, volume.Position.Z)
                : volume.Position;
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
                    var mesh = Placed(content, placement);
                    mesh.Name = placement.Name;
                    mesh.StartsOff = placement.Off;
                    meshes.Add(mesh);
                }

                foreach (var pad in Pads(chunk))
                {
                    meshes.Add(Placed(content, pad));
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

        /// <summary>The pads of a chunk's spawn points, teleporters and triggers: solid, and never flipped.</summary>
        public static IEnumerable<Placement> Pads(ZoneChunk chunk)
        {
            foreach (var spawn in chunk.Spawns)
            {
                if (spawn.Volume.Pad is { } pad)
                {
                    yield return pad;
                }
            }

            foreach (var teleporter in chunk.Teleporters)
            {
                if (teleporter.Volume.Pad is { } pad)
                {
                    yield return pad;
                }
            }

            foreach (var trigger in chunk.Triggers)
            {
                if (trigger.Volume.Pad is { } pad)
                {
                    yield return pad;
                }
            }
        }

        private static CollisionMesh Placed(ShapeLandContent content, Placement placement)
        {
            var triangles = placement.Primitive is null
                ? content.Props[placement.Prop].Collision
                : PrimitiveMesher.For(placement, content.Settings.Primitives);
            return CollisionMesh.Placed(triangles, placement.Matrix());
        }
    }
}

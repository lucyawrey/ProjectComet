using System.Collections.Generic;
using System.Numerics;
using Comet.Content;
using Comet.Simulation;
using ShapeLand.Shared.Content;

namespace ShapeLand.Shared.World
{
    /// <summary>Built the same way on client and server.</summary>
    public static class ShapeLandWorld
    {
        public static (Zone Zone, SpawnPoint Spawn) StartOf(ShapeLandContent content, Shape shape) =>
            Triggers.FindSpawn(content.Zones.All, shape.Spawn)
            ?? throw new System.InvalidOperationException($"{shape.Id}'s spawn '{shape.Spawn}' isn't in any zone.");

        /// <summary>
        /// Others moving faster than this are drawn jumping rather than gliding: half again the fastest shape's top
        /// speed or jump speed, so only respawns, snap-backs and speed cheats exceed it.
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

        public const string PropKind = "prop",
            PrimitiveKind = "primitive",
            WallKind = "wall";

        public static CollisionWorld Create(ShapeLandContent content, Zone zone)
        {
            var meshes = new List<CollisionMesh>();
            foreach (var chunk in zone.Chunks)
            {
                foreach (var placement in chunk.Placements)
                {
                    var mesh = Placed(content, placement);
                    mesh.Tag = placement.Tag;
                    mesh.StartsOff = placement.Off;
                    mesh.Kind = placement.Primitive is null ? PropKind : PrimitiveKind;
                    meshes.Add(mesh);
                }

                foreach (var pad in Pads(chunk))
                {
                    var mesh = Placed(content, pad);
                    mesh.Kind = PrimitiveKind;
                    meshes.Add(mesh);
                }

                foreach (var volume in chunk.Volumes)
                {
                    if (volume.Kind == VolumeKind.Wall)
                    {
                        var mesh = CollisionMesh.Box(
                            volume.Size,
                            Matrix4x4.CreateFromQuaternion(ZoneGrid.Rotation(volume.Rotation)) * Matrix4x4.CreateTranslation(volume.Position)
                        );
                        mesh.Kind = WallKind;
                        meshes.Add(mesh);
                    }
                }
            }

            return new CollisionWorld(new ZoneTerrain(zone), meshes, zone.KillHeight ?? content.Settings.World.KillHeight, content.Movement.MaxSlope);
        }

        /// <summary>Solid, and never flipped.</summary>
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

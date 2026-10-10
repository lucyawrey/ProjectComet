using System;
using System.Collections.Generic;
using System.Numerics;
using Comet.Simulation;
using ShapeLand.Shared.Content;

namespace ShapeLand.Shared.World
{
    /// <summary>
    /// The phase 0 world: the test island from content, centred on the origin, plus a few hard-coded blocks to
    /// jump on, until zone files exist. Built the same way on client and server.
    /// </summary>
    public static class ShapeLandWorld
    {
        public const float TerrainSpacing = 2f;
        public const float KillHeight = -30f;

        public static readonly MovementRules Rules = new MovementRules();

        /// <summary>Where players appear: the centre of the island, on the ground.</summary>
        public static Vector3 SpawnPoint(CollisionWorld world)
        {
            world.TryGetGround(Vector3.Zero, Rules.BodyRadius, 1000, out var ground);
            return new Vector3(0, ground, 0);
        }

        /// <summary>
        /// The highest ground under a position at or below it (terrain, or a block's top under the shared body), or
        /// null over a hole or past the edge: for dead-reckoning other players (Comet.Client's RemoteEntities).
        /// </summary>
        public static Func<Vector3, float?> GroundHeight(CollisionWorld world) =>
            feet => world.TryGetGround(feet, Rules.BodyRadius, feet.Y, out var height) ? height : null;

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

        public static CollisionWorld Create(ShapeLandContent content)
        {
            var terrain = TerrainGround.Centred(content.Terrain, TerrainSpacing, Vector3.Zero);
            var blocks = new List<Box>
            {
                // A low step, a block to jump onto, a taller one to jump up to from the first, and a long platform.
                Block(terrain, 6, 2, new Vector3(2, 0.3f, 2)),
                Block(terrain, 10, -4, new Vector3(3, 1f, 3)),
                Block(terrain, 13, -7, new Vector3(2, 2f, 2)),
                Block(terrain, -8, 8, new Vector3(6, 0.8f, 2)),
            };
            return new CollisionWorld(terrain, blocks, KillHeight);
        }

        // Stands a block on the terrain at (x, z), sunk 0.5 m so no gap shows on slopes.
        private static Box Block(TerrainGround terrain, float x, float z, Vector3 size)
        {
            terrain.TryGetHeight(x, z, out var ground);
            const float sink = 0.5f;
            return Box.Standing(x, z, ground - sink, new Vector3(size.X, size.Y + sink, size.Z));
        }
    }
}

using System.Collections.Generic;
using System.Numerics;

namespace Comet.Simulation
{
    /// <summary>
    /// The meshes one player passes through: ones a flip turned on around them, until they're fully out. Pass it to
    /// <see cref="PlayerMotor"/> and the world's queries; the server's movement check, the client and bots each keep
    /// one per player, so they agree on where a player may stand.
    /// </summary>
    public sealed class PassThrough : HashSet<int>
    {
        /// <summary>
        /// After the meshes tagged <paramref name="tag"/> flipped: adds each now on that a body standing at
        /// <paramref name="feet"/> overlaps.
        /// </summary>
        public void Flipped(CollisionWorld world, string tag, Vector3 feet, float radius, float bodyHeight)
        {
            foreach (var mesh in world.MeshesTagged(tag))
            {
                if (world.IsOn(mesh) && world.Overlaps(mesh, feet, radius, bodyHeight))
                {
                    Add(mesh);
                }
            }
        }

        /// <summary>Drops the meshes a body standing at <paramref name="feet"/> is fully out of.</summary>
        public void Prune(CollisionWorld world, Vector3 feet, float radius, float bodyHeight)
        {
            if (Count > 0)
            {
                RemoveWhere(mesh => !world.Overlaps(mesh, feet, radius, bodyHeight));
            }
        }
    }
}

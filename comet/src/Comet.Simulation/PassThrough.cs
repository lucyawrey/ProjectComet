using System.Collections.Generic;
using System.Numerics;

namespace Comet.Simulation
{
    public sealed class PassThrough : HashSet<int>
    {
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

        public void Prune(CollisionWorld world, Vector3 feet, float radius, float bodyHeight)
        {
            if (Count > 0)
            {
                RemoveWhere(mesh => !world.Overlaps(mesh, feet, radius, bodyHeight));
            }
        }
    }
}

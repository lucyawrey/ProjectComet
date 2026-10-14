using System.Numerics;
using Comet.Content;
using SharpGLTF.Schema2;
using Asset = Comet.Content.Asset;

namespace Comet.ContentBuild;

/// <summary>
/// Reads an asset's <c>.glb</c> model for its collision triangles and bounds, in the space Unity shows it in: glTF is
/// right-handed and Unity left-handed, and Unity's importer (glTFast) converts by negating X, so this does the same
/// and reverses each triangle's winding to match.
/// </summary>
public static class ModelBaker
{
    /// <summary>Fills in <paramref name="asset"/>'s collision and bounds from the model at <paramref name="path"/>; throws <see cref="ContentException"/> if it can't.</summary>
    public static void Bake(Asset asset, string path)
    {
        ModelRoot model;
        try
        {
            model = ModelRoot.Load(path);
        }
        catch (Exception e) when (e is IOException or SharpGLTF.Validation.ModelException or InvalidOperationException or ArgumentException)
        {
            throw new ContentException($"Can't read the model {path}: {e.Message}", e);
        }

        var meshes = (model.DefaultScene?.VisualChildren ?? Enumerable.Empty<Node>())
            .SelectMany(Descendants)
            .Where(n => n.Mesh is not null)
            .ToList();
        if (meshes.Count == 0)
        {
            throw new ContentException($"The model {path} has no meshes.");
        }

        var collisionNodes = meshes.Where(n => n.Name?.EndsWith(Asset.CollisionSuffix, StringComparison.Ordinal) == true).ToList();
        var collision = new List<Vector3>();
        foreach (var node in collisionNodes.Count > 0 ? collisionNodes : meshes)
        {
            collision.AddRange(Triangles(node));
        }

        var all = meshes.SelectMany(Triangles).ToList();
        asset.Collision = collision.ToArray();
        asset.BoundsMin = all.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        asset.BoundsMax = all.Aggregate(new Vector3(float.MinValue), Vector3.Max);
    }

    private static IEnumerable<Node> Descendants(Node node) => node.VisualChildren.SelectMany(Descendants).Prepend(node);

    // A node's triangles in the model's space, converted to Unity's: X negated, so the winding reverses too.
    private static IEnumerable<Vector3> Triangles(Node node)
    {
        var world = node.WorldMatrix;
        foreach (var primitive in node.Mesh.Primitives)
        {
            var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
            if (positions is null)
            {
                continue;
            }

            foreach (var (a, b, c) in primitive.GetTriangleIndices())
            {
                yield return ToUnity(Vector3.Transform(positions[a], world));
                yield return ToUnity(Vector3.Transform(positions[c], world));
                yield return ToUnity(Vector3.Transform(positions[b], world));
            }
        }
    }

    private static Vector3 ToUnity(Vector3 gltf) => new(-gltf.X, gltf.Y, gltf.Z);
}

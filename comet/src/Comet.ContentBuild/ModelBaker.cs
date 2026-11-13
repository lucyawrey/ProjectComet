using System.Numerics;
using Comet.Content;
using SharpGLTF.Schema2;
using Prop = Comet.Content.Prop;

namespace Comet.ContentBuild;

/// <summary>glTF is right-handed and Unity left-handed, so X is negated (as Unity's glTFast does) and each triangle's winding reversed.</summary>
public static class ModelBaker
{
    /// <summary>Throws <see cref="ContentException"/> if the model can't be read.</summary>
    public static void Bake(Prop prop, string path)
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

        var meshes = (model.DefaultScene?.VisualChildren ?? Enumerable.Empty<Node>()).SelectMany(Descendants).Where(n => n.Mesh is not null).ToList();
        if (meshes.Count == 0)
        {
            throw new ContentException($"The model {path} has no meshes.");
        }

        var collisionNodes = meshes.Where(n => n.Name?.EndsWith(Prop.CollisionSuffix, StringComparison.Ordinal) == true).ToList();
        var collision = new List<Vector3>();
        foreach (var node in collisionNodes.Count > 0 ? collisionNodes : meshes)
        {
            collision.AddRange(Triangles(node));
        }

        var all = meshes.SelectMany(Triangles).ToList();
        prop.Collision = collision.ToArray();
        prop.BoundsMin = all.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        prop.BoundsMax = all.Aggregate(new Vector3(float.MinValue), Vector3.Max);
    }

    private static IEnumerable<Node> Descendants(Node node) => node.VisualChildren.SelectMany(Descendants).Prepend(node);

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

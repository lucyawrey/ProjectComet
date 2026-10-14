using System.Numerics;
using SharpGLTF.Schema2;

namespace Comet.ContentBuild.Tests;

internal static class TestModels
{
    // A glTF model with one triangle per named node, each lying flat at y = 1, wound to face up (+Y) in glTF.
    public static string Model(params (string Name, Vector3 Offset)[] nodes)
    {
        var model = ModelRoot.CreateModel();
        var scene = model.UseScene("scene");
        foreach (var (name, offset) in nodes)
        {
            var mesh = model.CreateMesh(name);
            var positions = model.CreateAccessor();
#pragma warning disable CS0618 // SetData with DimensionType and EncodingType: plain enough for one test triangle
            positions.SetData(model.UseBufferView(Floats(new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 0))), 0, 3, DimensionType.VEC3, EncodingType.FLOAT, false);
#pragma warning restore CS0618
            mesh.CreatePrimitive().SetVertexAccessor("POSITION", positions);
            var node = scene.CreateNode(name);
            node.Mesh = mesh;
            node.LocalMatrix = Matrix4x4.CreateTranslation(offset);
        }

        var path = Path.Combine(Path.GetTempPath(), $"comet-model-{Guid.NewGuid():N}.glb");
        model.SaveGLB(path);
        return path;
    }

    private static byte[] Floats(params Vector3[] points) =>
        points.SelectMany(p => new[] { p.X, p.Y, p.Z }).SelectMany(BitConverter.GetBytes).ToArray();
}

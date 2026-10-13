using System.IO;
using System.Threading.Tasks;
using GLTFast.Export;
using ShapeLand.Shared.Content;
using UnityEditor;
using UnityEngine;

namespace ShapeLand.Client.Editor
{
    /// <summary>
    /// Exports each player shape's generated mesh as glTF (<c>artifacts/models/shapes/&lt;Shape&gt;.glb</c>), a
    /// starting point to model from in Blender: a root at the shape's feet, with the body above it and the eyes on
    /// the body. Not checked in; the edited models are, and the shape prefabs use those.
    /// </summary>
    public static class ShapeModelExport
    {
        private static string OutputFolder => Path.Combine(Application.dataPath, "..", "..", "..", "artifacts", "models", "shapes");

        [MenuItem("ShapeLand/Export Shape Models")]
        public static void Export() => _ = ExportAll();

        /// <summary>For batch mode (<c>-executeMethod</c>, without <c>-quit</c>): exits once the files are written.</summary>
        public static void ExportAndExit()
        {
            var export = ExportAll();
            EditorApplication.update += () =>
            {
                if (export.IsCompleted)
                {
                    EditorApplication.Exit(export.IsCompletedSuccessfully && export.Result ? 0 : 1);
                }
            };
        }

        private static async Task<bool> ExportAll()
        {
            Directory.CreateDirectory(OutputFolder);
            var shapeMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shape.mat");
            var eyeMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Eyes.mat");
            var succeeded = true;
            foreach (var kind in new[] { MeshKind.Cube, MeshKind.Diamond, MeshKind.Pyramid })
            {
                var look = WorldMeshes.Looks[kind];
                var root = new GameObject(kind.ToString());
                try
                {
                    var body = Part("Body", WorldMeshes.Shape(kind, look), shapeMaterial, root.transform);
                    body.transform.localPosition = new Vector3(0, look.Hover, 0);
                    Part("Eyes", WorldMeshes.Eyes(kind, look), eyeMaterial, body.transform);

                    var export = new GameObjectExport(new ExportSettings { Format = GltfFormat.Binary, FileConflictResolution = FileConflictResolution.Overwrite });
                    export.AddScene(new[] { root }, kind.ToString());
                    var path = Path.GetFullPath(Path.Combine(OutputFolder, $"{kind}.glb"));
                    var saved = await export.SaveToFileAndDispose(path);
                    Debug.Log(saved ? $"Exported {path}" : $"Couldn't export {path}");
                    succeeded &= saved;
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }

            return succeeded;
        }

        private static GameObject Part(string name, Mesh mesh, Material material, Transform parent)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = material;
            return part;
        }
    }
}

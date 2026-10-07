using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using Comet.Content;
using Comet.Protocol;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;
using ShapeLand.Shared.Content;
using UnityEngine;
using UnityEngine.Networking;

namespace ShapeLand.Client
{
    /// <summary>
    /// Step 2b's check: the shared packages work in this build. Loads the compiled content, finds each shape by key,
    /// draws the test island and round-trips a protocol frame, then logs one PASS or FAIL line. Run a player with
    /// <c>-quit-after-check</c> to exit once it's done.
    /// </summary>
    public sealed class SharedPackageCheck : MonoBehaviour
    {
        public const string LogPrefix = "SHARED PACKAGE CHECK";

        [SerializeField] private Material terrainMaterial;

        private readonly List<string> _lines = new List<string>();
        private string _verdict = "running…";

        /// <summary>Null while running, then whether the check passed.</summary>
        public bool? Passed { get; private set; }

        /// <summary>The PASS or FAIL line, once the check has finished.</summary>
        public string Result { get; private set; } = "";

        private IEnumerator Start()
        {
            var path = Path.Combine(Application.streamingAssetsPath, "content.bin");
            var url = path.Contains("://") ? path : "file://" + path;
            using var request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Couldn't load {url}: {request.error}");
                }

                Run(request.downloadHandler.data);
                Finish(true, "");
            }
            catch (Exception e)
            {
                Finish(false, e.ToString());
            }
        }

        private void Run(byte[] bytes)
        {
            var content = ShapeLandContent.Load(new MemoryStream(bytes));
            foreach (var key in new[] { "shape.cube", "shape.diamond", "shape.pyramid" })
            {
                var shape = content.Shapes[key];
                _lines.Add($"{shape.Id} #{shape.Number}: {shape.DisplayName}, {shape.Mesh}, {shape.MaxSpeed} m/s, jump {shape.JumpVelocity} m/s");
            }

            Expect(!content.Shapes.TryGet("shape.sphere", out _), "an unknown key was found");

            var terrain = content.Terrain;
            var holes = terrain.Samples.Count(s => s == Heightmap.Hole);
            Expect(holes > 0 && holes < terrain.Samples.Length, "the test island should have both ground and holes");
            var triangles = BuildIsland(terrain);
            _lines.Add($"terrain: {terrain.SizeX}×{terrain.SizeZ} samples, {holes} holes, {triangles} triangles");

            var writer = new MessageWriter();
            writer.BeginFrame(42);
            writer.Write(MessageIds.Welcome, new Welcome { EntityId = 7, TickRate = 30 });
            var reader = FrameReader.Create(writer.WrittenMemory);
            Expect(reader.TryReadNext(out var id, out var payload) && id == MessageIds.Welcome, "the frame didn't hold the Welcome message");
            var welcome = FrameReader.Decode<Welcome>(payload);
            Expect(reader.Tick == 42 && welcome.EntityId == 7 && welcome.TickRate == 30, "the Welcome message didn't round-trip");
            _lines.Add($"protocol: frame {reader.Tick} round-tripped Welcome(entity {welcome.EntityId}, {welcome.TickRate} Hz)");

            var required = typeof(Shape).GetProperty(nameof(Shape.Id))!.GetCustomAttributes(typeof(JsonRequiredAttribute), false).Length;
            _lines.Add($"System.Text.Json: loaded; [JsonRequired] on Shape.Id {(required == 1 ? "kept" : "stripped (harmless at run time)")}");
        }

        // A flat-shaded mesh: each triangle gets its own vertices, and exists only where all three samples are ground.
        private int BuildIsland(Heightmap map)
        {
            const float spacing = 2f;
            var vertices = new List<Vector3>();
            var origin = new Vector3(-(map.SizeX - 1) * spacing / 2, 0, -(map.SizeZ - 1) * spacing / 2);

            void Triangle((int X, int Z) a, (int X, int Z) b, (int X, int Z) c)
            {
                if (!map.TryGetHeight(a.X, a.Z, out var ha) || !map.TryGetHeight(b.X, b.Z, out var hb) || !map.TryGetHeight(c.X, c.Z, out var hc))
                {
                    return;
                }

                vertices.Add(origin + new Vector3(a.X * spacing, ha, a.Z * spacing));
                vertices.Add(origin + new Vector3(b.X * spacing, hb, b.Z * spacing));
                vertices.Add(origin + new Vector3(c.X * spacing, hc, c.Z * spacing));
            }

            for (var z = 0; z < map.SizeZ - 1; z++)
            {
                for (var x = 0; x < map.SizeX - 1; x++)
                {
                    Triangle((x, z), (x, z + 1), (x + 1, z));
                    Triangle((x + 1, z), (x, z + 1), (x + 1, z + 1));
                }
            }

            var mesh = new Mesh { name = "Test island" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(Enumerable.Range(0, vertices.Count).ToArray(), 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var island = new GameObject("Test island");
            island.AddComponent<MeshFilter>().sharedMesh = mesh;
            island.AddComponent<MeshRenderer>().sharedMaterial = terrainMaterial;
            return vertices.Count / 3;
        }

        private static void Expect(bool condition, string failure)
        {
            if (!condition)
            {
                throw new Exception(failure);
            }
        }

        private void Finish(bool passed, string error)
        {
            _verdict = passed ? "PASS" : "FAIL";
            var platform = $"{Application.platform}, {(Application.isEditor ? "editor" : "player")}, Unity {Application.unityVersion}";
            var details = passed ? string.Join("; ", _lines) : error;
            var line = $"{LogPrefix} {_verdict} ({platform}): {details}";
            Passed = passed;
            Result = line;
            if (passed)
            {
                Debug.Log(line);
            }
            else
            {
                Debug.LogError(line);
            }

            if (!Application.isEditor && Environment.GetCommandLineArgs().Contains("-quit-after-check"))
            {
                Application.Quit(passed ? 0 : 1);
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, Screen.width - 32, Screen.height - 32));
            GUILayout.Label($"Shared package check: {_verdict}");
            foreach (var line in _lines)
            {
                GUILayout.Label(line);
            }

            GUILayout.EndArea();
        }
    }
}

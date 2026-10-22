using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Comet.Simulation;
using NUnit.Framework;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.World;
using UnityEditor;
using UnityEngine;

namespace ShapeLand.Client.Tests
{
    // The client's rules and pieces that need no server: movement input, server addresses, fades, shapes and
    // interface text.
    public class ClientRulesTests
    {
        [TestCase(0, 0, 1, 0, 1)]
        [TestCase(90, 0, 1, 1, 0)]
        [TestCase(90, 1, 0, 0, -1)]
        [TestCase(180, 1, 0, -1, 0)]
        public void MovementIsRelativeToTheCamera(float yaw, float inputX, float inputY, float expectedX, float expectedZ)
        {
            var move = LocalPlayer.CameraRelative(new Vector2(inputX, inputY), yaw);
            Assert.That(move.X, Is.EqualTo(expectedX).Within(1e-5f));
            Assert.That(move.Y, Is.EqualTo(expectedZ).Within(1e-5f));
        }

        [TestCase("http://localhost:5080/", "ws://localhost:5080/ws")]
        [TestCase("https://example.org/play/index.html", "wss://example.org/ws")]
        [TestCase("https://example.org/?server=game.example.org%3A5080", "wss://game.example.org:5080/ws")]
        [TestCase("http://localhost:8000/?server=localhost%3A5080", "ws://localhost:5080/ws")]
        [TestCase("http://localhost:8000/?debug=1&server=wss://game.example.org/ws", "wss://game.example.org/ws")]
        [TestCase("file:///tmp/index.html", "localhost:5080")]
        public void WebBuildsConnectToTheServerTheyCameFrom(string page, string expected)
        {
            Assert.That(JoinClient.AddressForPage(page, "localhost:5080"), Is.EqualTo(expected));
        }

        // A page whose deploy names its game server (config.js on Vercel): that server, unless ?server= says otherwise.
        [TestCase("https://shapeland.example.org/", "wss://server.shapeland.example.org/ws")]
        [TestCase("https://shapeland.example.org/?server=localhost%3A5080", "wss://localhost:5080/ws")]
        public void WebBuildsConnectToTheServerTheirPageNames(string page, string expected)
        {
            Assert.That(JoinClient.AddressForPage(page, "localhost:5080", "wss://server.shapeland.example.org/ws"), Is.EqualTo(expected));
        }

        [TestCase("localhost:5080", "ws://localhost:5080/ws")]
        [TestCase(" wss://game.example.org/ws ", "wss://game.example.org/ws")]
        [TestCase("local host:5080", null)]
        [TestCase("localhost:abc", null)]
        [TestCase("localhost:99999", null)]
        [TestCase("http://localhost:5080/ws", null)]
        [TestCase("", null)]
        public void ServerAddressesAreCheckedBeforeConnecting(string address, string expected)
        {
            var valid = JoinClient.TryServerUrl(address, out var url);
            Assert.That(valid ? url.ToString() : null, Is.EqualTo(expected));
        }

        [Test]
        public void FallingFadesBeforeTheKillHeightAndEasesBack()
        {
            const float kill = -30;
            Assert.That(ShapeFade.FromFalling(kill + ShapeFade.FadeDistance, kill), Is.EqualTo(0));
            Assert.That(ShapeFade.FromFalling(kill + ShapeFade.FadeDistance / 2, kill), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(ShapeFade.FromFalling(kill - 5, kill), Is.EqualTo(1));

            // Falling fades at once, with no easing; a respawn there moves it on at once, and it eases back in.
            var fade = new ShapeFade();
            fade.Step(0, 0.01f, kill, kill);
            Assert.That(fade.Faded, Is.EqualTo(1));
            fade.Moved(0);
            Assert.That(fade.Step(0, 0.01f, 3, kill), Is.True, "Already faded: drawn at the spawn at once.");
            Assert.That(fade.Holding, Is.False);
            fade.Step(ShapeFade.FadeInSeconds / 2, ShapeFade.FadeInSeconds / 2, 3, kill);
            Assert.That(fade.Faded, Is.EqualTo(0.5f).Within(1e-5f));
            fade.Step(ShapeFade.FadeInSeconds, ShapeFade.FadeInSeconds, 3, kill);
            Assert.That(fade.Faded, Is.EqualTo(0));
        }

        [Test]
        public void ATeleportHoldsTheShapeUntilFadedOut()
        {
            const float kill = -30;
            var fade = new ShapeFade();

            // The pad finishes charging; the move comes partway through the fade-out, which it waits for.
            fade.Depart(0);
            fade.Step(0.1f, 0.1f, 3, kill);
            Assert.That(fade.Faded, Is.EqualTo(0.1f / ShapeFade.FadeOutSeconds).Within(1e-5f));
            fade.Moved(0.1f);
            Assert.That(fade.Step(0.2f, 0.1f, 3, kill), Is.False);
            Assert.That(fade.Holding, Is.True);
            Assert.That(fade.Step(ShapeFade.FadeOutSeconds, 0.05f, 3, kill), Is.True, "Faded out: drawn at the target.");
            Assert.That(fade.Holding, Is.False);

            // Hidden while the camera glides, then fading in.
            fade.HideFor(ShapeFade.FadeOutSeconds, 0.5f);
            fade.Step(ShapeFade.FadeOutSeconds + 0.4f, 0.4f, 3, kill);
            Assert.That(fade.Faded, Is.EqualTo(1));
            fade.Step(ShapeFade.FadeOutSeconds + 0.5f + ShapeFade.FadeInSeconds, ShapeFade.FadeInSeconds, 3, kill);
            Assert.That(fade.Faded, Is.EqualTo(0));
        }

        [Test]
        public void SteppingOffBeforeThePadFiresFadesBackIn()
        {
            const float kill = -30;
            var fade = new ShapeFade();

            // Started a fade-out ahead of the pad firing, which the player steps off before.
            fade.DepartFrom(0);
            fade.Step(0.1f, 0.1f, 3, kill);
            Assert.That(fade.Faded, Is.EqualTo(0.1f / ShapeFade.FadeOutSeconds).Within(1e-5f));
            fade.Cancel();
            Assert.That(fade.Departing, Is.False);
            fade.Step(0.1f + ShapeFade.FadeInSeconds, ShapeFade.FadeInSeconds, 3, kill);
            Assert.That(fade.Faded, Is.EqualTo(0));
        }

        [Test]
        public void AFadeOutWithNoMoveFadesBackIn()
        {
            const float kill = -30;
            var fade = new ShapeFade();
            fade.Depart(0);
            fade.Step(ShapeFade.FadeOutSeconds, ShapeFade.FadeOutSeconds, 3, kill);
            Assert.That(fade.Faded, Is.EqualTo(1));
            Assert.That(fade.Step(ShapeFade.FadeOutSeconds + ShapeFade.WaitSeconds, ShapeFade.WaitSeconds, 3, kill), Is.False);
            fade.Step(ShapeFade.FadeOutSeconds + ShapeFade.WaitSeconds + ShapeFade.FadeInSeconds, ShapeFade.FadeInSeconds, 3, kill);
            Assert.That(fade.Faded, Is.EqualTo(0));
        }

        [Test]
        public void ShapesSwapToTransparentMaterialsWhileFading()
        {
            var shape = Build("shape.cube");
            var body = shape.Body[0];
            var eyes = shape.Eyes[0];
            try
            {
                shape.SetColors(0xFF0000, 0x0000FF);
                var opaque = body.sharedMaterial;
                Assert.That(opaque.color, Is.EqualTo(Color.red));

                shape.Faded = 0.25f;
                Assert.That(body.sharedMaterial, Is.Not.SameAs(opaque));
                Assert.That(body.sharedMaterial.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent));
                Assert.That(body.sharedMaterial.color.r, Is.EqualTo(1));
                Assert.That(body.sharedMaterial.color.a, Is.EqualTo(0.75f).Within(1e-5f));
                Assert.That(eyes.sharedMaterial.color.b, Is.EqualTo(1));

                shape.Faded = 1;
                Assert.That(body.enabled || eyes.enabled, Is.False, "A shape fully faded is still drawn.");

                shape.Faded = 0;
                Assert.That(body.enabled && eyes.enabled, Is.True);
                Assert.That(body.sharedMaterial, Is.SameAs(opaque));
            }
            finally
            {
                Object.DestroyImmediate(shape.gameObject);
            }
        }

        // Each shape's look stands on the feet (plus its hover), about a player's height, facing +Z with its eyes on
        // the front, and colored through its body and eyes.
        [TestCase("shape.cube")]
        [TestCase("shape.diamond")]
        [TestCase("shape.pyramid")]
        public void EveryShapeHasALook(string id)
        {
            var shape = Build(id);
            try
            {
                Assert.That(shape.Body, Is.Not.Empty, "The look has no body part.");
                Assert.That(shape.Eyes, Is.Not.Empty, "The look has no eyes.");
                var body = shape.Body[0];
                Assert.That(body.bounds.min.y, Is.InRange(0, 0.2f), "The shape doesn't stand on its feet.");
                Assert.That(body.bounds.size.y, Is.InRange(0.3f, 2), "The shape isn't about a player's height.");
                Assert.That(shape.Eyes.Average(e => e.bounds.center.z), Is.GreaterThan(0.1f), "The eyes aren't on the front (+Z).");
                Assert.That(Mathf.Abs(shape.Eyes.Average(e => e.bounds.center.x)), Is.LessThan(0.01f), "The eyes aren't centered.");

                shape.SetColors(0x00FF00, 0xFF0000);
                Assert.That(body.sharedMaterial.color, Is.EqualTo(Color.green));
                Assert.That(shape.Eyes.All(e => e.sharedMaterial.color == Color.red));
            }
            finally
            {
                Object.DestroyImmediate(shape.gameObject);
            }
        }

        [Test]
        public void TerrainFacesUpUnderUnitysNormals()
        {
            // A flat 3×3-sample piece without skirts: Unity's recalculated normals must agree with the mesher's winding.
            var zone = new Comet.Content.Zone
            {
                Pieces = new[] { new Comet.Content.TerrainPiece { Name = "main", RimSkirts = false } },
                Chunks = new[]
                {
                    new Comet.Content.ZoneChunk
                    {
                        Tiles = new[]
                        {
                            new Comet.Content.TerrainTile
                            {
                                Piece = "main",
                                Heights = new Comet.Content.Heightmap { SizeX = 3, SizeZ = 3, Scale = 1, Offset = 0, Samples = Enumerable.Repeat((ushort)2, 9).ToArray() },
                            },
                        },
                    },
                },
            };
            var chunk = TerrainMesher.Build(new ZoneTerrain(zone), 0x336699, 0).Values.Single();
            var mesh = Comet.Unity.TerrainMeshes.Build("Test", chunk);
            try
            {
                Assert.That(mesh.triangles.Length, Is.EqualTo(8 * 3));
                Assert.That(mesh.normals, Has.All.EqualTo(Vector3.up));
                Assert.That(mesh.colors32[0], Is.EqualTo(new Color32(0x33, 0x66, 0x99, 0xff)));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [TestCase(0, 45, 0)]
        [TestCase(30, 0, 0)]
        [TestCase(0, 0, 60)]
        [TestCase(10, 200, -35)]
        [TestCase(-80, 15, 170)]
        public void PlacementRotationsMatchUnitys(float x, float y, float z)
        {
            // Zones store Euler angles; Comet turns them into rotations for collision, Unity for drawing. They must agree.
            var comet = Comet.Content.ZoneGrid.Rotation(new System.Numerics.Vector3(x, y, z));
            var unity = Quaternion.Euler(x, y, z);

            var forward = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, comet);
            var right = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, comet);
            Assert.That(Vector3.Distance(Vectors.ToUnity(forward), unity * Vector3.forward), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(Vectors.ToUnity(right), unity * Vector3.right), Is.LessThan(1e-4f));
        }

        [Test]
        public void ShapeStatsAreSpeedAndJumpHeight()
        {
            var rules = new MovementRules { Gravity = 25 };
            var (speed, jump) = JoinScreen.Stats(new Shape { MaxSpeed = 5, JumpVelocity = 8.5f }, rules);
            Assert.That(speed, Is.EqualTo(5));
            Assert.That(jump, Is.EqualTo(8.5f * 8.5f / (2 * 25)).Within(1e-4f));
        }

        // Every key the scripts, UXML and web page use is in the string file, so no "[key]" shows on screen.
        [Test]
        public void EveryInterfaceTextKeyExists()
        {
            var content = TestGameServer.LoadContent();

            var groups = string.Join("|", content.Strings.Entries.Keys.Select(k => k.Split('.')[0]).Distinct());
            var used = new Regex($"(?:\"#?|data-(?:text|label|title)=\")((?:{groups})\\.[a-z0-9_.]+)\"");
            var files = Directory.GetFiles("Assets/Scripts", "*.cs")
                .Concat(Directory.GetFiles("Assets/UI", "*.uxml"))
                .Concat(new[] { "Assets/WebGLTemplates/ShapeLand/index.html" });
            var keys = files.SelectMany(file => used.Matches(File.ReadAllText(file)).Cast<Match>().Select(m => (File: file, Key: m.Groups[1].Value))).ToList();

            Assert.That(keys.Count, Is.GreaterThan(20), "The keys weren't found; has the markup changed?");
            Assert.That(keys.Where(k => !content.Strings.Contains(k.Key)).Select(k => $"{k.File}: {k.Key}"), Is.Empty);
        }

        // A shape built as the game builds it, from the content and the game's materials.
        private static ShapeBody Build(string id)
        {
            var content = TestGameServer.LoadContent();
            Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/{name}.mat");
            return GameView.BuildShape(content, content.Shapes[id], 0xFFFFFF, 0x000000, null, Load("Shape"), Load("Eyes"), Load("ShapeFade"), Load("EyesFade"));
        }
    }
}

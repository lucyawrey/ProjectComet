using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
            Assert.That(RespawnFade.FromFalling(kill + RespawnFade.FadeDistance, kill), Is.EqualTo(0));
            Assert.That(RespawnFade.FromFalling(kill + RespawnFade.FadeDistance / 2, kill), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(RespawnFade.FromFalling(kill - 5, kill), Is.EqualTo(1));

            // Back on the ground after a respawn, it eases out over the fade-in time.
            var faded = 1f;
            faded = RespawnFade.Step(faded, 3, kill, RespawnFade.FadeInSeconds / 2);
            Assert.That(faded, Is.EqualTo(0.5f).Within(1e-5f));
            faded = RespawnFade.Step(faded, 3, kill, RespawnFade.FadeInSeconds);
            Assert.That(faded, Is.EqualTo(0));

            // Falling darkens at once, with no easing.
            Assert.That(RespawnFade.Step(0, kill, kill, 0.01f), Is.EqualTo(1));
        }

        [Test]
        public void ShapesSwapToTransparentMaterialsWhileFading()
        {
            var shape = Object.Instantiate(ShapePrefab(MeshKind.Cube));
            var body = shape.Body;
            var eyes = shape.Eyes;
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

        // Each prefab is its model with a ShapeBody on the root: standing on its origin, facing +Z with its eyes on
        // the front, and colored through the body and eyes.
        [TestCase(MeshKind.Cube)]
        [TestCase(MeshKind.Diamond)]
        [TestCase(MeshKind.Pyramid)]
        public void EveryShapeHasAPrefab(MeshKind kind)
        {
            var shape = Object.Instantiate(ShapePrefab(kind));
            try
            {
                var body = shape.Body;
                var eyes = shape.Eyes;
                Assert.That(body, Is.Not.Null.And.Not.SameAs(eyes), "The prefab's ShapeBody isn't set up.");
                Assert.That(body.bounds.min.y, Is.InRange(0, 0.2f), "The shape doesn't stand on its origin.");
                Assert.That(body.bounds.size.y, Is.InRange(0.3f, 2), "The shape isn't about a player's height.");
                Assert.That(eyes.bounds.center.z, Is.GreaterThan(0.1f), "The eyes aren't on the front (+Z).");
                Assert.That(Mathf.Abs(eyes.bounds.center.x), Is.LessThan(0.01f), "The eyes aren't centered.");

                shape.SetColors(0x00FF00, 0xFF0000);
                Assert.That(body.sharedMaterial.color, Is.EqualTo(Color.green));
                Assert.That(eyes.sharedMaterial.color, Is.EqualTo(Color.red));
            }
            finally
            {
                Object.DestroyImmediate(shape.gameObject);
            }
        }

        [Test]
        public void ShapeStatsAreSpeedAndJumpHeight()
        {
            var (speed, jump) = JoinScreen.Stats(new Shape { MaxSpeed = 5, JumpVelocity = 8.5f });
            Assert.That(speed, Is.EqualTo(5));
            Assert.That(jump, Is.EqualTo(8.5f * 8.5f / (2 * ShapeLandWorld.Rules.Gravity)).Within(1e-4f));
        }

        // Every key the scripts, UXML and web page use is in the string file, so no "[key]" shows on screen.
        [Test]
        public void EveryInterfaceTextKeyExists()
        {
            var contentFile = Path.Combine(Application.streamingAssetsPath, "content.bin");
            Assert.That(File.Exists(contentFile), "No content in StreamingAssets; run ShapeLand > Copy Content.");
            ShapeLandContent content;
            using (var stream = File.OpenRead(contentFile))
            {
                content = ShapeLandContent.Load(stream);
            }

            var groups = string.Join("|", content.Strings.Entries.Keys.Select(k => k.Split('.')[0]).Distinct());
            var used = new Regex($"(?:\"#?|data-(?:text|label|title)=\")((?:{groups})\\.[a-z0-9_.]+)\"");
            var files = Directory.GetFiles("Assets/Scripts", "*.cs")
                .Concat(Directory.GetFiles("Assets/UI", "*.uxml"))
                .Concat(new[] { "Assets/WebGLTemplates/ShapeLand/index.html" });
            var keys = files.SelectMany(file => used.Matches(File.ReadAllText(file)).Cast<Match>().Select(m => (File: file, Key: m.Groups[1].Value))).ToList();

            Assert.That(keys.Count, Is.GreaterThan(20), "The keys weren't found; has the markup changed?");
            Assert.That(keys.Where(k => !content.Strings.Contains(k.Key)).Select(k => $"{k.File}: {k.Key}"), Is.Empty);
        }

        private static ShapeBody ShapePrefab(MeshKind kind) =>
            AssetDatabase.LoadAssetAtPath<ShapeBody>($"Assets/Prefabs/Shapes/{kind}.prefab");
    }
}

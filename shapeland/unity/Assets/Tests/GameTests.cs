using System.Collections;
using System.Collections.Generic;
using System.IO;
using Comet.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace ShapeLand.Client.Tests
{
    // The game view against the real game server (step 3b): with a simulated keyboard, the player walks forward
    // and jumps in place, the camera follows, and the server accepts every report (no snap-backs); the speed
    // cheat is snapped back with a blend; walking off the island darkens the screen before the respawn and
    // lightens it after; real bots are drawn moving and removed when they leave.
    public class GameTests
    {
        private TestGameServer _server;
        private GameObject _game;
        private GameObject _camera;
        private Keyboard _keyboard;
        private JoinClient _join;
        private GameView _view;
        private InputSettings.EditorInputBehaviorInPlayMode _editorInput;
        private InputSettings.BackgroundBehavior _background;

        // Batch mode has no focused Game view, which the editor otherwise needs before keyboard input reaches Play mode.
        [SetUp]
        public void SetUp()
        {
            _editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            _background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        }

        [TearDown]
        public void TearDown()
        {
            if (_game != null)
            {
                Object.Destroy(_game);
                Object.Destroy(_camera);
            }

            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
            }

            InputSystem.settings.editorInputBehaviorInPlayMode = _editorInput;
            InputSystem.settings.backgroundBehavior = _background;

            _server?.Dispose();
            _server = null;
            _game = null;
            _camera = null;
            _keyboard = null;
        }

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

        [UnityTest]
        public IEnumerator WalksAndJumpsWithoutSnapBacks()
        {
            yield return StartGame();
            var view = _view;
            var join = _join;
            var player = view.Player;
            var start = player.transform.position;

            _keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
            yield return Wait(1.5f);
            var walked = Vector3.ProjectOnPlane(player.transform.position - start, Vector3.up).magnitude;
            Assert.That(walked, Is.GreaterThan(2f), "Didn't walk forward.");

            // Stop, then jump in place, so the hills don't change the height reached.
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return Wait(0.5f);
            var ground = player.Motor.Position.Y;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Space));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            var highest = ground;
            var deadline = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < deadline)
            {
                highest = Mathf.Max(highest, player.Motor.Position.Y);
                yield return null;
            }

            Assert.That(highest - ground, Is.GreaterThan(0.5f), "Didn't jump.");

            yield return Wait(1.5f);
            Assert.That(player.SnapBacks, Is.EqualTo(0), "The server snapped the player back.");
            Assert.That(player.Respawns, Is.EqualTo(0), "The player fell off.");
            Assert.That(Vector3.Distance(_camera.transform.position, player.transform.position), Is.LessThan(10f), "The camera didn't follow.");
            Debug.Log($"Walked {walked:0.0} m and jumped {highest - ground:0.00} m; {join.Session.StatesReceived} states received.");
        }

        [TestCase("http://localhost:5080/", "ws://localhost:5080/ws")]
        [TestCase("https://example.org/play/index.html", "wss://example.org/ws")]
        [TestCase("https://example.org/?server=game.example.org%3A5080", "game.example.org:5080")]
        [TestCase("http://localhost:8000/?debug=1&server=wss://game.example.org/ws", "wss://game.example.org/ws")]
        [TestCase("file:///tmp/index.html", "localhost:5080")]
        public void WebBuildsConnectToTheServerTheyCameFrom(string page, string expected)
        {
            Assert.That(JoinClient.AddressForPage(page, "localhost:5080"), Is.EqualTo(expected));
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
            var body = new GameObject("Body").AddComponent<MeshRenderer>();
            var eyes = new GameObject("Eyes").AddComponent<MeshRenderer>();
            body.sharedMaterial = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shape.mat")) { color = Color.red };
            eyes.sharedMaterial = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Eyes.mat")) { color = Color.blue };
            var opaque = body.sharedMaterial;
            var fade = new ShapeFade(body, eyes, AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ShapeFade.mat"), AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/EyesFade.mat"));
            try
            {
                fade.Set(0.25f);
                Assert.That(body.sharedMaterial, Is.Not.SameAs(opaque));
                Assert.That(body.sharedMaterial.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent));
                Assert.That(body.sharedMaterial.color.r, Is.EqualTo(1));
                Assert.That(body.sharedMaterial.color.a, Is.EqualTo(0.75f).Within(1e-5f));
                Assert.That(eyes.sharedMaterial.color.b, Is.EqualTo(1));

                fade.Set(1);
                Assert.That(body.enabled || eyes.enabled, Is.False, "A shape fully faded is still drawn.");

                fade.Set(0);
                Assert.That(body.enabled && eyes.enabled, Is.True);
                Assert.That(body.sharedMaterial, Is.SameAs(opaque));
            }
            finally
            {
                fade.Destroy();
                Object.Destroy(body.gameObject);
                Object.Destroy(eyes.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator SpeedCheatIsSnappedBackWithABlend()
        {
            yield return StartGame();
            var player = _view.Player;

            _keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W, Key.C));
            yield return TestGameServer.WaitFor(() => player.SnapBacks > 0, 5);
            Assert.That(player.SnapBacks, Is.GreaterThan(0), "The speed cheat was never snapped back.");
            var trailing = player.BlendDistance;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());

            // The drawn shape trails the corrected position, then catches up within the blend.
            Assert.That(trailing, Is.GreaterThan(0.05f), "The snap-back wasn't blended.");
            yield return Wait(0.3f);
            Assert.That(player.BlendDistance, Is.EqualTo(0), "The blend didn't finish.");
            Debug.Log($"Snapped back {player.SnapBacks} times; the drawn shape trailed by {trailing:0.00} m after the first.");
        }

        [UnityTest]
        public IEnumerator FallingOffDarkensBeforeTheRespawnAndLightensAfter()
        {
            yield return StartGame();
            var player = _view.Player;

            // Walk straight on until off the edge, noting how dark the screen was just before the respawn arrived.
            _keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
            var darknessBefore = 0f;
            var deadline = Time.realtimeSinceStartup + 20;
            while (player.Respawns == 0 && Time.realtimeSinceStartup < deadline)
            {
                darknessBefore = player.Darkness;
                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            Assert.That(player.Respawns, Is.EqualTo(1), "Never fell off.");
            Assert.That(darknessBefore, Is.GreaterThan(0.99f), "The screen wasn't dark before the respawn arrived.");

            yield return Wait(RespawnFade.FadeInSeconds + 0.3f);
            Assert.That(player.Darkness, Is.EqualTo(0), "The screen didn't lighten again.");
            Assert.That(_view.ScreenFade.Darkness, Is.EqualTo(0));
            Assert.That(player.Motor.Position.Y, Is.GreaterThan(0), "Not back on the island.");
            Assert.That(player.SnapBacks, Is.EqualTo(0), "The server snapped the player back.");
        }

        [UnityTest]
        public IEnumerator DrawsOtherPlayersMovingAndLeaving()
        {
            yield return StartGame();
            yield return _server.StartBots(2, 8);

            yield return TestGameServer.WaitFor(() => _view.Others.Count == 2, 15);
            Assert.That(_view.Others.Count, Is.EqualTo(2), "The bots never appeared.");
            var others = new List<Transform>(_view.Others.Values);
            var starts = others.ConvertAll(o => o.position);
            yield return Wait(2);
            var moved = 0f;
            for (var i = 0; i < others.Count; i++)
            {
                moved = Mathf.Max(moved, Vector3.Distance(starts[i], others[i].position));
            }

            Assert.That(moved, Is.GreaterThan(1f), "The bots weren't drawn moving.");

            yield return TestGameServer.WaitFor(() => _view.Others.Count == 0, 30);
            Assert.That(_view.Others.Count, Is.EqualTo(0), "The bots weren't removed after leaving.");
            Debug.Log($"Saw 2 bots; the furthest moved {moved:0.0} m in 2 s.");
        }

        // Builds the game view (connection, join client, view and orbit camera) against a fresh server.
        private IEnumerator StartGame()
        {
            Assert.That(File.Exists(Path.Combine(Application.streamingAssetsPath, "content.bin")), "No content in StreamingAssets; run ShapeLand > Copy Content.");

            _server = new TestGameServer();
            yield return _server.Start();

            _camera = new GameObject("Camera", typeof(Camera), typeof(OrbitCamera));
            _game = new GameObject("Game");
            _game.SetActive(false);
            _game.AddComponent<CometConnection>();
            _join = _game.AddComponent<JoinClient>();
            _join.Address = _server.Address;
            _join.PlayerName = "Unity test";
            _view = _game.AddComponent<GameView>();
            var serialized = new SerializedObject(_view);
            serialized.FindProperty("terrainMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Terrain.mat");
            serialized.FindProperty("blockMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Block.mat");
            serialized.FindProperty("shapeMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shape.mat");
            serialized.FindProperty("eyeMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Eyes.mat");
            serialized.FindProperty("shapeFadeMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ShapeFade.mat");
            serialized.FindProperty("eyeFadeMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/EyesFade.mat");
            serialized.FindProperty("orbitCamera").objectReferenceValue = _camera.GetComponent<OrbitCamera>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _game.SetActive(true);

            yield return TestGameServer.WaitFor(() => _view.Player != null || _join.Error != null, 15);
            Assert.That(_join.Error, Is.Null);
            Assert.That(_view.Player, Is.Not.Null, "Never spawned.");
        }

        private static IEnumerator Wait(float seconds) => TestGameServer.WaitFor(() => false, seconds);
    }
}

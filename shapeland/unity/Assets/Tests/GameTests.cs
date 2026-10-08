using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comet.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;

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
        private JoinScreen _screen;
        private Hud _hud;
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

            if (_hud != null)
            {
                Object.Destroy(_hud.gameObject);
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
            _screen = null;
            _hud = null;
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

        [TestCase(MeshKind.Cube)]
        [TestCase(MeshKind.Diamond)]
        [TestCase(MeshKind.Pyramid)]
        public void ShapeMeshesFollowTheirLook(MeshKind kind)
        {
            var look = new ShapeLook { Width = 1.2f, Depth = 0.6f, Height = 0.5f, Waist = 0.2f, EyeWidth = 0.1f, EyeHeight = 0.1f, EyeSpacing = 0.4f, EyeLevel = 0.25f };
            var body = WorldMeshes.Shape(kind, look).bounds;
            Assert.That(body.size.x, Is.EqualTo(1.2f).Within(1e-4f));
            Assert.That(body.size.y, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(body.size.z, Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(body.min.y, Is.EqualTo(0).Within(1e-4f), "The shape doesn't stand on its origin.");

            // The eyes sit on the front face at their level, either side of the centre line.
            var eyes = WorldMeshes.Eyes(kind, look).bounds;
            var (forward, _) = WorldMeshes.FrontFace(kind, look, look.EyeLevel);
            Assert.That(eyes.center.y, Is.EqualTo(look.EyeLevel).Within(0.02f));
            Assert.That(eyes.center.x, Is.EqualTo(0).Within(1e-4f));
            Assert.That(eyes.size.x, Is.EqualTo(look.EyeSpacing + look.EyeWidth).Within(0.01f));
            Assert.That(eyes.center.z, Is.EqualTo(forward).Within(0.04f));
        }

        [Test]
        public void ShapeStatsAreSpeedAndJumpHeight()
        {
            var (speed, jump) = JoinScreen.Stats(new Shape { MaxSpeed = 5, JumpVelocity = 8.5f });
            Assert.That(speed, Is.EqualTo(5));
            Assert.That(jump, Is.EqualTo(8.5f * 8.5f / (2 * ShapeLandWorld.Rules.Gravity)).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator JoinScreenJoinsWithTheChosenShapeAndColours()
        {
            yield return StartGame(joinScreen: true);
            Assert.That(_view.Player, Is.Null, "Joined before pressing Join.");

            _screen.Root.Q<TextField>("name").value = "Pebble";
            Click(ShapeButton(MeshKind.Cube));
            Click(Swatch("colours", ShapeLandRules.BodyColours[1]));
            Click(Swatch("eyes", ShapeLandRules.EyeColours[3]));
            // From content, so tuning the cube doesn't break the test.
            var cube = _join.Content.Shapes.All.First(shape => shape.Mesh == MeshKind.Cube);
            var jump = cube.JumpVelocity * cube.JumpVelocity / (2 * ShapeLandWorld.Rules.Gravity);
            Assert.That(StatValues(), Is.EqualTo(new[] { $"{cube.MaxSpeed:0.#} m/s", $"{jump:0.0} m" }), "The stats don't show the cube's.");
            Click(_screen.Root.Q<Button>("join-button"));

            yield return TestGameServer.WaitFor(() => _view.Player != null || _screen.Error != null, 15);
            Assert.That(_screen.Error, Is.Null);
            Assert.That(_view.Player, Is.Not.Null, "Never spawned.");
            Assert.That(_screen.Showing, Is.False, "The join screen stayed up.");
            var spawn = _join.Spawn.Value;
            Assert.That(spawn.Name, Is.EqualTo("Pebble"));
            Assert.That(_join.Content.Shapes[spawn.Shape].Mesh, Is.EqualTo(MeshKind.Cube));
            Assert.That(spawn.Colour, Is.EqualTo(ShapeLandRules.BodyColours[1]));
            Assert.That(spawn.EyeColour, Is.EqualTo(ShapeLandRules.EyeColours[3]));

            // The overlay fills the screen next to the join screen (a nested UIDocument once gave it no height).
            yield return null;
            var screen = _hud.Root.panel.visualTree.worldBound;
            Assert.That(_hud.Root.worldBound.height, Is.EqualTo(screen.height).Within(1), "The overlay doesn't fill the screen.");
            Assert.That(_hud.Root.Q("chat").worldBound.yMax, Is.LessThanOrEqualTo(screen.yMax).And.GreaterThan(screen.height / 2), "The chat box isn't in the lower part of the screen.");
        }

        [UnityTest]
        public IEnumerator JoinScreenShowsRefusalsAndTriesAgain()
        {
            yield return StartGame(joinScreen: true);
            yield return _server.StartBots(1, 30);
            yield return Wait(3); // time for the bot to join; this client can't see it before joining

            var name = _screen.Root.Q<TextField>("name");
            var join = _screen.Root.Q<Button>("join-button");
            name.value = "no!";
            Click(join);
            Assert.That(_screen.Error, Does.Contain("letters, digits"), "A bad name wasn't caught before sending.");

            name.value = "bot 01";
            Click(join);
            yield return TestGameServer.WaitFor(() => _screen.Error != null, 10);
            Assert.That(_screen.Error, Does.Contain("already called"), "A taken name wasn't reported.");
            Assert.That(join.enabledSelf, Is.True, "Join stayed disabled after a refusal.");

            name.value = "Pebble";
            Click(join);
            yield return TestGameServer.WaitFor(() => _view.Player != null, 10);
            Assert.That(_view.Player, Is.Not.Null, "Didn't join on the second try.");
            Assert.That(_screen.Showing, Is.False);
        }

        [UnityTest]
        public IEnumerator JoinScreenReportsAnUnreachableServer()
        {
            yield return StartGame(joinScreen: true);
            _screen.Root.Q<TextField>("name").value = "Pebble";
            _screen.Root.Q<TextField>("server").value = "127.0.0.1:9";
            Click(_screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => _screen.Error != null, 15);
            Assert.That(_screen.Error, Does.Contain("Couldn't reach the server at 127.0.0.1:9"));
            Assert.That(_screen.Showing, Is.True);
        }

        [UnityTest]
        public IEnumerator ChatGoesRoundWithALogLineAndABubble()
        {
            yield return StartGame();
            var player = _view.Player;
            _hud.OpenChat();
            Assert.That(player.InputEnabled, Is.False, "The shape can still move while typing.");
            _hud.Typed = "  hello island  ";
            _hud.SendChat();
            Assert.That(_hud.ChatOpen, Is.False);
            Assert.That(player.InputEnabled, Is.True);

            yield return TestGameServer.WaitFor(() => _hud.LogLines().Count > 0, 10);
            Assert.That(_hud.LogLines(), Is.EqualTo(new[] { "Unity test hello island" }));
            Assert.That(_hud.BubbleOf(_join.Session.EntityId), Is.EqualTo("hello island"), "No bubble over the player's own shape.");
            yield return Wait(0.6f);
            Assert.That(_hud.ChatOpacity, Is.EqualTo(1), "The log isn't showing after a message.");
        }

        [UnityTest]
        public IEnumerator EnterOpensChatAndTheShapeStaysPut()
        {
            yield return StartGame();
            var player = _view.Player;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Enter));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return Wait(0.3f);
            Assert.That(_hud.ChatOpen, Is.True, "Enter didn't open chat.");

            var start = player.transform.position;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
            yield return Wait(1);
            Assert.That(Vector3.Distance(start, player.transform.position), Is.LessThan(0.1f), "The shape moved while typing.");

            _hud.CloseChat();
            yield return Wait(1);
            Assert.That(Vector3.Distance(start, player.transform.position), Is.GreaterThan(1f), "The shape didn't move after chat closed.");
        }

        [UnityTest]
        public IEnumerator OthersChatInTheLogAndInBubbles()
        {
            yield return StartGame();
            yield return _server.StartBots(1, 20);
            yield return TestGameServer.WaitFor(() => _hud.LogLines().Any(line => line.StartsWith("Bot 01 ")), 15);
            var line = _hud.LogLines().FirstOrDefault(l => l.StartsWith("Bot 01 "));
            Assert.That(line, Is.Not.Null, "The bot's chat never reached the log.");
            var bot = _view.Spawns.First(p => p.Value.Name == "Bot 01").Key;
            Assert.That("Bot 01 " + _hud.BubbleOf(bot), Is.EqualTo(line), "The bot's bubble doesn't show its line.");
        }

        [UnityTest]
        public IEnumerator ChattingTooFastIsExplained()
        {
            yield return StartGame();
            for (var i = 0; i < 5; i++)
            {
                _hud.OpenChat();
                _hud.Typed = $"line {i}";
                _hud.SendChat();
            }

            yield return TestGameServer.WaitFor(() => _hud.LogLines().Any(l => l.Contains("too quickly")), 10);
            Assert.That(_hud.LogLines().Count(l => l.StartsWith("Unity test line")), Is.EqualTo(3), "The server's burst of three wasn't delivered.");
            Assert.That(_hud.LogLines().Any(l => l.Contains("too quickly")), Is.True, "Sending too fast wasn't explained.");
        }

        // Presses a button the way a keyboard or gamepad submit does.
        private static void Click(VisualElement button)
        {
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        private VisualElement ShapeButton(MeshKind kind) =>
            _screen.Root.Q("shapes").Children().First(b => ((Shape)b.userData).Mesh == kind);

        private VisualElement Swatch(string row, uint colour) =>
            _screen.Root.Q(row).Children().First(b => (uint)b.userData == colour);

        private string[] StatValues() =>
            _screen.Root.Q("stats").Query<Label>().ToList().Where(l => !l.ClassListContains("stat-name")).Select(l => l.text).ToArray();

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

        // Builds the game view (connection, join client, view and orbit camera) against a fresh server. Without
        // the join screen it joins at once; with it, it waits for the screen to come up.
        private IEnumerator StartGame(bool joinScreen = false)
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
            // Not a child of the game object, as in the scene: a UIDocument under another one is added inside its tree.
            var hud = new GameObject("Hud");
            hud.SetActive(false);
            var hudDocument = hud.AddComponent<UIDocument>();
            hudDocument.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/ShapeLandPanel.asset");
            hudDocument.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Hud.uxml");
            _hud = hud.AddComponent<Hud>();
            _hud.Attach(_join, _view);
            hud.SetActive(true);
            if (joinScreen)
            {
                _join.JoinOnStart = false;
                var document = _game.AddComponent<UIDocument>();
                document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/ShapeLandPanel.asset");
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/JoinScreen.uxml");
                _screen = _game.AddComponent<JoinScreen>();
                _screen.Remember = false;
                _game.SetActive(true);
                yield return TestGameServer.WaitFor(() => _screen.Showing, 15);
                Assert.That(_screen.Showing, Is.True, "The join screen never came up.");
                _screen.Root.Q<TextField>("server").value = _server.Address;
                yield break;
            }

            _game.SetActive(true);

            yield return TestGameServer.WaitFor(() => _view.Player != null || _join.Error != null, 15);
            Assert.That(_join.Error, Is.Null);
            Assert.That(_view.Player, Is.Not.Null, "Never spawned.");
        }

        private static IEnumerator Wait(float seconds) => TestGameServer.WaitFor(() => false, seconds);
    }
}

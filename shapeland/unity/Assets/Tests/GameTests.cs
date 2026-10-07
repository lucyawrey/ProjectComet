using System.Collections;
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
    // and jumps in place, the camera follows, and the server accepts every report (no snap-backs).
    public class GameTests
    {
        private TestGameServer _server;
        private GameObject _game;
        private GameObject _camera;
        private Keyboard _keyboard;
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
            Assert.That(File.Exists(Path.Combine(Application.streamingAssetsPath, "content.bin")), "No content in StreamingAssets; run ShapeLand > Copy Content.");

            _server = new TestGameServer();
            yield return _server.Start();

            _camera = new GameObject("Camera", typeof(Camera), typeof(OrbitCamera));
            _game = new GameObject("Game");
            _game.SetActive(false);
            _game.AddComponent<CometConnection>();
            var join = _game.AddComponent<JoinClient>();
            join.Address = _server.Address;
            join.PlayerName = "Unity test";
            var view = _game.AddComponent<GameView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("terrainMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Terrain.mat");
            serialized.FindProperty("blockMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Block.mat");
            serialized.FindProperty("shapeMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Shape.mat");
            serialized.FindProperty("eyeMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Eyes.mat");
            serialized.FindProperty("orbitCamera").objectReferenceValue = _camera.GetComponent<OrbitCamera>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _game.SetActive(true);

            yield return TestGameServer.WaitFor(() => view.Player != null || join.Error != null, 15);
            Assert.That(join.Error, Is.Null);
            Assert.That(view.Player, Is.Not.Null, "Never spawned.");
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

        private static IEnumerator Wait(float seconds) => TestGameServer.WaitFor(() => false, seconds);
    }
}

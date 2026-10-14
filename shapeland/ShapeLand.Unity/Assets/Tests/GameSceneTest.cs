using System.Collections;
using System.Linq;
using Comet.Content;
using NUnit.Framework;
using ShapeLand.Shared.Content;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace ShapeLand.Client.Tests
{
    // Tests of the real Game scene against the real game server: each loads the scene, starts a fresh server, and
    // joins through the join card as a player would (or leaves the card up for the test). A simulated keyboard
    // stands in for the player's. Needs the dotnet SDK on the PATH and the content built.
    public abstract class GameSceneTest
    {
        protected TestGameServer Server;
        protected JoinClient Join;
        protected GameView View;
        protected JoinScreen Screen;
        protected Hud Hud;
        protected Chat Chat;
        protected NameTags Tags;
        protected NetStats NetStats;
        protected OrbitCamera Camera;
        protected Keyboard Keyboard;

        private InputSettings.EditorInputBehaviorInPlayMode _editorInput;
        private InputSettings.BackgroundBehavior _background;

        protected StringTable Strings => Join.Content.Strings;

        // Batch mode has no focused Game view, which the editor otherwise needs before keyboard input reaches Play mode.
        [SetUp]
        public void SetUpInput()
        {
            _editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            _background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        }

        [TearDown]
        public void TearDownGame()
        {
            if (Keyboard != null)
            {
                InputSystem.RemoveDevice(Keyboard);
                Keyboard = null;
            }

            InputSystem.settings.editorInputBehaviorInPlayMode = _editorInput;
            InputSystem.settings.backgroundBehavior = _background;
            Server?.Dispose();
            Server = null;
        }

        // Loads the Game scene against a fresh server and, unless the test drives the card itself, joins as "Unity
        // test" with the card's choices.
        protected IEnumerator StartGame(bool join = true)
        {
            TestGameServer.LoadContent();

            Server = new TestGameServer();
            yield return Server.Start();

            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/Game.unity", new LoadSceneParameters(LoadSceneMode.Single));
            Join = Object.FindAnyObjectByType<JoinClient>();
            View = Object.FindAnyObjectByType<GameView>();
            Screen = Object.FindAnyObjectByType<JoinScreen>();
            Hud = Object.FindAnyObjectByType<Hud>();
            Chat = Object.FindAnyObjectByType<Chat>();
            Tags = Object.FindAnyObjectByType<NameTags>();
            NetStats = Object.FindAnyObjectByType<NetStats>();
            Camera = Object.FindAnyObjectByType<OrbitCamera>();
            Screen.Remember = false;
            NetStats.Remember = false;
            if (NetStats.Shown)
            {
                NetStats.Toggle(); // left on from playing in the editor
            }

            yield return TestGameServer.WaitFor(() => Screen.Showing, 15);
            Assert.That(Screen.Showing, Is.True, "The join screen never came up.");
            Field("server").value = Server.Address;
            if (!join)
            {
                yield break;
            }

            Field("name").value = "Unity test";
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => View.Player != null || Screen.Error != null, 15);
            Assert.That(Screen.Error, Is.Null);
            Assert.That(View.Player, Is.Not.Null, "Never spawned.");
        }

        protected TextField Field(string name) => Screen.Root.Q<TextField>(name);

        // Presses a button the way a keyboard or gamepad submit does.
        protected static void Click(VisualElement button)
        {
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        protected VisualElement ShapeButton(MeshKind kind) =>
            Screen.Root.Q("shapes").Children().First(b => ((Shape)b.userData).Mesh == kind);

        protected VisualElement Swatch(string row, uint color) =>
            Screen.Root.Q(row).Children().First(b => (uint)b.userData == color);

        protected void Press(params Key[] keys)
        {
            Keyboard ??= InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(Keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(keys));
        }

        protected static IEnumerator Wait(float seconds) => TestGameServer.WaitFor(() => false, seconds);
    }
}

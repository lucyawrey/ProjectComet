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
            JoinScreen.DevCharactersFile = null;
            if (Server != null)
            {
                if (Server.Address != null)
                {
                    JoinClient.ForgetToken(Server.Address);
                }

                Server.Dispose();
                Server = null;
            }
        }

        protected IEnumerator StartGame(bool join = true, bool accounts = false, string loginArguments = "", string devCharacters = "")
        {
            TestGameServer.LoadContent();

            Server = new TestGameServer(accounts, loginArguments);
            yield return Server.Start();

            JoinClient.TokenKey = "tests-login-token";
            JoinScreen.DevCharactersFile = devCharacters;
            if (accounts)
            {
                JoinClient.ForgetToken(Server.Address);
            }

            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/Game.unity", new LoadSceneParameters(LoadSceneMode.Single));
            Join = Object.FindAnyObjectByType<JoinClient>();
            View = Object.FindAnyObjectByType<GameView>();
            Screen = Object.FindAnyObjectByType<JoinScreen>();
            Hud = Object.FindAnyObjectByType<Hud>();
            Chat = Object.FindAnyObjectByType<Chat>();
            Tags = Object.FindAnyObjectByType<NameTags>();
            NetStats = Object.FindAnyObjectByType<NetStats>();
            Camera = Object.FindAnyObjectByType<OrbitCamera>();
            NetStats.Remember = false;
            if (NetStats.Shown)
            {
                NetStats.Toggle();
            }

            yield return TestGameServer.WaitFor(() => Screen.Showing, 15);
            Assert.That(Screen.Showing, Is.True, "The join screen never came up.");
            yield return LaidOut("join-button");
            if (accounts)
            {
                Field("server").value = Server.Address;
            }

            if (!join)
            {
                yield break;
            }

            Field("name").value = "UnityTest";
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => View.Player != null || Screen.Error != null, 15);
            Assert.That(Screen.Error, Is.Null);
            Assert.That(View.Player, Is.Not.Null, "Never spawned.");
        }

        protected TextField Field(string name) => Screen.Root.Q<TextField>(name);

        protected IEnumerator LaidOut(string button)
        {
            var element = Screen.Root.Q<Button>(button);
            yield return TestGameServer.WaitFor(() => element.worldBound.width > 0, 5);
        }

        protected static void Click(VisualElement button)
        {
            var center = button.worldBound.center;
            using (
                var down = PointerDownEvent.GetPooled(
                    new Event
                    {
                        type = EventType.MouseDown,
                        mousePosition = center,
                        button = 0,
                        clickCount = 1,
                    }
                )
            )
            {
                down.target = button;
                button.SendEvent(down);
            }

            using (
                var up = PointerUpEvent.GetPooled(
                    new Event
                    {
                        type = EventType.MouseUp,
                        mousePosition = center,
                        button = 0,
                        clickCount = 1,
                    }
                )
            )
            {
                up.target = button;
                button.SendEvent(up);
            }
        }

        protected VisualElement ShapeButton(string id) => Screen.Root.Q("shapes").Children().First(b => ((Shape)b.userData).Id == id);

        protected VisualElement Swatch(string row, int color) => Screen.Root.Q(row).Children().First(b => (int)b.userData == color);

        protected void Press(params Key[] keys)
        {
            Keyboard ??= InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(Keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(keys));
        }

        protected static IEnumerator Wait(float seconds) => TestGameServer.WaitFor(() => false, seconds);
    }
}

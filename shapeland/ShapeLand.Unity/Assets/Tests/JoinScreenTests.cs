using System.Collections;
using System.Linq;
using NUnit.Framework;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace ShapeLand.Client.Tests
{
    // The join card in the Game scene: joining with the chosen look, refusals and failures shown on the card,
    // changing the look in game, and coming back after a lost connection.
    public class JoinScreenTests : GameSceneTest
    {
        [UnityTest]
        public IEnumerator ShowsTheServersZoneBeforeJoining()
        {
            yield return StartGame(join: false);
            yield return TestGameServer.WaitFor(() => View.transform.Find(TestGameServer.Zone) != null, 5);

            Assert.That(View.transform.Find(TestGameServer.Zone), Is.Not.Null, "The server's zone wasn't drawn behind the join screen.");
            Assert.That(View.transform.Find(Join.Content.Settings.World.DefaultZone), Is.Null, "The default zone is still drawn.");
            Assert.That(View.Player, Is.Null, "Joined before pressing Join.");
        }

        [UnityTest]
        public IEnumerator JoinsWithTheChosenShapeAndColors()
        {
            yield return StartGame(join: false);
            Assert.That(View.Player, Is.Null, "Joined before pressing Join.");

            Field("name").value = "Pebble";
            Click(ShapeButton(MeshKind.Cube));
            Click(Swatch("colors", Join.Content.Settings.Colors.Body[1]));
            Click(Swatch("eyes", Join.Content.Settings.Colors.Eyes[3]));
            // From content, so tuning the cube doesn't break the test.
            var cube = Join.Content.Shapes.All.First(shape => shape.Mesh == MeshKind.Cube);
            var (_, jump) = JoinScreen.Stats(cube, Join.Content.Movement);
            var expected = new[]
            {
                Strings.Format("join.speed_value", ("speed", cube.MaxSpeed.ToString("0.#"))),
                Strings.Format("join.jump_value", ("height", jump.ToString("0.0"))),
            };
            Assert.That(StatValues(), Is.EqualTo(expected), "The stats don't show the cube's.");
            Click(Screen.Root.Q<Button>("join-button"));

            yield return TestGameServer.WaitFor(() => View.Player != null || Screen.Error != null, 15);
            Assert.That(Screen.Error, Is.Null);
            Assert.That(View.Player, Is.Not.Null, "Never spawned.");
            Assert.That(Screen.Showing, Is.False, "The join screen stayed up.");
            var spawn = Join.Spawn.Value;
            Assert.That(spawn.Name, Is.EqualTo("Pebble"));
            Assert.That(Join.Content.Shapes[spawn.Shape].Mesh, Is.EqualTo(MeshKind.Cube));
            Assert.That(spawn.Color, Is.EqualTo(Join.Content.Settings.Colors.Body[1]));
            Assert.That(spawn.EyeColor, Is.EqualTo(Join.Content.Settings.Colors.Eyes[3]));

            // The overlay fills the screen next to the join screen (a nested UIDocument once gave it no height).
            yield return null;
            var screen = Hud.Root.panel.visualTree.worldBound;
            Assert.That(Hud.Root.worldBound.height, Is.EqualTo(screen.height).Within(1), "The overlay doesn't fill the screen.");
            Assert.That(Hud.Root.Q("chat").worldBound.yMax, Is.LessThanOrEqualTo(screen.yMax).And.GreaterThan(screen.height / 2), "The chat box isn't in the lower part of the screen.");
        }

        [UnityTest]
        public IEnumerator ALostConnectionGoesBackToTheJoinScreen()
        {
            yield return StartGame(join: false);
            Field("name").value = "Pebble";
            Click(ShapeButton(MeshKind.Diamond));
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => View.Player != null, 15);
            Assert.That(View.Player, Is.Not.Null, "Never spawned.");

            Server.StopServer();
            yield return TestGameServer.WaitFor(() => Screen.Showing, 10);

            Assert.That(Screen.Showing, Is.True, "The join screen didn't come back.");
            var lostMessages = Strings.Entries.Where(e => e.Key.StartsWith("connection.")).Select(e => e.Value);
            Assert.That(lostMessages, Does.Contain(Screen.Error), "The card doesn't say the connection was lost.");
            Assert.That(View.Player, Is.Null, "The player's shape is still in the world.");
            Assert.That(Join.Joined, Is.False);
            Assert.That(Hud.Shown, Is.False, "The overlay is still showing.");
            Assert.That(Field("name").value, Is.EqualTo("Pebble"), "The name wasn't kept.");
            Assert.That(Screen.Root.Q<Button>("join-button").enabledSelf, Is.True, "Join isn't enabled for another try.");
        }

        [UnityTest]
        public IEnumerator RejoinsAServerThatRestarted()
        {
            yield return StartGame(join: false);
            Field("name").value = "Pebble";
            Click(ShapeButton(MeshKind.Cube));
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => View.Player != null, 15);
            Assert.That(View.Player, Is.Not.Null, "Never spawned.");

            yield return Server.ShutDownServer();
            yield return TestGameServer.WaitFor(() => Screen.Error == Strings.Get("join.rejoining"), 10);
            Assert.That(Screen.Showing, Is.True, "The join screen didn't come back.");
            Assert.That(Screen.Error, Is.EqualTo(Strings.Get("join.rejoining")), "The card doesn't say it's rejoining.");
            yield return Wait(3); // several tries fail meanwhile
            Assert.That(Screen.Error, Is.EqualTo(Strings.Get("join.rejoining")), "A failed try replaced the rejoining message.");

            yield return Server.StartAgain();
            yield return TestGameServer.WaitFor(() => View.Player != null, 15);
            Assert.That(View.Player, Is.Not.Null, "Didn't rejoin the restarted server.");
            Assert.That(Screen.Showing, Is.False, "The join screen stayed up.");
            Assert.That(Join.Spawn.Value.Name, Is.EqualTo("Pebble"));
            Assert.That(Join.Content.Shapes[Join.Spawn.Value.Shape].Mesh, Is.EqualTo(MeshKind.Cube));
        }

        [UnityTest]
        public IEnumerator ShowsRefusalsAndTriesAgain()
        {
            yield return StartGame(join: false);
            yield return Server.StartBots(1, 30);
            yield return Wait(3); // time for the bot to join; this client can't see it before joining

            var join = Screen.Root.Q<Button>("join-button");
            Field("name").value = "no!";
            Click(join);
            Assert.That(Screen.Error, Is.EqualTo(Strings.Get("join.name_rules")), "A bad name wasn't caught before sending.");

            Field("name").value = "bot 01";
            Click(join);
            yield return TestGameServer.WaitFor(() => Screen.Error != null, 10); // pressing Join cleared the last one
            Assert.That(Screen.Error, Is.EqualTo(Strings.Format("join.name_taken", ("name", "bot 01"))), "A taken name wasn't reported.");
            Assert.That(join.enabledSelf, Is.True, "Join stayed disabled after a refusal.");

            Field("name").value = "Pebble";
            Click(join);
            yield return TestGameServer.WaitFor(() => View.Player != null, 10);
            Assert.That(View.Player, Is.Not.Null, "Didn't join on the second try.");
            Assert.That(Screen.Showing, Is.False);
        }

        [UnityTest]
        public IEnumerator APlayerCanChangeTheirLookWhereTheyStand()
        {
            yield return StartGame(join: false);
            Field("name").value = "Pebble";
            Click(ShapeButton(MeshKind.Cube));
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => View.Player != null, 15);
            var player = View.Player;
            var id = Join.Session.EntityId;

            Hud.OpenLook();
            Assert.That(Screen.Changing, Is.True);
            Assert.That(Field("name").value, Is.EqualTo("Pebble"), "The card isn't filled in with the current look.");
            Assert.That(player.InputEnabled, Is.False, "The shape can still move while choosing a look.");
            Click(Screen.Root.Q<Button>("cancel-button"));
            yield return null;
            Assert.That(Screen.Showing, Is.False);
            Assert.That(player.InputEnabled, Is.True);

            Hud.OpenLook();
            Field("name").value = "Pebbles";
            Click(ShapeButton(MeshKind.Diamond));
            Click(Swatch("colors", Join.Content.Settings.Colors.Body[2]));
            var at = player.transform.position;
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => !Screen.Showing || Screen.Error != null, 10);

            Assert.That(Screen.Error, Is.Null);
            Assert.That(View.Player, Is.SameAs(player), "The player was rebuilt rather than changed where they stand.");
            var spawn = View.Spawns[id];
            Assert.That(spawn.Name, Is.EqualTo("Pebbles"));
            Assert.That(Join.Content.Shapes[spawn.Shape].Mesh, Is.EqualTo(MeshKind.Diamond));
            Assert.That(spawn.Color, Is.EqualTo(Join.Content.Settings.Colors.Body[2]));
            Assert.That(Vector3.Distance(player.transform.position, at), Is.LessThan(0.5f), "The player moved.");
            Assert.That(player.name, Is.EqualTo("Pebbles (you)"));
        }

        [UnityTest]
        public IEnumerator ReportsAnUnreachableServer()
        {
            yield return StartGame(join: false);
            Field("name").value = "Pebble";
            Field("server").value = "127.0.0.1:9";
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => Screen.Error != null, 15);
            Assert.That(Screen.Error, Is.EqualTo(Strings.Format("join.unreachable", ("address", "127.0.0.1:9"))));
            Assert.That(Screen.Showing, Is.True);
        }

        private string[] StatValues() =>
            Screen.Root.Q("stats").Query<Label>().ToList().Where(l => !l.ClassListContains("stat-name")).Select(l => l.text).ToArray();
    }
}

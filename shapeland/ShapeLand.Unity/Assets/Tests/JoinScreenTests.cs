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
        // Without accounts the card comes back as the person last played on the editor's server, never as one of its
        // bots, though the bots saved their spots later.
        [UnityTest]
        public IEnumerator WithoutAccountsTheCardComesBackAsTheLastPersonNotABot()
        {
            var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"shapeland-dev-{System.Guid.NewGuid():N}.json");
            System.IO.File.WriteAllText(
                file,
                "{\"Characters\":[{\"Id\":1,\"AccountId\":1,\"Name\":\"Ada\",\"Shape\":1,\"Color\":1,\"EyeColor\":1,\"IsDeleted\":false},"
                    + "{\"Id\":2,\"AccountId\":2,\"Name\":\"Bot0001\",\"Shape\":1,\"Color\":1,\"EyeColor\":1,\"IsDeleted\":false}],"
                    + "\"Statuses\":[{\"CharacterId\":1,\"UpdatedAt\":\"2026-11-04T10:00:00Z\"},{\"CharacterId\":2,\"UpdatedAt\":\"2026-11-04T11:00:00Z\"}]}"
            );
            var autoJoin = GameSettings.AutoJoin;
            GameSettings.AutoJoin = false;
            try
            {
                yield return StartGame(join: false, devCharacters: file);
                Assert.That(Field("name").value, Is.EqualTo("Ada"));
            }
            finally
            {
                GameSettings.AutoJoin = autoJoin;
                System.IO.File.Delete(file);
            }
        }

        [UnityTest]
        public IEnumerator ShowsThePickedShapesStartZoneThenTheServersOnceIn()
        {
            yield return StartGame(join: false);
            Click(ShapeButton("shape.cube"));
            var cube = Join.Content.Shapes.All.First(shape => shape.Id == "shape.cube");
            var start = ShapeLandWorld.StartOf(Join.Content, cube).Zone.Id;

            Assert.That(View.transform.Find(start), Is.Not.Null, "The shape's start zone wasn't drawn behind the join screen.");
            Assert.That(View.Player, Is.Null, "Joined before pressing Join.");

            // The tests' server starts everyone in its own zone.
            Field("name").value = "Pebble";
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => View.Player != null, 10);
            yield return null; // destroyed objects go at the end of the frame
            Assert.That(View.transform.Find(TestGameServer.Zone), Is.Not.Null, "The zone the server put the player in wasn't drawn.");
            Assert.That(View.transform.Find(start), Is.Null, "The start zone is still drawn.");
        }

        [UnityTest]
        public IEnumerator JoinsWithTheChosenShapeAndColors()
        {
            yield return StartGame(join: false);
            Assert.That(View.Player, Is.Null, "Joined before pressing Join.");

            Field("name").value = "Pebble";
            Click(ShapeButton("shape.cube"));
            Click(Swatch("colors", Join.Content.Colors.All[1].Number));
            Click(Swatch("eyes", Join.Content.Colors.All[3].Number));
            // From content, so tuning the cube doesn't break the test.
            var cube = Join.Content.Shapes.All.First(shape => shape.Id == "shape.cube");
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
            Assert.That(Join.Content.Shapes[spawn.Shape].Id, Is.EqualTo("shape.cube"));
            Assert.That(spawn.Color, Is.EqualTo(Join.Content.Colors.All[1].Number));
            Assert.That(spawn.EyeColor, Is.EqualTo(Join.Content.Colors.All[3].Number));

            // The overlay fills the screen next to the join screen (a nested UIDocument once gave it no height).
            yield return null;
            var screen = Hud.Root.panel.visualTree.worldBound;
            Assert.That(Hud.Root.worldBound.height, Is.EqualTo(screen.height).Within(1), "The overlay doesn't fill the screen.");
            Assert.That(
                Hud.Root.Q("chat").worldBound.yMax,
                Is.LessThanOrEqualTo(screen.yMax).And.GreaterThan(screen.height / 2),
                "The chat box isn't in the lower part of the screen."
            );
        }

        [UnityTest]
        public IEnumerator ALostConnectionGoesBackToTheJoinScreen()
        {
            yield return StartGame(join: false);
            Field("name").value = "Pebble";
            Click(ShapeButton("shape.diamond"));
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
            Assert.That(Field("name").value, Is.EqualTo("Pebble"), "The card lost the name to play again with."); // accounts off: no signed-in card
            Assert.That(Screen.Root.Q<Button>("join-button").enabledSelf, Is.True, "Play isn't enabled for another try.");
        }

        [UnityTest]
        public IEnumerator LeavingStartsTheCardFresh()
        {
            yield return StartGame(join: false);
            Field("name").value = "Pebble";
            Click(ShapeButton("shape.diamond"));
            Click(Swatch("colors", Join.Content.Colors.All[1].Number));
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => View.Player != null, 15);
            Assert.That(View.Player, Is.Not.Null, "Never spawned.");

            Join.Leave();
            yield return TestGameServer.WaitFor(() => Screen.Showing, 10);
            Assert.That(Field("name").value, Is.Empty, "The card kept the last player's name.");
            Assert.That(ShapeButton(Join.Content.Shapes.All[0].Id).ClassListContains("on"), Is.True, "The card kept the last player's shape.");
            Assert.That(
                Swatch("colors", Join.Content.Colors.All[0].Number).ClassListContains("on"),
                Is.True,
                "The card kept the last player's color."
            );
        }

        // Without accounts, as the editor's server restarting for new content: back in as the same name.
        [UnityTest]
        public IEnumerator RejoinsAServerThatRestarted() => Rejoins(accounts: false);

        [UnityTest, Category("Accounts")]
        public IEnumerator RejoinsAServerThatRestartedWithAccounts() => Rejoins(accounts: true);

        [UnityTest, Category("Accounts")]
        public IEnumerator AGuestJoinsThroughTheCard()
        {
            yield return StartGame(accounts: true);
            Assert.That(Join.SignedIn, Is.True, "Joining made no guest account.");
            Assert.That(Join.Spawn.Value.Name, Is.EqualTo("UnityTest"));
        }

        // With accounts off (the editor's default), Sign In and the Account entry stay where they are, off, saying why.
        [UnityTest]
        public IEnumerator WithoutAccountsSignInAndTheAccountPageAreOff()
        {
            yield return StartGame(join: false);
            Assert.That(Screen.Root.Q<Button>("sign-in-button").enabledSelf, Is.False, "Sign in is on without accounts.");
            Assert.That(Screen.Root.Q<Label>(className: "have-account-text").text, Is.EqualTo(Strings.Get("account.off_note")));
            Assert.That(Screen.Root.Q("server-line").ClassListContains("hidden"), Is.True, "The Login server line shows without accounts.");

            Field("name").value = "Pebble";
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => View.Player != null || Screen.Error != null, 15);
            Assert.That(View.Player, Is.Not.Null, "Never spawned.");
            Assert.That(Hud.Root.Q("menu-account").enabledSelf, Is.False, "The Account entry is on without accounts.");
        }

        private IEnumerator Rejoins(bool accounts)
        {
            yield return StartGame(join: false, accounts: accounts);
            Field("name").value = "Pebble";
            Click(ShapeButton("shape.cube"));
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => View.Player != null, 15);
            Assert.That(View.Player, Is.Not.Null, "Never spawned.");

            yield return Server.ShutDownServer();
            yield return TestGameServer.WaitFor(() => Screen.Connecting, 10);
            Assert.That(Screen.Connecting, Is.True, "The connecting pop-up didn't come up.");
            Assert.That(Screen.Showing, Is.False, "The card came up while rejoining.");
            yield return Wait(3); // several tries fail meanwhile
            Assert.That(Screen.Connecting, Is.True, "The pop-up went away while rejoining.");
            Assert.That(Screen.ConnectingError, Is.Null, "A failed try showed an error while rejoining.");

            yield return Server.StartAgain();
            yield return TestGameServer.WaitFor(() => View.Player != null, 15);
            Assert.That(View.Player, Is.Not.Null, "Didn't rejoin the restarted server.");
            Assert.That(Screen.Showing, Is.False, "The join screen stayed up.");
            Assert.That(Join.Spawn.Value.Name, Is.EqualTo("Pebble"));
            Assert.That(Join.Content.Shapes[Join.Spawn.Value.Shape].Id, Is.EqualTo("shape.cube"));
        }

        [UnityTest, Category("Accounts")]
        public IEnumerator ShowsRefusalsAndTriesAgain()
        {
            yield return StartGame(join: false, accounts: true);
            yield return Server.StartBots(1, 30);
            yield return Wait(3); // time for the bot to join; this client can't see it before joining

            var join = Screen.Root.Q<Button>("join-button");
            Field("name").value = "no!";
            Click(join);
            Assert.That(Screen.Error, Is.EqualTo(Strings.Get("join.name_rules")), "A bad name wasn't caught before sending.");

            Field("name").value = "bot0001"; // the bot's name, in other capitals
            Click(join);
            yield return TestGameServer.WaitFor(() => Screen.Error != null, 10); // pressing Join cleared the last one
            Assert.That(Screen.Error, Is.EqualTo(Strings.Format("join.name_taken_new", ("username", "bot0001"))), "A taken name wasn't reported.");
            Assert.That(Screen.Root.Q<Button>("sign-in-button").ClassListContains("pointed"), Is.True, "A taken name doesn't point to Sign in.");
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
            Click(ShapeButton("shape.cube"));
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => View.Player != null, 15);
            var player = View.Player;
            var id = Join.Session.EntityId;

            Hud.OpenLook();
            yield return LaidOut("cancel-button");
            Assert.That(Screen.Changing, Is.True);
            Assert.That(
                Screen.Root.Q("name-row").ClassListContains("hidden"),
                Is.True,
                "Change Shape still has the name row (renaming is on the Account page)."
            );
            Assert.That(player.InputEnabled, Is.False, "The shape can still move while choosing a look.");
            Click(Screen.Root.Q<Button>("cancel-button"));
            yield return null;
            Assert.That(Screen.Showing, Is.False);
            Assert.That(player.InputEnabled, Is.True);

            Hud.OpenLook();
            yield return LaidOut("cancel-button");
            Click(ShapeButton("shape.diamond"));
            Click(Swatch("colors", Join.Content.Colors.All[2].Number));
            var at = player.transform.position;
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => !Screen.Showing || Screen.Error != null, 10);

            Assert.That(Screen.Error, Is.Null);
            Assert.That(View.Player, Is.SameAs(player), "The player was rebuilt rather than changed where they stand.");
            var spawn = View.Spawns[id];
            Assert.That(spawn.Name, Is.EqualTo("Pebble"));
            Assert.That(Join.Content.Shapes[spawn.Shape].Id, Is.EqualTo("shape.diamond"));
            Assert.That(spawn.Color, Is.EqualTo(Join.Content.Colors.All[2].Number));
            Assert.That(Vector3.Distance(player.transform.position, at), Is.LessThan(0.5f), "The player moved.");
            Assert.That(player.name, Is.EqualTo("Pebble (you)"));
        }

        [UnityTest, Category("Accounts")]
        public IEnumerator ReportsAnUnreachableServer()
        {
            yield return StartGame(join: false, accounts: true);
            Field("name").value = "Pebble";
            Field("server").value = "127.0.0.1:9";
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => Screen.Error != null, 15);
            Assert.That(Screen.Error, Is.EqualTo(Strings.Get("account.unreachable")));
            Assert.That(Screen.Showing, Is.True);
        }

        private string[] StatValues() =>
            Screen.Root.Q("stats").Query<Label>().ToList().Where(l => !l.ClassListContains("stat-name")).Select(l => l.text).ToArray();
    }
}

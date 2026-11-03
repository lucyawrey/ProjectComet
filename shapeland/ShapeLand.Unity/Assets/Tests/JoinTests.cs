using System.Collections;
using Comet.Unity;
using NUnit.Framework;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace ShapeLand.Client.Tests
{
    // Joins the real ShapeLand servers from Play mode with the join client alone: makes a guest account at the Login
    // server, gets a ticket, and checks the welcome, the player's own spawn and the first pong. Needs the dotnet SDK on
    // the PATH, Docker, and the content built and copied (ShapeLand > Copy Content).
    [Category("Accounts")]
    public class JoinTests
    {
        private TestGameServer _server;
        private GameObject _client;

        [TearDown]
        public void TearDown()
        {
            if (_client != null)
            {
                Object.Destroy(_client);
            }

            if (_server != null)
            {
                JoinClient.ForgetToken(_server.Address);
                _server.Dispose();
                _server = null;
            }
        }

        [UnityTest]
        public IEnumerator JoinsTheGameServer()
        {
            yield return StartServer();
            var join = StartClient();

            yield return WaitForJoin(join);
            Assert.That(join.Session.Clock.Synced, "No pong arrived.");
            Debug.Log($"Joined as entity {join.Session.EntityId}; round trip {join.Session.Clock.RoundTrip * 1000:0.0} ms.");
        }

        // Leave game closes the connection with no error; the player stays signed in and plays again with a new ticket.
        [UnityTest]
        public IEnumerator LeavesAndJoinsAgain()
        {
            yield return StartServer();
            var join = StartClient();
            yield return WaitForJoin(join);

            Comet.Client.CloseReason? lost = null;
            join.ConnectionLost += reason => lost = reason;
            join.Leave();
            Assert.That(lost, Is.EqualTo(Comet.Client.CloseReason.None));
            Assert.That(join.Joined, Is.False);
            Assert.That(join.Session, Is.Null);
            Assert.That(join.Error, Is.Null);

            _ = join.PlayAsync();
            yield return WaitForJoin(join);
        }

        private IEnumerator StartServer()
        {
            TestGameServer.LoadContent();
            _server = new TestGameServer(accounts: true);
            yield return _server.Start();
        }

        private JoinClient StartClient()
        {
            JoinClient.TokenKey = "tests-login-token";
            JoinClient.ForgetToken(_server.Address);
            _client = new GameObject("Join client");
            _client.SetActive(false);
            _client.AddComponent<CometConnection>();
            var join = _client.AddComponent<JoinClient>();
            join.ContentLoaded += _ => Join(join);
            _client.SetActive(true);
            return join;
        }

        private void Join(JoinClient join)
        {
            join.UseLoginServer(_server.Address);
            _ = join.CreateGuestAsync("UnityTest", new NewCharacter
            {
                Shape = join.Content.Shapes.All[0].Number,
                Color = join.Content.Colors.All[0].Number,
                EyeColor = join.Content.Colors.All[0].Number,
            });
        }

        private static IEnumerator WaitForJoin(JoinClient join)
        {
            yield return TestGameServer.WaitFor(() => join.Joined && join.Session.Clock.Synced || join.Error != null || join.Rejected != null, 15);
            Assert.That(join.Error, Is.Null);
            Assert.That(join.Rejected, Is.Null);
            Assert.That(join.Joined, "Never spawned.");
        }
    }
}

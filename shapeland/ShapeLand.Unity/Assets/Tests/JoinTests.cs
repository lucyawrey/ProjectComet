using System.Collections;
using Comet.Unity;
using NUnit.Framework;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace ShapeLand.Client.Tests
{
    // Joins the real ShapeLand game server from Play mode with the join client alone: builds it, starts it on a free port and
    // checks the welcome, the player's own spawn and the first pong. Needs the dotnet SDK on the PATH and the
    // content built and copied (ShapeLand > Copy Content).
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

            _server?.Dispose();
            _server = null;
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

        // Leave game closes the connection with no error, and the name is free again for the next join.
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

            Join(join);
            yield return WaitForJoin(join);
        }

        private IEnumerator StartServer()
        {
            TestGameServer.LoadContent();
            _server = new TestGameServer();
            yield return _server.Start();
        }

        private JoinClient StartClient()
        {
            _client = new GameObject("Join client");
            _client.SetActive(false);
            _client.AddComponent<CometConnection>();
            var join = _client.AddComponent<JoinClient>();
            join.ContentLoaded += _ => Join(join);
            _client.SetActive(true);
            return join;
        }

        private void Join(JoinClient join) => join.Join(_server.Address, new JoinRequest
        {
            Name = "Unity test",
            Shape = join.Content.Shapes.All[0].Number,
            Color = join.Content.Settings.Colors.Players[0],
            EyeColor = join.Content.Settings.Colors.Players[0],
        });

        private static IEnumerator WaitForJoin(JoinClient join)
        {
            yield return TestGameServer.WaitFor(() => join.Joined && join.Session.Clock.Synced || join.Error != null || join.Rejected != null, 15);
            Assert.That(join.Error, Is.Null);
            Assert.That(join.Rejected, Is.Null);
            Assert.That(join.Joined, "Never spawned.");
        }
    }
}

using System.Collections;
using System.IO;
using Comet.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ShapeLand.Client.Tests
{
    // Joins the real ShapeLand game server from Play mode (step 3b): builds it, starts it on a free port and
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
        }

        [UnityTest]
        public IEnumerator JoinsTheGameServer()
        {
            Assert.That(File.Exists(Path.Combine(Application.streamingAssetsPath, "content.bin")), "No content in StreamingAssets; run ShapeLand > Copy Content.");

            _server = new TestGameServer();
            yield return _server.Start();

            _client = new GameObject("Join client");
            _client.SetActive(false);
            _client.AddComponent<CometConnection>();
            var join = _client.AddComponent<JoinClient>();
            join.Address = _server.Address;
            join.PlayerName = "Unity test";
            _client.SetActive(true);

            yield return TestGameServer.WaitFor(() => join.Joined && join.Session.Clock.Synced || join.Error != null || join.Rejected != null, 15);
            Assert.That(join.Error, Is.Null);
            Assert.That(join.Rejected, Is.Null);
            Assert.That(join.Joined, "Never spawned.");
            Assert.That(join.Session.Clock.Synced, "No pong arrived.");
            Debug.Log($"Joined as entity {join.Session.EntityId}; round trip {join.Session.Clock.RoundTrip * 1000:0.0} ms.");
        }
    }
}

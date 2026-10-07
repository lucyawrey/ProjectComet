using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Comet.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace ShapeLand.Client.Tests
{
    // Joins the real ShapeLand game server from Play mode (step 3b): builds it, starts it on a free port and
    // checks the welcome, the player's own spawn and the first pong. Needs the dotnet SDK on the PATH and the
    // content built and copied (ShapeLand > Copy Content).
    public class JoinTests
    {
        private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private Process _server;
        private GameObject _client;

        [TearDown]
        public void TearDown()
        {
            if (_client != null)
            {
                Object.Destroy(_client);
            }

            if (_server != null && !_server.HasExited)
            {
                _server.Kill();
            }

            _server?.Dispose();
        }

        [UnityTest]
        public IEnumerator JoinsTheGameServer()
        {
            Assert.That(File.Exists(Path.Combine(Application.streamingAssetsPath, "content.bin")), "No content in StreamingAssets; run ShapeLand > Copy Content.");

            using (var build = Start("build shapeland/src/ShapeLand.GameServer --nologo -v quiet"))
            {
                while (!build.HasExited)
                {
                    yield return null;
                }

                Assert.That(build.ExitCode, Is.EqualTo(0), "The game server didn't build.");
            }

            // The server's output isn't read: reading a child's output asynchronously keeps Unity's editor from exiting.
            var port = FreePort();
            _server = Start($"artifacts/bin/ShapeLand.GameServer/debug/ShapeLand.GameServer.dll --urls http://127.0.0.1:{port}");
            var listening = false;
            var deadline = Time.realtimeSinceStartup + 30;
            while (!listening && !_server.HasExited && Time.realtimeSinceStartup < deadline)
            {
                listening = Accepts(port);
                yield return null;
            }

            Assert.That(listening, "The game server didn't start.");

            _client = new GameObject("Join client");
            _client.SetActive(false);
            _client.AddComponent<CometConnection>();
            var join = _client.AddComponent<JoinClient>();
            join.Address = $"127.0.0.1:{port}";
            join.PlayerName = "Unity test";
            _client.SetActive(true);

            yield return WaitFor(() => join.Joined && join.Session.Clock.Synced || join.Error != null || join.Rejected != null, 15);
            Assert.That(join.Error, Is.Null);
            Assert.That(join.Rejected, Is.Null);
            Assert.That(join.Joined, "Never spawned.");
            Assert.That(join.Session.Clock.Synced, "No pong arrived.");
            Debug.Log($"Joined as entity {join.Session.EntityId}; round trip {join.Session.Clock.RoundTrip * 1000:0.0} ms.");
        }

        private static Process Start(string arguments)
        {
            var process = Process.Start(new ProcessStartInfo("dotnet", arguments)
            {
                WorkingDirectory = RepoRoot,
                UseShellExecute = false,
            });
            Assert.That(process, Is.Not.Null, "Couldn't start dotnet.");
            return process;
        }

        private static IEnumerator WaitFor(System.Func<bool> done, float seconds)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private static bool Accepts(int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    client.Connect(IPAddress.Loopback, port);
                    return true;
                }
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}

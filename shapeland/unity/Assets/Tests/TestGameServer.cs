using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using UnityEngine;

namespace ShapeLand.Client.Tests
{
    // The real ShapeLand game server for Play mode tests: builds it and starts it on a free port. Needs the dotnet
    // SDK on the PATH and the content built.
    public sealed class TestGameServer : IDisposable
    {
        private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private Process _process;

        /// <summary>The server's address, once <see cref="Start"/> has finished.</summary>
        public string Address { get; private set; }

        public IEnumerator Start()
        {
            using (var build = Run("build shapeland/src/ShapeLand.GameServer --nologo -v quiet"))
            {
                while (!build.HasExited)
                {
                    yield return null;
                }

                Assert.That(build.ExitCode, Is.EqualTo(0), "The game server didn't build.");
            }

            // The server's output isn't read: reading a child's output asynchronously keeps Unity's editor from exiting.
            var port = FreePort();
            _process = Run($"artifacts/bin/ShapeLand.GameServer/debug/ShapeLand.GameServer.dll --urls http://127.0.0.1:{port}");
            var listening = false;
            var deadline = Time.realtimeSinceStartup + 30;
            while (!listening && !_process.HasExited && Time.realtimeSinceStartup < deadline)
            {
                listening = Accepts(port);
                yield return null;
            }

            Assert.That(listening, "The game server didn't start.");
            Address = $"127.0.0.1:{port}";
        }

        public void Dispose()
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
            }

            _process?.Dispose();
        }

        public static IEnumerator WaitFor(Func<bool> done, float seconds)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private static Process Run(string arguments)
        {
            var process = Process.Start(new ProcessStartInfo("dotnet", arguments)
            {
                WorkingDirectory = RepoRoot,
                UseShellExecute = false,
            });
            Assert.That(process, Is.Not.Null, "Couldn't start dotnet.");
            return process;
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

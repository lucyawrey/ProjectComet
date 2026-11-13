using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using ShapeLand.Shared.Content;
using UnityEngine;

namespace ShapeLand.Client.Tests
{
    public sealed class TestGameServer : IDisposable
    {
        public const string Zone = "zone.tests";

        public const string StartSpawn = "spawn.tests.start";

        private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private Process _process;
        private Process _login;
        private Process _bots;
        private int _port;
        private int _loginPort;
        private string _database;
        private string _connection;
        private string _keys;
        private readonly bool _accounts;
        private readonly string _loginArguments;
        private readonly bool _usedAccounts = Comet.Unity.LocalServerPort.UseAccounts;
        private readonly int _editorPort = UnityEditor.SessionState.GetInt(Comet.Unity.LocalServerPort.Key, 0);

        public TestGameServer(bool accounts = false, string loginArguments = "")
        {
            _accounts = accounts;
            _loginArguments = loginArguments;
        }

        public static ShapeLandContent LoadContent()
        {
            var file = Path.Combine(Application.streamingAssetsPath, "content.bin");
            Assert.That(File.Exists(file), "No content in StreamingAssets; run ShapeLand > Copy Content.");
            using var stream = File.OpenRead(file);
            return ShapeLandContent.Load(stream);
        }

        public string Address { get; private set; }

        public string GameAddress => $"127.0.0.1:{_port}";

        public IEnumerator Start()
        {
            UnityEditor.EditorPrefs.SetBool(Comet.Unity.LocalServerPort.UseAccountsKey, _accounts);
            foreach (var project in _accounts ? new[] { "ShapeLand.LoginServer", "ShapeLand.GameServer" } : new[] { "ShapeLand.GameServer" })
            {
                using var build = Run($"build shapeland/src/{project} --nologo -v quiet");
                while (!build.HasExited)
                {
                    yield return null;
                }

                Assert.That(build.ExitCode, Is.EqualTo(0), $"{project} didn't build.");
            }

            _port = FreePort();
            UnityEditor.SessionState.SetInt(Comet.Unity.LocalServerPort.Key, _port);
            _keys = Path.Combine(Path.GetTempPath(), $"shapeland-unity-tests-{_port}");
            if (!_accounts)
            {
                yield return Launch();
                yield break;
            }

            _loginPort = FreePort();
            _database = $"shapeland_unity_tests_{_port}";
            _connection = Database("fresh", _database);

            _login = Run(
                $"artifacts/bin/ShapeLand.LoginServer/debug/ShapeLand.LoginServer.dll --urls http://127.0.0.1:{_loginPort} "
                    + $"\"--Login:ConnectionString={_connection}\" \"--Login:SigningKeyFile={Path.Combine(_keys, "login.pem")}\" "
                    + $"\"--Login:PublicKeysFolder={Path.Combine(_keys, "public")}\" --Login:CreateSigningKey=true "
                    + "--Login:RateLimits:ExemptAddresses:0=127.0.0.1 --Login:RateLimits:ExemptAddresses:1=::1 "
                    + $"--ShapeLand:GameServer:Address=ws://127.0.0.1:{_port}/ws {_loginArguments}"
            );
            yield return WaitFor(() => _login.HasExited || Accepts(_loginPort), 30);
            Assert.That(!_login.HasExited && Accepts(_loginPort), "The Login server didn't start.");
            yield return Launch();
            Address = $"127.0.0.1:{_loginPort}";
        }

        private IEnumerator Launch()
        {
            // The server's output isn't read: reading a child's output asynchronously keeps Unity's editor from exiting.
            var characters = _accounts
                ? $"\"--ShapeLand:ConnectionString={_connection}\" \"--ShapeLand:TicketKeysFolder={Path.Combine(_keys, "public")}\""
                : $"--ShapeLand:DevJoins=true \"--ShapeLand:DevCharactersFile={Path.Combine(_keys, "dev-characters.json")}\"";
            _process = Run(
                $"artifacts/bin/ShapeLand.GameServer/debug/ShapeLand.GameServer.dll --urls http://127.0.0.1:{_port} --ShapeLand:StartSpawn {StartSpawn} {characters}"
            );
            var listening = false;
            var deadline = Time.realtimeSinceStartup + 30;
            while (!listening && !_process.HasExited && Time.realtimeSinceStartup < deadline)
            {
                listening = Accepts(_port);
                yield return null;
            }

            Assert.That(listening, "The game server didn't start.");
        }

        public IEnumerator ShutDownServer()
        {
            if (_accounts)
            {
                using var term = Process.Start("kill", $"-TERM {_process.Id}");
                term.WaitForExit();
            }
            else
            {
                using var http = new System.Net.Http.HttpClient();
                http.PostAsync($"http://127.0.0.1:{_port}/dev/stop", null).Wait();
            }

            yield return WaitFor(() => _process.HasExited, 10);
            Assert.That(_process.HasExited, "The game server didn't stop.");
            _process.Dispose();
            _process = null;
        }

        public IEnumerator StartAgain() => Launch();

        public IEnumerator StartBots(int count, double seconds)
        {
            Assert.That(_accounts, "Bots sign in through the Login server: start the server with accounts.");
            using (var build = Run("build shapeland/src/ShapeLand.Bots --nologo -v quiet"))
            {
                while (!build.HasExited)
                {
                    yield return null;
                }

                Assert.That(build.ExitCode, Is.EqualTo(0), "The bots didn't build.");
            }

            _bots = Run(
                $"artifacts/bin/ShapeLand.Bots/debug/ShapeLand.Bots.dll --login http://{Address} --tokens \"{Path.Combine(_keys, "bots")}\" --count {count} --seconds {seconds} --chat"
            );
        }

        public void StopServer() => Stop(ref _process);

        public void Dispose()
        {
            UnityEditor.EditorPrefs.SetBool(Comet.Unity.LocalServerPort.UseAccountsKey, _usedAccounts);
            UnityEditor.SessionState.SetInt(Comet.Unity.LocalServerPort.Key, _editorPort);
            Stop(ref _bots);
            Stop(ref _process);
            Stop(ref _login);
            if (_database != null)
            {
                Database("drop", _database);
                _database = null;
            }

            if (_keys != null && Directory.Exists(_keys))
            {
                Directory.Delete(_keys, recursive: true);
            }
        }

        private static string Database(string command, string name)
        {
            using var process = Process.Start(
                new ProcessStartInfo("dotnet", $"run --file \"{Path.Combine(RepoRoot, "tools", "comet-postgres.cs")}\" -- {command} {name}")
                {
                    WorkingDirectory = RepoRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                }
            );
            Assert.That(process, Is.Not.Null, "Couldn't run tools/comet-postgres.cs.");
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            Assert.That(process.ExitCode, Is.EqualTo(0), $"tools/comet-postgres.cs {command} failed (is Docker running?).");
            return output;
        }

        private static void Stop(ref Process process)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // It exited between the check and the kill (Unity's Mono reports that as a Win32Exception).
            }

            process.Dispose();
            process = null;
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
            var process = Process.Start(new ProcessStartInfo("dotnet", arguments) { WorkingDirectory = RepoRoot, UseShellExecute = false });
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

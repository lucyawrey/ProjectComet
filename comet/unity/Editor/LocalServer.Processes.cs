using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Comet.Unity.Editor
{
    public static partial class LocalServer
    {
        private static bool Launch(
            CometSettings settings,
            Options options,
            string program,
            string bots,
            string login,
            string database,
            int port,
            int loginPort
        )
        {
            var content = ContentHashNow();
            if (content == null)
            {
                return false;
            }

            var arguments = ServerArguments(settings, ref options, program, bots, login, port, out var dev);
            var log = ContentBuild.ProjectPath(LogFile);
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.WriteAllText(log, "");

            var loginPid = 0;
            var loginStartTicks = 0L;
            if (login.Length > 0)
            {
                var host = options.Lan && LanAddress() is { } lan ? lan : "localhost";
                arguments += " " + settings.serverAccountArguments.Replace("{database}", database).Replace("{login}", $"http://{host}:{loginPort}");
                if (!TryStartLogin(settings, options, login, database, host, port, loginPort, out loginPid, out loginStartTicks))
                {
                    return false;
                }
            }

            var command = $"{arguments} {settings.serverArguments}";
            try
            {
                var (pid, startTicks) = StartDotnet(command);
                Write(
                    new State
                    {
                        pid = pid,
                        startTicks = startTicks,
                        content = content,
                        program = program,
                        botsProgram = bots,
                        loginPid = loginPid,
                        loginStartTicks = loginStartTicks,
                        loginProgram = login,
                        waiting = true,
                        dev = dev,
                        port = port,
                        loginPort = loginPort,
                        web = options.Web,
                        lan = options.Lan,
                        bots = options.Bots,
                    }
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"Couldn't start the local game server ({ContentBuild.Dotnet()} {command}): {e.Message}");
                return false;
            }

            Wait();
            return true;
        }

        private static string ServerArguments(
            CometSettings settings,
            ref Options options,
            string program,
            string bots,
            string login,
            int port,
            out bool dev
        )
        {
            if (options.Bots > 0 && (bots.Length == 0 || !File.Exists(bots)))
            {
                Debug.LogWarning(
                    string.IsNullOrEmpty(settings.botsProject)
                        ? "The Comet settings name no bots project, so no bots start."
                        : "Starting the server without bots."
                );
                options.Bots = 0;
            }

            var arguments =
                $"\"{program}\" --urls http://{(options.Lan ? "0.0.0.0" : "localhost")}:{port} \"--Comet:LogFile={ContentBuild.ProjectPath(LogFile)}\"";
            if (options.Web)
            {
                if (WebBuild(settings) is { } web)
                {
                    CopyContentInto(web);
                    arguments += " " + string.Format(settings.serverWebArgument, $"\"{web}\"");
                }
                else
                {
                    Debug.LogWarning("There's no web build to serve yet: make one in Comet › Server › Options…; starting the server without it.");
                    options.Web = false;
                }
            }

            if (options.Bots > 0)
            {
                arguments += " --Comet:Connections:MaxConnectionsPerAddress=1000";
            }

            dev = login.Length == 0 && !string.IsNullOrEmpty(settings.serverDevArguments);
            if (dev)
            {
                var characters = LocalServerPort.DevCharactersFile;
                arguments += " " + settings.serverDevArguments.Replace("{characters}", characters);
            }

            return arguments;
        }

        private static bool TryStartLogin(
            CometSettings settings,
            Options options,
            string login,
            string database,
            string host,
            int port,
            int loginPort,
            out int pid,
            out long startTicks
        )
        {
            var loginLog = ContentBuild.ProjectPath(LoginLogFile);
            File.WriteAllText(loginLog, "");
            var loginArguments =
                $"\"{login}\" --urls http://{(options.Lan ? "0.0.0.0" : "localhost")}:{loginPort} \"--Comet:LogFile={loginLog}\" "
                + settings
                    .loginArguments.Replace("{database}", database)
                    .Replace("{game}", $"ws://{host}:{port}/ws")
                    .Replace("{page}", $"http://{host}:{port}");
            try
            {
                (pid, startTicks) = StartDotnet(loginArguments);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Couldn't start the local Login server: {e.Message}");
                pid = 0;
                startTicks = 0;
                return false;
            }
        }

        private static (int Pid, long StartTicks) StartDotnet(string arguments)
        {
            using var process = Process.Start(
                new ProcessStartInfo(ContentBuild.Dotnet(), arguments)
                {
                    WorkingDirectory = ContentBuild.ProjectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            )!;
            return (process.Id, process.StartTime.ToUniversalTime().Ticks);
        }

        private readonly struct Built
        {
            public Built(string? program, string? error)
            {
                Program = program;
                Error = error;
            }

            public string? Program { get; }

            public string? Error { get; }
        }

        // Runs off the main thread, so no Unity calls.
        private static Built Build(string dotnet, string root, string project)
        {
            var artifacts = Path.Combine(root, "artifacts", "editor-server");
            try
            {
                var output = RunDotnet(
                    dotnet,
                    root,
                    $"build \"{project}\" -nologo -v quiet -t:Build -getProperty:TargetPath --artifacts-path \"{artifacts}\"",
                    out var exitCode
                );
                var program = exitCode == 0 ? output.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0) : null;
                return program != null && File.Exists(program) ? new Built(program, null) : new Built(null, output);
            }
            catch (Exception e)
            {
                return new Built(null, $"Couldn't run dotnet: {e.Message}. Is the .NET SDK installed?");
            }
        }

        private static Built Database(string dotnet, string tool, string root, string checkout, int port)
        {
            // The short form fails on older SDKs in the Unity folder, which holds several projects.
            string Run(string arguments, out int exitCode) => RunDotnet(dotnet, root, $"run --file \"{tool}\" -- {arguments}", out exitCode);

            try
            {
                var name = Run($"name \"{checkout}\" {port}", out var named);
                if (named != 0)
                {
                    return new Built(null, name);
                }

                var connection = Run($"database {name}", out var made);
                return made == 0 ? new Built(connection, null) : new Built(null, connection);
            }
            catch (Exception e)
            {
                return new Built(null, $"Couldn't run {tool}: {e.Message}");
            }
        }

        private static string RunDotnet(string dotnet, string root, string arguments, out int exitCode)
        {
            var start = new ProcessStartInfo(dotnet, arguments)
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            exitCode = process.ExitCode;
            return exitCode == 0 ? output : output + error.Result;
        }

        private static bool PortInUse(int port)
        {
            try
            {
                using var client = new TcpClient();
                return client.ConnectAsync("localhost", port).Wait(500) && client.Connected;
            }
            catch (Exception e) when (e is SocketException or AggregateException)
            {
                return false;
            }
        }

        [Serializable]
        private sealed class State
        {
            public int pid;
            public long startTicks;
            public string content = "";
            public string program = "";
            public int botsPid;
            public long botsStartTicks;
            public string botsProgram = "";
            public int loginPid;
            public long loginStartTicks;
            public string loginProgram = "";
            public bool waiting;
            public bool dev;
            public int port;
            public int loginPort;
            public bool web;
            public bool lan;
            public int bots;
        }

        private static State? Read()
        {
            var path = ContentBuild.ProjectPath(StateFile);
            try
            {
                return File.Exists(path) ? JsonUtility.FromJson<State>(File.ReadAllText(path)) : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static void Write(State state) => File.WriteAllText(ContentBuild.ProjectPath(StateFile), JsonUtility.ToJson(state, true));

        private static bool Kill(int pid, long startTicks)
        {
            if (pid <= 0 || !Alive(pid, startTicks))
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
                process.WaitForExit(3000);
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
            {
                // It has stopped by itself meanwhile.
            }

            return true;
        }

        private static bool Alive(int pid, long startTicks)
        {
            if (pid <= 0)
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                return !process.HasExited && Math.Abs(process.StartTime.ToUniversalTime().Ticks - startTicks) < TimeSpan.TicksPerSecond;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }
}

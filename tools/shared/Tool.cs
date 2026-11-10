using System.Diagnostics;
using System.Runtime.InteropServices;

public static class Tool
{
    public static readonly string Self = (string)AppContext.GetData("EntryPointFilePath")!;
    public static readonly string Repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Self)!, ".."));
    public static readonly bool IsWindows = OperatingSystem.IsWindows();
    public static readonly bool IsMac = OperatingSystem.IsMacOS();
    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static string Command(string tool) => IsWindows ? $"tools\\run {tool}" : $"tools/run {tool}";

    public static int RunTool(string tool, params string[] args) =>
        Run("dotnet", ["run", "--file", Path.Combine(Repo, "tools", tool + ".cs"), "--", .. args]);

    public static void Usage(int exitCode = 0)
    {
        foreach (var line in File.ReadLines(Self).TakeWhile(l => l.StartsWith("//", StringComparison.Ordinal)))
        {
            var text = line.Length > 2 && line[2] == ' ' ? line[3..] : line[2..];
            Console.WriteLine(IsWindows ? text.Replace("tools/run ", "tools\\run ") : text);
        }
        Environment.Exit(exitCode);
    }

    public static Exception Fail(string message, int exitCode = 1)
    {
        Console.Error.WriteLine(message);
        RunStops();
        Environment.Exit(exitCode);
        return new InvalidOperationException();
    }

    // Where a program is: on PATH (with Windows' extensions, so npm's .cmd shims are found), or in the places a Mac
    // app started from the Dock or Unity Hub doesn't have on its PATH.
    public static string Find(string program)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).ToList();
        if (IsMac)
            dirs.AddRange(["/usr/local/bin", "/opt/homebrew/bin", "/Applications/Docker.app/Contents/Resources/bin"]);
        var extensions = IsWindows ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';') : [""];
        foreach (var dir in dirs.Where(d => d != ""))
        foreach (var extension in extensions)
        {
            var path = Path.Combine(dir, program + extension.ToLowerInvariant());
            if (File.Exists(path))
                return path;
        }
        return program;
    }

    public static bool Has(string program) => Find(program) != program;

    public static ProcessStartInfo Info(string program, IEnumerable<string> args, string? folder = null, IDictionary<string, string?>? env = null)
    {
        var info = new ProcessStartInfo(Find(program)) { WorkingDirectory = folder ?? Environment.CurrentDirectory };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        if (env != null)
            foreach (var (key, value) in env)
                info.Environment[key] = value;
        return info;
    }

    public static int Run(string program, params string[] args) => Run(Info(program, args));

    public static int Run(ProcessStartInfo info)
    {
        using var process = Process.Start(info) ?? throw Fail($"Couldn't start {info.FileName}.");
        process.WaitForExit();
        return process.ExitCode;
    }

    public static (int Code, string Output) Capture(string program, params string[] args) => Capture(Info(program, args));

    public static (int Code, string Output) Capture(ProcessStartInfo info, bool withErrors = false)
    {
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = withErrors;
        info.RedirectStandardInput = true;
        using var process = Process.Start(info) ?? throw Fail($"Couldn't start {info.FileName}.");
        process.StandardInput.Close();
        var output = withErrors ? Task.Run(() => process.StandardError.ReadToEnd()) : Task.FromResult("");
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, withErrors ? text + output.Result : text);
    }

    public static string Output(string program, params string[] args)
    {
        var (code, text) = Capture(program, args);
        if (code != 0)
            throw Fail($"Failed: {program} {string.Join(' ', args)}");
        return text.Trim();
    }

    // Runs a build step quietly, showing its output only if it fails (dotnet prints compile errors to stdout).
    public static void Quietly(string program, params string[] args) => Quietly(Info(program, args));

    public static void Quietly(ProcessStartInfo info)
    {
        var (code, text) = Capture(info, withErrors: true);
        if (code == 0)
            return;
        Console.Error.WriteLine(text.TrimEnd());
        throw Fail($"Failed: {Path.GetFileName(info.FileName)} {string.Join(' ', info.ArgumentList)}");
    }

    public static Process Start(ProcessStartInfo info)
    {
        var process = Process.Start(info) ?? throw Fail($"Couldn't start {info.FileName}.");
        OnStop(() => Stop(process));
        return process;
    }

    // Asks a program to stop as Ctrl+C would (SIGTERM on Linux and macOS: servers then tell their clients and save),
    // and kills it with its children if it hasn't within the wait. Windows has no such signal, so there it's killed.
    public static void Stop(Process process, int waitMilliseconds = 10_000)
    {
        try
        {
            if (process.HasExited)
                return;
            if (
                !IsWindows
                && Capture(Info("kill", ["-TERM", process.Id.ToString()]), withErrors: true).Code == 0
                && process.WaitForExit(waitMilliseconds)
            )
                return;
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (InvalidOperationException) { } // already gone
    }

    // The arguments a tool was given. tools\run.cmd passes its whole command line on as it was typed, so cmd never
    // splits it (at = ; , as well as spaces), and the tool's own name comes first: dropped here.
    public static string[] Arguments(string[] args) =>
        Environment.GetEnvironmentVariable("COMET_TOOLS_RUN_CMD") == "1" && args.Length > 0 ? args[1..] : args;

    public static bool Answers(string url, bool success = true, string? body = null)
    {
        try
        {
            using var response = Http.GetAsync(url).GetAwaiter().GetResult();
            if (!success)
                return true;
            if (!response.IsSuccessStatusCode)
                return false;
            return body == null || response.Content.ReadAsStringAsync().GetAwaiter().GetResult() == body;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public static bool WaitFor(Func<bool> check, int tries, TimeSpan delay)
    {
        for (var i = 0; i < tries; i++)
        {
            if (check())
                return true;
            Thread.Sleep(delay);
        }
        return false;
    }

    public static void OpenInBrowser(string url)
    {
        try
        {
            if (IsWindows)
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else
                Capture(IsMac ? "open" : "xdg-open", url);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception) { }
    }

    static readonly List<Action> Stops = [];
    static readonly List<PosixSignalRegistration> Signals = [];

    public static void OnStop(Action stop)
    {
        lock (Stops)
        {
            if (Signals.Count == 0)
            {
                AppDomain.CurrentDomain.ProcessExit += (_, _) => RunStops();
                foreach (var (signal, code) in new[] { (PosixSignal.SIGINT, 130), (PosixSignal.SIGTERM, 143), (PosixSignal.SIGHUP, 129) })
                    Signals.Add(
                        PosixSignalRegistration.Create(
                            signal,
                            context =>
                            {
                                context.Cancel = true;
                                RunStops();
                                Environment.Exit(code);
                            }
                        )
                    );
            }
            Stops.Add(stop);
        }
    }

    public static void RunStops()
    {
        Action[] toRun;
        lock (Stops)
        {
            toRun = [.. Stops];
            Stops.Clear();
        }
        for (var i = toRun.Length - 1; i >= 0; i--)
        {
            try
            {
                toRun[i]();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"While stopping: {e.Message}");
            }
        }
    }

    public sealed class Args(string[] args)
    {
        int _next;
        public bool More => _next < args.Length;
        public string Peek => args[_next];
        public string[] Rest => args[_next..];

        public string Take() => args[_next++];

        public string Value(string option) => More ? Take() : throw Fail($"{option} needs a value (see --help)", 2);

        public int Number(string option) => int.TryParse(Value(option), out var n) && n >= 0 ? n : throw Fail($"{option} takes a whole number", 2);
    }
}

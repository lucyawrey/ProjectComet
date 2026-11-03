// Runs a Unity build or tests and returns once the work is done: in the Unity editor that has the project open, when
// it's reachable through the Unity CLI (the project has the Unity Pipeline package), else in a batch-mode editor. A
// batch-mode editor often hangs while quitting (seen on Linux after both -executeMethod and -runTests), so this
// watches the log for the line that ends the run, gives the editor a short grace period to exit, then kills it.
//
//   tools/run unity-batch [-p project] [-l logfile] [-t minutes] build <profile> <output>
//   tools/run unity-batch [-p project] [-l logfile] [-t minutes] test [PlayMode|EditMode] [results]
//   tools/run unity-batch [-p project] [-l logfile] [-t minutes] -- <Unity arguments>
//
//   tools/run unity-batch build Web ../../artifacts/unity/shapeland/web
//   tools/run unity-batch test PlayMode
//   tools/run unity-batch test PlayMode -- -testCategory "!Accounts"   (the tests that need no Docker)
//
// build builds a profile in Assets/Settings/Build Profiles/. An open editor must already be on the profile's
// platform: switching would reimport the whole project. test runs a test platform (default PlayMode) and writes its
// results (default Logs/results.xml in batch mode, Logs/results.json in an open editor). Everything after -- goes
// to a batch-mode editor as it is, after a build's or test's own arguments (so an open editor can't take those).
//
// The project defaults to shapeland/ShapeLand.Unity, the log to Logs/batch.log and the time limit to 30 minutes. Relative
// paths, in the log, the output, the results and Unity arguments, are relative to the project. Exit code: 0 done
// (tests passed), 2 tests failed, 1 if it failed or gave no result, 124 on the time limit. Runs tools/run unity-restore
// first when the project's NuGet packages were never restored or packages.config changed since.
#:project shared/Tools.Shared.csproj
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

args = Tool.Arguments(args);

var project = Path.Combine(Tool.Repo, "shapeland", "ShapeLand.Unity");
var log = "Logs/batch.log";
var minutes = 30;
var options = new Tool.Args(args);
while (options.More && options.Peek is not ("--" or "build" or "test"))
{
    switch (options.Take())
    {
        case "-p": project = Path.GetFullPath(options.Value("-p")); break;
        case "-l": log = options.Value("-l"); break;
        case "-t": minutes = options.Number("-t"); break;
        case "-h" or "--help": Tool.Usage(); break;
        case var other: throw Tool.Fail($"Unknown option {other} (Unity arguments go after --)", 2);
    }
}
if (!options.More) throw Tool.Fail("Say what to run: build, test or -- <Unity arguments>", 2);
var job = options.Take();
var rest = options.Rest;
string[] extra = [];
if (job != "--" && Array.IndexOf(rest, "--") is var split and >= 0)
{
    extra = rest[(split + 1)..];
    rest = rest[..split];
}
Environment.CurrentDirectory = project;
log = Path.GetFullPath(log);
var deadline = DateTime.UtcNow.AddMinutes(minutes);

string profile = "", output = "", asset = "", mode = "PlayMode", results = "";
if (job == "build")
{
    if (rest.Length != 2) throw Tool.Fail("build takes a profile and an output path", 2);
    (profile, output) = (rest[0], Path.GetFullPath(rest[1]));
    asset = $"Assets/Settings/Build Profiles/{profile}.asset";
    if (!File.Exists(asset)) throw Tool.Fail($"No build profile at {asset}", 2);
}
else if (job == "test")
{
    mode = rest.ElementAtOrDefault(0) ?? "PlayMode";
    results = rest.ElementAtOrDefault(1) ?? "";
    if (mode is not ("PlayMode" or "EditMode")) throw Tool.Fail("test takes PlayMode or EditMode", 2);
}

// A fresh clone or worktree has no NuGet packages, and the editor can't compile without them.
var restored = new FileInfo("Assets/Packages/.restored");
if (File.Exists("Assets/packages.config") && (!restored.Exists || restored.LastWriteTimeUtc <= File.GetLastWriteTimeUtc("Assets/packages.config"))
    && Tool.RunTool("unity-restore", project) != 0)
    throw Tool.Fail("The NuGet restore failed.");

Directory.CreateDirectory(Path.GetDirectoryName(log)!);
File.WriteAllText(log, "");

var port = job == "--" ? null : EditorPort();
if (port != null && extra.Length > 0)
    throw Tool.Fail("Unity arguments after -- need a batch-mode editor; close the editor that has the project open.", 2);
Environment.Exit(port != null ? InEditor() : InBatchMode());

// The open editor's Pipeline port, if one has this project open and ready. unity status fails when none is open.
// macOS and Windows paths ignore case, so a shell in ~/developer must still find an editor opened on ~/Developer.
string? EditorPort()
{
    if (!Tool.Has("unity")) return null;
    var (code, text) = Tool.Capture(Tool.Info("unity", ["status", "--json", "--non-interactive"]), withErrors: true);
    if (code != 0) return null;
    var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    try
    {
        using var status = JsonDocument.Parse(text);
        if (!status.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("instances", out var instances)) return null;
        foreach (var instance in instances.EnumerateArray())
            if (string.Equals(Path.TrimEndingDirectorySeparator(instance.GetProperty("project").GetString() ?? ""), project, comparison)
                && instance.GetProperty("state").GetString() == "ready")
                return instance.GetProperty("port").ToString();
    }
    catch (JsonException) { }
    return null;
}

// Runs a command in the open editor and returns its result, or fails with its error. Build reports and test
// messages hold raw control characters (tabs, line breaks), which JSON strings don't allow, so they become spaces.
JsonElement Editor(params string[] command)
{
    var (code, text) = Tool.Capture(Tool.Info("unity", ["command", .. command, "--project-path", project, "--json",
        "--non-interactive", "--no-pager", "--no-banner"]), withErrors: true);
    text = Regex.Replace(text, @"[\x00-\x1f]", " ");
    JsonElement json;
    try { json = JsonDocument.Parse(text).RootElement; }
    catch (JsonException) { throw Tool.Fail(text); }
    if (code != 0)
    {
        var errors = json.TryGetProperty("errors", out var list) ? list.EnumerateArray().Select(e => e.GetProperty("message").GetString()) : [text];
        throw Tool.Fail(string.Join('\n', errors));
    }
    return json.GetProperty("data").GetProperty("result");
}

// Polls a status command until its status is one of the given ones, then returns its result.
JsonElement Await(string command, params string[] ends)
{
    while (true)
    {
        var result = Editor(command);
        if (ends.Contains(result.GetProperty("status").GetString())) return result;
        if (DateTime.UtcNow >= deadline)
            throw Tool.Fail($"Unity ran past {minutes} minutes; stopped waiting (it may still be running in the editor)", 124);
        Thread.Sleep(2000);
    }
}

int InEditor()
{
    Console.WriteLine($"== In the open Unity editor (port {port})");
    if (job == "build")
    {
        var want = Regex.Match(File.ReadAllText(asset), @"^  m_BuildTarget: *(\S+)", RegexOptions.Multiline).Groups[1].Value;
        var have = Editor("eval", "--code", "return ((int)EditorUserBuildSettings.activeBuildTarget).ToString();").GetProperty("result").GetString();
        if (want != have)
            throw Tool.Fail($"The open editor is on another platform than the {profile} profile; switch to the profile there (File › Build Profiles) or close the editor.");
        Editor("build", "--confirm", "true", "--profileName", profile, "--outputPath", output);
        var build = Await("build_status", "completed");
        File.WriteAllText(log, build.GetRawText());
        var outcome = build.TryGetProperty("result", out var r) ? r.GetString() : "Unknown";
        if (build.TryGetProperty("errors", out var errors))
            foreach (var error in errors.EnumerateArray()) Console.WriteLine(error.ToString());
        Console.WriteLine($"Unity: build {outcome} (report: {log})");
        return outcome == "Succeeded" ? 0 : 1;
    }

    results = Path.GetFullPath(results == "" ? "Logs/results.json" : results);
    var done = false;
    Tool.OnStop(() => { if (!done) Tool.Capture(Tool.Info("unity", ["command", "cancel_tests", "--project-path", project, "--non-interactive"]), withErrors: true); });
    Editor("run_tests", "--mode", mode, "--async_tests", "true");
    var run = Await("test_status", "completed", "failed");
    done = true;
    Directory.CreateDirectory(Path.GetDirectoryName(results)!);
    File.WriteAllText(results, run.GetRawText());
    if (run.TryGetProperty("results", out var tests))
        foreach (var test in tests.EnumerateArray().Where(t => t.GetProperty("Status").GetString() == "Failed"))
            Console.WriteLine($"Failed: {test.GetProperty("FullName")}\n{(test.TryGetProperty("Message", out var m) ? m.ToString() : "")}");
    int Count(string name) => run.TryGetProperty("summary", out var s) && s.TryGetProperty(name, out var n) ? n.GetInt32() : 0;
    var (total, failed) = (Count("total"), Count("failed"));
    Console.WriteLine($"Unity: {total} tests, {failed} failed (results: {results})");
    return total == 0 ? 1 : failed > 0 ? 2 : 0;
}

int InBatchMode()
{
    string[] unityArgs = job switch
    {
        // -build alone goes on to open the project once the build is done.
        "build" => ["-activeBuildProfile", asset, "-build", output, "-quit"],
        "test" => ["-runTests", "-testPlatform", mode, "-testResults", results == "" ? "Logs/results.xml" : results],
        _ => rest,
    };
    unityArgs = [.. unityArgs, .. extra];
    var version = Regex.Match(File.ReadAllText("ProjectSettings/ProjectVersion.txt"), @"^m_EditorVersion: *(\S+)", RegexOptions.Multiline).Groups[1].Value;
    var unity = Tool.IsMac ? $"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity"
        : Tool.IsWindows ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor", version, "Editor", "Unity.exe")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Unity", "Hub", "Editor", version, "Editor", "Unity");
    if (!File.Exists(unity)) throw Tool.Fail($"No Unity {version} at {unity}", 2);

    // Output goes to the log only. Its pipes are this tool's, so the editor's helper processes, which outlive it,
    // can't hold a caller's output open.
    var info = Tool.Info(unity, ["-batchmode", "-projectPath", project, "-logFile", log, .. unityArgs]);
    info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
    var editor = Tool.Start(info); // stopping this tool stops the editor too, or it keeps the project locked
    editor.StandardInput.Close();
    editor.OutputDataReceived += (_, _) => { };
    editor.ErrorDataReceived += (_, _) => { };
    editor.BeginOutputReadLine();
    editor.BeginErrorReadLine();

    int? code = null;
    while (!editor.HasExited && (code = Result()) == null)
    {
        if (DateTime.UtcNow >= deadline)
            throw Tool.Fail($"Unity ran past {minutes} minutes; stopping it (log: {log})", 124);
        Thread.Sleep(1000);
    }
    code ??= Result();
    if (code != null)
    {
        // Done: allow a normal exit, then stop a hung quit.
        if (!editor.WaitForExit(20_000))
        {
            Console.Error.WriteLine("Unity finished but hung while quitting; stopping it");
            Tool.Stop(editor);
        }
    }
    else
    {
        // Exited before any result line (a crash, a license problem, another editor on the project, a method that
        // called Exit).
        code = editor.ExitCode == 0 ? 1 : editor.ExitCode;
        Console.Error.WriteLine(ReadLog().Contains("another Unity instance")
            ? "A Unity editor has the project open; close it, or make it reachable through the Unity CLI."
            : $"Unity exited without a result line (log: {log})");
    }
    Console.WriteLine($"Unity: exit {code} (log: {log})");
    return code.Value;
}

// The exit code the run's last line means, once the log has one.
int? Result()
{
    var line = Regex.Match(ReadLog(), @"Test run completed\. Exiting with code (\d+)|Build Finished, Result: \w+|Batchmode quit successfully invoked|Aborting batchmode|Scripts have compiler errors");
    if (!line.Success) return null;
    if (line.Groups[1].Success) return int.Parse(line.Groups[1].Value);
    return line.Value is "Build Finished, Result: Success" or "Batchmode quit successfully invoked" ? 0 : 1;
}

// The editor writes the log as it goes; read it without getting in its way.
string ReadLog()
{
    try
    {
        using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(stream).ReadToEnd();
    }
    catch (IOException) { return ""; }
}

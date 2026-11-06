// Restores a Unity project's NuGet packages (MessagePack) before its first open; run it after a fresh clone and
// whenever packages.config changes (tools/run unity-batch runs it itself then). The editor can't compile the shared
// packages, and so can't run NuGetForUnity's own restore, until MessagePack is there.
//
//   tools/run unity-restore [project]   (relative to the repository, or absolute; default: shapeland/ShapeLand.Unity)
//
// NuGetForUnity's CLI (4.5.0) writes analyzer DLLs' .meta files in a format Unity 6.6 rejects (PluginImporter
// version 1), so Unity would import the source generator as an ordinary library. This rewrites them in the
// current format, keeping their GUIDs. The CLI is pinned to that version; check whether a newer one fixes it
// before moving the pin.
//
// Packages only the editor uses (Tomlyn, for the zone files) are also set to load in the editor only, so they stay
// out of player builds, and their analyzers are left off: nothing in Unity uses Tomlyn's source generator.
#:project shared/Tools.Shared.csproj
using System.Text.RegularExpressions;

args = Tool.Arguments(args);

if (args.FirstOrDefault() is "-h" or "--help")
    Tool.Usage();
var project = Path.GetFullPath(Path.Combine(Tool.Repo, args.FirstOrDefault() ?? "shapeland/ShapeLand.Unity"));
if (!File.Exists(Path.Combine(project, "Assets", "packages.config")))
    throw Tool.Fail($"No Assets/packages.config in {project}", 2);

var restore = Tool.Info(
    "dotnet",
    ["dnx", "-y", "NuGetForUnity.Cli@4.5.0", "--", "restore", project],
    env: new Dictionary<string, string?> { ["DOTNET_ROLL_FORWARD"] = "Major" }
);
if (Tool.Run(restore) != 0)
    throw Tool.Fail("The NuGet restore failed.");

var packages = Path.Combine(project, "Assets", "Packages");
int analyzers = 0,
    editorOnly = 0;
foreach (var dll in Directory.EnumerateFiles(packages, "*.dll", SearchOption.AllDirectories))
{
    var isTomlyn = Path.GetRelativePath(packages, dll).StartsWith("Tomlyn.", StringComparison.Ordinal);
    var isAnalyzer = dll.Split(Path.DirectorySeparatorChar).Contains("analyzers");
    if (isAnalyzer && !isTomlyn)
    {
        WriteMeta(dll + ".meta", analyzer: true, editor: false, keepGuidOnly: true);
        analyzers++;
    }
    else if (isTomlyn)
    {
        WriteMeta(dll + ".meta", analyzer: false, editor: !isAnalyzer, keepGuidOnly: false);
        if (!isAnalyzer)
            editorOnly++;
    }
}

// When it last finished, so tools/run unity-batch can tell whether packages.config changed since. Unity skips dot files.
File.WriteAllText(Path.Combine(packages, ".restored"), "");
Console.WriteLine($"Restored {project}; rewrote {analyzers} analyzer .meta file(s) and set {editorOnly} package DLL(s) to the editor only.");

// Writes a plugin .meta, enabled nowhere or in the editor only, keeping the GUID of an existing one. The CLI writes
// analyzers' .meta files only, so a Tomlyn DLL may have none yet; Unity would otherwise make one on import with every
// platform enabled.
static void WriteMeta(string meta, bool analyzer, bool editor, bool keepGuidOnly)
{
    var existing = File.Exists(meta) ? Regex.Match(File.ReadAllText(meta), @"^guid: *(\S+)", RegexOptions.Multiline) : null;
    if (keepGuidOnly && existing is not { Success: true })
        throw Tool.Fail($"No GUID in {meta}");
    var guid = existing is { Success: true } ? existing.Groups[1].Value : Guid.NewGuid().ToString("N");
    File.WriteAllText(
        meta,
        $"""
        fileFormatVersion: 2
        guid: {guid}
        {(analyzer ? "labels:\n- RoslynAnalyzer\n" : "")}PluginImporter:
          externalObjects: {"{}"}
          serializedVersion: 3
          iconMap: {"{}"}
          executionOrder: {"{}"}
          defineConstraints: []
          isPreloaded: 0
          isOverridable: 0
          isExplicitlyReferenced: 0
          validateReferences: 1
          platformData:
            Any:
              enabled: 0
              settings: {"{}"}
            Editor:
              enabled: {(editor ? 1 : 0)}
              settings:
                DefaultValueInitialized: true
          userData:
          assetBundleName:
          assetBundleVariant:

        """.ReplaceLineEndings("\n")
    );
}

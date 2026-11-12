// Runs the checks CI runs: the formatting check, the content build and the .NET tests. Then lists the interface
// text still left as a placeholder (a string whose whole value is in square brackets), and fails if there is any.
//
//   tools/run check
#:project shared/Tools.Shared.csproj
#:property PublishAot=false

using System.Text.RegularExpressions;

args = Tool.Arguments(args);
if (args.FirstOrDefault() is "-h" or "--help")
    Tool.Usage();

string[][] steps =
[
    ["tool", "restore"],
    ["csharpier", "check", "."],
    ["run", "--project", "shapeland/src/ShapeLand.ContentBuild", "--", "build"],
    ["test", "--solution", "ProjectComet.slnx"],
];
foreach (var step in steps)
    if (Tool.Run(Tool.Info("dotnet", step, Tool.Repo)) is var code and not 0)
        Environment.Exit(code);

var placeholder = new Regex(@"=\s*(""\[[^""]*\]""|'\[[^']*\]')\s*$");
var found = Directory
    .EnumerateDirectories(Tool.Repo)
    .Select(game => Path.Combine(game, "content", "strings"))
    .Where(Directory.Exists)
    .SelectMany(folder => Directory.EnumerateFiles(folder, "*.toml"))
    .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, index, line)))
    .Where(l => placeholder.IsMatch(l.line))
    .ToList();
foreach (var (file, index, line) in found)
    Console.Error.WriteLine($"{Path.GetRelativePath(Tool.Repo, file)}:{index + 1}: placeholder text: {line.Trim()}");
if (found.Count > 0)
    throw Tool.Fail($"{found.Count} placeholder(s) left in the interface text.");

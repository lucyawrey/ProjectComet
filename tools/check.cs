// Runs the checks CI runs: the formatting check, the content build and the .NET tests.
//
//   tools/run check
#:project shared/Tools.Shared.csproj

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

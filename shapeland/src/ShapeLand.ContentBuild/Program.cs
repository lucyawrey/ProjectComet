using System.Text.Json;
using Comet.Content;
using Comet.ContentBuild;
using ShapeLand.ContentBuild;

// ShapeLand's content tool. Paths are fixed relative to the repository, so it runs the same from anywhere.
var repo = FindRepoRoot();
var content = Path.Combine(repo, "shapeland", "content");
var output = Path.Combine(repo, "artifacts", "content", "shapeland");

switch (args)
{
    case ["build"]:
        return Build(release: false);
    case ["build", "--release"]:
        return Build(release: true);
    case ["schema"]:
        WriteSchemas();
        return 0;
    default:
        Console.Error.WriteLine(
            """
            Usage: ShapeLand.ContentBuild <command>

              build [--release]     check shapeland/content, add new ids to ids.toml, write the compiled
                                    content and the editor schemas to artifacts/content/shapeland;
                                    blockout placements are warnings, or errors with --release
              schema                write only the editor schemas
            """);
        return 2;
}

int Build(bool release)
{
    var warnings = new List<ContentError>();
    var (compiled, errors) = ShapeLandBuild.Build(content, release: release, warnings: warnings);
    foreach (var error in warnings.Concat(errors))
    {
        Console.Error.WriteLine(error);
    }

    if (compiled is null)
    {
        Console.Error.WriteLine($"Content build failed with {errors.Count} error(s).");
        return 1;
    }

    Directory.CreateDirectory(output);
    // Written beside it and renamed over it: the editor builds when a zone is saved while shapeland-dev.sh's watcher
    // builds too, and a rename never meets the other build's lock or leaves readers a half-written file.
    var file = Path.Combine(output, "content.bin");
    var temporary = $"{file}.{Environment.ProcessId}.tmp";
    using (var stream = File.Create(temporary))
    {
        compiled.Save(stream);
    }

    File.Move(temporary, file, overwrite: true);

    WriteSchemas();
    Console.WriteLine($"Compiled {compiled.Shapes.All.Count} shapes, {compiled.Props.All.Count} props, {compiled.Zones.All.Count} zone(s) and {compiled.Strings.Entries.Count} interface strings to {Path.GetRelativePath(repo, file)}.");
    return 0;
}

void WriteSchemas()
{
    var schemas = Path.Combine(output, "schemas");
    Directory.CreateDirectory(schemas);
    foreach (var (kind, schema) in ShapeLandBuild.CreateBuilder(content).Schemas())
    {
        File.WriteAllText(Path.Combine(schemas, $"{kind}.schema.json"), schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    Console.WriteLine($"Wrote editor schemas to {Path.GetRelativePath(repo, schemas)}.");
}

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "ProjectComet.slnx")))
        {
            return dir.FullName;
        }
    }

    throw new InvalidOperationException("Can't find the repository root (ProjectComet.slnx) above the tool.");
}

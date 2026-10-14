using System.Text.Json;
using Comet.Content;
using ShapeLand.ContentBuild;

// ShapeLand's content tool. Paths are fixed relative to the repository, so it runs the same from anywhere.
var repo = FindRepoRoot();
var content = Path.Combine(repo, "shapeland", "content");
var output = Path.Combine(repo, "artifacts", "content", "shapeland");

switch (args)
{
    case ["build"]:
        return Build();
    case ["schema"]:
        WriteSchemas();
        return 0;
    default:
        Console.Error.WriteLine(
            """
            Usage: ShapeLand.ContentBuild <command>

              build                 check shapeland/content, add new ids to ids.toml, write the compiled
                                    content and the editor schemas to artifacts/content/shapeland
              schema                write only the editor schemas
            """);
        return 2;
}

int Build()
{
    var (compiled, errors) = ShapeLandBuild.Build(content);
    foreach (var error in errors)
    {
        Console.Error.WriteLine(error);
    }

    if (compiled is null)
    {
        Console.Error.WriteLine($"Content build failed with {errors.Count} error(s).");
        return 1;
    }

    Directory.CreateDirectory(output);
    var file = Path.Combine(output, "content.bin");
    using (var stream = File.Create(file))
    {
        compiled.Save(stream);
    }

    WriteSchemas();
    Console.WriteLine($"Compiled {compiled.Shapes.All.Count} shapes, {compiled.Assets.All.Count} assets, {compiled.Zones.All.Count} zone(s) and {compiled.Strings.Entries.Count} interface strings to {Path.GetRelativePath(repo, file)}.");
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

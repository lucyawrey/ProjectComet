using System;
using System.IO;

namespace ShapeLand.Shared
{
    public static class RepoPaths
    {
        public static string Root(string hint = "")
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ProjectComet.slnx")))
                {
                    return dir.FullName;
                }
            }

            throw new InvalidOperationException($"Can't find the repository root (ProjectComet.slnx). {hint}".TrimEnd());
        }

        public static string ContentFile(string? configured, string hint)
        {
            var path = string.IsNullOrEmpty(configured) ? DefaultContentFile(hint) : configured!;
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "No compiled content. Run the content build (dotnet run --project shapeland/src/ShapeLand.ContentBuild -- build) or set ShapeLand:ContentFile.",
                    path
                );
            }

            return path;
        }

        public static string DefaultContentFile(string hint = "") => Path.Combine(Root(hint), "artifacts", "content", "shapeland", "content.bin");
    }
}

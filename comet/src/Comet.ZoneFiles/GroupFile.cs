using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Comet.Content;
using Tomlyn.Model;

namespace Comet.ZoneFiles
{
    /// <summary>
    /// A group's file (<c>groups/&lt;name&gt;.toml</c>): its <c>id</c>, then a <c>[[part]]</c> table for each part,
    /// written like a zone's placements and relative to the group's pivot. Read by the content build and the editor.
    /// </summary>
    public static class GroupFile
    {
        /// <summary>Reads the group in <paramref name="path"/>; problems go into <paramref name="errors"/> against <paramref name="file"/>.</summary>
        public static Group? Read(string path, string file, List<ZoneFileError> errors)
        {
            var toml = new ZoneToml(file, errors);
            var table = toml.Parse(File.ReadAllText(path));
            if (table is null)
            {
                return null;
            }

            toml.CheckKeys(table, "", "id", "part");
            return new Group
            {
                Id = toml.String(table, "", "id"),
                Parts = toml.Tables(table, "part").Select(p => ZoneFolder.ReadPlacement(toml, p.Table, p.Path)).ToArray(),
            };
        }

        /// <summary>Every group in <paramref name="folder"/> (its <c>*.toml</c> files, in subfolders too), each with its file relative to the folder.</summary>
        public static List<(Group Group, string File)> ReadFolder(string folder, List<ZoneFileError> errors)
        {
            var groups = new List<(Group, string)>();
            if (!Directory.Exists(folder))
            {
                return groups;
            }

            foreach (var path in Directory.EnumerateFiles(folder, "*.toml", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                var file = Path.GetRelativePath(folder, path).Replace('\\', '/');
                var before = errors.Count;
                var group = Read(path, file, errors);
                if (group != null && errors.Count == before)
                {
                    groups.Add((group, file));
                }
            }

            return groups;
        }

        /// <summary>Writes <paramref name="group"/> to <paramref name="path"/>, leaving the file untouched if it's the same.</summary>
        public static void Write(string path, Group group)
        {
            var table = new TomlTable { ["id"] = group.Id };
            ZoneFolder.AddTables(table, "part", group.Parts, ZoneFolder.PlacementTable);
            var bytes = Encoding.UTF8.GetBytes(ZoneFolder.HexColors(ZoneToml.Write(table)));
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllBytes(path, bytes);
        }

        /// <summary>The file a group's key is kept in, relative to the groups folder: <c>group.tree</c> in <c>tree.toml</c>.</summary>
        public static string FileFor(string key) => (key.StartsWith("group.", StringComparison.Ordinal) ? key.Substring("group.".Length) : key) + ".toml";
    }
}

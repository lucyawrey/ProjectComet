using System;
using Comet.Content;

namespace Comet.ZoneFiles
{
    /// <summary>Gives a zone a new name, for saving it as a copy.</summary>
    public static class ZoneRename
    {
        /// <summary>
        /// Renames <paramref name="zone"/> to <paramref name="newId"/> (<c>zone.name</c>), with its spawn point,
        /// teleporter and trigger ids and the teleporter targets inside it. Targets in other zones are left alone.
        /// </summary>
        public static void Rename(Zone zone, string newId)
        {
            var oldName = zone.Id.Substring("zone.".Length);
            var newName = newId.Substring("zone.".Length);
            string Moved(string id, string kind) =>
                id.StartsWith($"{kind}.{oldName}.", StringComparison.Ordinal)
                    ? $"{kind}.{newName}.{id.Substring(kind.Length + oldName.Length + 2)}"
                    : id;

            zone.Id = newId;
            foreach (var chunk in zone.Chunks)
            {
                foreach (var spawn in chunk.Spawns)
                {
                    spawn.Id = Moved(spawn.Id, "spawn");
                }

                foreach (var teleporter in chunk.Teleporters)
                {
                    teleporter.Id = Moved(teleporter.Id, "teleporter");
                    teleporter.Target = Moved(teleporter.Target, "teleporter");
                }

                foreach (var trigger in chunk.Triggers)
                {
                    trigger.Id = Moved(trigger.Id, "trigger");
                }
            }
        }
    }
}

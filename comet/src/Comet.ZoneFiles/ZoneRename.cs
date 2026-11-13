using System;
using Comet.Content;

namespace Comet.ZoneFiles
{
    public static class ZoneRename
    {
        /// <summary>Also renames the zone's spawn, teleporter and trigger ids and teleporter targets in it; targets in other zones stay.</summary>
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

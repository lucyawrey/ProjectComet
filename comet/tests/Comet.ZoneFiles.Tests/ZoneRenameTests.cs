using Comet.Content;

namespace Comet.ZoneFiles.Tests;

public class ZoneRenameTests
{
    [Fact]
    public void MovesTheZonesIdsAndTargetsInsideItButNotTargetsElsewhere()
    {
        var zone = Zones.Sample();
        var chunk = zone.Chunks[1];
        chunk.Teleporters =
        [
            chunk.Teleporters[0],
            new Teleporter
            {
                Id = "teleporter.meadow.back",
                Target = "teleporter.meadow.to_cave",
                Volume = chunk.Teleporters[0].Volume,
            },
        ];

        ZoneRename.Rename(zone, "zone.field");

        Assert.Equal("zone.field", zone.Id);
        Assert.Equal("spawn.field.gate", chunk.Spawns[0].Id);
        Assert.Equal("trigger.field.plate", chunk.Triggers[0].Id);
        Assert.Equal(
            [("teleporter.field.to_cave", "teleporter.cave.to_meadow"), ("teleporter.field.back", "teleporter.field.to_cave")],
            chunk.Teleporters.Select(t => (t.Id, t.Target))
        );
        Assert.Empty(ZoneValidation.Check(zone));
    }
}

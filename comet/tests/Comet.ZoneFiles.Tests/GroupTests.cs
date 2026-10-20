using System.Numerics;
using Comet.Content;
using Group = Comet.Content.Group;

namespace Comet.ZoneFiles.Tests;

public class GroupTests
{
    private static Placement Box(Vector3 position, Vector3 size, Vector3 rotation = default, uint? color = null) =>
        new() { Primitive = new Primitive { Kind = PrimitiveKind.Box }, Position = position, Rotation = rotation, Scale = size, Color = color };

    private static Group Tree() => new()
    {
        Id = "group.tree",
        Parts =
        [
            Box(Vector3.Zero, new Vector3(0.4f, 2, 0.4f), color: 0x7A5C3E),
            Box(new Vector3(1, 2, 0), new Vector3(2, 1, 2), new Vector3(0, 0, 30), 0x43A047),
        ],
    };

    private static float Angle(Quaternion a, Quaternion b) => 2 * MathF.Acos(Math.Min(1, MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))))) * 180 / MathF.PI;

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(10, 20, 30)]
    [InlineData(-45, 170, -100)]
    [InlineData(89.5f, 40, 10)]
    [InlineData(90, 40, 10)]
    [InlineData(-90, -120, 35)]
    public void EulerAnglesRoundTrip(float x, float y, float z)
    {
        var rotation = ZoneGrid.Rotation(new Vector3(x, y, z));
        Assert.True(Angle(rotation, ZoneGrid.Rotation(ZoneGrid.Euler(rotation))) < 0.05f);
    }

    [Fact]
    public void CopiesPlaceTheirPartsByTheirTransform()
    {
        var tree = Tree();
        var copy = new Placement { Group = "group.tree", Position = new Vector3(10, 1, 5), Rotation = new Vector3(0, 90, 0), Scale = new Vector3(2, 2, 2) };

        var parts = Groups.Expand(copy, key => key == tree.Id ? tree : null);

        Assert.Equal(2, parts.Count);
        foreach (var (part, original) in parts.Zip(tree.Parts))
        {
            // Each part lands where the copy's matrix puts it, turned by both rotations.
            var expected = Vector3.Transform(original.Position, Matrix4x4.CreateScale(2) * Matrix4x4.CreateFromQuaternion(ZoneGrid.Rotation(copy.Rotation)) * Matrix4x4.CreateTranslation(copy.Position));
            Assert.True(Vector3.Distance(expected, part.Position) < 1e-4f, $"{part.Position} vs {expected}");
            Assert.True(Angle(ZoneGrid.Rotation(part.Rotation), ZoneGrid.Rotation(original.Rotation) * Quaternion.Identity * ZoneGrid.Rotation(copy.Rotation)) < 0.05f
                || Angle(ZoneGrid.Rotation(part.Rotation), Quaternion.Concatenate(ZoneGrid.Rotation(original.Rotation), ZoneGrid.Rotation(copy.Rotation))) < 0.05f);
            Assert.Equal(original.Scale * 2, part.Scale);
            Assert.Equal(original.Color, part.Color);
            Assert.Null(part.Group);
        }

        // The second part (1 m out along X, 2 m up) ends up along -Z after a quarter turn, at twice the distance.
        Assert.True(Vector3.Distance(new Vector3(10, 5, 3), parts[1].Position) < 1e-4f, $"{parts[1].Position}");
    }

    [Fact]
    public void NestedGroupsExpandAndBlockoutCarriesDown()
    {
        var tree = Tree();
        var grove = new Group
        {
            Id = "group.grove",
            Parts = [new Placement { Group = "group.tree", Position = new Vector3(-3, 0, 0) }, new Placement { Group = "group.tree", Position = new Vector3(3, 0, 0), Rotation = new Vector3(0, 45, 0) }],
        };
        Group? Find(string key) => key == tree.Id ? tree : key == grove.Id ? grove : null;

        var parts = Groups.Expand(new Placement { Group = "group.grove", Position = new Vector3(0, 0, 20), Blockout = true }, Find);

        Assert.Equal(4, parts.Count);
        Assert.All(parts, p => Assert.True(p.Blockout));
        Assert.Equal(new Vector3(-3, 0, 20), parts[0].Position);
    }

    [Fact]
    public void ExpandingAZoneMovesPartsIntoTheirChunks()
    {
        var tree = Tree();
        var zone = new Zone
        {
            Id = "zone.test",
            Chunks = [new ZoneChunk { Coord = new ChunkCoord(0, 0), Placements = [new Placement { Group = "group.tree", Position = new Vector3(63.5f, 0, 10) }, Box(new Vector3(5, 0, 5), Vector3.One)] }],
        };

        Groups.Expand(zone, key => key == tree.Id ? tree : null);

        Assert.Equal([new ChunkCoord(0, 0), new ChunkCoord(1, 0)], zone.Chunks.Select(c => c.Coord));
        Assert.Equal(2, zone.Chunks[0].Placements.Length); // the box and the trunk
        Assert.Single(zone.Chunks[1].Placements); // the crown, 1 m over the border
        Assert.DoesNotContain(zone.Chunks.SelectMany(c => c.Placements), p => p.Group != null);
    }

    [Fact]
    public void GroupFilesRoundTrip()
    {
        using var folder = new TempFolder();
        var tree = Tree();
        tree.Parts[0].Role = "trunk";
        tree.Parts = [.. tree.Parts, new Placement { Group = "group.rock", Position = new Vector3(0, 0, 2), Scale = new Vector3(0.5f, 0.5f, 0.5f) }];
        GroupFile.Write(folder.PathOf("tree.toml"), tree);

        var errors = new List<ZoneFileError>();
        var read = GroupFile.Read(folder.PathOf("tree.toml"), "tree.toml", errors);

        Assert.Empty(errors);
        Assert.Equal("group.tree", read!.Id);
        Assert.Equal(3, read.Parts.Length);
        Assert.Equal("trunk", read.Parts[0].Role);
        Assert.Equal(0x43A047u, read.Parts[1].Color);
        Assert.Equal("group.rock", read.Parts[2].Group);
        var text = File.ReadAllText(folder.PathOf("tree.toml"));
        Assert.Contains("[[part]]", text);
        Assert.Contains("color = 0x43A047", text);
        Assert.Contains("group = \"group.rock\"", text);
        Assert.Equal("tree.toml", GroupFile.FileFor("group.tree"));
    }

    [Fact]
    public void GroupChecksCatchBadPartsAndCycles()
    {
        var a = new Group { Id = "group.a", Parts = [new Placement { Group = "group.b" }, Box(Vector3.Zero, Vector3.One)] };
        a.Parts[1].Name = "door";
        var b = new Group { Id = "group.b", Parts = [new Placement { Group = "group.a", Scale = new Vector3(1, 2, 1) }, new Placement { Asset = "asset.missing" }] };
        var empty = new Group { Id = "group.empty" };
        var bad = new Group { Id = "group.bad", Parts = [new Placement { Group = "group.nowhere" }, Box(Vector3.Zero, Vector3.One)] };
        bad.Parts[1].Role = "Not A Role";

        var errors = ZoneValidation.CheckGroups([(a, "a.toml"), (b, "b.toml"), (empty, "empty.toml"), (bad, "bad.toml")], _ => null).Select(e => e.ToString()).ToList();

        Assert.Contains(errors, e => e.Contains("a.toml") && e.Contains("a group's parts have no name"));
        Assert.Contains(errors, e => e.Contains("b.toml") && e.Contains("a group scales evenly"));
        Assert.Contains(errors, e => e.Contains("b.toml") && e.Contains("there's no asset 'asset.missing'"));
        Assert.Contains(errors, e => e.Contains("empty.toml") && e.Contains("at least one part"));
        Assert.Contains(errors, e => e.Contains("bad.toml") && e.Contains("there's no group 'group.nowhere'"));
        Assert.Contains(errors, e => e.Contains("bad.toml") && e.Contains("isn't a valid role"));
        Assert.Single(errors, e => e.Contains("contains itself"));
    }

    [Fact]
    public void ZoneCopiesAreChecked()
    {
        var tree = Tree();
        var zone = Zones.Sample();
        var at = zone.Chunks[0].Placements[0].Position;
        zone.Chunks[0].Placements =
        [
            new Placement { Group = "group.tree", Position = at, Scale = new Vector3(1, 2, 1), Color = 0xFF0000, Name = "tree" },
            new Placement { Group = "group.nowhere", Position = at },
            Box(at, Vector3.One),
        ];
        zone.Chunks[0].Placements[2].Role = "body";

        var errors = ZoneValidation.Check(zone).Concat(ZoneValidation.CheckPlacements(zone, _ => null, key => key == tree.Id ? tree : null)).Select(e => e.ToString()).ToList();

        Assert.Contains(errors, e => e.Contains("group.tree") && e.Contains("scales evenly"));
        Assert.Contains(errors, e => e.Contains("group.tree") && e.Contains("no color of their own"));
        Assert.Contains(errors, e => e.Contains("group.tree") && e.Contains("copies have no name"));
        Assert.Contains(errors, e => e.Contains("there's no group 'group.nowhere'"));
        Assert.Contains(errors, e => e.Contains("only a group's parts have a role"));
    }
}

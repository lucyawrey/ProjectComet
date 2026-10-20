using System.Numerics;
using Comet.Content;

namespace Comet.Simulation.Tests;

public class TriggersTests
{
    private static TriggerVolume Volume(PrimitiveKind kind, float x, float z, Vector3 size, float yaw = 0) =>
        new() { Kind = kind, Position = new Vector3(x, 1, z), Size = size, Yaw = yaw };

    [Fact]
    public void BoxesAndCylindersReachFromBottomToTop()
    {
        var box = Volume(PrimitiveKind.Box, 8, 8, new Vector3(4, 2, 2));
        var cylinder = Volume(PrimitiveKind.Cylinder, 8, 8, new Vector3(4, 2, 2));

        Assert.True(Triggers.Inside(box, new Vector3(9.9f, 1, 8.9f)));
        Assert.False(Triggers.Inside(box, new Vector3(10.1f, 1, 8)));
        Assert.False(Triggers.Inside(box, new Vector3(8, 0.9f, 8))); // below the bottom
        Assert.False(Triggers.Inside(box, new Vector3(8, 3.1f, 8))); // above the top
        Assert.True(Triggers.Inside(cylinder, new Vector3(9.9f, 3, 8)));
        Assert.False(Triggers.Inside(cylinder, new Vector3(9.9f, 1, 8.9f))); // a box's corner, outside the ellipse
    }

    [Fact]
    public void SpheresFillTheirSize()
    {
        var sphere = Volume(PrimitiveKind.Sphere, 8, 8, new Vector3(2, 2, 2));

        Assert.True(Triggers.Inside(sphere, new Vector3(8, 2, 8)));
        Assert.True(Triggers.Inside(sphere, new Vector3(8.9f, 2, 8)));
        Assert.False(Triggers.Inside(sphere, new Vector3(8.9f, 1.1f, 8))); // the bottom's edge is round
    }

    [Fact]
    public void VolumesTurnWithTheirYaw()
    {
        var box = Volume(PrimitiveKind.Box, 8, 8, new Vector3(4, 2, 1), yaw: 90);

        Assert.True(Triggers.Inside(box, new Vector3(8, 1, 9.9f)));
        Assert.False(Triggers.Inside(box, new Vector3(9.9f, 1, 8)));
    }

    [Fact]
    public void ArrivalsScatterOverGroundInTheFootprint()
    {
        var world = TestWorlds.Create();
        var volume = Volume(PrimitiveKind.Box, 8, 4, new Vector3(4, 2, 2), yaw: 30);
        var random = new Random(1);
        for (var i = 0; i < 50; i++)
        {
            var (feet, _) = Triggers.Arrive(volume, SpawnFacing.Direction, world, TestWorlds.Rules, random);

            Assert.True(Triggers.Inside(volume, feet));
            Assert.Equal(1f, feet.Y, 3);
            Assert.False(world.IsBlocked(feet, TestWorlds.Rules.BodyRadius, TestWorlds.Rules.BodyHeight));
        }
    }

    [Fact]
    public void ArrivalsLandOnAPadUnderTheVolume()
    {
        var world = TestWorlds.Create(TestWorlds.Block(8, 8, 1, new Vector3(3, 0.4f, 3)));
        var volume = Volume(PrimitiveKind.Cylinder, 8, 8, new Vector3(2, 2, 2));
        volume.Position = new Vector3(8, 1.4f, 8);

        var (feet, _) = Triggers.Arrive(volume, SpawnFacing.Direction, world, TestWorlds.Rules, new Random(4));

        Assert.Equal(1.4f, feet.Y, 3);
    }

    [Fact]
    public void SpotsWithoutRoomFallBackToTheCenter()
    {
        // A volume wholly inside a tall block.
        var world = TestWorlds.Create(TestWorlds.Block(10, 10, 1, new Vector3(6, 3, 6)));
        var volume = Volume(PrimitiveKind.Cylinder, 10, 10, new Vector3(2, 2, 2));

        var (feet, _) = Triggers.Arrive(volume, SpawnFacing.Direction, world, TestWorlds.Rules, new Random(2));

        Assert.Equal(new Vector3(10, 1, 10), feet);
    }

    [Fact]
    public void FacingFollowsTheMode()
    {
        var world = TestWorlds.Create();
        var random = new Random(3);

        Assert.Equal(MathF.PI / 2, Triggers.Arrive(Volume(PrimitiveKind.Box, 8, 4, new Vector3(0.1f, 2, 0.1f), yaw: 90), SpawnFacing.Direction, world, TestWorlds.Rules, random).Facing, 4);
        var (feet, facing) = Triggers.Arrive(Volume(PrimitiveKind.Cylinder, 8, 4, new Vector3(4, 2, 4)), SpawnFacing.TowardPoint, world, TestWorlds.Rules, random);
        var toward = new Vector2(8 - feet.X, 4 - feet.Z);
        Assert.Equal(MathF.Atan2(toward.X, toward.Y), facing, 4);
    }

    [Fact]
    public void FindsSpawnsAndTeleportersByKeyAndWhereFeetAre()
    {
        var zone = new Zone
        {
            Chunks =
            [
                new ZoneChunk
                {
                    Spawns = [new SpawnPoint { Id = "spawn.test.a", Number = 7, Volume = Volume(PrimitiveKind.Cylinder, 4, 4, new Vector3(4, 2, 4)) }],
                    Teleporters = [new Teleporter { Id = "teleporter.test.b", Volume = Volume(PrimitiveKind.Box, 10, 4, new Vector3(2, 2, 2)) }],
                },
            ],
        };
        var triggers = new Triggers(zone);

        Assert.True(triggers.TryGetSpawn(7, out var spawn));
        Assert.Same(spawn, triggers.SpawnAt(new Vector3(5, 1, 4)));
        Assert.True(triggers.TryGetTeleporter("teleporter.test.b", out var teleporter));
        Assert.Same(teleporter, triggers.TeleporterAt(new Vector3(10, 2, 4)));
        Assert.Null(triggers.SpawnAt(new Vector3(10, 2, 4)));
        Assert.Null(triggers.TeleporterAt(new Vector3(7, 1, 4)));
    }
}

using System.Numerics;
using Comet.Content;

namespace Comet.Simulation.Tests;

public class SpawnsTests
{
    private static SpawnPoint Spawn(float x, float z, float radius, SpawnFacing facing = SpawnFacing.Direction, float yaw = 0, bool isDefault = false) =>
        new() { Id = $"spawn.at_{x}_{z}", Number = (int)(x * 100 + z), Position = new Vector3(x, 1, z), Radius = radius, Facing = facing, Yaw = yaw, IsDefault = isDefault };

    [Fact]
    public void ArrivalsScatterOverGroundWithinTheRadius()
    {
        var world = TestWorlds.Create();
        var spawn = Spawn(8, 4, 2);
        var random = new Random(1);
        for (var i = 0; i < 50; i++)
        {
            var (feet, _) = Spawns.Arrive(spawn, world, TestWorlds.Rules, random);

            Assert.InRange(Vector2.Distance(new Vector2(feet.X, feet.Z), new Vector2(8, 4)), 0, 2.001f);
            Assert.Equal(1f, feet.Y, 3);
            Assert.False(world.IsBlocked(feet, TestWorlds.Rules.BodyRadius, TestWorlds.Rules.BodyHeight));
        }
    }

    [Fact]
    public void SpotsWithoutRoomFallBackToThePoint()
    {
        // A spawn whose whole radius is inside a tall block.
        var world = TestWorlds.Create(TestWorlds.Block(10, 10, 1, new Vector3(6, 3, 6)));
        var spawn = new SpawnPoint { Id = "spawn.boxed", Position = new Vector3(10, 1, 10), Radius = 1 };

        var (feet, _) = Spawns.Arrive(spawn, world, TestWorlds.Rules, new Random(2));

        Assert.Equal(new Vector3(10, 1, 10), feet);
    }

    [Fact]
    public void FacingFollowsTheSpawnsMode()
    {
        var world = TestWorlds.Create();
        var random = new Random(3);

        Assert.Equal(MathF.PI / 2, Spawns.Arrive(Spawn(8, 4, 0, SpawnFacing.Direction, yaw: 90), world, TestWorlds.Rules, random).Facing, 4);
        var (feet, facing) = Spawns.Arrive(Spawn(8, 4, 2, SpawnFacing.TowardPoint), world, TestWorlds.Rules, random);
        var toward = new Vector2(8 - feet.X, 4 - feet.Z);
        Assert.Equal(MathF.Atan2(toward.X, toward.Y), facing, 4);
    }

    [Fact]
    public void FindsSpawnsByKeyAndWhereABodyStands()
    {
        var zone = new Zone { Chunks = [new ZoneChunk { Spawns = [Spawn(4, 4, 2, isDefault: true), Spawn(10, 4, 3)] }] };
        var spawns = new Spawns(zone);

        Assert.Equal("spawn.at_4_4", spawns.Default!.Id);
        Assert.True(spawns.TryGet("spawn.at_10_4", out var far));
        Assert.Same(far, spawns.StandingIn(new Vector3(12, 1, 4), reach: 1));
        Assert.Null(spawns.StandingIn(new Vector3(12, 5, 4), reach: 1)); // too far above
        Assert.Null(spawns.StandingIn(new Vector3(6.5f, 1, 4), reach: 1)); // between them
    }
}

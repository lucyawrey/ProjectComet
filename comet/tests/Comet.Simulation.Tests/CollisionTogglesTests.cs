using System.Numerics;

namespace Comet.Simulation.Tests;

public class CollisionTogglesTests
{
    private static CollisionMesh Door(bool startsOff = false)
    {
        var door = TestWorlds.Block(8, 8, 1, new Vector3(2, 2, 2));
        door.Tag = "door";
        door.StartsOff = startsOff;
        return door;
    }

    private static readonly MovementRules Rules = TestWorlds.Rules;

    [Fact]
    public void MeshesThatAreOffAreNotSolid()
    {
        var world = TestWorlds.Create(Door(startsOff: true));
        var inDoor = new Vector3(8, 1, 8);

        Assert.False(world.IsBlocked(inDoor, Rules.BodyRadius, Rules.BodyHeight));
        world.SetFlipped("door", true);
        Assert.True(world.IsBlocked(inDoor, Rules.BodyRadius, Rules.BodyHeight));
        Assert.True(world.TryGetGround(new Vector3(8, 5, 8), Rules.BodyRadius, 5, out var top));
        Assert.Equal(3f, top, 3);
        world.SetFlipped("door", false);
        Assert.False(world.IsBlocked(inDoor, Rules.BodyRadius, Rules.BodyHeight));
        Assert.True(world.TryGetGround(new Vector3(8, 5, 8), Rules.BodyRadius, 5, out var ground));
        Assert.Equal(1f, ground, 3);
    }

    [Fact]
    public void FlippingSwapsMeshesThatStartOnForOnesThatStartOff()
    {
        var open = TestWorlds.Block(12, 8, 1, new Vector3(1, 1, 1));
        open.Tag = "door";
        open.StartsOff = true;
        var world = TestWorlds.Create(Door(), open);
        var (closed, opened) = (world.MeshesTagged("door")[0], world.MeshesTagged("door")[1]);

        Assert.Equal((true, false), (world.IsOn(closed), world.IsOn(opened)));
        world.SetFlipped("door", true);
        Assert.Equal((false, true), (world.IsOn(closed), world.IsOn(opened)));
    }

    [Fact]
    public void APlayerPassesThroughTheirOwnSet()
    {
        var world = TestWorlds.Create(Door());
        var door = Assert.Single(world.MeshesTagged("door"));
        var inDoor = new Vector3(8, 1, 8);
        var passThrough = new HashSet<int> { door };

        Assert.True(world.Overlaps(door, inDoor, Rules.BodyRadius, Rules.BodyHeight));
        Assert.False(world.IsBlocked(inDoor, Rules.BodyRadius, Rules.BodyHeight, passThrough));
        Assert.True(world.IsBlocked(inDoor, Rules.BodyRadius, Rules.BodyHeight));
        Assert.False(world.Overlaps(door, new Vector3(12, 1, 8), Rules.BodyRadius, Rules.BodyHeight));
    }

    [Fact]
    public void TheMotorWalksOutOfAMeshItPassesThrough()
    {
        var world = TestWorlds.Create(Door());
        var state = new MotorState { Position = new Vector3(8, 1, 8), Grounded = true };
        var passThrough = new HashSet<int>(world.MeshesTagged("door"));
        for (var i = 0; i < 24; i++)
        {
            PlayerMotor.Step(ref state, new Vector2(1, 0), jump: false, 1f / 30, 5, 5, world, Rules, passThrough);
        }

        Assert.True(state.Position.X > 10);
        Assert.Equal(1f, state.Position.Y, 3);
    }
}

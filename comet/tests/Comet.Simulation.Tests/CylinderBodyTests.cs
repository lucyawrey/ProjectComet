using System.Numerics;
using Comet.Content;

namespace Comet.Simulation.Tests;

/// <summary>The cylinder body against ledges, pads, ramps and steep sides.</summary>
public class CylinderBodyTests
{
    private static float Walk(CollisionWorld world, Vector3 from, Vector2 move, float seconds)
    {
        var state = new MotorState { Position = from, Grounded = true };
        for (var i = 0; i < seconds * 60; i++)
        {
            PlayerMotor.Step(ref state, move, false, 1 / 60f, 5, 8, world, TestWorlds.Rules);
        }
        return state.Position.X;
    }

    [Fact]
    public void StepsDownOffALedge() => Assert.True(Walk(TestWorlds.Create(), new Vector3(4, 1.3f, 10), new Vector2(1, 0), 1) > 6.5f);

    [Fact]
    public void StepsUpOntoALedge() => Assert.True(Walk(TestWorlds.Create(), new Vector3(1.5f, 1, 10), new Vector2(1, 0), 1) > 4.5f);

    [Fact]
    public void StepsDownOffAPad()
    {
        var pad = new Placement { Position = new Vector3(4, 1, 7), Scale = new Vector3(2, 0.15f, 2), Primitive = new Primitive { Kind = PrimitiveKind.Cylinder } };
        var mesh = CollisionMesh.Placed(PrimitiveMesher.For(pad, new PolygonBudget()), pad.Matrix());
        Assert.True(Walk(TestWorlds.Create(mesh), new Vector3(4, 1.15f, 7), new Vector2(1, 0), 1) > 6.5f);
    }

    [Fact]
    public void WalksUpAWedge()
    {
        var ramp = new Placement { Position = new Vector3(8, 1, 7), Scale = new Vector3(4, 1, 2), Primitive = new Primitive { Kind = PrimitiveKind.Wedge } };
        var mesh = CollisionMesh.Placed(PrimitiveMesher.For(ramp, new PolygonBudget()), ramp.Matrix());
        Assert.True(Walk(TestWorlds.Create(mesh), new Vector3(5, 1, 7), new Vector2(1, 0), 1.5f) > 9.5f);
    }

    [Fact]
    public void LandingAgainstAConesSideDoesNotStick()
    {
        var cone = new Placement { Position = new Vector3(8, 1, 7), Scale = new Vector3(3, 3, 3), Primitive = new Primitive { Kind = PrimitiveKind.Cone } };
        var mesh = CollisionMesh.Placed(PrimitiveMesher.For(cone, new PolygonBudget()), cone.Matrix());
        // Dropped from above at the cone's foot, overlapping its side, then walking away from it.
        var world = TestWorlds.Create(mesh);
        var state = new MotorState { Position = new Vector3(6.6f, 2, 7), Grounded = false };
        for (var i = 0; i < 60; i++)
        {
            PlayerMotor.Step(ref state, new Vector2(-1, 0), false, 1 / 60f, 5, 8, world, TestWorlds.Rules);
        }
        Assert.True(state.Position.X < 5.5f);
        Assert.False(world.IsBlocked(state.Position, 0.4f, 1));
    }

    [Fact]
    public void JumpingUpASlopeKeepsItsSpeed()
    {
        // Running up the rise, standing on its slope.
        var world = TestWorlds.Create();
        Assert.True(world.TryGetGround(new Vector3(14.5f, 2, 7), 0.4f, 2, out var ground));
        var state = new MotorState { Position = new Vector3(14.5f, ground, 7), Velocity = new Vector3(5, 0, 0), Grounded = true };
        PlayerMotor.Step(ref state, new Vector2(1, 0), true, 1 / 60f, 5, 8, world, TestWorlds.Rules);
        Assert.Equal(5, state.Velocity.X, 3);
        Assert.True(state.Position.X > 14.55f);
    }
}

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
        for (var i = 0; i < 6; i++)
        {
            PlayerMotor.Step(ref state, new Vector2(1, 0), i == 0, 1 / 60f, 5, 8, world, TestWorlds.Rules);
        }

        // Against the slope for at most the first step, keeping its speed.
        Assert.Equal(5, state.Velocity.X, 3);
        Assert.True(state.Position.X > 14.5f + 5 * 5 / 60f - 0.01f);
    }

    private static CollisionMesh Mesh(PrimitiveKind kind, Vector3 at, Vector3 scale, float yaw = 0)
    {
        var placement = new Placement { Position = at, Scale = scale, Rotation = new Vector3(0, yaw, 0), Primitive = new Primitive { Kind = kind } };
        return CollisionMesh.Placed(PrimitiveMesher.For(placement, new PolygonBudget()), placement.Matrix());
    }

    [Fact]
    public void AFallIntoASteepVStopsWhereItFits()
    {
        // Two steep wedges back to back, their slopes forming a V narrower than the body.
        var world = TestWorlds.Create(Mesh(PrimitiveKind.Wedge, new Vector3(7.5f, 1, 7), new Vector3(1, 4, 2), 180), Mesh(PrimitiveKind.Wedge, new Vector3(8.5f, 1, 7), new Vector3(1, 4, 2)));
        var state = new MotorState { Position = new Vector3(8, 6, 7) };
        for (var i = 0; i < 120; i++)
        {
            PlayerMotor.Step(ref state, Vector2.Zero, false, 1 / 60f, 5, 8, world, TestWorlds.Rules);
        }

        Assert.True(state.Position.Y > 2);
        Assert.True(state.Grounded);
        Assert.False(world.IsBlocked(state.Position, 0.4f, 1));

        // Standing there, it can jump out.
        var wedged = state.Position.Y;
        PlayerMotor.Step(ref state, Vector2.Zero, true, 1 / 60f, 5, 8, world, TestWorlds.Rules);
        Assert.True(state.Position.Y > wedged);
    }

    [Theory]
    [InlineData(5, 0.1f, 0.35f, true)]
    [InlineData(5, 0.25f, 0.35f, false)] // more than terrain climbs over one move at 5 m/s
    [InlineData(8, 0.25f, 0.35f, true)]
    [InlineData(8, 0.25f, 0.2f, false)] // more than the mantle height
    [InlineData(5, 0.1f, 0f, false)]
    public void InTheAirMantlesOntoALowLedge(float speed, float below, float mantleHeight, bool mantles)
    {
        // A crate with its top at 1.4 m, the floor at 1 m; a fall beside it, moving into its side.
        var world = TestWorlds.Create(Mesh(PrimitiveKind.Box, new Vector3(6, 1, 7), new Vector3(2, 0.4f, 2)));
        var rules = new MovementRules { MantleHeight = mantleHeight };
        var state = new MotorState { Position = new Vector3(4.55f, 1.4f - below + 0.02f, 7), Velocity = new Vector3(speed, 0, 0) };
        for (var i = 0; i < 20; i++)
        {
            PlayerMotor.Step(ref state, new Vector2(1, 0), false, 1 / 60f, speed, 8, world, rules);
        }

        Assert.Equal(mantles, state.Position.Y > 1.39f);
    }

    [Fact]
    public void RisingIntoALedgeCarriesOnOverIt()
    {
        // A jump that meets the crate's side just below its top: lifted onto it on the way up, it keeps rising and
        // comes down on the crate.
        var world = TestWorlds.Create(Mesh(PrimitiveKind.Box, new Vector3(6, 1, 7), new Vector3(2, 0.4f, 2)));
        var state = new MotorState { Position = new Vector3(4.55f, 1.3f, 7), Velocity = new Vector3(5, 3, 0) };
        var highest = 0f;
        for (var i = 0; i < 60 && !state.Grounded; i++)
        {
            PlayerMotor.Step(ref state, new Vector2(1, 0), false, 1 / 60f, 5, 8, world, TestWorlds.Rules);
            highest = MathF.Max(highest, state.Position.Y);
        }

        Assert.True(highest > 1.45f);
        Assert.True(state.Grounded);
        Assert.Equal(1.4f, state.Position.Y, 3);
    }
}

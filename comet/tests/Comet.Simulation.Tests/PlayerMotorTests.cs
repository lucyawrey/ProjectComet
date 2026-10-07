using System.Numerics;

namespace Comet.Simulation.Tests;

public class PlayerMotorTests
{
    private const float Step = 1f / 60;
    private readonly CollisionWorld _world = TestWorlds.Create();

    private MotorState Run(MotorState state, Vector2 move, float seconds, bool jumpFirst = false, float maxSpeed = 6, float jumpVelocity = 7)
    {
        for (var t = 0f; t < seconds; t += Step)
        {
            PlayerMotor.Step(ref state, move, jumpFirst && t == 0, Step, maxSpeed, jumpVelocity, _world, TestWorlds.Rules);
        }

        return state;
    }

    private static MotorState Standing(float x, float z, float y = 1) => new() { Position = new Vector3(x, y, z), Grounded = true };

    [Fact]
    public void SlidesToMaxSpeedAndStaysOnTheGround()
    {
        var state = Run(Standing(1, 1), new Vector2(1, 0), 1);

        Assert.True(state.Grounded);
        Assert.Equal(6f, state.Velocity.X, 3);
        Assert.Equal(1f, state.Position.Y, 3);
        Assert.InRange(state.Position.X, 5.5f, 7f); // ~0.15 s to reach full speed
    }

    [Fact]
    public void FollowsSlopesUphill()
    {
        var state = Run(Standing(13, 1), new Vector2(1, 0), 0.4f);

        Assert.True(state.Grounded);
        Assert.True(_world.Terrain!.TryGetHeight(state.Position.X, state.Position.Z, out var ground));
        Assert.Equal(ground, state.Position.Y, 3);
    }

    [Fact]
    public void ClimbsSteepSlopesFastWithoutSinking()
    {
        // 30 m/s up the 1 m rise: more than a step's height per frame.
        var state = Run(Standing(12, 1), new Vector2(1, 0), 0.15f, maxSpeed: 30);

        Assert.True(_world.Terrain!.TryGetHeight(state.Position.X, state.Position.Z, out var ground));
        Assert.True(state.Position.Y >= ground - 0.001f);
    }

    [Fact]
    public void JumpsToTheApexAndLands()
    {
        var state = Standing(1, 14);
        var highest = 0f;
        for (var i = 0; i < 120; i++)
        {
            PlayerMotor.Step(ref state, Vector2.Zero, i == 0, Step, 6, 7, _world, TestWorlds.Rules);
            highest = MathF.Max(highest, state.Position.Y);
        }

        Assert.Equal(1 + TestWorlds.Rules.JumpApex(7), highest, 1);
        Assert.True(state.Grounded);
        Assert.Equal(1f, state.Position.Y, 3);
    }

    [Fact]
    public void WalksUpStepsButNotBlocks()
    {
        var step = Run(Standing(1, 10), new Vector2(1, 0), 0.5f); // on the step, not yet past it
        Assert.InRange(step.Position.X, 3f, 5f);
        Assert.Equal(1.3f, step.Position.Y, 3);

        var block = Run(Standing(1, 4), new Vector2(1, 0), 1);
        Assert.Equal(3 - 0.4f, block.Position.X, 2); // stopped against the cube's face
        Assert.Equal(1f, block.Position.Y, 3);
    }

    [Fact]
    public void SlidesAlongAWallInsteadOfStopping()
    {
        // West of the cube, pushing diagonally into its west face.
        var state = Run(Standing(2.5f, 3.5f), new Vector2(1, 1), 0.5f);

        Assert.True(state.Position.Z > 5, "should keep moving along z");
        Assert.InRange(state.Position.X, 2.5f, 2.61f);
    }

    [Fact]
    public void JumpsOntoABlock()
    {
        // Apex 2.4 m above the ground, clearing the 2 m cube before reaching its face.
        var state = Run(Standing(1, 4), new Vector2(1, 0), 0.75f, jumpFirst: true, jumpVelocity: 11); // landed, not yet past the far edge

        Assert.Equal(3f, state.Position.Y, 3);
        Assert.True(state.Grounded);
    }

    [Fact]
    public void FallsThroughHoles()
    {
        var state = Run(Standing(10, 12), new Vector2(1, 0), 2);

        Assert.False(state.Grounded);
        Assert.True(state.Position.Y < -10);
    }
}

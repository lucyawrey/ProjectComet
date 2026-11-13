using System.Numerics;
using Comet.Simulation;
using ShapeLand.Bots;
using ShapeLand.ContentBuild;
using ShapeLand.Shared.Content;

namespace ShapeLand.GameServer.Tests;

public sealed class WanderTests
{
    private const float Step = 1f / 60;

    private static readonly ShapeLandContent Content = Build();

    private static ShapeLandContent Build()
    {
        var (content, errors) = ShapeLandBuild.Build(Path.Combine(TestPaths.RepoRoot(), "shapeland", "content"), saveRegistry: false);
        Assert.Empty(errors);
        return content!;
    }

    [Theory]
    [InlineData("zone.tests", 100, 1)]
    [InlineData("zone.tests", 100, 2)]
    [InlineData("zone.tests", 100, 3)]
    [InlineData("zone.main", 50, 1)]
    public async Task RoamsTheZoneOnTheGroundWithoutStallingAndFindsTeleporters(string id, int minCells, int seed)
    {
        var zone = Content.Zones.All.Single(z => z.Id == id);
        var worlds = new ZoneWorlds(Content);
        var (world, grid, triggers) = (worlds.For(zone), await worlds.GridFor(zone), worlds.TriggersFor(zone));
        var wanderer = new Wanderer(grid, triggers, new Random(seed), calm: true);
        var shape = Content.Shapes.All[0];
        var state = new MotorState { Position = Triggers.Center(triggers.Spawns[0], world), Grounded = true };

        var visited = new HashSet<(int, int)>();
        var (airborne, crossings, longestStill) = (0, 0, 0.0);
        var (anchor, anchorSince, onPadSince) = (state.Position, 0.0, -1.0);
        const int steps = 60 * 600;
        for (var i = 0; i < steps; i++)
        {
            var now = i * Step;
            var move = wanderer.Steer(now, state.Position, shape.MaxSpeed * Step);
            PlayerMotor.Step(ref state, move, false, Step, shape.MaxSpeed, shape.JumpVelocity, world, Content.Movement);
            airborne += state.Grounded ? 0 : 1;
            Assert.True(state.Position.Y > world.KillHeight, $"fell at {state.Position} after {now:0.0} s");
            visited.Add(((int)MathF.Round(state.Position.X), (int)MathF.Round(state.Position.Z)));
            if (Vector3.Distance(state.Position, anchor) > 0.5f)
            {
                (anchor, anchorSince) = (state.Position, now);
            }

            longestStill = Math.Max(longestStill, now - anchorSince);
            if (triggers.TeleporterAt(state.Position) is { } pad)
            {
                onPadSince = onPadSince < 0 ? now : onPadSince;
                if (now - onPadSince >= pad.Volume.StandSeconds)
                {
                    crossings++;
                    state.Position = Triggers.Center(triggers.Spawns[0], world);
                    onPadSince = -1;
                }
            }
            else
            {
                onPadSince = -1;
            }
        }

        Assert.True(visited.Count >= minCells, $"visited {visited.Count} cells");
        Assert.True(airborne < steps / 100, $"{airborne} airborne steps");
        Assert.True(longestStill < 15, $"stood still for {longestStill:0} s");
        Assert.True(crossings > 0, "never stood on a teleporter long enough to use it");
    }

    [Fact]
    public async Task FindsGroundInAZoneWithOnlyATeleporter()
    {
        var zone = Content.Zones.All.Single(z => z.Id == "zone.tests_far");

        Assert.Empty(new Triggers(zone).Spawns);
        Assert.NotEqual(0, (await new ZoneWorlds(Content).GridFor(zone)).Count);
    }
}

using System.Numerics;
using Comet.Content;
using Comet.Protocol.Messages;
using Comet.Server.Movement;
using Comet.Simulation;

namespace Comet.Server.Tests;

public class MovementValidatorTests
{
    private const int TickRate = 30;
    private const float MaxSpeed = 6;
    private const float JumpVelocity = 7;

    private readonly MovementValidator _validator;

    public MovementValidatorTests()
    {
        var map = new Heightmap { SizeX = 33, SizeZ = 33 };
        map.Samples = Enumerable.Repeat(map.ToSample(0), 33 * 33).ToArray();
        map.Samples[28 * 33 + 28] = Heightmap.Hole;
        var zone = new Zone
        {
            Pieces = [new TerrainPiece { Name = "main", SkirtDepth = 5 }],
            Chunks = [new ZoneChunk { Tiles = [new TerrainTile { Piece = "main", Heights = map }] }],
        };
        var world = new CollisionWorld(new ZoneTerrain(zone), [CollisionMesh.Box(new Vector3(19, 0, 19), new Vector3(21, 2, 21))], killHeight: -20);
        _validator = new MovementValidator(world, new MovementRules(), new MovementTolerances(), TickRate);
        _validator.Reset(new Vector3(4, 0, 4), 0, tick: 100);
    }

    private MovementVerdict Report(float x, float y, float z, uint tick, uint sequence = 0) =>
        _validator.Check(
            new PositionReport
            {
                X = x,
                Y = y,
                Z = z,
                CorrectionSequence = sequence,
            },
            tick,
            MaxSpeed,
            JumpVelocity
        );

    [Fact]
    public void AcceptsWalkingAtFullSpeed()
    {
        for (var i = 1; i <= 30; i++)
        {
            Assert.Equal(MovementVerdict.Accepted, Report(4 + 0.4f * i, 0, 4, (uint)(100 + 2 * i)));
        }

        Assert.Equal(new Vector3(16, 0, 4), _validator.Position);
        Assert.Equal(0, _validator.Violations);
    }

    [Fact]
    public void RejectsMovingTooFast()
    {
        var verdicts = Enumerable.Range(1, 15).Select(i => Report(4 + 0.6f * i, 0, 4, (uint)(100 + 2 * i))).ToList();

        Assert.Contains(MovementVerdict.TooFast, verdicts);
        Assert.Equal(MovementVerdict.Accepted, verdicts[0]);
    }

    [Fact]
    public void AcceptsABurstAfterAStall()
    {
        for (var i = 1; i <= 7; i++)
        {
            Assert.Equal(MovementVerdict.Accepted, Report(4 + 0.4f * i, 0, 4, 115));
        }
    }

    [Fact]
    public void DoesNotBankMovementForADash()
    {
        Assert.Equal(MovementVerdict.TooFast, Report(4 + 10, 0, 4, 400));
        Assert.Equal(MovementVerdict.Accepted, Report(4 + 6.5f, 0, 4, 401));
    }

    [Fact]
    public void AllowsAJumpButNotMore()
    {
        Assert.Equal(MovementVerdict.Accepted, Report(4, 1.1f, 4, 110));
        Assert.Equal(MovementVerdict.TooHigh, Report(4, 2f, 4, 112));
    }

    [Fact]
    public void JumpsCountFromTheGroundStoodOn()
    {
        _validator.Reset(new Vector3(20, 2, 20), 0, 100);

        Assert.Equal(MovementVerdict.Accepted, Report(20, 3.1f, 20, 110));
    }

    [Fact]
    public void RejectsBeingInsideABoxOrUnderground()
    {
        _validator.Reset(new Vector3(17.5f, 0, 20), 0, 100);

        Assert.Equal(MovementVerdict.InsideSolid, Report(19, 0, 20, 130));
        Assert.Equal(MovementVerdict.InsideSolid, Report(17.5f, -1, 20, 131));
    }

    [Fact]
    public void AWedgedPlayerStandsAndCanJumpOut()
    {
        static CollisionMesh Wedge(float x, float yaw)
        {
            var placement = new Placement
            {
                Position = new Vector3(x, 0, 4),
                Scale = new Vector3(1, 4, 2),
                Rotation = new Vector3(0, yaw, 0),
                Primitive = new Primitive { Kind = PrimitiveKind.Wedge },
            };
            return CollisionMesh.Placed(PrimitiveMesher.For(placement, new PolygonBudget()), placement.Matrix());
        }

        var world = new CollisionWorld(null, [Wedge(7.5f, 180), Wedge(8.5f, 0)], killHeight: -20);
        var rules = new MovementRules();
        var state = new MotorState { Position = new Vector3(8, 5, 4) };
        for (var i = 0; i < 120; i++)
        {
            PlayerMotor.Step(ref state, Vector2.Zero, false, 1 / 60f, MaxSpeed, JumpVelocity, world, rules);
        }

        var validator = new MovementValidator(world, rules, new MovementTolerances(), TickRate);
        validator.Reset(state.Position, 0, tick: 100);
        var wedged = state.Position.Y;
        Assert.Equal(
            MovementVerdict.Accepted,
            validator.Check(
                new PositionReport
                {
                    X = 8,
                    Y = wedged,
                    Z = 4,
                },
                160,
                MaxSpeed,
                JumpVelocity
            )
        );
        Assert.Equal(
            MovementVerdict.Accepted,
            validator.Check(
                new PositionReport
                {
                    X = 8,
                    Y = wedged,
                    Z = 4,
                },
                220,
                MaxSpeed,
                JumpVelocity
            )
        );
        Assert.Equal(
            MovementVerdict.Accepted,
            validator.Check(
                new PositionReport
                {
                    X = 8,
                    Y = wedged + 0.9f,
                    Z = 4,
                },
                230,
                MaxSpeed,
                JumpVelocity
            )
        );
    }

    [Fact]
    public void FallingOutRespawnsOnTheLastSafeGround()
    {
        Assert.Equal(MovementVerdict.Accepted, Report(5, 0, 4, 110));
        Assert.Equal(MovementVerdict.FellOut, Report(5, -25, 4, 140));

        var correction = _validator.Correct(CorrectionReason.Respawn, 140);

        Assert.Equal(CorrectionReason.Respawn, correction.Reason);
        Assert.Equal((5f, 0f, 4f), (correction.X, correction.Y, correction.Z));
        Assert.Equal(0, _validator.Violations);
    }

    [Fact]
    public void ReportsSentBeforeACorrectionAreIgnored()
    {
        Assert.Equal(MovementVerdict.TooFast, Report(14, 0, 4, 102));
        var correction = _validator.Correct(CorrectionReason.SnapBack, 102);
        Assert.Equal(1u, correction.Sequence);
        Assert.Equal((4f, 4f), (correction.X, correction.Z));

        Assert.Equal(MovementVerdict.Stale, Report(14.4f, 0, 4, 104));

        Assert.Equal(MovementVerdict.Accepted, Report(4.4f, 0, 4, 106, sequence: 1));
        Assert.Equal(1, _validator.Violations);
    }

    private MovementVerdict Report(Vector3 at, Vector3 velocity, uint tick, uint? stamp = null, uint sequence = 0) =>
        _validator.Check(
            new PositionReport
            {
                X = at.X,
                Y = at.Y,
                Z = at.Z,
                VelocityX = velocity.X,
                VelocityY = velocity.Y,
                VelocityZ = velocity.Z,
                CorrectionSequence = sequence,
            },
            tick,
            MaxSpeed,
            JumpVelocity,
            stamp
        );

    private static float JumpHeight(float t) => MathF.Max(0, JumpVelocity * t - 0.5f * new MovementRules().Gravity * t * t);

    [Fact]
    public void AcceptsAJumpThatFollowsGravity()
    {
        for (var i = 1; i <= 20; i++)
        {
            var t = 2f * i / TickRate;
            Assert.Equal(MovementVerdict.Accepted, Report(4 + MaxSpeed * t, JumpHeight(t), 4, (uint)(100 + 2 * i)));
        }

        Assert.Equal(0, _validator.Violations);
    }

    [Fact]
    public void RejectsHoveringOnceGravityWouldHavePulledThemDown()
    {
        Assert.Equal(MovementVerdict.Accepted, Report(4, 0.9f, 4, 106));
        var verdicts = Enumerable.Range(1, 30).Select(i => Report(4, 0.9f, 4, (uint)(106 + 2 * i))).ToList();

        Assert.Equal(MovementVerdict.Accepted, verdicts[0]);
        Assert.Contains(MovementVerdict.TooHigh, verdicts);
    }

    [Fact]
    public void LateReportsAfterAStallAreTimedByTheirStamps()
    {
        for (var i = 1; i <= 6; i++)
        {
            var t = 2f * i / TickRate;
            Assert.Equal(MovementVerdict.Accepted, Report(new Vector3(4, JumpHeight(t), 4), Vector3.Zero, tick: 130, stamp: (uint)(100 + 2 * i)));
        }
    }

    [Fact]
    public void ASilentPlayerInTheAirFallsAndLandsWithTheirMomentum()
    {
        Assert.Equal(MovementVerdict.Accepted, Report(new Vector3(4, 0.6f, 4), new Vector3(4, 3, 0), tick: 103));
        var results = new List<FallResult>();
        for (uint tick = 104; tick < 200 && !results.Contains(FallResult.Landed); tick++)
        {
            results.Add(_validator.Fall(tick, MaxSpeed, JumpVelocity));
        }

        Assert.Equal(FallResult.None, results[0]);
        Assert.Equal(FallResult.Landed, results[^1]);
        Assert.Equal(results.Count - 1, results.Count(r => r == FallResult.None));
        Assert.Equal(0, _validator.Position.Y, 0.05f);
        Assert.True(_validator.Position.X > 5, $"Didn't keep moving forward (landed at x = {_validator.Position.X}).");

        var landing = _validator.Landing();
        Assert.Equal(_validator.Position.X, landing.X);
        Assert.Equal(MovementVerdict.Stale, Report(new Vector3(4, 0.9f, 4), Vector3.Zero, tick: 200));
        Assert.Equal(MovementVerdict.Accepted, Report(_validator.Position, Vector3.Zero, tick: 202, sequence: landing.Sequence));
        Assert.Equal(0, _validator.Violations);
    }

    [Fact]
    public void ASilentPlayerOffTheEdgeFallsOut()
    {
        _validator.Reset(new Vector3(31.6f, 0, 4), 0, 108);
        Assert.Equal(MovementVerdict.Accepted, Report(new Vector3(32.4f, -0.2f, 4), new Vector3(MaxSpeed, -2, 0), tick: 112));
        var result = FallResult.None;
        float? takenOverAt = null;
        for (uint tick = 113; tick < 400 && result != FallResult.FellOut; tick++)
        {
            result = _validator.Fall(tick, MaxSpeed, JumpVelocity);
            takenOverAt ??= result == FallResult.Moved ? _validator.Position.Y : null;
        }

        Assert.Equal(FallResult.FellOut, result);
        var silence = new MovementTolerances().SilenceSeconds;
        Assert.NotNull(takenOverAt);
        Assert.True(takenOverAt < -0.2f - 0.4f * new MovementRules().Gravity * silence * silence, $"Taken over at y = {takenOverAt}.");
    }

    [Fact]
    public void ASilentPlayerOnTheGroundStaysPut()
    {
        Assert.Equal(MovementVerdict.Accepted, Report(new Vector3(4.4f, 0, 4), new Vector3(MaxSpeed, 0, 0), tick: 102));
        for (uint tick = 103; tick < 200; tick++)
        {
            Assert.Equal(FallResult.None, _validator.Fall(tick, MaxSpeed, JumpVelocity));
        }

        Assert.Equal(new Vector3(4.4f, 0, 4), _validator.Position);
    }

    [Fact]
    public void AFakedVelocityDoesNotCarryASilentFall()
    {
        Assert.Equal(MovementVerdict.Accepted, Report(new Vector3(4, 0.6f, 4), new Vector3(300, 0, 0), tick: 103));
        Assert.True(_validator.Velocity.Length() <= MaxSpeed * new MovementTolerances().SpeedFactor + 0.001f);
        var result = FallResult.None;
        for (uint tick = 104; tick < 300 && result is FallResult.None or FallResult.Moved; tick++)
        {
            result = _validator.Fall(tick, MaxSpeed, JumpVelocity);
        }

        Assert.Equal(FallResult.Landed, result);
        Assert.True(_validator.Position.X < 8, $"Landed at x = {_validator.Position.X}.");
    }

    [Fact]
    public void ReportedVelocityIsHeldToTheRules()
    {
        Assert.Equal(MovementVerdict.Accepted, Report(new Vector3(4.4f, 0, 4), new Vector3(1e6f, 1e6f, 0), tick: 102));
        Assert.Equal(MaxSpeed * new MovementTolerances().SpeedFactor, _validator.Velocity.X, 0.001f);
        Assert.Equal(JumpVelocity, _validator.Velocity.Y, 0.001f);

        Assert.Equal(MovementVerdict.Accepted, Report(new Vector3(4.8f, 0, 4), new Vector3(0, -1e6f, 0), tick: 104));
        Assert.True(_validator.Velocity.Y > -30, $"Velocity y = {_validator.Velocity.Y}.");
    }

    [Theory]
    [InlineData(float.NaN, 0, 0, 0)]
    [InlineData(0, float.PositiveInfinity, 0, 0)]
    [InlineData(0, 0, float.NaN, 0)]
    [InlineData(0, 0, 0, float.NaN)]
    public void NumbersThatArentFiniteAreRejected(float x, float velocityX, float velocityY, float facing)
    {
        var report = new PositionReport
        {
            X = 4.4f + x,
            Y = 0,
            Z = 4,
            VelocityX = velocityX,
            VelocityY = velocityY,
            Facing = facing,
        };
        Assert.Equal(MovementVerdict.NotFinite, _validator.Check(report, 102, MaxSpeed, JumpVelocity));
        Assert.Equal(new Vector3(4, 0, 4), _validator.Position);
        Assert.Equal(1, _validator.Violations);
    }

    [Fact]
    public void ANaNReportDoesNotTurnOffTheSpeedCheck()
    {
        Assert.Equal(MovementVerdict.NotFinite, Report(float.NaN, 0, 4, 102));
        Assert.Equal(MovementVerdict.TooFast, Report(29, 0, 29, 104));
        Assert.Equal(MovementVerdict.Accepted, Report(4.4f, 0, 4, 106));
    }

    [Fact]
    public void ANaNVelocityDoesNotLeaveASilentPlayerFallingForever()
    {
        Assert.Equal(MovementVerdict.NotFinite, Report(new Vector3(4, 0.6f, 4), new Vector3(0, float.NaN, 0), tick: 103));
        Assert.Equal(MovementVerdict.Accepted, Report(new Vector3(4, 0.6f, 4), new Vector3(0, 1, 0), tick: 104));
        var result = FallResult.None;
        for (uint tick = 105; tick < 400 && result is FallResult.None or FallResult.Moved; tick++)
        {
            result = _validator.Fall(tick, MaxSpeed, JumpVelocity);
        }

        Assert.Equal(FallResult.Landed, result);
        Assert.True(float.IsFinite(_validator.Position.Y));
    }

    [Fact]
    public void AReportGuessingTheNextSequenceDuringAServerFallIsStale()
    {
        _validator.Reset(new Vector3(31.6f, 0, 4), 0, 108);
        Assert.Equal(MovementVerdict.Accepted, Report(new Vector3(32.4f, -0.2f, 4), new Vector3(MaxSpeed, -2, 0), tick: 112));
        var tick = 113u;
        while (_validator.Fall(tick, MaxSpeed, JumpVelocity) != FallResult.Moved)
        {
            tick++;
        }

        Assert.Equal(MovementVerdict.Stale, Report(new Vector3(4, 0, 4), Vector3.Zero, tick + 1, sequence: _validator.CorrectionSequence));
        Assert.Equal(FallResult.Moved, _validator.Fall(tick + 1, MaxSpeed, JumpVelocity));
    }

    [Theory]
    [InlineData(float.MaxValue)]
    [InlineData(-float.MaxValue)]
    [InlineData(100f)]
    public void FacingIsHeldToOneTurn(float facing)
    {
        Assert.Equal(
            MovementVerdict.Accepted,
            _validator.Check(
                new PositionReport
                {
                    X = 4.1f,
                    Z = 4,
                    Facing = facing,
                },
                102,
                MaxSpeed,
                JumpVelocity
            )
        );
        Assert.InRange(_validator.Facing, -MathF.PI, MathF.PI);
        if (MathF.Abs(facing) < 1000)
        {
            Assert.Equal(MathF.Sin(facing), MathF.Sin(_validator.Facing), 0.001f);
            Assert.Equal(MathF.Cos(facing), MathF.Cos(_validator.Facing), 0.001f);
        }
    }

    [Fact]
    public void HeadroomMeasuresTheBurstAStallNeeded()
    {
        for (var i = 1; i <= 15; i++)
        {
            Assert.Equal(MovementVerdict.Accepted, Report(4 + 0.4f * i, 0, 4, (uint)(100 + 2 * i)));
        }
        Assert.Equal(0, _validator.Headroom.BurstSecondsNeeded(0), 3);

        for (var i = 1; i <= 7; i++)
        {
            Assert.Equal(MovementVerdict.Accepted, Report(10 + 0.4f * i, 0, 4, 145));
        }
        var factor1 = _validator.Headroom.BurstSecondsNeeded(0);
        Assert.InRange(factor1, 0.4f, 0.5f);
        Assert.True(_validator.Headroom.BurstSecondsNeeded(MovementHeadroom.SpeedFactors.Length - 1) < factor1);
    }

    [Fact]
    public void HeadroomMeasuresHeightAboveAnExactJump()
    {
        var apex = new MovementRules().JumpApex(JumpVelocity);
        Assert.Equal(MovementVerdict.Accepted, Report(new Vector3(4, apex + 0.1f, 4), Vector3.Zero, tick: 112));
        Assert.Equal(0.1f, _validator.Headroom.MaxRiseOverApex, 3);
    }
}

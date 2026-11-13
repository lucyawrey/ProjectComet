using System.Numerics;
using Comet.Content;
using Comet.Simulation;

namespace ShapeLand.Bots;

/// <summary>Picks where a bot walks in one zone: routes over the walk grid to random cells and, now and then, to a teleporter. A calm one mostly walks, rests at each goal and cuts corners; any other moves flat out.</summary>
public sealed class Wanderer(WalkGrid grid, Triggers triggers, Random random, bool calm)
{
    private const double StallSeconds = 3;
    private const double PadWaitSeconds = 3;
    private const double RouteSeconds = 30;
    private const float PadChance = 0.2f;
    private const float ReachedDistance = 0.4f;
    private const float DriftDistance = 4f;
    private const float ProgressDistance = 0.1f;
    private const float PassedDistance = 0.5f;
    private const float JumpReach = 4.5f;
    private const float LookaheadSteps = 4;
    private const double RestSeconds = 2;
    private const double RestVariation = 4;
    private const float WalkPace = 0.6f;
    private const float RunChance = 0.2f;

    private List<Vector2> _route = [];
    private int _next;
    private Teleporter? _pad;
    private double _padSince = -1;
    private double _routeSince;
    private double _progressAt;
    private float _bestDistance = float.MaxValue;
    private float _pace = 1;
    private double _restUntil;

    /// <summary>The direction to move now (a unit vector, or zero to stand still). <paramref name="stepDistance"/> is how far the bot moves in one step, so a fast one doesn't skip past a waypoint or zigzag between them.</summary>
    public Vector2 Steer(double now, Vector3 feet, float stepDistance)
    {
        var position = new Vector2(feet.X, feet.Z);
        if (now < _restUntil)
        {
            return Vector2.Zero;
        }

        if (_pad != null && Triggers.Inside(_pad.Volume, feet))
        {
            _padSince = _padSince < 0 ? now : _padSince;
            if (now - _padSince < _pad.Volume.StandSeconds + PadWaitSeconds)
            {
                return Vector2.Zero;
            }

            _next = _route.Count;
        }

        var distance = DistanceToNext(position);
        while (_next < _route.Count && (distance < MathF.Max(ReachedDistance, stepDistance) || distance > _bestDistance + PassedDistance))
        {
            _next++;
            _bestDistance = float.MaxValue;
            _progressAt = now;
            distance = DistanceToNext(position);
        }

        if (calm && _route.Count > 0 && _next >= _route.Count)
        {
            (_route, _next, _restUntil) = ([], 0, now + RestSeconds + random.NextDouble() * RestVariation);
            return Vector2.Zero;
        }

        if (distance < _bestDistance - ProgressDistance)
        {
            _bestDistance = distance;
            _progressAt = now;
        }

        if (_next >= _route.Count || now - _routeSince > RouteSeconds || now - _progressAt > StallSeconds || distance > DriftDistance)
        {
            NewRoute(now, feet);
        }

        var ahead = Math.Max(calm ? 1 : 0, (int)(stepDistance * LookaheadSteps));
        return _next < _route.Count ? Vector2.Normalize(_route[Math.Min(_next + ahead, _route.Count - 1)] - position) * _pace : Vector2.Zero;
    }

    private float DistanceToNext(Vector2 position) => _next < _route.Count ? Vector2.Distance(position, _route[_next]) : 0;

    /// <summary>Whether a jump along <paramref name="direction"/> would land on ground a bot can walk on.</summary>
    public bool CanJump(Vector3 feet, Vector2 direction)
    {
        var position = new Vector2(feet.X, feet.Z);
        return direction != Vector2.Zero && Enumerable.Range(1, 3).All(i => grid.Contains(position + direction * (JumpReach * i / 3)));
    }

    private void NewRoute(double now, Vector3 feet)
    {
        var position = new Vector2(feet.X, feet.Z);
        _route = [];
        _next = 0;
        _pad = null;
        _padSince = -1;
        _routeSince = now;
        _progressAt = now;
        _bestDistance = float.MaxValue;
        _pace = calm && random.NextDouble() >= RunChance ? WalkPace : 1;
        if (grid.Nearest(position) is not { } here)
        {
            return;
        }

        Teleporter? pad = null;
        (int X, int Z)? goal = null;
        if (triggers.Teleporters.Count > 0 && random.NextDouble() < PadChance)
        {
            var candidate = triggers.Teleporters[random.Next(triggers.Teleporters.Count)];
            if (!Triggers.Inside(candidate.Volume, feet) && grid.Nearest(PadCenter(candidate)) is { } cell)
            {
                pad = candidate;
                goal = cell;
            }
        }

        if (grid.Route(here, goal ?? grid.RandomCell(here, random)) is not { } cells)
        {
            return;
        }

        _route = cells.Select(WalkGrid.Center).ToList();
        if (pad != null)
        {
            if (_route.Count > 0)
            {
                _route.RemoveAt(_route.Count - 1);
            }

            _route.Add(PadCenter(pad));
            _pad = pad;
        }
    }

    private static Vector2 PadCenter(Teleporter pad) => new(pad.Volume.Position.X, pad.Volume.Position.Z);
}

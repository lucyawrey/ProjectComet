using System.Numerics;
using Comet.Simulation;

namespace ShapeLand.Bots;

/// <summary>The ground a player can walk in one zone: one cell per meter, joined where a step between them is possible. Ignores flips and stacked floors.</summary>
public sealed class WalkGrid
{
    public const float Spacing = 1;

    private const int StepSamples = 4;
    private const int SnapRadius = 3;
    private const int EdgeCost = 4;

    private static readonly (int X, int Z)[] Neighbors = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private readonly Dictionary<(int X, int Z), int> _component = [];
    private readonly Dictionary<(int X, int Z), int> _edges = [];
    private readonly List<List<(int X, int Z)>> _components = [];
    private readonly List<List<(int X, int Z)>> _goals = [];
    private readonly CollisionWorld _world;
    private readonly MovementRules _rules;

    public WalkGrid(CollisionWorld world, Triggers triggers, MovementRules rules)
    {
        _world = world;
        _rules = rules;
        foreach (var volume in triggers.Spawns.Select(s => s.Volume).Concat(triggers.Teleporters.Select(t => t.Volume)))
        {
            if (TrySeed(Triggers.Center(volume, world)) is { } seed && !_component.ContainsKey(seed.Cell))
            {
                Fill(seed.Cell, seed.Height);
            }
        }

        // A body at speed carries a little past a corner, so goals keep a cell of ground around them where there is room.
        foreach (var cells in _components)
        {
            var inner = cells.Where(cell => !IsEdge(cell)).ToList();
            _goals.Add(inner.Count > 0 ? inner : cells);
        }
    }

    public int Count => _component.Count;

    public static Vector2 Center((int X, int Z) cell) => new(cell.X * Spacing, cell.Z * Spacing);

    /// <summary>Whether the cell under a position is in the grid.</summary>
    public bool Contains(Vector2 position) =>
        _component.ContainsKey(((int)MathF.Round(position.X / Spacing), (int)MathF.Round(position.Y / Spacing)));

    /// <summary>The cell nearest a position, or null when none is within a few meters.</summary>
    public (int X, int Z)? Nearest(Vector2 position)
    {
        (int X, int Z)? best = null;
        var bestDistance = float.MaxValue;
        int x = (int)MathF.Round(position.X / Spacing),
            z = (int)MathF.Round(position.Y / Spacing);
        for (var dz = -SnapRadius; dz <= SnapRadius; dz++)
        {
            for (var dx = -SnapRadius; dx <= SnapRadius; dx++)
            {
                var cell = (x + dx, z + dz);
                var distance = Vector2.DistanceSquared(Center(cell), position);
                if (_component.ContainsKey(cell) && distance < bestDistance)
                {
                    best = cell;
                    bestDistance = distance;
                }
            }
        }

        return best;
    }

    /// <summary>A random cell joined to <paramref name="from"/> by walking.</summary>
    public (int X, int Z) RandomCell((int X, int Z) from, Random random)
    {
        var cells = _goals[_component[from]];
        return cells[random.Next(cells.Count)];
    }

    /// <summary>The cells to walk through from one cell to another (not the first), or null when they aren't joined.</summary>
    public List<(int X, int Z)>? Route((int X, int Z) from, (int X, int Z) to)
    {
        if (!_component.TryGetValue(from, out var component) || _component.GetValueOrDefault(to, -1) != component)
        {
            return null;
        }

        var cameFrom = new Dictionary<(int X, int Z), (int X, int Z)>();
        var cost = new Dictionary<(int X, int Z), int> { [from] = 0 };
        var open = new PriorityQueue<(int X, int Z), float>();
        open.Enqueue(from, 0);
        while (open.TryDequeue(out var cell, out _))
        {
            if (cell == to)
            {
                break;
            }

            for (var d = 0; d < Neighbors.Length; d++)
            {
                var next = (cell.X + Neighbors[d].X, cell.Z + Neighbors[d].Z);
                var nextCost = cost[cell] + (IsEdge(next) ? EdgeCost : 1);
                if (
                    (_edges.GetValueOrDefault(cell) & (1 << d)) != 0
                    && _component[next] == component
                    && nextCost < cost.GetValueOrDefault(next, int.MaxValue)
                )
                {
                    cost[next] = nextCost;
                    cameFrom[next] = cell;
                    // The tiny cross-product term settles ties toward the straight line from start to goal, so routes don't stair-step.
                    var offLine = Math.Abs((next.Item1 - to.X) * (from.Z - to.Z) - (from.X - to.X) * (next.Item2 - to.Z));
                    open.Enqueue(next, cost[next] + Math.Abs(to.X - next.Item1) + Math.Abs(to.Z - next.Item2) + offLine * 0.001f);
                }
            }
        }

        if (from != to && !cameFrom.ContainsKey(to))
        {
            return null;
        }

        var route = new List<(int X, int Z)>();
        for (var cell = to; cell != from; cell = cameFrom[cell])
        {
            route.Add(cell);
        }

        route.Reverse();
        return route;
    }

    private bool IsEdge((int X, int Z) cell) => Neighbors.Any(n => !_component.ContainsKey((cell.X + n.X, cell.Z + n.Z)));

    private ((int X, int Z) Cell, float Height)? TrySeed(Vector3 center)
    {
        int x = (int)MathF.Round(center.X / Spacing),
            z = (int)MathF.Round(center.Z / Spacing);
        for (var dz = 0; dz <= 1; dz++)
        {
            for (var dx = 0; dx <= 1; dx++)
            {
                var cell = (x + dx, z + dz);
                var at = Center(cell);
                if (Stand(new Vector3(at.X, center.Y, at.Y), out var height))
                {
                    return (cell, height);
                }
            }
        }

        return null;
    }

    private void Fill((int X, int Z) seed, float seedHeight)
    {
        var index = _components.Count;
        var cells = new List<(int X, int Z)> { seed };
        _components.Add(cells);
        _component[seed] = index;
        var heights = new Dictionary<(int X, int Z), float> { [seed] = seedHeight };
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            for (var d = 0; d < Neighbors.Length; d++)
            {
                var next = (cell.X + Neighbors[d].X, cell.Z + Neighbors[d].Z);
                if (!TryWalk(cell, heights[cell], next, out var height))
                {
                    continue;
                }

                _edges[cell] = _edges.GetValueOrDefault(cell) | (1 << d);
                if (!_component.ContainsKey(next))
                {
                    _component[next] = index;
                    heights[next] = height;
                    cells.Add(next);
                }
            }
        }
    }

    /// <summary>Walks from a cell to the next in short strides, the way the player's own movement would: up by a step or down by a step at most, never into the body of something.</summary>
    private bool TryWalk((int X, int Z) from, float fromHeight, (int X, int Z) to, out float height)
    {
        var start = Center(from);
        var end = Center(to);
        height = fromHeight;
        for (var i = 1; i <= StepSamples; i++)
        {
            var at = Vector2.Lerp(start, end, i / (float)StepSamples);
            if (!Stand(new Vector3(at.X, height, at.Y), out var ground) || MathF.Abs(ground - height) > _rules.StepHeight)
            {
                return false;
            }

            height = ground;
        }

        return true;
    }

    /// <summary>The ground a body at <paramref name="feet"/> can stand on within a step of it, with room for the body.</summary>
    private bool Stand(Vector3 feet, out float height) =>
        _world.TryGetGround(feet, _rules.BodyRadius, feet.Y + _rules.StepHeight, out height)
        && !_world.IsBlocked(new Vector3(feet.X, height, feet.Z), _rules.BodyRadius, _rules.BodyHeight);
}

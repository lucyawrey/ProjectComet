using System.Collections.Concurrent;
using Comet.Content;
using Comet.Simulation;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.World;

namespace ShapeLand.Bots;

public sealed class ZoneWorlds(ShapeLandContent content)
{
    private readonly ConcurrentDictionary<int, Lazy<CollisionWorld>> _worlds = new();
    private readonly ConcurrentDictionary<int, Lazy<Triggers>> _triggers = new();
    private readonly ConcurrentDictionary<int, Lazy<Task<WalkGrid>>> _grids = new();

    public CollisionWorld For(Zone zone) =>
        _worlds.GetOrAdd(zone.Number, _ => new Lazy<CollisionWorld>(() => ShapeLandWorld.Create(content, zone))).Value;

    public Triggers TriggersFor(Zone zone) => _triggers.GetOrAdd(zone.Number, _ => new Lazy<Triggers>(() => new Triggers(zone))).Value;

    public Task<WalkGrid> GridFor(Zone zone) =>
        _grids
            .GetOrAdd(zone.Number, _ => new Lazy<Task<WalkGrid>>(() => Task.Run(() => new WalkGrid(For(zone), TriggersFor(zone), content.Movement))))
            .Value;
}

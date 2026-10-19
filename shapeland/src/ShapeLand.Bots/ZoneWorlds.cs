using System.Collections.Concurrent;
using Comet.Content;
using Comet.Simulation;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.World;

namespace ShapeLand.Bots;

/// <summary>The collision worlds of content's zones, each built once when a bot first needs it; safe across threads.</summary>
public sealed class ZoneWorlds(ShapeLandContent content)
{
    private readonly ConcurrentDictionary<int, Lazy<CollisionWorld>> _worlds = new();

    public CollisionWorld For(Zone zone) => _worlds.GetOrAdd(zone.Number, _ => new Lazy<CollisionWorld>(() => ShapeLandWorld.Create(content, zone))).Value;
}

using Comet.Content;
using Comet.Simulation;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.World;

namespace ShapeLand.GameServer;

/// <summary>
/// One zone as the server plays it: its world (with placements flipped by triggers), spawn points, teleporters and
/// triggers, the players in it, and its live trigger state, which resets when the server restarts. Tick thread only.
/// </summary>
internal sealed class ZoneState
{
    private readonly Dictionary<Trigger, TriggerCharge> _charges = [];
    // The tick each flipped tag with a revert time flips back.
    private readonly Dictionary<string, uint> _reverts = [];

    private readonly MovementRules _rules;

    public ZoneState(ShapeLandContent content, Zone zone)
    {
        _rules = content.Movement;
        Zone = zone;
        World = ShapeLandWorld.Create(content, zone);
        Triggers = new Triggers(zone);
        foreach (var trigger in Triggers.All)
        {
            _charges.Add(trigger, new TriggerCharge());
        }
    }

    public Zone Zone { get; }

    public CollisionWorld World { get; }

    public Triggers Triggers { get; }

    /// <summary>The joined players here.</summary>
    public HashSet<Player> Players { get; } = [];

    /// <summary>The names of placements flipped from how they start.</summary>
    public HashSet<string> Flipped { get; } = [];

    /// <summary>
    /// Times each trigger (one state per trigger, shared by everyone in it) and fires its effects, then flips back
    /// names whose revert time is up. Reports each flip or flip back, for the server to tell the zone's players.
    /// </summary>
    public void Tick(uint tick, float seconds, Action<string, bool> flipped)
    {
        foreach (var (trigger, charge) in _charges)
        {
            var anyoneInside = Players.Any(p => Comet.Simulation.Triggers.Inside(trigger.Volume, p.Validator.Position));
            if (charge.Step(anyoneInside ? trigger : null, trigger.Volume, seconds))
            {
                foreach (var effect in trigger.Effects)
                {
                    Fire(effect, tick, flipped);
                }
            }
        }

        foreach (var (tag, at) in _reverts.Where(r => r.Value <= tick).ToList())
        {
            _reverts.Remove(tag);
            Flip(tag, flipped);
        }
    }

    // Flips the effect's placements, unless a revert is pending: then it only restarts the wait.
    private void Fire(ToggleEffect effect, uint tick, Action<string, bool> flipped)
    {
        var revertAt = tick + (uint)MathF.Round(effect.RevertSeconds * ShapeLandRules.TickRate);
        if (_reverts.ContainsKey(effect.Tag))
        {
            _reverts[effect.Tag] = revertAt;
            return;
        }

        Flip(effect.Tag, flipped);
        if (effect.RevertSeconds > 0)
        {
            _reverts[effect.Tag] = revertAt;
        }
    }

    private void Flip(string tag, Action<string, bool> flipped)
    {
        var now = Flipped.Add(tag) || !Flipped.Remove(tag);
        World.SetFlipped(tag, now);

        // Anyone inside a placement that just turned solid passes through it until they're out.
        foreach (var mesh in World.MeshesTagged(tag).Where(World.IsOn))
        {
            foreach (var player in Players)
            {
                if (World.Overlaps(mesh, player.Validator.Position, _rules.BodyRadius, _rules.BodyHeight))
                {
                    player.Validator.PassThrough.Add(mesh);
                }
            }
        }

        flipped(tag, now);
    }
}

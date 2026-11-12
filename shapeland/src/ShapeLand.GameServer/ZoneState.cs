using Comet.Content;
using Comet.Simulation;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.World;

namespace ShapeLand.GameServer;

internal sealed class ZoneState
{
    private readonly Dictionary<Trigger, TriggerCharge> _charges = [];

    private readonly Dictionary<string, uint> _reverts = [];

    private readonly List<string> _due = [];

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

    public HashSet<Player> Players { get; } = [];

    public HashSet<string> Flipped { get; } = [];

    public void Tick(uint tick, float seconds, Action<string, bool> flipped)
    {
        foreach (var (trigger, charge) in _charges)
        {
            var anyoneInside = false;
            foreach (var player in Players)
            {
                if (Comet.Simulation.Triggers.Inside(trigger.Volume, player.Validator.Position))
                {
                    anyoneInside = true;
                    break;
                }
            }

            if (charge.Step(anyoneInside ? trigger : null, trigger.Volume, seconds))
            {
                foreach (var effect in trigger.Effects)
                {
                    Fire(effect, tick, flipped);
                }
            }
        }

        if (_reverts.Count == 0)
        {
            return;
        }

        _due.Clear();
        foreach (var (tag, at) in _reverts)
        {
            if (at <= tick)
            {
                _due.Add(tag);
            }
        }

        foreach (var tag in _due)
        {
            _reverts.Remove(tag);
            Flip(tag, flipped);
        }
    }

    private void Fire(ToggleEffect effect, uint tick, Action<string, bool> flipped)
    {
        var revertAt = tick + (uint)MathF.Round(effect.RevertSeconds * ShapeLandRules.TickRate);
        if (_reverts.ContainsKey(effect.Tag))
        {
            if (effect.RevertSeconds > 0)
            {
                _reverts[effect.Tag] = revertAt;
            }

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

        foreach (var player in Players)
        {
            player.Validator.PassThrough.Flipped(World, tag, player.Validator.Position, _rules.BodyRadius, _rules.BodyHeight);
        }

        flipped(tag, now);
    }
}

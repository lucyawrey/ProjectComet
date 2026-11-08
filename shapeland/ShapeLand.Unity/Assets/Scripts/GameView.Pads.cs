using System.Linq;
using Comet.Content;
using Comet.Protocol.Messages;
using Comet.Simulation;
using Comet.Unity;
using UnityEngine;

namespace ShapeLand.Client
{
    public sealed partial class GameView
    {
        // A spawn point's, teleporter's or trigger's pad, if it has one: drawn on its own, to be colored.
        private void AddPad(object owner, string id, TriggerVolume volume, Transform parent)
        {
            if (volume.Pad is not { } pad)
            {
                return;
            }

            var thing = _drawing.PlacePrimitive(pad, parent);
            thing.name = id;
            var color =
                owner is SpawnPoint
                    ? Conversions.FromRgb(_content.Settings.Spawns.UnsetColor)
                    : Conversions.FromRgb(pad.Color ?? _content.Settings.World.PlacedColor);
            _pads.Add(new Pad(owner, thing.GetComponentsInChildren<Renderer>(), color));
        }

        private sealed class Pad
        {
            public Pad(object owner, Renderer[] renderers, Color color)
            {
                Owner = owner;
                Renderers = renderers;
                Color = color;
            }

            /// <summary>The spawn point, teleporter or trigger it's under.</summary>
            public object Owner { get; }

            public Renderer[] Renderers { get; }

            /// <summary>Its color at rest (a spawn's while it isn't the player's).</summary>
            public Color Color { get; }
        }

        // The player's own spawn's pad takes their body color, other spawns' a pale color used nowhere else, and
        // other pads their own. A pad the player stands in charges toward firing (Charging): a spawn's toward their
        // body color, a teleporter's and a trigger's toward their charge color.
        private void ColorPads()
        {
            if (_content == null || _pads.Count == 0)
            {
                return;
            }

            _padColor ??= new MaterialPropertyBlock();
            var now = Time.time;
            var own = Player != null ? _join.SpawnNumber : null;
            var body =
                Player != null && _join.Spawn is { } me
                    ? Conversions.FromRgb(_content.ColorRgb(me.Color))
                    : Conversions.FromRgb(_content.Settings.Spawns.UnsetColor);
            if (Player != null)
            {
                var feet = Player.Motor.Position;
                var spawn = _triggers.SpawnAt(feet);
                var teleporter = _triggers.TeleporterAt(feet) is { } at && _teleporterIds.Contains(at.Target) ? at : null;
                var trigger = _triggers.All.FirstOrDefault(t => Triggers.Inside(t.Volume, feet));
                _spawnCharge.Step(spawn, spawn?.Volume, now);
                _triggerCharge.Step(trigger, trigger?.Volume, now);
                if (_teleportCharge.Step(teleporter, teleporter?.Volume, now))
                {
                    _sending = teleporter;
                    _padFading = false;
                }

                // The shape starts fading out just before the pad fires, to be gone as it does; stepping off first
                // brings it back.
                if (teleporter != null && _teleportCharge.Inside == teleporter && !_teleportCharge.Fired)
                {
                    var left = (1 - _teleportCharge.Progress(now)) * teleporter.Volume.StandSeconds;
                    if (!_padFading && left <= ShapeFade.FadeOutSeconds)
                    {
                        Player.TeleportSoon(left);
                        _padFading = true;
                    }
                }
                else if (_padFading)
                {
                    Player.TeleportCancelled();
                    _padFading = false;
                }

                if (_sending != null && _teleportCharge.Inside != _sending && !Player.Departing)
                {
                    _sending = null;
                }
            }

            foreach (var pad in _pads)
            {
                Color color;
                if (pad.Owner is SpawnPoint spawn)
                {
                    var charge = _spawnCharge.Inside == spawn ? (_spawnCharge.Fired ? 1 : _spawnCharge.Progress(now)) : 0;
                    color = spawn.Number == own ? body : Charging(pad.Color, body, charge, spawn.Volume.StandSeconds);
                }
                else if (pad.Owner is Teleporter teleporter)
                {
                    // Lit from firing until the player has faded out of it, and the target from arriving until they step off.
                    var charge =
                        teleporter == _sending && Player != null && Player.Departing ? 1
                        : _teleportCharge.Inside != teleporter ? 0
                        : _teleportCharge.Fired ? 1
                        : _teleportCharge.Progress(now);
                    var to = Conversions.FromRgb(teleporter.ChargeColor ?? _content.Settings.Teleporters.ChargeColor);
                    color = Charging(pad.Color, to, charge, teleporter.Volume.StandSeconds);
                }
                else if (pad.Owner is Trigger trigger)
                {
                    var charge =
                        _triggerCharge.Inside != trigger ? 0
                        : _triggerCharge.Fired ? 1
                        : _triggerCharge.Progress(now);
                    var to = Conversions.FromRgb(trigger.ChargeColor ?? _content.Settings.Triggers.ChargeColor);
                    color = Charging(pad.Color, to, charge, trigger.Volume.StandSeconds);
                }
                else
                {
                    continue;
                }

                _padColor.SetColor(BaseColor, color);
                foreach (var renderer in pad.Renderers)
                {
                    renderer.SetPropertyBlock(_padColor);
                }
            }
        }

        // Arriving somewhere (a spawn, a teleporter's target) counts as already in what's there: it charges only after a step out and in.
        private void OnCorrected(PositionCorrection correction)
        {
            if (_zonePending)
            {
                _pendingArrival = correction; // in the zone not drawn yet: its triggers, once it is
                return;
            }

            if (_triggers == null)
            {
                return;
            }

            var feet = new System.Numerics.Vector3(correction.X, correction.Y, correction.Z);
            if (_triggers.SpawnAt(feet) is { } spawn)
            {
                _spawnCharge.Arrived(spawn, spawn.Volume);
            }

            if (_triggers.TeleporterAt(feet) is { } teleporter)
            {
                _teleportCharge.Arrived(teleporter, teleporter.Volume);
            }
        }

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        // Every pad charges alike: from its resting color straight toward the color it charges to, over its stand time
        // (charge 0 to 1, or 1 once fired), blinking back to its resting color once, BlinkSeconds long, just before it
        // fires, so the moment shows on any color. A pad that fires at once has no time to blink.
        private const float BlinkSeconds = 0.1f;

        private static Color Charging(Color rest, Color to, float charge, float standSeconds)
        {
            var left = (1 - charge) * standSeconds;
            return charge > 0 && left > BlinkSeconds && left <= 2 * BlinkSeconds ? rest : UnityEngine.Color.Lerp(rest, to, charge);
        }
    }
}

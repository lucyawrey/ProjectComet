using System.Collections.Generic;
using System.Linq;
using Comet.Content;
using Comet.Simulation;
using MessagePack;
using UnityEngine;
using static Comet.Unity.Conversions;

namespace Comet.Unity.Editor
{
    public static partial class ZoneEditor
    {
        public static Zone Gather(ZoneEditing editing)
        {
            var zone = MessagePackSerializer.Deserialize<Zone>(editing.ZoneData, Serializer);
            var chunks = zone.Chunks.ToDictionary(c => c.Coord);
            var settings = CometSettings.Find(quiet: true);

            uint? OwnChargeColor(Color color, Color? fallback) =>
                fallback is { } usual && Conversions.ToRgb(color) == Conversions.ToRgb(usual) ? null : Conversions.ToRgb(color);
            ZoneChunk ChunkAt(System.Numerics.Vector3 position)
            {
                var coord = ChunkCoord.Containing(position);
                if (!chunks.TryGetValue(coord, out var chunk))
                {
                    chunk = new ZoneChunk { Coord = coord };
                    chunks.Add(coord, chunk);
                }

                return chunk;
            }

            foreach (
                var placed in editing
                    .GetComponentsInChildren<ZonePlacement>(true)
                    .Where(p => p.IsLoose && p.GetComponentInParent<ZoneTrigger>(true) == null)
            )
            {
                var chunk = ChunkAt(ToNumerics(placed.transform.position));
                chunk.Placements = chunk.Placements.Append(PlacementOf(placed)).ToArray();
            }

            foreach (var spawn in editing.GetComponentsInChildren<ZoneSpawn>(true))
            {
                var chunk = ChunkAt(ToNumerics(spawn.transform.position));
                chunk.Spawns = chunk
                    .Spawns.Append(
                        new SpawnPoint
                        {
                            Id = spawn.id,
                            Volume = VolumeOf(spawn),
                            Facing = (SpawnFacing)spawn.facing,
                        }
                    )
                    .ToArray();
            }

            foreach (var teleporter in editing.GetComponentsInChildren<ZoneTeleporter>(true))
            {
                var chunk = ChunkAt(ToNumerics(teleporter.transform.position));
                chunk.Teleporters = chunk
                    .Teleporters.Append(
                        new Teleporter
                        {
                            Id = teleporter.id,
                            Volume = VolumeOf(teleporter),
                            Facing = (SpawnFacing)teleporter.facing,
                            Target = teleporter.target,
                            ChargeColor = OwnChargeColor(teleporter.chargeColor, settings?.teleporterChargeColor),
                        }
                    )
                    .ToArray();
            }

            foreach (var trigger in editing.GetComponentsInChildren<ZoneToggleTrigger>(true))
            {
                var chunk = ChunkAt(ToNumerics(trigger.transform.position));
                chunk.Triggers = chunk
                    .Triggers.Append(
                        new Trigger
                        {
                            Id = trigger.id,
                            Volume = VolumeOf(trigger),
                            Effects = trigger.effects.Select(e => new ToggleEffect { Tag = e.toggleTag, RevertSeconds = e.revertSeconds }).ToArray(),
                            ChargeColor = OwnChargeColor(trigger.chargeColor, settings?.triggerChargeColor),
                        }
                    )
                    .ToArray();
            }

            foreach (var volume in editing.GetComponentsInChildren<ZoneVolume>(true))
            {
                var t = volume.transform;
                var chunk = ChunkAt(ToNumerics(t.position));
                chunk.Volumes = chunk
                    .Volumes.Append(
                        new CollisionVolume
                        {
                            Kind = (VolumeKind)volume.kind,
                            Position = ToNumerics(t.position),
                            Rotation = Angles(t.rotation.eulerAngles),
                            Size = ToNumerics(volume.size),
                        }
                    )
                    .ToArray();
            }

            foreach (var sign in editing.GetComponentsInChildren<ZoneSign>(true).Where(s => !IsTriggerSign(s)))
            {
                var chunk = ChunkAt(ToNumerics(sign.transform.position));
                chunk.Signs = chunk.Signs.Append(SignOf(sign, ToNumerics(sign.transform.position))).ToArray();
            }

            ZoneTerrainEditing.Crop(zone);

            zone.Chunks = chunks
                .Values.Where(c =>
                    c.Tiles.Length
                        + c.Placements.Length
                        + c.Spawns.Length
                        + c.Teleporters.Length
                        + c.Triggers.Length
                        + c.Volumes.Length
                        + c.Signs.Length
                    > 0
                )
                .OrderBy(c => c.Coord.X)
                .ThenBy(c => c.Coord.Z)
                .ToArray();
            return zone;
        }

        private static TriggerVolume VolumeOf(ZoneTrigger trigger)
        {
            var t = trigger.transform;
            return new TriggerVolume
            {
                Kind = trigger.Shape,
                Position = ToNumerics(t.position),
                Yaw = Angle(t.rotation.eulerAngles.y),
                Size = ToNumerics(trigger.size),
                StandSeconds = trigger.standSeconds,
                Rearm = (Rearm)trigger.rearm,
                Pad = trigger.Pad is { } pad ? PlacementOf(pad) : null,
                Sign = trigger.Sign is { } sign
                    ? SignOf(sign, ToNumerics(Quaternion.Euler(0, -t.eulerAngles.y, 0) * (sign.transform.position - t.position)))
                    : null,
            };
        }

        private static bool IsTriggerSign(ZoneSign sign) =>
            sign.transform.parent != null && sign.transform.parent.GetComponent<ZoneTrigger>() is { } trigger && trigger.Sign == sign;

        private static Sign SignOf(ZoneSign sign, System.Numerics.Vector3 position) =>
            new Sign
            {
                Position = position,
                Text = sign.text.Replace("\r\n", "\n"),
                TextColor = Conversions.ToRgb(sign.textColor),
                Background = sign.background,
                BackgroundColor = Conversions.ToRgb(sign.backgroundColor),
                BackgroundOpacity = sign.backgroundOpacity,
                ShowDistance = sign.showDistance,
                ScreenSized = sign.screenSized,
                FontSize = sign.fontSize,
                TextHeight = sign.textHeight,
                Arrow = sign.arrow,
                Off = !sign.gameObject.activeSelf,
            };

        internal static Placement PlacementOf(ZonePlacement placed, Transform? relativeTo = null)
        {
            var t = placed.transform;
            var position = relativeTo != null ? relativeTo.InverseTransformPoint(t.position) : t.position;
            var rotation = relativeTo != null ? Quaternion.Inverse(relativeTo.rotation) * t.rotation : t.rotation;
            var lossy =
                relativeTo != null
                    ? Vector3.Scale(t.lossyScale, new Vector3(1 / relativeTo.lossyScale.x, 1 / relativeTo.lossyScale.y, 1 / relativeTo.lossyScale.z))
                    : t.lossyScale;
            return new Placement
            {
                Prop = placed.isPrimitive || placed.IsGroup ? "" : placed.prop,
                Primitive = placed.IsGroup ? null : placed.Primitive,
                Group = placed.IsGroup ? placed.group : null,
                Blockout = placed.blockout,
                Off = !placed.gameObject.activeSelf,
                Tag = string.IsNullOrEmpty(placed.placementTag) ? null : placed.placementTag,
                Position = ToNumerics(position),
                Rotation = Angles(rotation.eulerAngles),
                Scale = ToNumerics(placed.isPrimitive && !placed.IsGroup ? Vector3.Scale(placed.size, lossy) : lossy),
                Color = placed.ownColor && !placed.IsGroup ? Conversions.ToRgb(placed.color) : (uint?)null,
                Role =
                    !string.IsNullOrEmpty(placed.role) && (relativeTo != null || placed.GetComponentInParent<ZoneEditing>(true) is { IsGroup: true })
                        ? placed.role
                        : null,
                Colors = placed.IsGroup && placed.roleColors.Count > 0 ? RoleColors(placed) : null,
            };
        }

        private static Dictionary<string, uint> RoleColors(ZonePlacement copy)
        {
            var colors = new Dictionary<string, uint>();
            foreach (var c in copy.roleColors.Where(c => !string.IsNullOrEmpty(c.role)))
            {
                colors[c.role] = Conversions.ToRgb(c.color);
            }

            return colors;
        }
    }
}

using System;
using System.Collections.Generic;
using Comet.Content;

namespace Comet.ZoneFiles
{
    public static partial class ZoneValidation
    {
        // A sign's text, colors and sizes; where it is is checked by the caller.
        private static void CheckSign(Sign sign, string what, string file, Action<string, string> error)
        {
            if (string.IsNullOrWhiteSpace(sign.Text))
            {
                error(file, $"{what}: its text is empty.");
            }
            else if (sign.Text.Length > Sign.MaxText)
            {
                error(file, $"{what}: its text is {sign.Text.Length} characters long; the most is {Sign.MaxText}.");
            }

            if (sign.BackgroundOpacity < 0 || sign.BackgroundOpacity > 1)
            {
                error(file, $"{what}: background_opacity should be 0 to 1.");
            }

            if (sign.ShowDistance < 1 || sign.ShowDistance > Sign.MaxShowDistance)
            {
                error(file, $"{what}: show_distance should be 1 to {Sign.MaxShowDistance} meters.");
            }

            if (sign.ScreenSized && (sign.FontSize < Sign.MinFontSize || sign.FontSize > Sign.MaxFontSize))
            {
                error(file, $"{what}: font_size should be {Sign.MinFontSize} to {Sign.MaxFontSize}.");
            }

            if (!sign.ScreenSized && (sign.TextHeight < Sign.MinTextHeight || sign.TextHeight > Sign.MaxTextHeight))
            {
                error(file, $"{what}: text_height should be {Sign.MinTextHeight} to {Sign.MaxTextHeight} meters.");
            }
        }

        // A spawn point's, teleporter's or trigger's id, volume, pad and sign.
        private static void CheckTrigger(
            string id,
            string kind,
            TriggerVolume volume,
            ChunkCoord chunk,
            string zoneName,
            Dictionary<string, string> idFiles,
            string file,
            Action<string, string> error
        )
        {
            if (!IsZonedId(id, kind) || id.Split('.')[1] != zoneName)
            {
                error(file, $"{kind}: '{id}' isn't a valid {kind} id here: use '{kind}.{zoneName}.' then lowercase letters, digits and underscores.");
            }
            else if (idFiles.TryGetValue(id, out var first))
            {
                error(file, $"{kind}: '{id}' is already used in {first}.");
            }
            else
            {
                idFiles.Add(id, file);
            }

            CheckPivot(chunk, volume.Position, id, file, error);
            if (Array.IndexOf(VolumeKinds, volume.Kind) < 0)
            {
                error(file, $"{id}: a volume is a box, a cylinder or a sphere, not a {ZoneToml.SnakeCase(volume.Kind.ToString())}.");
            }

            CheckBox(volume.Size, id, file, error);
            if (volume.StandSeconds < 0)
            {
                error(file, $"{id}: stand_seconds can't be negative.");
            }
            else if (volume.Rearm == Rearm.Repeat && volume.StandSeconds == 0)
            {
                error(file, $"{id}: rearm = \"repeat\" needs a stand_seconds above 0, or it fires every tick.");
            }

            if (volume.Pad is { } pad)
            {
                if (pad.Primitive is null)
                {
                    error(file, $"{id}: a pad is a primitive.");
                }
                else
                {
                    CheckPlaced(pad, $"{id}'s pad", inGroup: false, file, error);
                }

                if (pad.Tag is not null || pad.Off)
                {
                    error(file, $"{id}: a pad can't be tagged or start off; pads aren't toggled.");
                }

                CheckPivot(chunk, pad.Position, $"{id}'s pad", file, error);
            }

            if (volume.Sign is { } sign)
            {
                // Its offset may reach into the next chunk: it goes with its trigger.
                CheckSign(sign, $"{id}'s sign", file, error);
            }
        }
    }
}

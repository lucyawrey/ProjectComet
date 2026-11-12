using UnityEditor;
using UnityEngine;

namespace Comet.Unity.Editor
{
    internal static class ZoneSwap
    {
        public static GameObject? ForProp(ZonePlacement placed, string id, CometSettings settings)
        {
            if (!placed.isPrimitive || placed.IsGroup || settings.props == null || !settings.props.TryGet(id, out var entry))
            {
                return null;
            }

            var placement = ZoneEditor.PlacementOf(placed);
            var size = Conversions.ToUnity(placement.Scale);
            var bounds = entry.bounds.size;
            var scale = new Vector3(Ratio(size.x, bounds.x), Ratio(size.y, bounds.y), Ratio(size.z, bounds.z));
            if (entry.uniformScale)
            {
                scale = Vector3.one * ((scale.x + scale.y + scale.z) / 3);
            }

            var rotation = placed.transform.rotation;
            var bottom = new Vector3(entry.bounds.center.x, entry.bounds.min.y, entry.bounds.center.z);
            placement.Primitive = null;
            placement.Prop = id;
            placement.Blockout = false;
            placement.Scale = Conversions.ToNumerics(scale);
            placement.Position = Conversions.ToNumerics(placed.transform.position - rotation * Vector3.Scale(scale, bottom));
            return Replace(placed, placement, settings, $"Swap for {id}");
        }

        public static GameObject? ForBox(ZonePlacement placed, CometSettings settings)
        {
            if (placed.isPrimitive || placed.IsGroup || settings.props == null || !settings.props.TryGet(placed.prop, out var entry))
            {
                return null;
            }

            var placement = ZoneEditor.PlacementOf(placed);
            var scale = Conversions.ToUnity(placement.Scale);
            var rotation = placed.transform.rotation;
            var bottom = new Vector3(entry.bounds.center.x, entry.bounds.min.y, entry.bounds.center.z);
            var size = Vector3.Scale(scale, entry.bounds.size);
            placement.Position = Conversions.ToNumerics(placed.transform.position + rotation * Vector3.Scale(scale, bottom));
            placement.Prop = "";
            placement.Primitive = new Comet.Content.Primitive { Kind = Comet.Content.PrimitiveKind.Box };
            placement.Scale = Conversions.ToNumerics(new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));

            placement.Color ??= entry.hasColor ? Conversions.ToRgb(entry.color) : (uint?)null;
            return Replace(placed, placement, settings, "Swap for box");
        }

        private static float Ratio(float size, float bounds) => bounds > 1e-4f ? size / bounds : 1;

        private static GameObject Replace(ZonePlacement placed, Comet.Content.Placement placement, CometSettings settings, string undo)
        {
            Undo.SetCurrentGroupName(undo);
            var group = Undo.GetCurrentGroup();
            var old = placed.transform;
            var thing = ZoneEditor.CreatePlaced(placement, old.parent, settings);
            thing.transform.SetSiblingIndex(old.GetSiblingIndex());
            Undo.RegisterCreatedObjectUndo(thing, undo);
            Undo.DestroyObjectImmediate(placed.gameObject);
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = thing;
            return thing;
        }
    }
}

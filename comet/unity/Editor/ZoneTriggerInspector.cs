using Comet.Content;
using UnityEditor;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// A spawn point's, teleporter's or trigger's Inspector: its fields, then buttons to fit its volume to its pad after
    /// the pad is moved or resized, or to give it a pad if it has none.
    /// </summary>
    [CustomEditor(typeof(ZoneTrigger), true)]
    internal sealed class ZoneTriggerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var trigger = (ZoneTrigger)target;
            EditorGUILayout.Space();
            if (trigger.Pad != null)
            {
                if (GUILayout.Button(new GUIContent("Fit Trigger to Pad", "Stands the volume on the pad's top, as wide and deep as the pad; its height stays.")))
                {
                    ZoneEditor.FitToPad(trigger);
                }

                return;
            }

            if (GUILayout.Button(new GUIContent("Add Pad", "A flat solid primitive under the volume, as wide and deep as it, for players to stand on.")))
            {
                AddPad(trigger);
            }
        }

        // A flat box (a cylinder under a round volume) under the volume, sunk halfway into the ground there.
        private static void AddPad(ZoneTrigger trigger)
        {
            var settings = CometSettings.Find(quiet: true);
            if (settings == null)
            {
                return;
            }

            var height = trigger is ZoneTeleporter ? settings.teleporterPadSize.y : settings.spawnPadSize.y;
            var kind = trigger.volume == ZoneTrigger.VolumeKind.Box ? PrimitiveKind.Box : PrimitiveKind.Cylinder;
            var (primitive, _) = ZoneEditor.Starting(kind);
            var t = trigger.transform;
            var pad = ZoneEditor.CreatePlaced(new Placement
            {
                Primitive = primitive,
                Position = ZoneEditor.ToNumerics(t.position - Vector3.up * (height / 2)),
                Rotation = new System.Numerics.Vector3(0, t.eulerAngles.y, 0),
                Scale = new System.Numerics.Vector3(trigger.size.x, height, trigger.size.z),
            }, t, settings);
            pad.name = $"{trigger.id} pad";
            Undo.RegisterCreatedObjectUndo(pad, "Add Pad");
            ZoneEditor.FitToPad(trigger);
        }
    }
}

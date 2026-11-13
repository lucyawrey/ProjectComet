using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comet.Content;
using Comet.Simulation;
using Comet.ZoneFiles;
using MessagePack;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static Comet.Unity.Conversions;

namespace Comet.Unity.Editor
{
    public static partial class ZoneEditor
    {
        internal static readonly MessagePackSerializerOptions Serializer = ContentSerializer.CreateOptions(
            MessagePack.Resolvers.StandardResolver.Instance
        );

        public static bool Open(string folder, CometSettings settings)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Zones can't be opened in play mode: stop playing first.");
                return false;
            }

            var errors = new List<ZoneFileError>();
            var zone = ZoneFolder.Read(folder, errors);
            if (errors.Count > 0)
            {
                Report($"Can't open {folder}", errors);
                return false;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            ZoneTerrainEditing.Forget();

            // A new scene unloads assets nothing in it uses, the settings and prop library among them: load them again.
            settings = CometSettings.Find(quiet: true) ?? settings;
            ZonePlacement.Budget = settings.Budget;
            var root = new GameObject($"Zone {zone.Id}");
            var editing = root.AddComponent<ZoneEditing>();
            editing.folder = RelativeToProject(folder);
            Build(root.transform, zone, settings);

            foreach (var chunk in zone.Chunks)
            {
                chunk.Placements = new Placement[0];
                chunk.Spawns = new SpawnPoint[0];
                chunk.Teleporters = new Teleporter[0];
                chunk.Triggers = new Trigger[0];
                chunk.Volumes = new CollisionVolume[0];
                chunk.Signs = new Sign[0];
            }

            editing.ZoneData = MessagePackSerializer.Serialize(zone, Serializer);

            Directory.CreateDirectory(ProjectPath(settings.scenesFolder));
            ZoneEditorScenes.Quiet = true;
            try
            {
                EditorSceneManager.SaveScene(scene, $"{settings.scenesFolder}/{Path.GetFileName(folder.TrimEnd('/', '\\'))}.unity");
            }
            finally
            {
                ZoneEditorScenes.Quiet = false;
            }

            Selection.activeGameObject = root;
            Remember(editing.folder);
            return true;
        }

        public static bool Save(ZoneEditing editing)
        {
            if (!editing.HasZoneData)
            {
                Debug.LogError(
                    $"This scene has no zone data to save; open the zone again (Comet › Zones › Open…). Its files in {editing.folder} are unchanged."
                );
                return false;
            }

            if (editing.IsGroup)
            {
                if (!ZoneGroups.Save(editing))
                {
                    return false;
                }

                Saved?.Invoke(editing);
                return true;
            }

            if (!Write(Gather(editing), ProjectPath(editing.folder)))
            {
                return false;
            }

            Saved?.Invoke(editing);
            return true;
        }

        private static bool Write(Zone zone, string folder)
        {
            var errors = ZoneValidation.Check(zone);
            var library = CometSettings.Find(quiet: true)?.props;
            if (library != null)
            {
                errors.AddRange(ZoneValidation.CheckPlacements(zone, id => PropFor(library, id), ZoneGroups.Find));
            }

            if (errors.Count > 0)
            {
                Report($"Didn't save {zone.Id}", errors);
                return false;
            }

            ZoneFolder.Write(folder, zone);
            Debug.Log($"Saved {zone.Id} to {RelativeToProject(folder)}.");
            return true;
        }

        public static ZoneSign AddSign(ZoneTrigger trigger)
        {
            var sign = Child(trigger.transform, $"{trigger.id} sign").gameObject.AddComponent<ZoneSign>();
            sign.transform.localPosition = Vector3.up * (trigger.size.y + TriggerSignLift);
            sign.text = "Sign";
            sign.arrow = true;
            Undo.RegisterCreatedObjectUndo(sign.gameObject, "Add Sign");
            return sign;
        }

        private const float TriggerSignLift = 0.5f;

        public static ZoneSign AddSign(ZoneEditing editing, Vector3 position)
        {
            var parent = editing.transform.Find(SignsFolder) ?? Child(editing.transform, SignsFolder);
            var sign = Child(parent, "Sign").gameObject.AddComponent<ZoneSign>();
            sign.transform.position = position;
            sign.text = "Sign";
            Undo.RegisterCreatedObjectUndo(sign.gameObject, "Add sign");
            return sign;
        }

        internal static Prop? PropFor(PropLibrary library, string id)
        {
            if (!library.TryGet(id, out var entry))
            {
                return null;
            }

            return new Prop
            {
                Id = id,
                UniformScale = entry.uniformScale,
                BoundsMin = ToNumerics(entry.bounds.min),
                BoundsMax = ToNumerics(entry.bounds.max),
            };
        }

        public static GameObject Place(ZoneEditing editing, string placeable, Vector3 position, CometSettings settings, Color? color = null)
        {
            var parent =
                ZoneGroups.InPlace is { } inPlace && inPlace.GetComponentInParent<ZoneEditing>(true) == editing
                    ? inPlace.transform
                    : editing.transform.Find("Placed") ?? Child(editing.transform, "Placed");
            GameObject thing;
            ZonePlacement placement;
            if (PrimitiveNamed(placeable) is { } kind)
            {
                var (primitive, size) = Starting(kind);
                thing = CreatePrimitive(primitive, size, settings, parent);
                placement = thing.GetComponent<ZonePlacement>();
            }
            else if (placeable.StartsWith("group.", StringComparison.Ordinal))
            {
                thing = ZoneGroups.CreateCopy(placeable, parent);
                thing.transform.position = position;
                Undo.RegisterCreatedObjectUndo(thing, $"Place {placeable}");
                return thing;
            }
            else
            {
                thing = Instantiate(placeable, settings, parent);
                placement = thing.AddComponent<ZonePlacement>();
                placement.prop = placeable;
            }

            thing.transform.position = position;
            SetColors(placement, settings, color is { } c ? Conversions.ToRgb(c) : (uint?)null);
            Undo.RegisterCreatedObjectUndo(thing, $"Place {placeable}");
            return thing;
        }

        public static T AddTrigger<T>(ZoneEditing editing, string id, Vector3 position, CometSettings settings)
            where T : ZoneTrigger
        {
            var parent = editing.transform.Find(TriggersFolder<T>()) ?? Child(editing.transform, TriggersFolder<T>());
            var point = Child(parent, id);
            point.position = position;
            var trigger = point.gameObject.AddComponent<T>();
            trigger.id = id;
            Undo.RegisterCreatedObjectUndo(point.gameObject, $"Add {id}");
            var (padKind, padSize, standSeconds, height) = trigger switch
            {
                ZoneSpawn => (settings.spawnPad, settings.spawnPadSize, settings.spawnStandSeconds, settings.spawnVolumeHeight),
                ZoneTeleporter => (
                    settings.teleporterPad,
                    settings.teleporterPadSize,
                    settings.teleporterStandSeconds,
                    settings.teleporterVolumeHeight
                ),
                _ => ("", Vector3.zero, settings.triggerStandSeconds, settings.triggerVolumeHeight),
            };
            trigger.standSeconds = standSeconds;
            trigger.size.y = height;
            if (trigger is ZoneSpawn)
            {
                trigger.volume = ZoneTrigger.VolumeKind.Cylinder;
            }

            if (trigger is ZoneTeleporter teleporter)
            {
                teleporter.chargeColor = settings.teleporterChargeColor;
            }
            else if (trigger is ZoneToggleTrigger toggle)
            {
                toggle.chargeColor = settings.triggerChargeColor;
            }

            if (PrimitiveNamed(padKind) is { } kind)
            {
                var (primitive, _) = Starting(kind);
                var pad = CreatePlaced(
                    new Placement
                    {
                        Primitive = primitive,
                        Position = ToNumerics(position - Vector3.up * (padSize.y / 2)),
                        Scale = ToNumerics(padSize),
                        Color = trigger is ZoneTeleporter ? Conversions.ToRgb(settings.teleporterPadColor) : (uint?)null,
                    },
                    point,
                    settings,
                    PadColor(trigger, settings)
                );
                pad.name = $"{id} pad";
                Undo.RegisterCreatedObjectUndo(pad, $"Add {id}");
                FitToPad(trigger);
            }

            return trigger;
        }

        public static void FitToPad(ZoneTrigger trigger)
        {
            if (trigger.Pad is not { } pad)
            {
                return;
            }

            var padTransform = pad.transform;
            var (position, rotation) = (padTransform.position, padTransform.rotation);
            var size = pad.isPrimitive ? Vector3.Scale(pad.size, padTransform.lossyScale) : padTransform.lossyScale;
            Undo.RecordObject(trigger, "Fit Trigger to Pad");
            Undo.RecordObject(trigger.transform, "Fit Trigger to Pad");
            Undo.RecordObject(padTransform, "Fit Trigger to Pad");
            trigger.transform.SetPositionAndRotation(position + Vector3.up * size.y, Quaternion.Euler(0, rotation.eulerAngles.y, 0));
            padTransform.SetPositionAndRotation(position, rotation);
            trigger.size = new Vector3(size.x, trigger.size.y, size.z);
            trigger.volume =
                pad.isPrimitive && pad.kind is PrimitiveKind.Cylinder or PrimitiveKind.Cone or PrimitiveKind.Sphere
                    ? ZoneTrigger.VolumeKind.Cylinder
                    : ZoneTrigger.VolumeKind.Box;
        }

        internal static void Report(string title, List<ZoneFileError> errors)
        {
            foreach (var error in errors)
            {
                Debug.LogError($"{title}: {error}");
            }

            var shown = string.Join("\n", errors.Take(8).Select(e => e.ToString()));
            EditorUtility.DisplayDialog(title, errors.Count > 8 ? $"{shown}\n…and {errors.Count - 8} more (see the Console)." : shown, "OK");
        }

        public static void RedrawIfMissing(ZoneEditing editing)
        {
            var settings = CometSettings.Find(quiet: true);
            var terrain = editing.transform.Find(TerrainName);
            var drawn =
                terrain != null
                && terrain.Cast<Transform>().All(c => c.GetComponent<MeshFilter>() is { } f && f.sharedMesh != null && f.sharedMesh.vertexCount > 0);
            if (settings == null || !editing.HasZoneData || drawn)
            {
                return;
            }

            ZoneTerrainEditing.Forget();
            RebuildTerrain(editing, MessagePackSerializer.Deserialize<Zone>(editing.ZoneData, Serializer), settings);
        }

        private static void SetWorldScale(Transform t, Vector3 scale)
        {
            var parent = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(scale.x / parent.x, scale.y / parent.y, scale.z / parent.z);
        }

        internal static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath)!;

        internal static string ProjectPath(string relative) => Path.GetFullPath(Path.Combine(ProjectRoot, relative));

        internal static string RelativeToProject(string path) => Path.GetRelativePath(ProjectRoot, path).Replace('\\', '/');

        private static System.Numerics.Vector3 Angles(Vector3 euler) => new System.Numerics.Vector3(Angle(euler.x), Angle(euler.y), Angle(euler.z));

        private static float Angle(float degrees)
        {
            degrees %= 360;
            return degrees > 180 ? degrees - 360
                : degrees <= -180 ? degrees + 360
                : degrees;
        }
    }
}

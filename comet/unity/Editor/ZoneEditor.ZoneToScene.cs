using System.Collections.Generic;
using System.Linq;
using Comet.Content;
using Comet.Simulation;
using UnityEditor;
using UnityEngine;
using static Comet.Unity.Conversions;

namespace Comet.Unity.Editor
{
    public static partial class ZoneEditor
    {
        public static void RebuildTerrain(ZoneEditing editing, Zone zone, CometSettings settings, ISet<ChunkCoord>? only = null)
        {
            var root = editing.transform;
            var terrain = root.Find(TerrainName);
            if (terrain == null)
            {
                terrain = Child(root, TerrainName);
                terrain.SetAsFirstSibling();
                terrain.gameObject.hideFlags = HideFlags.NotEditable | HideFlags.DontSaveInEditor;
                only = null;
            }

            var zoneTerrain = new ZoneTerrain(zone);
            var bottoms = zoneTerrain.Pieces.Select(p => p.BottomHeight).ToArray();
            if (!DrawnBottoms.TryGetValue(editing, out var drawn) || !drawn.SequenceEqual(bottoms))
            {
                only = null;
            }

            DrawnBottoms.AddOrUpdate(editing, bottoms);
            var meshes = TerrainMesher.Build(zoneTerrain, ToRgb(settings.groundColor), ToRgb(settings.underColor), only);

            var keep = new HashSet<string>(meshes.Keys.Select(ChunkName));
            var redrawn = only?.Select(ChunkName).ToHashSet();
            foreach (var chunk in terrain.Cast<Transform>().ToList())
            {
                if (!keep.Contains(chunk.name) && (redrawn == null || redrawn.Contains(chunk.name)))
                {
                    if (chunk.GetComponent<MeshFilter>() is { } filter)
                    {
                        UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
                    }

                    UnityEngine.Object.DestroyImmediate(chunk.gameObject);
                }
            }

            foreach (var mesh in meshes)
            {
                var name = ChunkName(mesh.Key);
                var chunk = terrain.Find(name);
                if (chunk == null)
                {
                    chunk = Child(terrain, name);
                    chunk.gameObject.hideFlags = HideFlags.NotEditable | HideFlags.DontSaveInEditor;
                    chunk.localPosition = new Vector3(mesh.Key.Origin.X, mesh.Key.Origin.Y, mesh.Key.Origin.Z);
                    chunk.gameObject.AddComponent<MeshFilter>();
                    chunk.gameObject.AddComponent<MeshRenderer>();
                }

                // Made here too for a chunk whose mesh was lost (it's in no asset), as a script reload can.
                var filter = chunk.GetComponent<MeshFilter>();
                if (filter.sharedMesh == null)
                {
                    filter.sharedMesh = new Mesh { name = name };
                }

                chunk.GetComponent<MeshRenderer>().sharedMaterial = settings.terrainMaterial;
                TerrainMeshes.Fill(chunk.GetComponent<MeshFilter>().sharedMesh, mesh.Value);
            }
        }

        private static string ChunkName(ChunkCoord coord) => $"Chunk {coord}";

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ZoneEditing, float[]> DrawnBottoms =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ZoneEditing, float[]>();

        private const string TerrainName = "Terrain (preview)";

        private static void Build(Transform root, Zone zone, CometSettings settings)
        {
            RebuildTerrain(root.GetComponent<ZoneEditing>(), zone, settings);
            var placed = Child(root, "Placed");
            var volumes = Child(root, "Volumes");
            foreach (var chunk in zone.Chunks)
            {
                foreach (var placement in chunk.Placements)
                {
                    CreatePlaced(placement, placed, settings);
                }

                foreach (var spawn in chunk.Spawns)
                {
                    var component = CreateTrigger<ZoneSpawn>(root, spawn.Id, spawn.Volume, settings);
                    component.facing = (ZoneTrigger.FacingMode)spawn.Facing;
                }

                foreach (var teleporter in chunk.Teleporters)
                {
                    var component = CreateTrigger<ZoneTeleporter>(root, teleporter.Id, teleporter.Volume, settings);
                    component.facing = (ZoneTrigger.FacingMode)teleporter.Facing;
                    component.target = teleporter.Target;
                    component.chargeColor = teleporter.ChargeColor is { } color ? Conversions.FromRgb(color) : settings.teleporterChargeColor;
                }

                foreach (var trigger in chunk.Triggers)
                {
                    var component = CreateTrigger<ZoneToggleTrigger>(root, trigger.Id, trigger.Volume, settings);
                    component.effects = trigger
                        .Effects.Select(e => new ZoneToggleTrigger.Effect { toggleTag = e.Tag, revertSeconds = e.RevertSeconds })
                        .ToList();
                    component.chargeColor = trigger.ChargeColor is { } color ? Conversions.FromRgb(color) : settings.triggerChargeColor;
                }

                foreach (var sign in chunk.Signs)
                {
                    var component = Child(root.Find(SignsFolder) ?? Child(root, SignsFolder), SignName(sign.Text))
                        .gameObject.AddComponent<ZoneSign>();
                    component.transform.position = ToUnity(sign.Position);
                    Fill(component, sign);
                }

                foreach (var volume in chunk.Volumes)
                {
                    var box = Child(volumes, $"{volume.Kind} volume");
                    var r = volume.Rotation;
                    box.SetPositionAndRotation(ToUnity(volume.Position), Quaternion.Euler(r.X, r.Y, r.Z));
                    var component = box.gameObject.AddComponent<ZoneVolume>();
                    component.kind = (ZoneVolume.VolumeKind)volume.Kind;
                    component.size = ToUnity(volume.Size);
                }
            }
        }

        private const string SignsFolder = "Signs";

        private static string SignName(string text)
        {
            var line = text.Split('\n')[0].Trim();
            return "Sign " + (line.Length > 24 ? line.Substring(0, 24) + "…" : line);
        }

        private static void Fill(ZoneSign component, Sign sign)
        {
            component.text = sign.Text;
            component.textColor = Conversions.FromRgb(sign.TextColor);
            component.background = sign.Background;
            component.backgroundColor = Conversions.FromRgb(sign.BackgroundColor);
            component.backgroundOpacity = sign.BackgroundOpacity;
            component.showDistance = sign.ShowDistance;
            component.screenSized = sign.ScreenSized;
            component.fontSize = sign.FontSize;
            component.textHeight = sign.TextHeight;
            component.arrow = sign.Arrow;
            component.gameObject.SetActive(!sign.Off);
        }

        private static string TriggersFolder<T>()
            where T : ZoneTrigger =>
            typeof(T) == typeof(ZoneSpawn) ? "Spawns"
            : typeof(T) == typeof(ZoneTeleporter) ? "Teleporters"
            : "Triggers";

        private static T CreateTrigger<T>(Transform root, string id, TriggerVolume volume, CometSettings settings)
            where T : ZoneTrigger
        {
            var parent = root.Find(TriggersFolder<T>()) ?? Child(root, TriggersFolder<T>());
            var point = Child(parent, id);
            point.SetPositionAndRotation(ToUnity(volume.Position), Quaternion.Euler(0, volume.Yaw, 0));
            var component = point.gameObject.AddComponent<T>();
            component.id = id;
            component.volume = (ZoneTrigger.VolumeKind)System.Enum.Parse(typeof(ZoneTrigger.VolumeKind), volume.Kind.ToString());
            component.size = ToUnity(volume.Size);
            component.standSeconds = volume.StandSeconds;
            component.rearm = (ZoneTrigger.RearmMode)volume.Rearm;
            if (volume.Pad is { } pad)
            {
                var thing = CreatePlaced(pad, point, settings, PadColor(component, settings));
                thing.name = $"{id} pad";
            }

            if (volume.Sign is { } sign)
            {
                var child = Child(point, $"{id} sign").gameObject.AddComponent<ZoneSign>();
                child.transform.localPosition = ToUnity(sign.Position);
                Fill(child, sign);
            }

            return component;
        }

        private static Color? PadColor(ZoneTrigger trigger, CometSettings settings) => trigger is ZoneSpawn ? settings.spawnPadColor : (Color?)null;

        internal static GameObject CreatePlaced(Placement placement, Transform parent, CometSettings settings, Color? defaultColor = null)
        {
            var thing =
                placement.Primitive is { } primitive ? CreatePrimitive(primitive, ToUnity(placement.Scale), settings, parent)
                : placement.Group is { } group ? ZoneGroups.CreateCopy(group, parent)
                : Instantiate(placement.Prop, settings, parent);
            var r = placement.Rotation;
            thing.transform.SetPositionAndRotation(ToUnity(placement.Position), Quaternion.Euler(r.X, r.Y, r.Z));
            if (placement.Primitive is null)
            {
                SetWorldScale(thing.transform, ToUnity(placement.Scale));
            }

            var marker = thing.GetComponent<ZonePlacement>() ?? thing.AddComponent<ZonePlacement>();
            marker.prop = placement.Prop;
            marker.placementTag = placement.Tag ?? "";
            marker.blockout = placement.Blockout;
            marker.role = placement.Role ?? "";
            marker.roleColors =
                placement
                    .Colors?.OrderBy(c => c.Key, System.StringComparer.Ordinal)
                    .Select(c => new ZonePlacement.RoleColor { role = c.Key, color = Conversions.FromRgb(c.Value) })
                    .ToList()
                ?? new List<ZonePlacement.RoleColor>();
            SetColors(marker, settings, placement.Color, defaultColor);
            thing.SetActive(!placement.Off);
            return thing;
        }

        internal static void SetColors(ZonePlacement placement, CometSettings settings, uint? own, Color? defaultColor = null)
        {
            Color? prop =
                !placement.isPrimitive && settings.props != null && settings.props.TryGet(placement.prop, out var entry) && entry.hasColor
                    ? entry.color
                    : null;
            placement.ownColor = own != null;
            if (own is { } rgb)
            {
                placement.color = Conversions.FromRgb(rgb);
            }

            placement.SetDefaultColor(defaultColor ?? prop ?? settings.placedColor);
        }

        public static PrimitiveKind? PrimitiveNamed(string name) => PrimitiveKinds.Named(name);

        public static (Primitive Primitive, Vector3 Size) Starting(PrimitiveKind kind) =>
            kind switch
            {
                PrimitiveKind.Wedge => (new Primitive { Kind = kind }, new Vector3(2, 1, 2)),
                PrimitiveKind.Stairs => (new Primitive { Kind = kind }, new Vector3(2, 1, 1.5f)),
                PrimitiveKind.Wall => (new Primitive { Kind = kind }, new Vector3(4, 3, 0.3f)),
                PrimitiveKind.Doorway => (
                    new Primitive
                    {
                        Kind = kind,
                        OpeningWidth = 1.2f,
                        OpeningHeight = 2.2f,
                    },
                    new Vector3(4, 3, 0.3f)
                ),
                PrimitiveKind.Arch => (new Primitive { Kind = kind, Thickness = 0.4f }, new Vector3(3, 3, 0.4f)),
                PrimitiveKind.Tetrahedron or PrimitiveKind.Octahedron or PrimitiveKind.Dodecahedron or PrimitiveKind.Icosahedron => (
                    new Primitive { Kind = kind },
                    Vector3.one
                ),
                _ => (new Primitive { Kind = kind }, new Vector3(2, 2, 2)),
            };

        private static GameObject CreatePrimitive(Primitive primitive, Vector3 size, CometSettings settings, Transform parent)
        {
            var name = PrimitiveKinds.Name(primitive.Kind);
            var thing = new GameObject();
            thing.transform.SetParent(parent, false);
            thing.name = GameObjectUtility.GetUniqueNameForSibling(parent, name);
            thing.AddComponent<MeshFilter>();
            thing.AddComponent<MeshRenderer>().sharedMaterial =
                settings.placedMaterial != null ? settings.placedMaterial : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            thing.AddComponent<MeshCollider>();
            var placement = thing.AddComponent<ZonePlacement>();
            placement.isPrimitive = true;
            placement.kind = primitive.Kind;
            placement.size = size;
            placement.sides = primitive.Sides ?? 0;
            placement.bevel = primitive.Bevel;
            placement.steps = primitive.Steps;
            placement.opening = new Vector2(primitive.OpeningWidth, primitive.OpeningHeight);
            placement.thickness = primitive.Thickness;
            placement.Rebuild();
            return thing;
        }

        private static GameObject Instantiate(string prop, CometSettings settings, Transform parent)
        {
            var model = settings.props != null ? settings.props.Model(prop) : null;
            GameObject thing;
            if (model != null)
            {
                thing = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            }
            else
            {
                Debug.LogWarning($"No model for {prop} in the prop library; showing a stand-in cube.");
                thing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                thing.transform.SetParent(parent, false);
            }

            thing.name = "";
            thing.name = GameObjectUtility.GetUniqueNameForSibling(parent, prop);
            PlacedProps.HideCollision(thing);
            if (settings.placedMaterial != null)
            {
                PlacedProps.SetMaterial(thing, settings.placedMaterial);
            }

            foreach (var renderer in thing.GetComponentsInChildren<Renderer>())
            {
                // Lets Unity's surface snapping land other objects on it.
                if (
                    !PlacedProps.IsCollision(renderer)
                    && renderer.TryGetComponent<MeshFilter>(out var filter)
                    && !renderer.TryGetComponent<Collider>(out _)
                )
                {
                    renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                }
            }

            return thing;
        }
    }
}

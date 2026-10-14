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

namespace Comet.Unity.Editor
{
    /// <summary>
    /// Opens a zone into a working scene named after it and saves it back to its files (Comet › Zones, or saving the
    /// scene: <see cref="ZoneEditorScenes"/>). The scene is never the source of truth: the zone's files are, and the
    /// scene file sits in an ignored folder. Terrain shows as preview meshes; placed assets, spawns and volumes are
    /// objects to move with Unity's own tools, and on saving each belongs to the chunk holding its pivot. A spawn's
    /// marker is a child of its spawn point, so they move together.
    /// </summary>
    public static class ZoneEditor
    {
        private static readonly MessagePackSerializerOptions Serializer = ContentSerializer.CreateOptions(MessagePack.Resolvers.StandardResolver.Instance);

        [MenuItem("Comet/Zones/Open…", priority = 1)]
        public static void OpenMenu()
        {
            var settings = CometSettings.Find(quiet: false);
            if (settings == null)
            {
                return;
            }

            var picked = EditorUtility.OpenFolderPanel("Open zone", ProjectPath(settings.zonesFolder), "");
            if (string.IsNullOrEmpty(picked) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Open(picked, settings);
        }

        /// <summary>Raised after a zone's files are saved, so a game can rebuild its content from them.</summary>
        public static event Action<ZoneEditing>? Saved;

        // Saving the scene saves its zones too (ZoneEditorScenes).
        [MenuItem("Comet/Zones/Save", priority = 2)]
        public static void SaveMenu()
        {
            foreach (var scene in UnityEngine.Object.FindObjectsByType<ZoneEditing>().Select(z => z.gameObject.scene).Distinct())
            {
                EditorSceneManager.SaveScene(scene);
            }
        }

        [MenuItem("Comet/Zones/Save", true)]
        private static bool CanSave() => UnityEngine.Object.FindAnyObjectByType<ZoneEditing>() != null;

        /// <summary>Opens the zone in <paramref name="folder"/> into a new scene; false (with the reasons logged) if its files have errors.</summary>
        public static bool Open(string folder, CometSettings settings)
        {
            var errors = new List<ZoneFileError>();
            var zone = ZoneFolder.Read(folder, errors);
            if (errors.Count > 0)
            {
                Report($"Can't open {folder}", errors);
                return false;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            ZoneTerrainEditing.Forget();

            // A new scene unloads assets nothing in it uses, the settings and asset library among them: load them again.
            settings = CometSettings.Find(quiet: true) ?? settings;
            var root = new GameObject($"Zone {zone.Id}");
            var editing = root.AddComponent<ZoneEditing>();
            editing.folder = RelativeToProject(folder);
            Build(root.transform, zone, settings);

            // Keep everything but the objects, which the scene holds now.
            foreach (var chunk in zone.Chunks)
            {
                chunk.Placements = new Placement[0];
                chunk.Spawns = new SpawnPoint[0];
                chunk.Volumes = new CollisionVolume[0];
            }

            editing.ZoneData = MessagePackSerializer.Serialize(zone, Serializer);

            // Saved at once into the ignored scenes folder, so the scene has the zone's name and Unity's Save saves the zone.
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
            return true;
        }

        /// <summary>Writes an opened zone back to its files; false (with the reasons logged) if it doesn't validate.</summary>
        public static bool Save(ZoneEditing editing)
        {
            var zone = Gather(editing);
            var errors = ZoneValidation.Check(zone);
            var library = CometSettings.Find(quiet: true)?.assets;
            if (library != null)
            {
                errors.AddRange(ZoneValidation.CheckPlacements(zone, key => AssetFor(library, key)));
            }

            if (errors.Count > 0)
            {
                Report($"Didn't save {zone.Id}", errors);
                return false;
            }

            ZoneFolder.Write(ProjectPath(editing.folder), zone);
            Debug.Log($"Saved {zone.Id} to {editing.folder}.");
            Saved?.Invoke(editing);
            return true;
        }

        /// <summary>The zone as the scene has it now: its stored parts plus the objects under <paramref name="editing"/>.</summary>
        public static Zone Gather(ZoneEditing editing)
        {
            var zone = MessagePackSerializer.Deserialize<Zone>(editing.ZoneData, Serializer);
            var chunks = zone.Chunks.ToDictionary(c => c.Coord);
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

            foreach (var placed in editing.GetComponentsInChildren<ZonePlacement>())
            {
                var t = placed.transform;
                var chunk = ChunkAt(ToNumerics(t.position));
                chunk.Placements = chunk.Placements.Append(new Placement
                {
                    Asset = placed.asset,
                    // A spawn's marker is named with its key, which may have changed since.
                    Name = placed.GetComponentInParent<ZoneSpawn>() is { } spawn ? spawn.key
                        : string.IsNullOrEmpty(placed.placementName) ? null : placed.placementName,
                    Position = ToNumerics(t.position),
                    Rotation = Angles(t.rotation.eulerAngles),
                    Scale = ToNumerics(t.lossyScale),
                    Color = placed.ownColor ? PlacedColors.ToRgb(placed.color) : (uint?)null,
                }).ToArray();
            }

            foreach (var spawn in editing.GetComponentsInChildren<ZoneSpawn>())
            {
                var t = spawn.transform;
                var chunk = ChunkAt(ToNumerics(t.position));
                chunk.Spawns = chunk.Spawns.Append(new SpawnPoint
                {
                    Id = spawn.key,
                    Position = ToNumerics(t.position),
                    Radius = spawn.radius,
                    Facing = (SpawnFacing)spawn.facing,
                    Yaw = Angle(t.rotation.eulerAngles.y),
                    IsDefault = spawn.isDefault,
                }).ToArray();
            }

            foreach (var volume in editing.GetComponentsInChildren<ZoneVolume>())
            {
                var t = volume.transform;
                var chunk = ChunkAt(ToNumerics(t.position));
                chunk.Volumes = chunk.Volumes.Append(new CollisionVolume
                {
                    Kind = (VolumeKind)volume.kind,
                    Position = ToNumerics(t.position),
                    Rotation = Angles(t.rotation.eulerAngles),
                    Size = ToNumerics(volume.size),
                }).ToArray();
            }

            // Tiles are kept whole-chunk while editing; the files keep only their ground.
            ZoneTerrainEditing.Crop(zone);

            // A chunk left with nothing in it is no longer part of the zone.
            zone.Chunks = chunks.Values
                .Where(c => c.Tiles.Length + c.Placements.Length + c.Spawns.Length + c.Volumes.Length > 0)
                .OrderBy(c => c.Coord.X).ThenBy(c => c.Coord.Z)
                .ToArray();
            return zone;
        }

        /// <summary>Redraws a zone's terrain preview from <paramref name="zone"/>'s pieces.</summary>
        public static void RebuildTerrain(ZoneEditing editing, Zone zone, CometSettings settings)
        {
            var root = editing.transform;
            var old = root.Find(TerrainName);
            if (old != null)
            {
                foreach (var filter in old.GetComponentsInChildren<MeshFilter>())
                {
                    UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
                }

                UnityEngine.Object.DestroyImmediate(old.gameObject);
            }

            var terrain = Child(root, TerrainName);
            terrain.SetAsFirstSibling();
            terrain.gameObject.hideFlags = HideFlags.NotEditable | HideFlags.DontSaveInEditor;
            foreach (var chunk in TerrainMesher.Build(new ZoneTerrain(zone), Rgb(settings.groundColor), Rgb(settings.underColor)))
            {
                var name = $"Chunk {chunk.Key}";
                var drawn = Child(terrain, name);
                drawn.gameObject.hideFlags = HideFlags.NotEditable | HideFlags.DontSaveInEditor;
                drawn.localPosition = new Vector3(chunk.Key.Origin.X, chunk.Key.Origin.Y, chunk.Key.Origin.Z);
                drawn.gameObject.AddComponent<MeshFilter>().sharedMesh = TerrainMeshes.Build(name, chunk.Value);
                drawn.gameObject.AddComponent<MeshRenderer>().sharedMaterial = settings.terrainMaterial;
            }
        }

        private const string TerrainName = "Terrain (preview)";

        private static void Build(Transform root, Zone zone, CometSettings settings)
        {
            RebuildTerrain(root.GetComponent<ZoneEditing>(), zone, settings);
            var placed = Child(root, "Placed");
            var spawns = Child(root, "Spawns");
            var volumes = Child(root, "Volumes");
            var spawnsByKey = new Dictionary<string, Transform>();
            foreach (var spawn in zone.Chunks.SelectMany(c => c.Spawns))
            {
                var point = Child(spawns, spawn.Id);
                point.SetPositionAndRotation(ToUnity(spawn.Position), Quaternion.Euler(0, spawn.Yaw, 0));
                var component = point.gameObject.AddComponent<ZoneSpawn>();
                component.key = spawn.Id;
                component.radius = spawn.Radius;
                component.facing = (ZoneSpawn.FacingMode)spawn.Facing;
                component.isDefault = spawn.IsDefault;
                spawnsByKey[spawn.Id] = point;
            }

            foreach (var chunk in zone.Chunks)
            {
                foreach (var placement in chunk.Placements)
                {
                    // A placement named with a spawn's key is its marker, kept under it.
                    var isMarker = placement.Name != null && spawnsByKey.ContainsKey(placement.Name);
                    var thing = Instantiate(placement.Asset, settings, isMarker ? spawnsByKey[placement.Name!] : placed);
                    var r = placement.Rotation;
                    thing.transform.SetPositionAndRotation(ToUnity(placement.Position), Quaternion.Euler(r.X, r.Y, r.Z));
                    SetWorldScale(thing.transform, ToUnity(placement.Scale));
                    if (isMarker)
                    {
                        thing.name = $"{placement.Name} marker";
                    }

                    var marker = thing.AddComponent<ZonePlacement>();
                    marker.asset = placement.Asset;
                    marker.placementName = placement.Name ?? "";
                    SetColors(marker, settings, placement.Color);
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

        // A placement's colors: its asset's, from the library, and its own if it has one.
        private static void SetColors(ZonePlacement placement, CometSettings settings, uint? own)
        {
            Color? asset = settings.assets != null && settings.assets.TryGet(placement.asset, out var entry) && entry.hasColor ? entry.color : null;
            placement.ownColor = own != null;
            if (own is { } rgb)
            {
                placement.color = PlacedColors.FromRgb(rgb);
            }

            placement.SetDefaultColor(asset ?? settings.placedColor);
        }

        // The asset's model, linked to it, with its collision objects hidden; a stand-in cube if the library has none.
        private static GameObject Instantiate(string asset, CometSettings settings, Transform parent)
        {
            var model = settings.assets != null ? settings.assets.Model(asset) : null;
            GameObject thing;
            if (model != null)
            {
                thing = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            }
            else
            {
                Debug.LogWarning($"No model for {asset} in the asset library; showing a stand-in cube.");
                thing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                thing.transform.SetParent(parent, false);
            }

            // Numbered like Unity's own copies (asset.cube, asset.cube (1), ...); saving ignores names.
            thing.name = "";
            thing.name = GameObjectUtility.GetUniqueNameForSibling(parent, asset);
            foreach (var renderer in thing.GetComponentsInChildren<Renderer>())
            {
                if (renderer.name.EndsWith(Asset.CollisionSuffix, StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                }
                else if (settings.placedMaterial != null)
                {
                    renderer.sharedMaterial = settings.placedMaterial;
                }
            }

            return thing;
        }

        // What the placement checks need to know of an asset, from the library: its scale rule and bounds.
        private static Asset? AssetFor(AssetLibrary library, string key)
        {
            if (!library.TryGet(key, out var entry))
            {
                return null;
            }

            return new Asset
            {
                Id = key,
                UniformScale = entry.uniformScale,
                BoundsMin = ToNumerics(entry.bounds.min),
                BoundsMax = ToNumerics(entry.bounds.max),
            };
        }

        /// <summary>Places an asset at <paramref name="position"/> in an opened zone, as a linked model with its placement marker.</summary>
        public static GameObject Place(ZoneEditing editing, string asset, Vector3 position, CometSettings settings)
        {
            var parent = editing.transform.Find("Placed") ?? Child(editing.transform, "Placed");
            var thing = Instantiate(asset, settings, parent);
            thing.transform.position = position;
            var placement = thing.AddComponent<ZonePlacement>();
            placement.asset = asset;
            SetColors(placement, settings, null);
            Undo.RegisterCreatedObjectUndo(thing, $"Place {asset}");
            return thing;
        }

        /// <summary>
        /// Adds a spawn point at <paramref name="position"/> in an opened zone (the zone's first is its default), with
        /// its marker if the settings name one: the marker asset, flattened, sized to the spawn's radius and named with its key.
        /// </summary>
        public static GameObject AddSpawn(ZoneEditing editing, string key, Vector3 position, CometSettings settings)
        {
            var parent = editing.transform.Find("Spawns") ?? Child(editing.transform, "Spawns");
            var marker = Child(parent, key);
            marker.position = position;
            var spawn = marker.gameObject.AddComponent<ZoneSpawn>();
            spawn.key = key;
            spawn.isDefault = editing.GetComponentsInChildren<ZoneSpawn>().Length == 1;
            Undo.RegisterCreatedObjectUndo(marker.gameObject, $"Add {key}");
            if (!string.IsNullOrEmpty(settings.spawnMarkerAsset))
            {
                var placed = Place(editing, settings.spawnMarkerAsset, position, settings);
                var bounds = settings.assets != null && settings.assets.TryGet(settings.spawnMarkerAsset, out var entry) ? entry.bounds : new Bounds(Vector3.zero, Vector3.one);
                var size = bounds.size;
                placed.transform.localScale = new Vector3(2 * spawn.radius / Mathf.Max(size.x, 0.01f), settings.spawnMarkerHeight / Mathf.Max(size.y, 0.01f), 2 * spawn.radius / Mathf.Max(size.z, 0.01f));

                // The marker is sunk halfway into the ground and players arrive on its top.
                var scaleY = placed.transform.localScale.y;
                placed.transform.position = position - Vector3.up * (bounds.center.y * scaleY);
                marker.position = position + Vector3.up * (bounds.extents.y * scaleY);
                placed.GetComponent<ZonePlacement>().placementName = key;
                placed.name = $"{key} marker";
                Undo.SetTransformParent(placed.transform, marker, $"Add {key}");
            }

            return marker.gameObject;
        }

        private static void Report(string title, List<ZoneFileError> errors)
        {
            foreach (var error in errors)
            {
                Debug.LogError($"{title}: {error}");
            }

            var shown = string.Join("\n", errors.Take(8).Select(e => e.ToString()));
            EditorUtility.DisplayDialog(title, errors.Count > 8 ? $"{shown}\n…and {errors.Count - 8} more (see the Console)." : shown, "OK");
        }

        /// <summary>Draws an opened zone's terrain preview again if it's missing (it isn't saved with the scene).</summary>
        public static void RedrawIfMissing(ZoneEditing editing)
        {
            var settings = CometSettings.Find(quiet: true);
            if (settings == null || !editing.HasZoneData || editing.transform.Find(TerrainName) != null)
            {
                return;
            }

            ZoneTerrainEditing.Forget();
            RebuildTerrain(editing, MessagePackSerializer.Deserialize<Zone>(editing.ZoneData, Serializer), settings);
        }

        // Scale in world space; the same as localScale under an unscaled, unrotated parent.
        private static void SetWorldScale(Transform t, Vector3 scale)
        {
            var parent = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(scale.x / parent.x, scale.y / parent.y, scale.z / parent.z);
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath)!;

        private static string ProjectPath(string relative) => Path.GetFullPath(Path.Combine(ProjectRoot, relative));

        private static string RelativeToProject(string path) => Path.GetRelativePath(ProjectRoot, path).Replace('\\', '/');

        // Euler angles in (-180, 180], so a placement turned a hair below zero saves as -0.01 rather than 359.99.
        private static System.Numerics.Vector3 Angles(Vector3 euler) => new System.Numerics.Vector3(Angle(euler.x), Angle(euler.y), Angle(euler.z));

        private static float Angle(float degrees)
        {
            degrees %= 360;
            return degrees > 180 ? degrees - 360 : degrees <= -180 ? degrees + 360 : degrees;
        }

        private static uint Rgb(Color color)
        {
            Color32 c = color;
            return (uint)(c.r << 16 | c.g << 8 | c.b);
        }

        private static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        private static System.Numerics.Vector3 ToNumerics(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);
    }
}

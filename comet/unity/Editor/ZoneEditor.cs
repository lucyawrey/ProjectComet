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
        internal static readonly MessagePackSerializerOptions Serializer = ContentSerializer.CreateOptions(MessagePack.Resolvers.StandardResolver.Instance);

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

        /// <summary>Drops the opened zone's (or group's) unsaved changes, reading it again from its files.</summary>
        [MenuItem("Comet/Zones/Discard Changes", priority = 3)]
        public static void DiscardMenu()
        {
            var editing = UnityEngine.Object.FindAnyObjectByType<ZoneEditing>();
            var settings = CometSettings.Find(quiet: false);
            if (editing == null || settings == null)
            {
                return;
            }

            var what = editing.IsGroup ? editing.groupKey : Path.GetFileName(editing.folder);
            if (!EditorUtility.DisplayDialog("Discard changes", $"Discard every change to {what} since it was last saved, and read it again from its files? This can't be undone.", "Discard", "Keep"))
            {
                return;
            }

            if (editing.IsGroup)
            {
                ZoneGroups.Open(editing.groupKey, settings);
            }
            else
            {
                Open(ProjectPath(editing.folder), settings);
            }
        }

        [MenuItem("Comet/Zones/Discard Changes", true)]
        private static bool CanDiscard() => UnityEngine.Object.FindAnyObjectByType<ZoneEditing>() != null;

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
            ZonePlacement.Budget = settings.Budget;
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
            if (!editing.HasZoneData)
            {
                // A working scene saved by an older editor, which kept the zone data another way.
                Debug.LogError($"This scene has no zone data to save; open the zone again (Comet › Zones › Open…). Its files in {editing.folder} are unchanged.");
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

            var zone = Gather(editing);
            var errors = ZoneValidation.Check(zone);
            var library = CometSettings.Find(quiet: true)?.assets;
            if (library != null)
            {
                errors.AddRange(ZoneValidation.CheckPlacements(zone, key => AssetFor(library, key), ZoneGroups.Find));
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

            // A copy being edited in place is saved as the copy; its parts go to its group only when that's saved.
            foreach (var placed in editing.GetComponentsInChildren<ZonePlacement>().Where(p => p.IsLoose))
            {
                var chunk = ChunkAt(ToNumerics(placed.transform.position));
                chunk.Placements = chunk.Placements.Append(PlacementOf(placed)).ToArray();
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

        /// <summary>
        /// Redraws a zone's terrain preview from <paramref name="zone"/>'s pieces: only the chunks in
        /// <paramref name="only"/> (where a brush went), or all of them when it's null. Chunk objects and their meshes
        /// are reused. A piece's flat bottom follows its lowest ground, so when that moves, every chunk is redrawn.
        /// </summary>
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
            var meshes = TerrainMesher.Build(zoneTerrain, Rgb(settings.groundColor), Rgb(settings.underColor), only);

            // Chunks redrawn without terrain go; the rest are refilled, or made.
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
                    chunk.gameObject.AddComponent<MeshFilter>().sharedMesh = new Mesh { name = name };
                    chunk.gameObject.AddComponent<MeshRenderer>();
                }

                chunk.GetComponent<MeshRenderer>().sharedMaterial = settings.terrainMaterial;
                TerrainMeshes.Fill(chunk.GetComponent<MeshFilter>().sharedMesh, mesh.Value);
            }
        }

        private static string ChunkName(ChunkCoord coord) => $"Chunk {coord}";

        // Each zone's pieces' flat bottoms as last drawn, to tell when a partial redraw isn't enough.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ZoneEditing, float[]> DrawnBottoms =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ZoneEditing, float[]>();

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
                    var thing = CreatePlaced(placement, isMarker ? spawnsByKey[placement.Name!] : placed, settings, isMarker);
                    if (isMarker)
                    {
                        thing.name = $"{placement.Name} marker";
                    }
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

        /// <summary>
        /// The object for a placement (an asset, a primitive or a group copy) under <paramref name="parent"/>, at the
        /// placement's world transform, with its marker component.
        /// </summary>
        internal static GameObject CreatePlaced(Placement placement, Transform parent, CometSettings settings, bool isMarker = false)
        {
            var thing = placement.Primitive is { } primitive ? CreatePrimitive(primitive, ToUnity(placement.Scale), settings, parent)
                : placement.Group is { } group ? ZoneGroups.CreateCopy(group, parent)
                : Instantiate(placement.Asset, settings, parent);
            var r = placement.Rotation;
            thing.transform.SetPositionAndRotation(ToUnity(placement.Position), Quaternion.Euler(r.X, r.Y, r.Z));
            if (placement.Primitive is null)
            {
                SetWorldScale(thing.transform, ToUnity(placement.Scale));
            }

            var marker = thing.GetComponent<ZonePlacement>() ?? thing.AddComponent<ZonePlacement>();
            marker.asset = placement.Asset;
            marker.placementName = placement.Name ?? "";
            marker.blockout = placement.Blockout;
            marker.role = placement.Role ?? "";
            marker.roleColors = placement.Colors?.OrderBy(c => c.Key, System.StringComparer.Ordinal)
                .Select(c => new ZonePlacement.RoleColor { role = c.Key, color = PlacedColors.FromRgb(c.Value) }).ToList() ?? new List<ZonePlacement.RoleColor>();
            SetColors(marker, settings, placement.Color, isMarker);
            return thing;
        }

        /// <summary>
        /// A placement as the object <paramref name="placed"/> has it now: at its world transform, or relative to
        /// <paramref name="relativeTo"/> (a copy whose group's parts are being saved).
        /// </summary>
        internal static Placement PlacementOf(ZonePlacement placed, Transform? relativeTo = null)
        {
            var t = placed.transform;
            var position = relativeTo != null ? relativeTo.InverseTransformPoint(t.position) : t.position;
            var rotation = relativeTo != null ? Quaternion.Inverse(relativeTo.rotation) * t.rotation : t.rotation;
            var lossy = relativeTo != null ? Vector3.Scale(t.lossyScale, new Vector3(1 / relativeTo.lossyScale.x, 1 / relativeTo.lossyScale.y, 1 / relativeTo.lossyScale.z)) : t.lossyScale;
            return new Placement
            {
                Asset = placed.isPrimitive || placed.IsGroup ? "" : placed.asset,
                Primitive = placed.IsGroup ? null : placed.Primitive,
                Group = placed.IsGroup ? placed.group : null,
                Blockout = placed.blockout,
                // A spawn's marker is named with its key, which may have changed since.
                Name = placed.GetComponentInParent<ZoneSpawn>() is { } spawn ? spawn.key
                    : string.IsNullOrEmpty(placed.placementName) ? null : placed.placementName,
                Position = ToNumerics(position),
                Rotation = Angles(rotation.eulerAngles),
                Scale = ToNumerics(placed.isPrimitive && !placed.IsGroup ? Vector3.Scale(placed.size, lossy) : lossy),
                Color = placed.ownColor && !placed.IsGroup ? PlacedColors.ToRgb(placed.color) : (uint?)null,
                // Only a group's parts have a role: one unpacked into a zone drops it.
                Role = !string.IsNullOrEmpty(placed.role) && (relativeTo != null || placed.GetComponentInParent<ZoneEditing>() is { IsGroup: true }) ? placed.role : null,
                Colors = placed.IsGroup && placed.roleColors.Count > 0 ? RoleColors(placed) : null,
            };
        }

        // A copy's colors by role, each role once.
        private static Dictionary<string, uint> RoleColors(ZonePlacement copy)
        {
            var colors = new Dictionary<string, uint>();
            foreach (var c in copy.roleColors.Where(c => !string.IsNullOrEmpty(c.role)))
            {
                colors[c.role] = PlacedColors.ToRgb(c.color);
            }

            return colors;
        }

        // A placement's colors: its asset's, from the library (a spawn's marker: the settings' marker color), and its own if it has one.
        internal static void SetColors(ZonePlacement placement, CometSettings settings, uint? own, bool spawnMarker = false)
        {
            Color? asset = !placement.isPrimitive && settings.assets != null && settings.assets.TryGet(placement.asset, out var entry) && entry.hasColor ? entry.color : null;
            placement.ownColor = own != null;
            if (own is { } rgb)
            {
                placement.color = PlacedColors.FromRgb(rgb);
            }

            placement.SetDefaultColor(spawnMarker ? settings.spawnMarkerColor : asset ?? settings.placedColor);
        }

        /// <summary>The primitive's kind for a placeable name: an asset key (<c>asset.prop</c>) is null.</summary>
        public static PrimitiveKind? PrimitiveNamed(string name) => PrimitiveKinds.Named(name);

        /// <summary>A newly placed primitive of <paramref name="kind"/>: its values and the size it starts at.</summary>
        public static (Primitive Primitive, Vector3 Size) Starting(PrimitiveKind kind) => kind switch
        {
            PrimitiveKind.Wedge => (new Primitive { Kind = kind }, new Vector3(2, 1, 2)),
            PrimitiveKind.Stairs => (new Primitive { Kind = kind }, new Vector3(2, 1, 1.5f)),
            PrimitiveKind.Wall => (new Primitive { Kind = kind }, new Vector3(4, 3, 0.3f)),
            PrimitiveKind.Doorway => (new Primitive { Kind = kind, OpeningWidth = 1.2f, OpeningHeight = 2.2f }, new Vector3(4, 3, 0.3f)),
            PrimitiveKind.Arch => (new Primitive { Kind = kind, Thickness = 0.4f }, new Vector3(3, 3, 0.4f)),
            PrimitiveKind.Tetrahedron or PrimitiveKind.Octahedron or PrimitiveKind.Dodecahedron or PrimitiveKind.Icosahedron => (new Primitive { Kind = kind }, Vector3.one),
            _ => (new Primitive { Kind = kind }, new Vector3(2, 2, 2)),
        };

        // A primitive's object: its mesh built at its size, drawn in the placed objects' material, with a collider for
        // surface snapping. Its placement component is added here, since the mesh comes from it.
        private static GameObject CreatePrimitive(Primitive primitive, Vector3 size, CometSettings settings, Transform parent)
        {
            var name = PrimitiveKinds.Name(primitive.Kind);
            var thing = new GameObject();
            thing.transform.SetParent(parent, false);
            thing.name = GameObjectUtility.GetUniqueNameForSibling(parent, name);
            thing.AddComponent<MeshFilter>();
            thing.AddComponent<MeshRenderer>().sharedMaterial = settings.placedMaterial != null ? settings.placedMaterial : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
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

            // Numbered like Unity's own copies (asset.prop, asset.prop (1), ...); saving ignores names.
            thing.name = "";
            thing.name = GameObjectUtility.GetUniqueNameForSibling(parent, asset);
            foreach (var renderer in thing.GetComponentsInChildren<Renderer>())
            {
                if (renderer.name.EndsWith(Asset.CollisionSuffix, StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                    continue;
                }

                if (settings.placedMaterial != null)
                {
                    renderer.sharedMaterial = settings.placedMaterial;
                }

                // Lets Unity's surface snapping land other objects on it.
                if (renderer.TryGetComponent<MeshFilter>(out var filter) && !renderer.TryGetComponent<Collider>(out _))
                {
                    renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                }
            }

            return thing;
        }

        // What the placement checks need to know of an asset, from the library: its scale rule and bounds.
        internal static Asset? AssetFor(AssetLibrary library, string key)
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

        /// <summary>
        /// Places an asset (by key) or a primitive (by kind, <c>box</c>) at <paramref name="position"/> in an opened
        /// zone: an asset as a linked model with its placement marker, a primitive at its starting size. With a
        /// <paramref name="color"/>, the placement has it as its own.
        /// </summary>
        public static GameObject Place(ZoneEditing editing, string asset, Vector3 position, CometSettings settings, Color? color = null)
        {
            // While a group is edited in place, what's placed goes into it.
            var parent = ZoneGroups.InPlace is { } inPlace && inPlace.GetComponentInParent<ZoneEditing>() == editing ? inPlace.transform
                : editing.transform.Find("Placed") ?? Child(editing.transform, "Placed");
            GameObject thing;
            ZonePlacement placement;
            if (PrimitiveNamed(asset) is { } kind)
            {
                var (primitive, size) = Starting(kind);
                thing = CreatePrimitive(primitive, size, settings, parent);
                placement = thing.GetComponent<ZonePlacement>();
            }
            else if (asset.StartsWith("group.", StringComparison.Ordinal))
            {
                // A copy looks like its group: it has no color of its own.
                thing = ZoneGroups.CreateCopy(asset, parent);
                thing.transform.position = position;
                Undo.RegisterCreatedObjectUndo(thing, $"Place {asset}");
                return thing;
            }
            else
            {
                thing = Instantiate(asset, settings, parent);
                placement = thing.AddComponent<ZonePlacement>();
                placement.asset = asset;
            }

            thing.transform.position = position;
            SetColors(placement, settings, color is { } c ? PlacedColors.ToRgb(c) : (uint?)null);
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
                var placement = placed.GetComponent<ZonePlacement>();
                var markerSize = new Vector3(2 * spawn.radius, settings.spawnMarkerHeight, 2 * spawn.radius);
                if (placement.isPrimitive)
                {
                    // Standing on its pivot: sunk halfway into the ground, and players arrive on its top.
                    placement.size = markerSize;
                    placement.Rebuild();
                    placed.transform.position = position - Vector3.up * (markerSize.y / 2);
                    marker.position = position + Vector3.up * (markerSize.y / 2);
                }
                else
                {
                    var bounds = settings.assets != null && settings.assets.TryGet(settings.spawnMarkerAsset, out var entry) ? entry.bounds : new Bounds(Vector3.zero, Vector3.one);
                    var size = bounds.size;
                    placed.transform.localScale = new Vector3(markerSize.x / Mathf.Max(size.x, 0.01f), markerSize.y / Mathf.Max(size.y, 0.01f), markerSize.z / Mathf.Max(size.z, 0.01f));

                    // The marker is sunk halfway into the ground and players arrive on its top.
                    var scaleY = placed.transform.localScale.y;
                    placed.transform.position = position - Vector3.up * (bounds.center.y * scaleY);
                    marker.position = position + Vector3.up * (bounds.extents.y * scaleY);
                }

                placement.placementName = key;
                SetColors(placement, settings, null, spawnMarker: true);
                placed.name = $"{key} marker";
                Undo.SetTransformParent(placed.transform, marker, $"Add {key}");
            }

            return marker.gameObject;
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

        internal static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath)!;

        internal static string ProjectPath(string relative) => Path.GetFullPath(Path.Combine(ProjectRoot, relative));

        internal static string RelativeToProject(string path) => Path.GetRelativePath(ProjectRoot, path).Replace('\\', '/');

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

        internal static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        internal static System.Numerics.Vector3 ToNumerics(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);
    }
}

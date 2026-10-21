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
    /// scene file sits in an ignored folder. Terrain shows as preview meshes; placed props, spawns and volumes are
    /// objects to move with Unity's own tools, and on saving each belongs to the chunk holding its pivot. A spawn
    /// point's, teleporter's or trigger's pad is a child of it, so they move together, and is saved inside it.
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

            var what = editing.IsGroup ? editing.groupId : Path.GetFileName(editing.folder);
            if (!EditorUtility.DisplayDialog("Discard changes", $"Discard every change to {what} since it was last saved, and read it again from its files? This can't be undone.", "Discard", "Keep"))
            {
                return;
            }

            if (editing.IsGroup)
            {
                ZoneGroups.Open(editing.groupId, settings);
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

            // A new scene unloads assets nothing in it uses, the settings and prop library among them: load them again.
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
                chunk.Teleporters = new Teleporter[0];
                chunk.Triggers = new Trigger[0];
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
            var settings = CometSettings.Find(quiet: true);

            // A charge color is saved only when it isn't the game's default for its kind, so a later change to the
            // default reaches the rest.
            uint? OwnChargeColor(Color color, Color? fallback) =>
                fallback is { } usual && PlacedColors.ToRgb(color) == PlacedColors.ToRgb(usual) ? null : PlacedColors.ToRgb(color);
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
            // Pads are saved inside their spawn points, teleporters and triggers.
            foreach (var placed in editing.GetComponentsInChildren<ZonePlacement>(true).Where(p => p.IsLoose && p.GetComponentInParent<ZoneTrigger>(true) == null))
            {
                var chunk = ChunkAt(ToNumerics(placed.transform.position));
                chunk.Placements = chunk.Placements.Append(PlacementOf(placed)).ToArray();
            }

            foreach (var spawn in editing.GetComponentsInChildren<ZoneSpawn>(true))
            {
                var chunk = ChunkAt(ToNumerics(spawn.transform.position));
                chunk.Spawns = chunk.Spawns.Append(new SpawnPoint { Id = spawn.id, Volume = VolumeOf(spawn), Facing = (SpawnFacing)spawn.facing }).ToArray();
            }

            foreach (var teleporter in editing.GetComponentsInChildren<ZoneTeleporter>(true))
            {
                var chunk = ChunkAt(ToNumerics(teleporter.transform.position));
                chunk.Teleporters = chunk.Teleporters.Append(new Teleporter
                {
                    Id = teleporter.id, Volume = VolumeOf(teleporter), Facing = (SpawnFacing)teleporter.facing, Target = teleporter.target,
                    ChargeColor = OwnChargeColor(teleporter.chargeColor, settings?.teleporterChargeColor),
                }).ToArray();
            }

            foreach (var trigger in editing.GetComponentsInChildren<ZoneToggleTrigger>(true))
            {
                var chunk = ChunkAt(ToNumerics(trigger.transform.position));
                chunk.Triggers = chunk.Triggers.Append(new Trigger
                {
                    Id = trigger.id,
                    Volume = VolumeOf(trigger),
                    Effects = trigger.effects.Select(e => new ToggleEffect { Tag = e.toggleTag, RevertSeconds = e.revertSeconds }).ToArray(),
                    ChargeColor = OwnChargeColor(trigger.chargeColor, settings?.triggerChargeColor),
                }).ToArray();
            }

            foreach (var volume in editing.GetComponentsInChildren<ZoneVolume>(true))
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
                .Where(c => c.Tiles.Length + c.Placements.Length + c.Spawns.Length + c.Teleporters.Length + c.Triggers.Length + c.Volumes.Length > 0)
                .OrderBy(c => c.Coord.X).ThenBy(c => c.Coord.Z)
                .ToArray();
            return zone;
        }

        // A trigger's volume as the scene has it: standing on its object, turned by its Y rotation, with its pad.
        private static TriggerVolume VolumeOf(ZoneTrigger trigger)
        {
            var t = trigger.transform;
            return new TriggerVolume
            {
                Kind = (PrimitiveKind)System.Enum.Parse(typeof(PrimitiveKind), trigger.volume.ToString()),
                Position = ToNumerics(t.position),
                Yaw = Angle(t.rotation.eulerAngles.y),
                Size = ToNumerics(trigger.size),
                StandSeconds = trigger.standSeconds,
                Rearm = (Rearm)trigger.rearm,
                Pad = trigger.Pad is { } pad ? PlacementOf(pad) : null,
            };
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
                    component.chargeColor = teleporter.ChargeColor is { } color ? PlacedColors.FromRgb(color) : settings.teleporterChargeColor;
                }

                foreach (var trigger in chunk.Triggers)
                {
                    var component = CreateTrigger<ZoneToggleTrigger>(root, trigger.Id, trigger.Volume, settings);
                    component.effects = trigger.Effects.Select(e => new ZoneToggleTrigger.Effect { toggleTag = e.Tag, revertSeconds = e.RevertSeconds }).ToList();
                    component.chargeColor = trigger.ChargeColor is { } color ? PlacedColors.FromRgb(color) : settings.triggerChargeColor;
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

        // The folder objects each kind of trigger goes under.
        private static string TriggersFolder<T>() where T : ZoneTrigger =>
            typeof(T) == typeof(ZoneSpawn) ? "Spawns" : typeof(T) == typeof(ZoneTeleporter) ? "Teleporters" : "Triggers";

        // A spawn point's, teleporter's or trigger's object, with its volume and its pad.
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

            return component;
        }

        // The color a kind's pads are drawn in without their own: spawn pads as the game draws one that isn't the player's.
        private static Color? PadColor(ZoneTrigger trigger, CometSettings settings) => trigger is ZoneSpawn ? settings.spawnPadColor : (Color?)null;

        /// <summary>
        /// The object for a placement (a prop, a primitive or a group copy) under <paramref name="parent"/>, at the
        /// placement's world transform, with its marker component.
        /// </summary>
        internal static GameObject CreatePlaced(Placement placement, Transform parent, CometSettings settings, Color? defaultColor = null)
        {
            var thing = placement.Primitive is { } primitive ? CreatePrimitive(primitive, ToUnity(placement.Scale), settings, parent)
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
            marker.roleColors = placement.Colors?.OrderBy(c => c.Key, System.StringComparer.Ordinal)
                .Select(c => new ZonePlacement.RoleColor { role = c.Key, color = PlacedColors.FromRgb(c.Value) }).ToList() ?? new List<ZonePlacement.RoleColor>();
            SetColors(marker, settings, placement.Color, defaultColor);
            // An object turned off (unchecked) is a placement that starts off.
            thing.SetActive(!placement.Off);
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
                Prop = placed.isPrimitive || placed.IsGroup ? "" : placed.prop,
                Primitive = placed.IsGroup ? null : placed.Primitive,
                Group = placed.IsGroup ? placed.group : null,
                Blockout = placed.blockout,
                Off = !placed.gameObject.activeSelf,
                Tag = string.IsNullOrEmpty(placed.placementTag) ? null : placed.placementTag,
                Position = ToNumerics(position),
                Rotation = Angles(rotation.eulerAngles),
                Scale = ToNumerics(placed.isPrimitive && !placed.IsGroup ? Vector3.Scale(placed.size, lossy) : lossy),
                Color = placed.ownColor && !placed.IsGroup ? PlacedColors.ToRgb(placed.color) : (uint?)null,
                // Only a group's parts have a role: one unpacked into a zone drops it.
                Role = !string.IsNullOrEmpty(placed.role) && (relativeTo != null || placed.GetComponentInParent<ZoneEditing>(true) is { IsGroup: true }) ? placed.role : null,
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

        // A placement's colors: its prop's, from the library (or defaultColor, for a pad), and its own if it has one.
        internal static void SetColors(ZonePlacement placement, CometSettings settings, uint? own, Color? defaultColor = null)
        {
            Color? prop = !placement.isPrimitive && settings.props != null && settings.props.TryGet(placement.prop, out var entry) && entry.hasColor ? entry.color : null;
            placement.ownColor = own != null;
            if (own is { } rgb)
            {
                placement.color = PlacedColors.FromRgb(rgb);
            }

            placement.SetDefaultColor(defaultColor ?? prop ?? settings.placedColor);
        }

        /// <summary>The primitive's kind for a placeable name: a prop id (<c>prop.rock</c>) is null.</summary>
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

        // The prop's model, linked to it, with its collision objects hidden; a stand-in cube if the library has none.
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

            // Numbered like Unity's own copies (prop.rock, prop.rock (1), ...); saving ignores names.
            thing.name = "";
            thing.name = GameObjectUtility.GetUniqueNameForSibling(parent, prop);
            foreach (var renderer in thing.GetComponentsInChildren<Renderer>())
            {
                if (renderer.name.EndsWith(Prop.CollisionSuffix, StringComparison.Ordinal))
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

        // What the placement checks need to know of a prop, from the library: its scale rule and bounds.
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

        /// <summary>
        /// Places a prop (by id), a primitive (by kind, <c>box</c>) or a group (by id) at <paramref name="position"/> in an opened
        /// zone: a prop as a linked model with its placement marker, a primitive at its starting size. With a
        /// <paramref name="color"/>, the placement has it as its own.
        /// </summary>
        public static GameObject Place(ZoneEditing editing, string placeable, Vector3 position, CometSettings settings, Color? color = null)
        {
            // While a group is edited in place, what's placed goes into it.
            var parent = ZoneGroups.InPlace is { } inPlace && inPlace.GetComponentInParent<ZoneEditing>(true) == editing ? inPlace.transform
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
                // A copy looks like its group: it has no color of its own.
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
            SetColors(placement, settings, color is { } c ? PlacedColors.ToRgb(c) : (uint?)null);
            Undo.RegisterCreatedObjectUndo(thing, $"Place {placeable}");
            return thing;
        }

        /// <summary>
        /// Adds a spawn point, teleporter or trigger (<typeparamref name="T"/>) standing at <paramref name="position"/>
        /// in an opened zone, with the settings' defaults for its kind: a pad (none for triggers), flattened and sunk
        /// halfway into the ground, with the volume fitted on its top.
        /// </summary>
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
                ZoneTeleporter => (settings.teleporterPad, settings.teleporterPadSize, settings.teleporterStandSeconds, settings.teleporterVolumeHeight),
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
                var pad = CreatePlaced(new Placement
                {
                    Primitive = primitive,
                    Position = ToNumerics(position - Vector3.up * (padSize.y / 2)),
                    Scale = ToNumerics(padSize),
                    Color = trigger is ZoneTeleporter ? PlacedColors.ToRgb(settings.teleporterPadColor) : (uint?)null,
                }, point, settings, PadColor(trigger, settings));
                pad.name = $"{id} pad";
                Undo.RegisterCreatedObjectUndo(pad, $"Add {id}");
                FitToPad(trigger);
            }

            return trigger;
        }

        /// <summary>
        /// Fits a trigger's volume to its pad: standing on the pad's top, as wide and deep as the pad and turned with
        /// it, its height kept; round pads get a cylinder, the rest a box. The pad stays where it is.
        /// </summary>
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
            trigger.volume = pad.isPrimitive && pad.kind is PrimitiveKind.Cylinder or PrimitiveKind.Cone or PrimitiveKind.Sphere
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

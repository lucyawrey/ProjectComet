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
        internal static readonly MessagePackSerializerOptions Serializer = ContentSerializer.CreateOptions(
            MessagePack.Resolvers.StandardResolver.Instance
        );

        [MenuItem("Comet/Zones/New Zone…", priority = 0)]
        public static void NewMenu()
        {
            var settings = CometSettings.Find(quiet: false);
            if (settings == null || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var folder = PickNewZoneFolder("New zone", settings);
            if (folder != null)
            {
                ZoneFolder.Write(folder, new Zone { Id = "zone." + Path.GetFileName(folder) });
                Open(folder, settings);
            }
        }

        [MenuItem("Comet/Zones/New Zone…", true)]
        private static bool CanNew() => !EditorApplication.isPlaying;

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

        // Zones open into a scene of their own, which play mode can't do.
        [MenuItem("Comet/Zones/Open…", true)]
        private static bool CanOpen() => !EditorApplication.isPlaying;

        // The zones opened lately on this computer, newest first: their folders relative to the project, per project.
        private const int RecentCount = 8;
        private static string RecentKey => "Comet.Zones.Recent." + ProjectRoot;

        internal static List<string> Recent() =>
            EditorPrefs
                .GetString(RecentKey, "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(f => Directory.Exists(ProjectPath(f)))
                .ToList();

        private static void Remember(string folder)
        {
            var recent = Recent();
            recent.Remove(folder);
            recent.Insert(0, folder);
            EditorPrefs.SetString(RecentKey, string.Join("\n", recent.Take(RecentCount)));
        }

        // A menu can't list items that change, and a pop-up menu can't open from one, so this opens a small window.
        [MenuItem("Comet/Zones/Open Recent…", priority = 2)]
        public static void OpenRecentMenu() => RecentZonesWindow.Show();

        [MenuItem("Comet/Zones/Open Recent…", true)]
        private static bool CanOpenRecent() => !EditorApplication.isPlaying;

        /// <summary>Opens a zone from <see cref="Recent"/>, after offering to save the current scene.</summary>
        internal static void OpenRecent(string folder)
        {
            var settings = CometSettings.Find(quiet: false);
            if (settings != null && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Open(ProjectPath(folder), settings);
            }
        }

        /// <summary>Raised after a zone's files are saved, so a game can rebuild its content from them.</summary>
        public static event Action<ZoneEditing>? Saved;

        // Saving the scene saves its zones too (ZoneEditorScenes).
        [MenuItem("Comet/Zones/Save", priority = 3)]
        public static void SaveMenu()
        {
            foreach (var scene in UnityEngine.Object.FindObjectsByType<ZoneEditing>().Select(z => z.gameObject.scene).Distinct())
            {
                EditorSceneManager.SaveScene(scene);
            }
        }

        [MenuItem("Comet/Zones/Save", true)]
        private static bool CanSave() => UnityEngine.Object.FindAnyObjectByType<ZoneEditing>() != null;

        /// <summary>
        /// Saves the opened zone into a new folder as a zone named after it, then opens that. The zone's own files
        /// keep their last save.
        /// </summary>
        [MenuItem("Comet/Zones/Save As…", priority = 4)]
        public static void SaveAsMenu()
        {
            var editing = UnityEngine.Object.FindAnyObjectByType<ZoneEditing>();
            var settings = CometSettings.Find(quiet: false);
            if (editing == null || settings == null)
            {
                return;
            }

            var folder = PickNewZoneFolder("Save zone as", settings);
            if (folder == null)
            {
                return;
            }

            var zone = Gather(editing);
            ZoneRename.Rename(zone, "zone." + Path.GetFileName(folder));
            if (!Write(zone, folder))
            {
                RemoveIfEmpty(folder);
                return;
            }

            if (Open(folder, settings))
            {
                Saved?.Invoke(UnityEngine.Object.FindAnyObjectByType<ZoneEditing>()!);
            }
        }

        [MenuItem("Comet/Zones/Save As…", true)]
        private static bool CanSaveAs() =>
            !EditorApplication.isPlaying && UnityEngine.Object.FindAnyObjectByType<ZoneEditing>() is { IsGroup: false, HasZoneData: true };

        // A folder for a new zone, picked directly inside the zones folder, new or empty, and named as a zone is; null
        // if the pick was canceled or refused (saying why).
        private static string? PickNewZoneFolder(string title, CometSettings settings)
        {
            var zones = ProjectPath(settings.zonesFolder).TrimEnd('/', '\\');
            var picked = EditorUtility.SaveFolderPanel(title, zones, "");
            if (string.IsNullOrEmpty(picked))
            {
                return null;
            }

            picked = Path.GetFullPath(picked).TrimEnd('/', '\\');
            var name = Path.GetFileName(picked);
            var problem =
                Path.GetDirectoryName(picked) != zones ? $"Zones go directly in {settings.zonesFolder}."
                : !ZoneValidation.IsName(name) ? $"'{name}' isn't a zone name: use lowercase letters, digits and underscores, starting with a letter."
                : Directory.Exists(picked) && Directory.EnumerateFileSystemEntries(picked).Any() ? $"{name} already has files in it."
                : null;
            if (problem == null)
            {
                return picked;
            }

            // The folder dialog can make the folder; left empty in the zones folder, it would fail the content build.
            RemoveIfEmpty(picked);
            EditorUtility.DisplayDialog(title, problem, "OK");
            return null;
        }

        private static void RemoveIfEmpty(string folder)
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }

        /// <summary>Drops the opened zone's (or group's) unsaved changes, reading it again from its files.</summary>
        [MenuItem("Comet/Zones/Discard Changes", priority = 5)]
        public static void DiscardMenu()
        {
            var editing = UnityEngine.Object.FindAnyObjectByType<ZoneEditing>();
            var settings = CometSettings.Find(quiet: false);
            if (editing == null || settings == null)
            {
                return;
            }

            var what = editing.IsGroup ? editing.groupId : Path.GetFileName(editing.folder);
            if (
                !EditorUtility.DisplayDialog(
                    "Discard changes",
                    $"Discard every change to {what} since it was last saved, and read it again from its files? This can't be undone.",
                    "Discard",
                    "Keep"
                )
            )
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
        private static bool CanDiscard() => !EditorApplication.isPlaying && UnityEngine.Object.FindAnyObjectByType<ZoneEditing>() != null;

        /// <summary>Opens the zone in <paramref name="folder"/> into a new scene; false (with the reasons logged) if its files have errors.</summary>
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

            // Keep everything but the objects, which the scene holds now.
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
            Remember(editing.folder);
            return true;
        }

        /// <summary>Writes an opened zone back to its files; false (with the reasons logged) if it doesn't validate.</summary>
        public static bool Save(ZoneEditing editing)
        {
            if (!editing.HasZoneData)
            {
                // A working scene saved by an older editor, which kept the zone data another way.
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

        // Writes a zone to its folder; false (with the reasons logged) if it doesn't validate.
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

            // Tiles are kept whole-chunk while editing; the files keep only their ground.
            ZoneTerrainEditing.Crop(zone);

            // A chunk left with nothing in it is no longer part of the zone.
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

        // A trigger's volume as the scene has it: standing on its object, turned by its Y rotation, with its pad.
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
                // From the volume's bottom center, turned back by its yaw: the sign's place in the volume's own space.
                Sign = trigger.Sign is { } sign
                    ? SignOf(sign, ToNumerics(Quaternion.Euler(0, -t.eulerAngles.y, 0) * (sign.transform.position - t.position)))
                    : null,
            };
        }

        // Whether a sign is saved with a trigger (a direct child of one) rather than on its own.
        private static bool IsTriggerSign(ZoneSign sign) =>
            sign.transform.parent != null && sign.transform.parent.GetComponent<ZoneTrigger>() is { } trigger && trigger.Sign == sign;

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
                    component.effects = trigger
                        .Effects.Select(e => new ZoneToggleTrigger.Effect { toggleTag = e.Tag, revertSeconds = e.RevertSeconds })
                        .ToList();
                    component.chargeColor = trigger.ChargeColor is { } color ? PlacedColors.FromRgb(color) : settings.triggerChargeColor;
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

        // A sign's object name: its first line, shortened.
        private static string SignName(string text)
        {
            var line = text.Split('\n')[0].Trim();
            return "Sign " + (line.Length > 24 ? line.Substring(0, 24) + "…" : line);
        }

        // A sign as the scene has it, at the given position.
        private static Sign SignOf(ZoneSign sign, System.Numerics.Vector3 position) =>
            new Sign
            {
                Position = position,
                Text = sign.text.Replace("\r\n", "\n"),
                TextColor = PlacedColors.ToRgb(sign.textColor),
                Background = sign.background,
                BackgroundColor = PlacedColors.ToRgb(sign.backgroundColor),
                BackgroundOpacity = sign.backgroundOpacity,
                ShowDistance = sign.showDistance,
                ScreenSized = sign.screenSized,
                FontSize = sign.fontSize,
                TextHeight = sign.textHeight,
                Arrow = sign.arrow,
                Off = !sign.gameObject.activeSelf,
            };

        // A sign object's fields from a zone file's sign (its position is set by the caller).
        private static void Fill(ZoneSign component, Sign sign)
        {
            component.text = sign.Text;
            component.textColor = PlacedColors.FromRgb(sign.TextColor);
            component.background = sign.Background;
            component.backgroundColor = PlacedColors.FromRgb(sign.BackgroundColor);
            component.backgroundOpacity = sign.BackgroundOpacity;
            component.showDistance = sign.ShowDistance;
            component.screenSized = sign.ScreenSized;
            component.fontSize = sign.FontSize;
            component.textHeight = sign.TextHeight;
            component.arrow = sign.Arrow;
            component.gameObject.SetActive(!sign.Off); // turned off (unchecked): kept but not shown in the game
        }

        /// <summary>
        /// Adds a sign to a spawn point, teleporter or trigger, saved with it: over the middle of its volume's top,
        /// with a new sign's defaults and its arrow on.
        /// </summary>
        public static ZoneSign AddSign(ZoneTrigger trigger)
        {
            var sign = Child(trigger.transform, $"{trigger.id} sign").gameObject.AddComponent<ZoneSign>();
            sign.transform.localPosition = Vector3.up * (trigger.size.y + TriggerSignLift);
            sign.text = "Sign";
            sign.arrow = true; // pointing down at its trigger
            Undo.RegisterCreatedObjectUndo(sign.gameObject, "Add Sign");
            return sign;
        }

        // How far above a trigger's volume a sign added to it goes, in meters.
        private const float TriggerSignLift = 0.5f;

        /// <summary>Adds a sign at <paramref name="position"/> in an opened zone, with a new sign's defaults.</summary>
        public static ZoneSign AddSign(ZoneEditing editing, Vector3 position)
        {
            var parent = editing.transform.Find(SignsFolder) ?? Child(editing.transform, SignsFolder);
            var sign = Child(parent, "Sign").gameObject.AddComponent<ZoneSign>();
            sign.transform.position = position;
            sign.text = "Sign";
            Undo.RegisterCreatedObjectUndo(sign.gameObject, "Add sign");
            return sign;
        }

        // The folder objects each kind of trigger goes under.
        private static string TriggersFolder<T>()
            where T : ZoneTrigger =>
            typeof(T) == typeof(ZoneSpawn) ? "Spawns"
            : typeof(T) == typeof(ZoneTeleporter) ? "Teleporters"
            : "Triggers";

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

            if (volume.Sign is { } sign)
            {
                var child = Child(point, $"{id} sign").gameObject.AddComponent<ZoneSign>();
                child.transform.localPosition = ToUnity(sign.Position);
                Fill(child, sign);
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
                    .Select(c => new ZonePlacement.RoleColor { role = c.Key, color = PlacedColors.FromRgb(c.Value) })
                    .ToList()
                ?? new List<ZonePlacement.RoleColor>();
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
                Color = placed.ownColor && !placed.IsGroup ? PlacedColors.ToRgb(placed.color) : (uint?)null,
                // Only a group's parts have a role: one unpacked into a zone drops it.
                Role =
                    !string.IsNullOrEmpty(placed.role) && (relativeTo != null || placed.GetComponentInParent<ZoneEditing>(true) is { IsGroup: true })
                        ? placed.role
                        : null,
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
            Color? prop =
                !placement.isPrimitive && settings.props != null && settings.props.TryGet(placement.prop, out var entry) && entry.hasColor
                    ? entry.color
                    : null;
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

        // A primitive's object: its mesh built at its size, drawn in the placed objects' material, with a collider for
        // surface snapping. Its placement component is added here, since the mesh comes from it.
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
                        Color = trigger is ZoneTeleporter ? PlacedColors.ToRgb(settings.teleporterPadColor) : (uint?)null,
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

        /// <summary>Draws an opened zone's terrain preview again if it or a chunk's mesh is missing (they aren't saved with the scene).</summary>
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
            return degrees > 180 ? degrees - 360
                : degrees <= -180 ? degrees + 360
                : degrees;
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

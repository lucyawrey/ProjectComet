using System.Collections.Generic;
using System.Linq;
using Comet.Content;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// The Scene view's terrain tool for a zone opened in the editor (in the Tools overlay, or Comet › Zones › Terrain
    /// Tool): pick a piece and a brush in its panel, then paint with the left mouse button. Each stroke is one undo
    /// step. The panel also makes and deletes pieces, edits their settings, and warns where two pieces' ground is too
    /// close for a body to fit between.
    /// </summary>
    [EditorTool("Zone Terrain")]
    public sealed class ZoneTerrainTool : EditorTool
    {
        private enum Brush
        {
            Raise,
            Lower,
            Smooth,
            Flatten,
            Hole,
            Ground,
            Color,
        }

        private static readonly string[] BrushNames = { "Raise", "Lower", "Smooth", "Flatten", "Hole", "Ground", "Color" };
        private static readonly string[] SurfaceNames = { "Ground", "Underside" };

        [SerializeField] private Brush brush;
        [Tooltip("Whether the brushes work on the ground or on the underside map (Hole and Ground only work on the ground).")]
        [SerializeField] private bool underside;
        [SerializeField] private float radius = 3;
        [SerializeField] private float strength = 2;
        [SerializeField] private Color paint = new Color32(0x8c, 0x7a, 0x52, 0xff);
        [SerializeField] private string piece = "";
        [SerializeField] private string newPiece = "piece";
        [SerializeField] private float newHeight = 4;
        [SerializeField] private Vector3 move;
        [Tooltip("Whether moving a piece carries the placed objects and spawn points standing on it.")]
        [SerializeField] private bool carry = true;

        private CometSettings? _settings;
        private bool _painting;
        private bool _redraw;
        // The chunks the stroke changed since its last redraw; null when it changed which ground there is (holes and
        // ground decide which edges are the piece's outside, anywhere on it), so everything is redrawn.
        private HashSet<ChunkCoord>? _changed = new HashSet<ChunkCoord>();
        private double _lastRedraw;
        private float _flattenHeight;
        private Vector3? _hover;
        private List<Vector3> _overlaps = new List<Vector3>();

        // The undo step a drag in Unity's color picker started, so the whole drag is one step.
        private int? _colorUndoGroup;

        private GUIContent? _icon;

        /// <summary>The tool's one instance, for its panel.</summary>
        internal static ZoneTerrainTool? Instance { get; private set; }

        [MenuItem("Comet/Zones/Terrain Tool", priority = 20)]
        public static void Select() => ToolManager.SetActiveTool<ZoneTerrainTool>();

        [MenuItem("Comet/Zones/Terrain Tool", true)]
        private static bool CanSelect() => Editing != null;

        public override GUIContent toolbarIcon => _icon ??= new GUIContent(EditorGUIUtility.IconContent("Terrain Icon").image, "Zone Terrain: paint the opened zone's terrain");

        public override bool IsAvailable() => Editing is { IsGroup: false };

        private void OnEnable()
        {
            Instance = this;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        public override void OnActivated() => _settings = CometSettings.Find(quiet: true);

        private static ZoneEditing Editing => FindAnyObjectByType<ZoneEditing>();

        private void OnUndoRedo()
        {
            ZoneTerrainEditing.Forget();
            var editing = Editing;
            if (editing != null && _settings != null)
            {
                ZoneEditor.RebuildTerrain(editing, ZoneTerrainEditing.For(editing).Zone, _settings);
                RefreshOverlaps();
            }

            SceneView.RepaintAll();
        }

        /// <summary>The tool's panel in the Scene view.</summary>
        internal void PanelGui()
        {
            var editing = Editing;
            if (editing == null || _settings == null)
            {
                EditorGUILayout.HelpBox(_settings == null ? "This project has no Comet Settings asset." : "Open a zone first: Comet › Zones › Open…", MessageType.Info);
                return;
            }

            var terrain = ZoneTerrainEditing.For(editing);
            var names = terrain.Zone.Pieces.Select(p => p.Name).ToArray();
            EditorGUIUtility.labelWidth = 110;
            EditorGUILayout.BeginVertical(GUILayout.Width(280));
            EditorGUILayout.LabelField(terrain.Zone.Id, EditorStyles.boldLabel);

            if (names.Length > 0)
            {
                var index = Mathf.Max(0, System.Array.IndexOf(names, piece));
                piece = names[EditorGUILayout.Popup("Piece", index, names)];
                PieceSettings(terrain);
            }
            else
            {
                piece = "";
                EditorGUILayout.HelpBox("The zone has no terrain pieces yet.", MessageType.None);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
            underside = GUILayout.Toolbar(underside ? 1 : 0, SurfaceNames) == 1;
            if ((int)brush >= BrushNames.Length || (underside && !OnUnderside(brush)))
            {
                brush = Brush.Raise;
            }

            BrushGrid();
            radius = EditorGUILayout.Slider("Radius (m)", radius, 0.5f, 32);
            strength = EditorGUILayout.Slider(brush == Brush.Smooth || brush == Brush.Flatten ? "Strength" : "Strength (m/s)", strength, 0.1f, 10);
            if (brush == Brush.Color)
            {
                paint = EditorGUILayout.ColorField("Color", paint);
            }

            if (underside)
            {
                EditorGUILayout.HelpBox("The underside map sets where the piece's skirts end; painting it starts one. Color paints the underside and the skirt walls above it, where there's an underside map.", MessageType.None);
            }

            EditorGUILayout.HelpBox("Paint with the left mouse button; hold Alt to orbit as usual. Flatten and Ground use the height where the stroke starts.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("New piece", EditorStyles.boldLabel);
            newPiece = EditorGUILayout.TextField("Name", newPiece);
            newHeight = EditorGUILayout.FloatField("Height (m)", newHeight);
            if (GUILayout.Button("Add a 16 m square at the Scene view's center"))
            {
                AddPiece(terrain);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Check overlaps"))
            {
                RefreshOverlaps();
            }

            if (_overlaps.Count > 0)
            {
                EditorGUILayout.HelpBox($"{_overlaps.Count} samples where two pieces' ground is closer than {_settings.bodyHeight} m (marked in red). Allowed, but a body may not fit between them.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        // The brushes, three to a row; on the underside, Hole and Ground are greyed out.
        private void BrushGrid()
        {
            var brushes = (Brush[])System.Enum.GetValues(typeof(Brush));
            for (var i = 0; i < brushes.Length; i += 3)
            {
                EditorGUILayout.BeginHorizontal();
                foreach (var b in brushes.Skip(i).Take(3))
                {
                    using (new EditorGUI.DisabledScope(underside && !OnUnderside(b)))
                    {
                        if (GUILayout.Toggle(brush == b, BrushNames[(int)b], GUI.skin.button) && brush != b)
                        {
                            brush = b;
                        }
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private static bool OnUnderside(Brush b) => b != Brush.Hole && b != Brush.Ground;

        // A piece's own color, or null for the game's: editing the field sets its own, the reset button clears it.
        private static uint? OwnColor(string label, string what, uint? own, Color game)
        {
            var content = new GUIContent(label, $"The color of the piece's {what}. Reset: the game's, from Comet Settings.");
            var (color, changed, reset) = ColorFields.WithReset(content, ToColor(own ?? Rgb(game)), own != null);
            return reset ? null : changed ? Rgb(color) : own;
        }

        private static bool ColorPickerFocused => EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.GetType().Name == "ColorPicker";

        private void PieceSettings(ZoneTerrainEditing terrain)
        {
            // A picker session ends once focus leaves it (say, to paint), so later changes get steps of their own.
            if (!ColorPickerFocused)
            {
                _colorUndoGroup = null;
            }

            var settings = terrain.Zone.Pieces.First(p => p.Name == piece);
            EditorGUI.BeginChangeCheck();
            var depth = Mathf.Max(0, EditorGUILayout.FloatField(new GUIContent("Skirt depth (m)", "Without an underside map, the piece's flat bottom sits this far below its lowest ground."), settings.SkirtDepth));
            var rim = EditorGUILayout.Toggle("Rim skirts", settings.RimSkirts);
            var holes = EditorGUILayout.Toggle("Hole skirts", settings.HoleSkirts);
            var ground = OwnColor("Top color", "unpainted ground", settings.GroundColor, _settings!.groundColor);
            var under = OwnColor("Underside color", "unpainted skirt walls and bottom", settings.UnderColor, _settings!.underColor);

            // The reset buttons may not count as a change.
            if (EditorGUI.EndChangeCheck() || ground != settings.GroundColor || under != settings.UnderColor)
            {
                settings.SkirtDepth = depth;
                settings.RimSkirts = rim;
                settings.HoleSkirts = holes;
                uint Ground() => settings.GroundColor ?? Rgb(_settings!.groundColor);
                uint Under() => settings.UnderColor ?? Rgb(_settings!.underColor);
                var (oldGround, oldUnder) = (Ground(), Under());
                settings.GroundColor = ground;
                settings.UnderColor = under;
                terrain.ReplaceDefaultColors(piece, oldGround, Ground(), oldUnder, Under());
                terrain.Commit("Change piece settings", _settings!);

                // Unity's color picker reports every step of a drag as a change; while it has focus, they all go into
                // the first one's undo step.
                if (!ColorPickerFocused)
                {
                    _colorUndoGroup = null;
                }
                else if (_colorUndoGroup is { } group)
                {
                    Undo.CollapseUndoOperations(group);
                }
                else
                {
                    _colorUndoGroup = Undo.GetCurrentGroup();
                }
            }

            // On once the Underside brushes have painted; turning it off removes the underside and its colors.
            var hasUnderside = terrain.HasUnderside(piece);
            using (new EditorGUI.DisabledScope(!hasUnderside))
            {
                var keep = EditorGUILayout.Toggle(new GUIContent("Underside map", "Made by painting with the Underside brushes. Turn off to remove it and its colors; the skirts end at the flat bottom again."), hasUnderside);
                if (hasUnderside && !keep && EditorUtility.DisplayDialog("Remove the underside map", $"Remove '{piece}''s underside map and underside colors? Its skirts will end at its flat bottom again.", "Remove", "Keep"))
                {
                    terrain.ClearUnderside(piece);
                    terrain.Commit("Remove underside map", _settings!);
                }
            }

            EditorGUILayout.Space();
            move = EditorGUILayout.Vector3Field(new GUIContent("Move by (m)", "Sideways (X and Z) in whole meters, the terrain's grid; up and down (Y) by any amount."), move);
            move = new Vector3(Mathf.Round(move.x / ZoneGrid.SampleSpacing) * ZoneGrid.SampleSpacing, move.y, Mathf.Round(move.z / ZoneGrid.SampleSpacing) * ZoneGrid.SampleSpacing);
            carry = EditorGUILayout.Toggle(new GUIContent("Carry what's on it", "Also move the placed objects and spawn points standing on the piece: those over its ground, with no other piece's ground between."), carry);
            using (new EditorGUI.DisabledScope(move == Vector3.zero))
            {
                if (GUILayout.Button($"Move piece '{piece}'"))
                {
                    MovePiece(terrain, settings);
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button($"Delete piece '{piece}'") && EditorUtility.DisplayDialog("Delete piece", $"Delete the piece '{piece}' and all its terrain?", "Delete", "Keep"))
            {
                terrain.RemovePiece(piece);
                terrain.Commit("Delete piece", _settings!);
                piece = "";
            }
        }

        private void MovePiece(ZoneTerrainEditing terrain, TerrainPiece settings)
        {
            var dx = Mathf.RoundToInt(move.x / ZoneGrid.SampleSpacing);
            var dz = Mathf.RoundToInt(move.z / ZoneGrid.SampleSpacing);
            var offset = new Vector3(dx * ZoneGrid.SampleSpacing, move.y, dz * ZoneGrid.SampleSpacing);
            Undo.SetCurrentGroupName($"Move piece {piece}");
            var group = Undo.GetCurrentGroup();
            if (carry)
            {
                // Found before the terrain moves. A spawn's marker moves with its spawn.
                var carried = terrain.Editing.GetComponentsInChildren<ZonePlacement>().Where(p => p.IsLoose && p.GetComponentInParent<ZoneSpawn>() == null).Select(p => p.transform)
                    .Concat(terrain.Editing.GetComponentsInChildren<ZoneSpawn>().Select(s => s.transform))
                    .Where(t => StandsOn(terrain, t.position))
                    .ToArray();
                Undo.RecordObjects(carried.Cast<Object>().ToArray(), $"Move piece {piece}");
                foreach (var t in carried)
                {
                    t.position += offset;
                }
            }

            terrain.MovePiece(piece, dx, dz, move.y, settings.GroundColor ?? Rgb(_settings!.groundColor), settings.UnderColor ?? Rgb(_settings!.underColor));
            terrain.Commit($"Move piece {piece}", _settings!);
            Undo.CollapseUndoOperations(group);
            RefreshOverlaps();
        }

        // Whether a point stands on the selected piece: over its ground, which is the nearest ground below it (within
        // a meter, since pivots are often sunk into it), with no other piece's ground in between.
        private bool StandsOn(ZoneTerrainEditing terrain, Vector3 point)
        {
            const float sink = 1;
            if (!terrain.Terrain[piece].TryGetHeightAt(point.x, point.z, out var ground) || ground > point.y + sink)
            {
                return false;
            }

            return !terrain.Terrain.Pieces.Any(other => other != terrain.Terrain[piece]
                && other.TryGetHeightAt(point.x, point.z, out var height) && height > ground && height <= point.y + sink);
        }

        private void AddPiece(ZoneTerrainEditing terrain)
        {
            if (!Comet.ZoneFiles.ZoneValidation.IsName(newPiece) || terrain.Zone.Pieces.Any(p => p.Name == newPiece))
            {
                EditorUtility.DisplayDialog("New piece", "Pick a new name: lowercase letters, digits and underscores, starting with a letter.", "OK");
                return;
            }

            var pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            var center = (Mathf.RoundToInt(pivot.x / ZoneGrid.SampleSpacing), Mathf.RoundToInt(pivot.z / ZoneGrid.SampleSpacing));
            terrain.AddPiece(new TerrainPiece { Name = newPiece, SkirtDepth = 4 }, center, 16, newHeight);
            terrain.Commit("Add piece", _settings!);
            piece = newPiece;
        }

        private void RefreshOverlaps()
        {
            var editing = Editing;
            _overlaps = editing != null && _settings != null ? ZoneTerrainEditing.For(editing).Overlaps(_settings.bodyHeight) : new List<Vector3>();
            SceneView.RepaintAll();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            var editing = Editing;
            if (editing == null || _settings == null || string.IsNullOrEmpty(piece))
            {
                return;
            }

            var terrain = ZoneTerrainEditing.For(editing);
            if (terrain.Zone.Pieces.All(p => p.Name != piece))
            {
                return;
            }

            Handles.color = Color.red;
            foreach (var overlap in _overlaps)
            {
                Handles.DrawWireDisc(overlap, Vector3.up, 0.3f);
            }

            var e = Event.current;
            if (e.alt)
            {
                return;
            }

            // Take the left mouse button from the scene's selection while the tool is picked.
            var control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(control);
            }

            _hover = Pick(terrain, HandleUtility.GUIPointToWorldRay(e.mousePosition));
            if (_hover is { } at)
            {
                Handles.color = brush == Brush.Hole ? Color.red : Color.white;
                Handles.DrawWireDisc(at, Vector3.up, radius);
            }

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && _hover != null:
                    _painting = true;
                    _flattenHeight = underside
                        ? terrain.UndersideAt(piece, Mathf.RoundToInt(_hover.Value.x / ZoneGrid.SampleSpacing), Mathf.RoundToInt(_hover.Value.z / ZoneGrid.SampleSpacing))
                        : _hover.Value.y;
                    GUIUtility.hotControl = control;
                    Paint(terrain, _hover.Value, 1f / 30);
                    e.Use();
                    break;
                case EventType.MouseDrag when _painting && e.button == 0:
                    if (_hover != null)
                    {
                        Paint(terrain, _hover.Value, 1f / 30);
                    }

                    RedrawDuringStroke(terrain);
                    e.Use();
                    break;
                case EventType.MouseUp when _painting && e.button == 0:
                    _painting = false;
                    _redraw = false;
                    _changed = new HashSet<ChunkCoord>();
                    GUIUtility.hotControl = 0;
                    terrain.Commit($"Terrain: {(underside ? "Underside " : "")}{BrushNames[(int)brush]}", _settings!);
                    if (_overlaps.Count > 0)
                    {
                        RefreshOverlaps();
                    }

                    e.Use();
                    break;
                case EventType.MouseMove:
                    window.Repaint();
                    break;
            }
        }

        // Where the mouse ray first enters the selected piece, the solid between its underside and its ground (so it
        // finds the ground from above and the underside from below), marching along it a quarter meter at a time and
        // narrowing in on the crossing; else where it meets the height the last stroke started at (for painting
        // ground where there's none yet).
        private Vector3? Pick(ZoneTerrainEditing terrain, Ray ray)
        {
            var piece = terrain.Terrain[this.piece];
            bool Inside(Vector3 p) => piece.TryGetHeightAt(p.x, p.z, out var ground) && p.y <= ground && p.y >= piece.BottomAt(p.x, p.z);

            const float step = 0.25f, far = 1000;
            var previous = ray.origin;
            var wasInside = Inside(previous);
            for (var t = step; t < far; t += step)
            {
                var point = ray.GetPoint(t);
                var inside = Inside(point);
                if (inside && !wasInside)
                {
                    var (a, b) = (previous, point);
                    for (var i = 0; i < 8; i++)
                    {
                        var middle = (a + b) / 2;
                        if (Inside(middle))
                        {
                            b = middle;
                        }
                        else
                        {
                            a = middle;
                        }
                    }

                    return b;
                }

                previous = point;
                wasInside = inside;
            }

            if (brush == Brush.Ground && Mathf.Abs(ray.direction.y) > 1e-4f)
            {
                var t = (_flattenHeight - ray.origin.y) / ray.direction.y;
                return t > 0 ? ray.GetPoint(t) : (Vector3?)null;
            }

            return null;
        }

        // Applies the brush around `center` for `seconds` of painting (one mouse event), with a smooth falloff to the rim.
        private void Paint(ZoneTerrainEditing terrain, Vector3 center, float seconds)
        {
            var spacing = ZoneGrid.SampleSpacing;
            int minX = Mathf.FloorToInt((center.x - radius) / spacing), maxX = Mathf.CeilToInt((center.x + radius) / spacing);
            int minZ = Mathf.FloorToInt((center.z - radius) / spacing), maxZ = Mathf.CeilToInt((center.z + radius) / spacing);
            if (!underside && (brush == Brush.Hole || brush == Brush.Ground))
            {
                _changed = null;
            }
            else if (_changed != null)
            {
                // The cells around each changed sample, and so their chunks (a sample on a border is in both).
                for (var cz = FloorDiv(minZ - 1, ZoneGrid.ChunkSize); cz <= FloorDiv(maxZ, ZoneGrid.ChunkSize); cz++)
                {
                    for (var cx = FloorDiv(minX - 1, ZoneGrid.ChunkSize); cx <= FloorDiv(maxX, ZoneGrid.ChunkSize); cx++)
                    {
                        _changed.Add(new ChunkCoord(cx, cz));
                    }
                }
            }

            var changes = new List<(int X, int Z, float Weight)>();
            for (var z = minZ; z <= maxZ; z++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x * spacing, z * spacing), new Vector2(center.x, center.z));
                    if (distance <= radius)
                    {
                        var falloff = 1 - distance / radius;
                        changes.Add((x, z, falloff * falloff * (3 - 2 * falloff)));
                    }
                }
            }

            // Smoothing reads the neighbors before any changes, so the result doesn't depend on the order.
            var before = brush == Brush.Smooth ? changes.ToDictionary(c => (c.X, c.Z), c => Average(terrain, c.X, c.Z)) : null;
            foreach (var (x, z, weight) in changes)
            {
                var has = terrain.TryGetHeight(piece, x, z, out var height);
                if (underside)
                {
                    if (has)
                    {
                        PaintUnderside(terrain, x, z, height, seconds * weight, before);
                    }

                    continue;
                }

                switch (brush)
                {
                    case Brush.Raise when has:
                        terrain.SetHeight(piece, x, z, height + strength * seconds * weight);
                        break;
                    case Brush.Lower when has:
                        terrain.SetHeight(piece, x, z, height - strength * seconds * weight);
                        break;
                    case Brush.Smooth when has && before![(x, z)] is { } average:
                        terrain.SetHeight(piece, x, z, Mathf.Lerp(height, average, Mathf.Clamp01(strength * seconds * 3 * weight)));
                        break;
                    case Brush.Flatten when has:
                        terrain.SetHeight(piece, x, z, Mathf.Lerp(height, _flattenHeight, Mathf.Clamp01(strength * seconds * 3 * weight)));
                        break;
                    case Brush.Hole when has:
                        terrain.SetHeight(piece, x, z, null);
                        break;
                    case Brush.Ground when !has:
                        terrain.SetHeight(piece, x, z, _flattenHeight);
                        var pieceSettings = terrain.Terrain[piece].Settings;
                        terrain.ResetColors(piece, x, z, pieceSettings.GroundColor ?? Rgb(_settings!.groundColor), pieceSettings.UnderColor ?? Rgb(_settings!.underColor));
                        break;
                    case Brush.Color when has:
                        terrain.SetColor(piece, x, z, Rgb(paint), terrain.Terrain[piece].Settings.GroundColor ?? Rgb(_settings!.groundColor));
                        break;
                }
            }

            if (!underside && (brush == Brush.Hole || brush == Brush.Ground))
            {
                terrain.Reindex();
            }

            // Redrawn as the stroke goes (RedrawDuringStroke); the undo step is written when it ends.
            _redraw = true;
        }

        // A stroke redraws only the chunks it changed (about 2 ms a chunk), at most 60 times a second; its end redraws
        // in full.
        private void RedrawDuringStroke(ZoneTerrainEditing terrain)
        {
            if (_redraw && EditorApplication.timeSinceStartup - _lastRedraw >= 1.0 / 60)
            {
                ZoneEditor.RebuildTerrain(terrain.Editing, terrain.Zone, _settings!, _changed);
                _lastRedraw = EditorApplication.timeSinceStartup;
                _redraw = false;
                _changed = new HashSet<ChunkCoord>();
            }
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;

        // The underside brushes at one ground sample (at `height`); `amount` is the stroke's time times its falloff.
        // The underside never rises above the ground.
        private void PaintUnderside(ZoneTerrainEditing terrain, int x, int z, float height, float amount, Dictionary<(int, int), float?>? before)
        {
            var under = terrain.UndersideAt(piece, x, z);
            switch (brush)
            {
                case Brush.Raise:
                    terrain.SetUnderside(piece, x, z, Mathf.Min(height, under + strength * amount));
                    break;
                case Brush.Lower:
                    terrain.SetUnderside(piece, x, z, under - strength * amount);
                    break;
                case Brush.Smooth when before![(x, z)] is { } average:
                    terrain.SetUnderside(piece, x, z, Mathf.Min(height, Mathf.Lerp(under, average, Mathf.Clamp01(strength * amount * 3))));
                    break;
                case Brush.Flatten:
                    terrain.SetUnderside(piece, x, z, Mathf.Min(height, Mathf.Lerp(under, _flattenHeight, Mathf.Clamp01(strength * amount * 3))));
                    break;
                case Brush.Color:
                    terrain.SetUndersideColor(piece, x, z, Rgb(paint), terrain.Terrain[piece].Settings.UnderColor ?? Rgb(_settings!.underColor));
                    break;
            }
        }

        // The average of a sample and its neighbors that have ground: of their heights, or on the underside, their undersides.
        private float? Average(ZoneTerrainEditing terrain, int x, int z)
        {
            float sum = 0;
            var count = 0;
            for (var dz = -1; dz <= 1; dz++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (terrain.TryGetHeight(piece, x + dx, z + dz, out var h))
                    {
                        sum += underside ? terrain.UndersideAt(piece, x + dx, z + dz) : h;
                        count++;
                    }
                }
            }

            return count > 0 ? sum / count : (float?)null;
        }

        private static uint Rgb(Color color)
        {
            Color32 c = color;
            return (uint)(c.r << 16 | c.g << 8 | c.b);
        }

        private static Color ToColor(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }

    /// <summary>The terrain tool's panel, shown in the Scene view while the tool is picked.</summary>
    [Overlay(typeof(SceneView), "comet-zone-terrain", "Zone Terrain")]
    internal sealed class ZoneTerrainPanel : IMGUIOverlay, ITransientOverlay
    {
        public bool visible => ToolManager.activeToolType == typeof(ZoneTerrainTool) && ZoneTerrainTool.Instance != null;

        public override void OnGUI() => ZoneTerrainTool.Instance?.PanelGui();
    }
}

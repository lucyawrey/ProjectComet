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
            UndersideUp,
            UndersideDown,
        }

        private static readonly string[] BrushNames = { "Raise", "Lower", "Smooth", "Flatten", "Hole", "Ground", "Color", "Under ↑", "Under ↓" };

        [SerializeField] private Brush brush;
        [SerializeField] private float radius = 3;
        [SerializeField] private float strength = 2;
        [SerializeField] private Color paint = new Color32(0x8c, 0x7a, 0x52, 0xff);
        [SerializeField] private string piece;
        [SerializeField] private string newPiece = "piece";
        [SerializeField] private float newHeight = 4;

        private CometSettings _settings;
        private bool _painting;
        private bool _redraw;
        private double _lastRedraw;
        private float _flattenHeight;
        private Vector3? _hover;
        private List<Vector3> _overlaps = new List<Vector3>();

        private GUIContent _icon;

        /// <summary>The tool's one instance, for its panel.</summary>
        internal static ZoneTerrainTool Instance { get; private set; }

        [MenuItem("Comet/Zones/Terrain Tool", priority = 20)]
        public static void Select() => ToolManager.SetActiveTool<ZoneTerrainTool>();

        [MenuItem("Comet/Zones/Terrain Tool", true)]
        private static bool CanSelect() => Editing != null;

        public override GUIContent toolbarIcon => _icon ??= new GUIContent(EditorGUIUtility.IconContent("Terrain Icon").image, "Zone Terrain: paint the opened zone's terrain");

        public override bool IsAvailable() => Editing != null;

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
                piece = null;
                EditorGUILayout.HelpBox("The zone has no terrain pieces yet.", MessageType.None);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
            brush = (Brush)GUILayout.SelectionGrid((int)brush, BrushNames, 3);
            radius = EditorGUILayout.Slider("Radius (m)", radius, 0.5f, 32);
            strength = EditorGUILayout.Slider(brush == Brush.Smooth || brush == Brush.Flatten ? "Strength" : "Strength (m/s)", strength, 0.1f, 10);
            if (brush == Brush.Color)
            {
                paint = EditorGUILayout.ColorField("Color", paint);
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

        private void PieceSettings(ZoneTerrainEditing terrain)
        {
            var settings = terrain.Zone.Pieces.First(p => p.Name == piece);
            EditorGUI.BeginChangeCheck();
            var bottom = EditorGUILayout.FloatField("Bottom height (m)", settings.BottomHeight);
            var rim = EditorGUILayout.Toggle("Rim skirts", settings.RimSkirts);
            var holes = EditorGUILayout.Toggle("Hole skirts", settings.HoleSkirts);
            if (EditorGUI.EndChangeCheck())
            {
                settings.BottomHeight = bottom;
                settings.RimSkirts = rim;
                settings.HoleSkirts = holes;
                terrain.Commit("Change piece settings", _settings);
            }

            if (GUILayout.Button($"Delete piece '{piece}'") && EditorUtility.DisplayDialog("Delete piece", $"Delete the piece '{piece}' and all its terrain?", "Delete", "Keep"))
            {
                terrain.RemovePiece(piece);
                terrain.Commit("Delete piece", _settings);
                piece = null;
            }
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
            terrain.AddPiece(new TerrainPiece { Name = newPiece, BottomHeight = newHeight - 4 }, center, 16, newHeight);
            terrain.Commit("Add piece", _settings);
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
                    _flattenHeight = _hover.Value.y;
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
                    GUIUtility.hotControl = 0;
                    terrain.Commit($"Terrain: {BrushNames[(int)brush]}", _settings);
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

        // Where the mouse ray meets the selected piece's ground, marching along it a quarter meter at a time and
        // narrowing in on the crossing; else where it meets the height the last stroke started at (for painting
        // ground where there's none yet).
        private Vector3? Pick(ZoneTerrainEditing terrain, Ray ray)
        {
            var piece = terrain.Terrain[this.piece];
            const float step = 0.25f, far = 1000;
            var previous = ray.origin;
            for (var t = step; t < far; t += step)
            {
                var point = ray.GetPoint(t);
                if (piece.TryGetHeightAt(point.x, point.z, out var ground) && point.y <= ground)
                {
                    var (a, b) = (previous, point);
                    for (var i = 0; i < 8; i++)
                    {
                        var middle = (a + b) / 2;
                        if (piece.TryGetHeightAt(middle.x, middle.z, out var h) && middle.y <= h)
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
                        break;
                    case Brush.Color when has:
                        terrain.SetColor(piece, x, z, Rgb(paint), Rgb(_settings.groundColor));
                        break;
                    case Brush.UndersideUp when has:
                        terrain.SetUnderside(piece, x, z, Mathf.Min(height, terrain.UndersideAt(piece, x, z) + strength * seconds * weight));
                        break;
                    case Brush.UndersideDown when has:
                        terrain.SetUnderside(piece, x, z, terrain.UndersideAt(piece, x, z) - strength * seconds * weight);
                        break;
                }
            }

            if (brush == Brush.Hole || brush == Brush.Ground)
            {
                terrain.Reindex();
            }

            // Redrawn as the stroke goes (RedrawDuringStroke); the undo step is written when it ends.
            _redraw = true;
        }

        // Meshing the terrain takes about 5 ms a chunk, too slow for every mouse event, so a stroke redraws at most
        // 20 times a second; its end redraws in full.
        private void RedrawDuringStroke(ZoneTerrainEditing terrain)
        {
            if (_redraw && EditorApplication.timeSinceStartup - _lastRedraw >= 0.05)
            {
                ZoneEditor.RebuildTerrain(terrain.Editing, terrain.Zone, _settings);
                _lastRedraw = EditorApplication.timeSinceStartup;
                _redraw = false;
            }
        }

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
                        sum += h;
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
    }

    /// <summary>The terrain tool's panel, shown in the Scene view while the tool is picked.</summary>
    [Overlay(typeof(SceneView), "comet-zone-terrain", "Zone Terrain")]
    internal sealed class ZoneTerrainPanel : IMGUIOverlay, ITransientOverlay
    {
        public bool visible => ToolManager.activeToolType == typeof(ZoneTerrainTool) && ZoneTerrainTool.Instance != null;

        public override void OnGUI() => ZoneTerrainTool.Instance?.PanelGui();
    }
}

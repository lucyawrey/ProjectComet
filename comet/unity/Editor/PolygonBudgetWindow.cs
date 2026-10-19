using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Comet.Content;
using UnityEditor;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// Tunes the polygon budget (Comet › Zones › Polygon Budget): changes show at once on every primitive in the opened
    /// zone, and Save writes them to the game's settings file, so the content build and the game use them too. Until
    /// saved, the next content build puts the game's values back.
    /// </summary>
    public sealed class PolygonBudgetWindow : EditorWindow
    {
        [MenuItem("Comet/Zones/Polygon Budget", priority = 30)]
        public static void Open() => GetWindow<PolygonBudgetWindow>("Polygon Budget");

        private void OnGUI()
        {
            var settings = CometSettings.Find(quiet: true);
            if (settings == null)
            {
                EditorGUILayout.HelpBox("This project has no Comet Settings asset.", MessageType.Info);
                return;
            }

            EditorGUI.BeginChangeCheck();
            var facet = EditorGUILayout.Slider(new GUIContent("Facet length (m)", "About how long each side of a round primitive is."), settings.facetLength, 0.05f, 4);
            var min = EditorGUILayout.IntSlider(new GUIContent("Fewest sides", "However small a round primitive is."), settings.minSides, 3, 64);
            var max = EditorGUILayout.IntSlider(new GUIContent("Most sides", "However large a round primitive is."), settings.maxSides, 3, 256);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(settings, "Change polygon budget");
                settings.facetLength = facet;
                settings.minSides = min;
                settings.maxSides = Mathf.Max(min, max);
                EditorUtility.SetDirty(settings);
                ZonePrimitiveEditing.Preview(settings);
            }

            var budget = settings.Budget;
            int Around(float diameter) => budget.Sides(Mathf.PI * diameter);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sides around", $"0.5 m: {Around(0.5f)}   1 m: {Around(1)}   2 m: {Around(2)}   5 m: {Around(5)}   10 m: {Around(10)}");

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(settings.budgetFile)))
            {
                if (GUILayout.Button(string.IsNullOrEmpty(settings.budgetFile) ? "Save (the Comet Settings name no budget file)" : $"Save to {settings.budgetFile}"))
                {
                    Save(settings);
                }
            }

            EditorGUILayout.HelpBox("Changes show on the opened zone's primitives at once. Save writes them to the game's settings; until then, the next content build puts the saved values back.", MessageType.None);
        }

        // Rewrites the budget's three values in the settings file's table, keeping the rest of the file (and its comments) as it is.
        private static void Save(CometSettings settings)
        {
            var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, settings.budgetFile));
            if (!File.Exists(path))
            {
                EditorUtility.DisplayDialog("Polygon budget", $"There's no {settings.budgetFile}.", "OK");
                return;
            }

            var lines = File.ReadAllLines(path).ToList();
            var start = lines.FindIndex(l => l.Trim() == $"[{settings.budgetTable}]");
            if (start < 0)
            {
                EditorUtility.DisplayDialog("Polygon budget", $"{settings.budgetFile} has no [{settings.budgetTable}] table.", "OK");
                return;
            }

            var values = new (string Key, string Value)[]
            {
                ("facet_length", settings.facetLength.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture)),
                ("min_sides", settings.minSides.ToString()),
                ("max_sides", Mathf.Max(settings.minSides, settings.maxSides).ToString()),
            };
            var end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith("["));
            end = end < 0 ? lines.Count : end;
            foreach (var (key, value) in values)
            {
                var pattern = new Regex($@"^(\s*{key}\s*=\s*)[^#]*?(\s*(#.*)?)$");
                var at = lines.FindIndex(start + 1, end - start - 1, l => pattern.IsMatch(l));
                if (at < 0)
                {
                    lines.Insert(end++, $"{key} = {value}");
                }
                else
                {
                    lines[at] = pattern.Replace(lines[at], m => m.Groups[1].Value + value + m.Groups[2].Value);
                }
            }

            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            Debug.Log($"Saved the polygon budget to {settings.budgetFile}; rebuild the content to use it in the game.");
        }
    }

    /// <summary>
    /// Keeps the opened zone's primitives built at their size: a resize with Unity's scale tool folds into the
    /// primitive's size when the mouse is let go, and the mesh is built again with the polygon budget.
    /// </summary>
    [InitializeOnLoad]
    public static class ZonePrimitiveEditing
    {
        static ZonePrimitiveEditing()
        {
            SceneView.duringSceneGui += _ =>
            {
                if (Event.current.type == EventType.MouseUp)
                {
                    EditorApplication.delayCall += FoldScales;
                }
            };
            Selection.selectionChanged += FoldScales;
            Undo.undoRedoPerformed += RebuildAll;
            var settings = CometSettings.Find(quiet: true);
            if (settings != null)
            {
                ZonePlacement.Budget = settings.Budget;
            }
        }

        /// <summary>Shows <paramref name="settings"/>' budget on every primitive in the open scenes.</summary>
        public static void Preview(CometSettings settings)
        {
            ZonePlacement.Budget = settings.Budget;
            RebuildAll();
        }

        private static void RebuildAll()
        {
            foreach (var placement in Object.FindObjectsByType<ZonePlacement>(FindObjectsSortMode.None))
            {
                placement.Rebuild();
            }

            SceneView.RepaintAll();
        }

        // Folds the scale of every scaled primitive in the open scenes into its size.
        private static void FoldScales()
        {
            foreach (var placement in Object.FindObjectsByType<ZonePlacement>(FindObjectsSortMode.None))
            {
                var t = placement.transform;
                if (!placement.isPrimitive || t.localScale == Vector3.one)
                {
                    continue;
                }

                Undo.RecordObjects(new Object[] { placement, t }, "Resize primitive");
                var size = Vector3.Scale(placement.size, t.localScale);
                placement.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                t.localScale = Vector3.one;
                placement.Rebuild();
            }
        }
    }
}

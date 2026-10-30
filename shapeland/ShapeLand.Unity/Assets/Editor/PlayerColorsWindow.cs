using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Comet.Unity.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace ShapeLand.Client.Editor
{
    /// <summary>
    /// Picks the colors players choose from (Comet › ShapeLand › Player Colors): a list to edit, add to and reorder, and the
    /// swatches laid out ten a line as on the look card. Save writes them to <c>[colors] players</c> in the game's
    /// settings, each with its name as the line's comment, and rebuilds the content.
    /// </summary>
    public sealed class PlayerColorsWindow : EditorWindow
    {
        /// <summary>The game's settings file, relative to the Unity project.</summary>
        private const string SettingsFile = "../content/settings/game.toml";

        private const int PerLine = 10; // as on the look card (.swatches in ShapeLand.uss)
        private const float Swatch = 26, Gap = 6;

        private static readonly Regex Entry = new(@"^\s*0x([0-9A-Fa-f]{6})\s*,?\s*(?:#\s*(.*?))?\s*$");

        [System.Serializable]
        private sealed class PlayerColor
        {
            public Color color = Color.white;
            public string name = "";
        }

        [SerializeField] private List<PlayerColor> colors = new();
        [SerializeField] private bool loaded;
        [SerializeField] private bool changed;

        private ReorderableList _list;
        private Vector2 _scroll;

        [MenuItem("Comet/ShapeLand/Player Colors")]
        public static void Open() => GetWindow<PlayerColorsWindow>("Player Colors");

        private static string FullPath => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), SettingsFile));

        private void OnGUI()
        {
            if (!loaded)
            {
                Load();
            }

            _list ??= MakeList();
            DrawSwatches();
            EditorGUILayout.Space();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUI.BeginChangeCheck();
            _list.DoLayoutList();
            if (EditorGUI.EndChangeCheck())
            {
                changed = true;
            }

            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reload") && (!changed || EditorUtility.DisplayDialog("Player colors", "Drop the unsaved changes?", "Drop", "Keep")))
                {
                    Load();
                }

                using (new EditorGUI.DisabledScope(!changed || colors.Count == 0))
                {
                    if (GUILayout.Button("Save and rebuild the content"))
                    {
                        Save();
                    }
                }
            }

            EditorGUILayout.HelpBox($"{colors.Count} colors, for both body and eyes. Save writes them to {SettingsFile}, keeping the rest of the file.", MessageType.None);
        }

        private ReorderableList MakeList()
        {
            var list = new ReorderableList(colors, typeof(PlayerColor), true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Colors (drag to reorder)"),
                elementHeight = EditorGUIUtility.singleLineHeight + 4,
                onAddCallback = l =>
                {
                    colors.Add(new PlayerColor { color = colors.Count > 0 ? colors[^1].color : Color.white });
                    l.index = colors.Count - 1;
                    changed = true;
                },
                onRemoveCallback = l =>
                {
                    colors.RemoveAt(l.index);
                    l.index = Mathf.Min(l.index, colors.Count - 1);
                    changed = true;
                },
                onReorderCallback = _ => changed = true,
            };
            list.drawElementCallback = (rect, index, _, _) =>
            {
                var entry = colors[index];
                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;
                var number = new Rect(rect.x, rect.y, 24, rect.height);
                var field = new Rect(number.xMax, rect.y, 70, rect.height);
                var hex = new Rect(field.xMax + 6, rect.y, 70, rect.height);
                var name = new Rect(hex.xMax + 6, rect.y, rect.xMax - hex.xMax - 6, rect.height);
                EditorGUI.LabelField(number, (index + 1).ToString());
                entry.color = EditorGUI.ColorField(field, GUIContent.none, entry.color, true, false, false);
                var typed = EditorGUI.DelayedTextField(hex, Hex(entry.color));
                if (ColorUtility.TryParseHtmlString(typed.StartsWith("#") ? typed : "#" + typed, out var parsed))
                {
                    entry.color = parsed;
                }

                entry.name = EditorGUI.TextField(name, entry.name);
            };
            return list;
        }

        // The swatches as the look card lays them out, ten a line; clicking one selects it in the list.
        private void DrawSwatches()
        {
            var lines = Mathf.Max(1, Mathf.CeilToInt(colors.Count / (float)PerLine));
            var area = GUILayoutUtility.GetRect(PerLine * (Swatch + Gap), lines * (Swatch + Gap) + Gap, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(new Rect(area.x, area.y, PerLine * (Swatch + Gap) + Gap, area.height), new Color(0.12f, 0.13f, 0.16f));
            for (var i = 0; i < colors.Count; i++)
            {
                var rect = new Rect(area.x + Gap + i % PerLine * (Swatch + Gap), area.y + Gap + i / PerLine * (Swatch + Gap), Swatch, Swatch);
                if (_list != null && _list.index == i)
                {
                    EditorGUI.DrawRect(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), Color.white);
                }

                EditorGUI.DrawRect(rect, colors[i].color);
                var tip = string.IsNullOrEmpty(colors[i].name) ? Hex(colors[i].color) : $"{colors[i].name} ({Hex(colors[i].color)})";
                if (GUI.Button(rect, new GUIContent("", tip), GUIStyle.none) && _list != null)
                {
                    _list.index = i;
                    GUI.FocusControl(null);
                }
            }
        }

        private static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);

        // The players array's lines: from "players = [" to its closing "]", within the [colors] table.
        private static (List<string> Lines, int Open, int Close)? Find()
        {
            if (!File.Exists(FullPath))
            {
                return null;
            }

            var lines = File.ReadAllLines(FullPath).ToList();
            var table = lines.FindIndex(l => l.Trim() == "[colors]");
            var open = table < 0 ? -1 : lines.FindIndex(table + 1, l => Regex.IsMatch(l, @"^\s*players\s*=\s*\[\s*(#.*)?$"));
            var close = open < 0 ? -1 : lines.FindIndex(open + 1, l => l.Trim().StartsWith("]"));
            return close < 0 ? null : (lines, open, close);
        }

        private void Load()
        {
            loaded = true;
            changed = false;
            colors.Clear();
            _list = null;
            var found = Find();
            if (found == null)
            {
                Debug.LogWarning($"Player colors: {SettingsFile} has no [colors] players = [ … ] array with one color a line.");
                return;
            }

            var (lines, open, close) = found.Value;
            for (var i = open + 1; i < close; i++)
            {
                var match = Entry.Match(lines[i]);
                if (match.Success)
                {
                    var rgb = uint.Parse(match.Groups[1].Value, NumberStyles.HexNumber);
                    colors.Add(new PlayerColor { color = GameView.Color(rgb), name = match.Groups[2].Value });
                }
            }
        }

        // Rewrites the array's lines, keeping the rest of the file (and its comments) as it is, then rebuilds the content.
        private void Save()
        {
            var found = Find();
            if (found == null)
            {
                EditorUtility.DisplayDialog("Player colors", $"{SettingsFile} has no [colors] players = [ … ] array with one color a line.", "OK");
                return;
            }

            var (lines, open, close) = found.Value;
            var entries = colors.Select(c => string.IsNullOrWhiteSpace(c.name) ? $"  0x{Hex(c.color)}," : $"  0x{Hex(c.color)}, # {c.name.Trim()}");
            lines.RemoveRange(open + 1, close - open - 1);
            lines.InsertRange(open + 1, entries);
            File.WriteAllText(FullPath, string.Join("\n", lines) + "\n");
            changed = false;
            Debug.Log($"Saved {colors.Count} player colors to {SettingsFile}; rebuilding the content.");
            ContentBuild.Build();
        }
    }
}

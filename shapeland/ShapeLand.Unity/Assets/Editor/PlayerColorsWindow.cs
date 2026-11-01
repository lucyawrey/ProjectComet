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
    /// swatches laid out ten a line as on the look card. Save writes them to <c>[colors]</c> in the game's settings as
    /// <c>key = 0xRRGGBB</c> lines in the list's order, and rebuilds the content. Each key gets a permanent number
    /// (ids.toml) that saved characters keep, so a renamed key is a new color to them.
    /// </summary>
    public sealed class PlayerColorsWindow : EditorWindow
    {
        /// <summary>The game's settings file, relative to the Unity project.</summary>
        private const string SettingsFile = "../content/settings/game.toml";

        private const int PerLine = 10; // as on the look card (.swatches in ShapeLand.uss)
        private const float Swatch = 26, Gap = 6;

        private static readonly Regex Entry = new(@"^\s*([a-z][a-z0-9_]*)\s*=\s*0x([0-9A-Fa-f]{6})\s*(?:#.*)?$");
        private static readonly Regex Key = new("^[a-z][a-z0-9_]*$");

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

            EditorGUILayout.HelpBox($"{colors.Count} colors, for both body and eyes. Each needs a key (lowercase letters, digits and _): saved characters keep colors by the key's number, so renaming one makes it a new color. Save writes them to {SettingsFile}, keeping the rest of the file.", MessageType.None);
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

        // The [colors] table's lines: after its header, up to the next table or the end of the file.
        private static (List<string> Lines, int Open, int Close)? Find()
        {
            if (!File.Exists(FullPath))
            {
                return null;
            }

            var lines = File.ReadAllLines(FullPath).ToList();
            var open = lines.FindIndex(l => l.Trim() == "[colors]");
            if (open < 0)
            {
                return null;
            }

            var close = lines.FindIndex(open + 1, l => l.TrimStart().StartsWith("["));
            return (lines, open, close < 0 ? lines.Count : close);
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
                Debug.LogWarning($"Player colors: {SettingsFile} has no [colors] table.");
                return;
            }

            var (lines, open, close) = found.Value;
            for (var i = open + 1; i < close; i++)
            {
                var match = Entry.Match(lines[i]);
                if (match.Success)
                {
                    var rgb = uint.Parse(match.Groups[2].Value, NumberStyles.HexNumber);
                    colors.Add(new PlayerColor { color = GameView.Color(rgb), name = match.Groups[1].Value });
                }
            }
        }

        // Rewrites the table's color lines in the list's order, keeping the rest of the file (and the table's comments)
        // as it is, then rebuilds the content.
        private void Save()
        {
            var found = Find();
            if (found == null)
            {
                EditorUtility.DisplayDialog("Player colors", $"{SettingsFile} has no [colors] table.", "OK");
                return;
            }

            var keys = colors.Select(c => c.name.Trim()).ToList();
            if (keys.FirstOrDefault(k => !Key.IsMatch(k)) is { } bad)
            {
                EditorUtility.DisplayDialog("Player colors", $"\"{bad}\" isn't a key: use a lowercase letter, then lowercase letters, digits and _.", "OK");
                return;
            }

            if (keys.GroupBy(k => k).FirstOrDefault(g => g.Count() > 1) is { } twice)
            {
                EditorUtility.DisplayDialog("Player colors", $"\"{twice.Key}\" is used twice: each color needs its own key.", "OK");
                return;
            }

            var (lines, open, close) = found.Value;
            var at = lines.FindIndex(open + 1, close - open - 1, l => Entry.IsMatch(l));
            at = at < 0 ? close : at;
            for (var i = close - 1; i > open; i--)
            {
                if (Entry.IsMatch(lines[i]))
                {
                    lines.RemoveAt(i);
                }
            }

            lines.InsertRange(at, colors.Select(c => $"{c.name.Trim()} = 0x{Hex(c.color)}"));
            File.WriteAllText(FullPath, string.Join("\n", lines) + "\n");
            changed = false;
            Debug.Log($"Saved {colors.Count} player colors to {SettingsFile}; rebuilding the content.");
            ContentBuild.Build();
        }
    }
}

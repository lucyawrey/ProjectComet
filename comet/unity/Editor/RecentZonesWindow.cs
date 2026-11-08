using System.IO;
using UnityEditor;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// The zones opened lately on this computer (<see cref="ZoneEditor.Recent"/>), one button each. Closes on picking
    /// one or on clicking away.
    /// </summary>
    internal sealed class RecentZonesWindow : EditorWindow
    {
        internal static new void Show()
        {
            var window = CreateInstance<RecentZonesWindow>();
            window.titleContent = new GUIContent("Open Recent Zone");
            window.minSize = new Vector2(240, 60);
            window.ShowAuxWindow();
        }

        private void OnGUI()
        {
            var recent = ZoneEditor.Recent();
            if (recent.Count == 0)
            {
                EditorGUILayout.LabelField("No zones opened yet.");
                return;
            }

            foreach (var folder in recent)
            {
                if (GUILayout.Button(new GUIContent(Path.GetFileName(folder), folder)))
                {
                    Close();
                    ZoneEditor.OpenRecent(folder);
                    GUIUtility.ExitGUI();
                }
            }
        }
    }
}

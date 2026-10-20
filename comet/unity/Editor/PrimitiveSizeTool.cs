using System;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// The tool context while zone placements are selected: Unity's own tools, except that scaling one primitive
    /// edits its size rather than its transform's scale, so its facets follow the polygon budget as it's dragged.
    /// Made the active context whenever a placement is selected.
    /// </summary>
    [EditorToolContext("Zone", typeof(ZonePlacement))]
    internal sealed class ZoneToolContext : EditorToolContext
    {
        // Unity's own transform tools, which it doesn't make public: the rest of this context.
        private static Type? Builtin(string name) => typeof(EditorTool).Assembly.GetType("UnityEditor." + name);

        protected override Type? GetEditorToolType(Tool tool) => tool switch
        {
            Tool.Scale => typeof(ZoneScaleTool),
            Tool.Move => Builtin("MoveTool"),
            Tool.Rotate => Builtin("RotateTool"),
            Tool.Rect => Builtin("RectTool"),
            Tool.Transform => Builtin("TransformTool"),
            _ => null,
        };

        // Follows the selection, checked each editor update (Unity's selection events don't always come): this
        // context while a placement is selected, Unity's own otherwise. Left alone while a tool of its own (the zone
        // place or terrain tool) is active, which a change of context would turn off.
        [InitializeOnLoadMethod]
        private static void FollowSelection() => EditorApplication.update += () =>
        {
            if (Tools.current == Tool.Custom && ToolManager.activeToolType != typeof(ZoneScaleTool))
            {
                return;
            }

            var placements = Selection.gameObjects.Any(g => g.TryGetComponent<ZonePlacement>(out _));
            var ours = ToolManager.activeContextType == typeof(ZoneToolContext);
            try
            {
                if (placements && !ours)
                {
                    ToolManager.SetActiveContext<ZoneToolContext>();
                }
                else if (!placements && ours)
                {
                    ToolManager.SetActiveContext<GameObjectToolContext>();
                }
            }
            catch (InvalidOperationException)
            {
                // Unity hasn't told its tools about the new selection yet: next update.
            }
        };
    }

    /// <summary>
    /// The Zone context's scale tool: one selected primitive is resized by its size (along its own axes); anything
    /// else scales as with Unity's tool, each object's scale multiplied by the handle's.
    /// </summary>
    [EditorTool("Zone Scale", typeof(ZonePlacement), typeof(ZoneToolContext))]
    internal sealed class ZoneScaleTool : EditorTool
    {
        private bool _resizing;
        private Vector3[]? _start;
        private Vector3 _handle = Vector3.one;

        public override GUIContent toolbarIcon => EditorGUIUtility.IconContent("ScaleTool");

        private static ZonePlacement? Single =>
            Selection.count == 1 && Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent<ZonePlacement>(out var p) && p.isPrimitive && !p.IsGroup ? p : null;

        public override void OnToolGUI(EditorWindow window)
        {
            if (Single is { } placement)
            {
                ResizePrimitive(placement);
            }
            else
            {
                ScaleSelection();
            }
        }

        private void ResizePrimitive(ZonePlacement placement)
        {
            var t = placement.transform;
            EditorGUI.BeginChangeCheck();
            var size = Handles.ScaleHandle(placement.size, t.position, t.rotation, HandleUtility.GetHandleSize(t.position));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(placement, "Resize primitive");
                placement.size = new Vector3(Mathf.Max(Mathf.Abs(size.x), 0.01f), Mathf.Max(Mathf.Abs(size.y), 0.01f), Mathf.Max(Mathf.Abs(size.z), 0.01f));
                placement.PreviewResize();
                _resizing = true;
            }

            // Back to the shared mesh, and its collider, once the drag ends.
            if (_resizing && GUIUtility.hotControl == 0)
            {
                _resizing = false;
                placement.Rebuild();
            }
        }

        // As Unity's scale tool: one handle at the active object, each selected object's scale multiplied by it.
        private void ScaleSelection()
        {
            var transforms = Selection.transforms;
            var active = Selection.activeTransform;
            if (active == null || transforms.Length == 0)
            {
                return;
            }

            if (GUIUtility.hotControl == 0)
            {
                _start = null;
                _handle = Vector3.one;
            }

            var rotation = transforms.Length == 1 ? active.rotation : Tools.handleRotation;
            EditorGUI.BeginChangeCheck();
            var handle = Handles.ScaleHandle(_handle, Tools.handlePosition, rotation, HandleUtility.GetHandleSize(Tools.handlePosition));
            if (EditorGUI.EndChangeCheck())
            {
                _start ??= transforms.Select(t => t.localScale).ToArray();
                _handle = handle;
                Undo.RecordObjects(transforms.Cast<UnityEngine.Object>().ToArray(), "Scale");
                for (var i = 0; i < transforms.Length; i++)
                {
                    transforms[i].localScale = Vector3.Scale(_start[i], handle);
                }
            }
        }
    }
}

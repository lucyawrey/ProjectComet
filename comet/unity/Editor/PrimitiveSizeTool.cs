using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// The scale tool for one selected primitive: its handle edits the primitive's size rather than its transform's
    /// scale, so its facets follow the polygon budget as it's dragged. Picking Unity's scale tool (R) with one
    /// primitive selected switches to it, and selecting anything else switches back.
    /// </summary>
    [EditorTool("Primitive Size", typeof(ZonePlacement))]
    internal sealed class PrimitiveSizeTool : EditorTool
    {
        private bool _resizing;
        private GUIContent? _icon;

        public override GUIContent toolbarIcon => _icon ??= new GUIContent(EditorGUIUtility.IconContent("ScaleTool").image, "Primitive Size: resize the selected primitive (R)");

        public override bool IsAvailable() => Single != null;

        // The one selected primitive, if exactly one is selected.
        private static ZonePlacement? Single =>
            Selection.count == 1 && Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent<ZonePlacement>(out var p) && p.isPrimitive && !p.IsGroup ? p : null;

        [InitializeOnLoadMethod]
        private static void SwapWithScale()
        {
            void Swap()
            {
                if (Single != null && Tools.current == Tool.Scale)
                {
                    ToolManager.SetActiveTool<PrimitiveSizeTool>();
                }
                else if (Single == null && ToolManager.activeToolType == typeof(PrimitiveSizeTool))
                {
                    Tools.current = Tool.Scale;
                }
            }

            ToolManager.activeToolChanged += Swap;
            Selection.selectionChanged += Swap;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (Single is not { } placement)
            {
                return;
            }

            var t = placement.transform;
            EditorGUI.BeginChangeCheck();
            // Along the primitive's own axes, as its size is.
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
    }
}

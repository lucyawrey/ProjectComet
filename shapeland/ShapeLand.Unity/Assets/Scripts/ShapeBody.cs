using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// A player's drawn shape, at the player's feet, built from its look (a group, <see cref="GameView.BuildShape"/>):
    /// parts with the role <c>body</c> in the player's color, <c>eyes</c> in their eye color, others in their own, and
    /// how far the shape has faded. While it fades it swaps to transparent copies of its materials (made the first
    /// time), which cast no shadow; fully visible, it's drawn opaque as usual. Shown through walls, it's also drawn in
    /// its body color wherever something hides it. The materials it makes are freed with it.
    /// </summary>
    public sealed class ShapeBody : MonoBehaviour
    {
        private sealed class Part
        {
            public MeshRenderer Renderer;
            public string Role;
            public Color Own;
            public Material Template;
            public Material FadeTemplate;
            public Material Opaque;
            public Material Fade;
        }

        private readonly List<Part> _parts = new List<Part>();
        private Transform _look;
        private float _faded;
        private Color _bodyColor;
        private Material _mask;
        private Material _silhouetteTemplate;
        private Material _silhouette;

        /// <summary>The body's renderers (role <c>body</c>).</summary>
        public IReadOnlyList<MeshRenderer> Body => _parts.Where(p => p.Role == "body").Select(p => p.Renderer).ToList();

        /// <summary>The eyes' renderers (role <c>eyes</c>).</summary>
        public IReadOnlyList<MeshRenderer> Eyes => _parts.Where(p => p.Role == "eyes").Select(p => p.Renderer).ToList();

        /// <summary>The middle of the top of the body, in world space, where a name tag goes above.</summary>
        public Vector3 Top
        {
            get
            {
                var body = Body.Count > 0 ? Body : _parts.Select(p => p.Renderer).ToList();
                var top = body.Count > 0 ? body.Max(r => r.bounds.max.y) : transform.position.y;
                return new Vector3(transform.position.x, top, transform.position.z);
            }
        }

        /// <summary>How far the shape has faded: 0 fully visible, 1 gone.</summary>
        public float Faded
        {
            get => _faded;
            set => SetFaded(Mathf.Clamp01(value));
        }

        /// <summary>
        /// An empty shape named <paramref name="name"/> at <paramref name="parent"/>'s origin, its look drawn
        /// <paramref name="scale"/> times its size and <paramref name="hover"/> meters (before scaling) above the feet.
        /// </summary>
        public static ShapeBody Create(string name, Transform parent, float scale, float hover)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var shape = root.AddComponent<ShapeBody>();
            shape._look = new GameObject("Look").transform;
            shape._look.SetParent(root.transform, false);
            shape._look.localPosition = Vector3.up * (hover * scale);
            shape._look.localScale = Vector3.one * scale;
            return shape;
        }

        /// <summary>
        /// Adds one part of the look, placed in the look's space, drawn with <paramref name="material"/> (and
        /// <paramref name="fadeMaterial"/> while fading), in its role's color or else <paramref name="own"/>.
        /// </summary>
        public void AddPart(Mesh mesh, Vector3 position, Quaternion rotation, string role, Color own, Material material, Material fadeMaterial)
        {
            var part = new GameObject(role ?? "part");
            part.transform.SetParent(_look, false);
            part.transform.SetLocalPositionAndRotation(position, rotation);
            part.layer = gameObject.layer;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            var added = new Part { Renderer = renderer, Role = role, Own = own, Template = material, FadeTemplate = fadeMaterial };
            _parts.Add(added);
            Draw(added, material);
        }

        /// <summary>Colors the body and eyes, each as 0xRRGGBB; other parts keep their own color.</summary>
        public void SetColors(uint bodyColor, uint eyeColor)
        {
            FreeMaterials();
            _bodyColor = GameView.Color(bodyColor);
            foreach (var part in _parts)
            {
                var color = part.Role == "body" ? GameView.Color(bodyColor) : part.Role == "eyes" ? GameView.Color(eyeColor) : part.Own;
                part.Opaque = new Material(part.Template) { color = color };
                Draw(part, part.Opaque);
                part.Renderer.enabled = true;
            }

            _faded = 0;
            ColorSilhouette();
        }

        /// <summary>
        /// Draws the shape wherever blocks, props or terrain hide it, flat in its body color at
        /// <paramref name="silhouette"/>'s opacity, after <paramref name="mask"/> marks where it's in sight (the
        /// two materials of Comet's Silhouette shader).
        /// </summary>
        public void ShowThroughWalls(Material mask, Material silhouette)
        {
            _mask = mask;
            _silhouetteTemplate = silhouette;
            _silhouette = new Material(silhouette);
            foreach (var part in _parts)
            {
                Draw(part, part.Renderer.sharedMaterial);
            }

            ColorSilhouette();
        }

        /// <summary>Turns off shadows and moves the shape's parts to <paramref name="layer"/>, as the join card's preview does.</summary>
        public void ShowOnLayer(int layer)
        {
            foreach (var part in _parts)
            {
                part.Renderer.gameObject.layer = layer;
                part.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private void SetFaded(float faded)
        {
            if (faded == _faded)
            {
                return;
            }

            _faded = faded;
            foreach (var part in _parts)
            {
                part.Renderer.enabled = faded < 1;
                if (faded <= 0)
                {
                    Draw(part, part.Opaque);
                    continue;
                }

                if (part.Fade == null)
                {
                    part.Fade = new Material(part.FadeTemplate) { color = part.Opaque != null ? part.Opaque.color : part.Own };
                }

                var color = part.Fade.color;
                color.a = 1 - faded;
                part.Fade.color = color;
                Draw(part, part.Fade);
            }

            ColorSilhouette();
        }

        // The silhouette's materials draw the part's one submesh again, after its own.
        private void Draw(Part part, Material material) =>
            part.Renderer.sharedMaterials = _silhouette != null ? new[] { material, _mask, _silhouette } : new[] { material };

        private void ColorSilhouette()
        {
            if (_silhouette != null)
            {
                _silhouette.color = new Color(_bodyColor.r, _bodyColor.g, _bodyColor.b, _silhouetteTemplate.color.a * (1 - _faded));
            }
        }

        private void FreeMaterials()
        {
            foreach (var part in _parts)
            {
                if (part.Opaque != null)
                {
                    Destroy(part.Opaque);
                }

                if (part.Fade != null)
                {
                    Destroy(part.Fade);
                }

                part.Opaque = part.Fade = null;
            }
        }

        private void OnDestroy()
        {
            FreeMaterials();
            if (_silhouette != null)
            {
                Destroy(_silhouette);
            }
        }
    }
}

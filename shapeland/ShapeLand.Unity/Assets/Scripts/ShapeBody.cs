using System.Collections.Generic;
using System.Linq;
using Comet.Unity;
using UnityEngine;

namespace ShapeLand.Client
{
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
            public readonly Material[] Alone = new Material[1];
            public readonly Material[] WithSilhouette = new Material[3];
        }

        private readonly List<Part> _parts = new List<Part>();
        private Transform _look;
        private float _faded;
        private Color _bodyColor;
        private Material _mask;
        private Material _silhouetteTemplate;
        private Material _silhouette;

        public IReadOnlyList<MeshRenderer> Body => _parts.Where(p => p.Role == "body").Select(p => p.Renderer).ToList();

        public IReadOnlyList<MeshRenderer> Eyes => _parts.Where(p => p.Role == "eyes").Select(p => p.Renderer).ToList();

        public Vector3 Top
        {
            get
            {
                var body = Body.Count > 0 ? Body : _parts.Select(p => p.Renderer).ToList();
                var top = body.Count > 0 ? body.Max(r => r.bounds.max.y) : transform.position.y;
                return new Vector3(transform.position.x, top, transform.position.z);
            }
        }

        public float Faded
        {
            get => _faded;
            set => SetFaded(Mathf.Clamp01(value));
        }

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

        public void AddPart(Mesh mesh, Vector3 position, Quaternion rotation, string role, Color own, Material material, Material fadeMaterial)
        {
            var part = new GameObject(role ?? "part");
            part.transform.SetParent(_look, false);
            part.transform.SetLocalPositionAndRotation(position, rotation);
            part.layer = gameObject.layer;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            var added = new Part
            {
                Renderer = renderer,
                Role = role,
                Own = own,
                Template = material,
                FadeTemplate = fadeMaterial,
            };
            _parts.Add(added);
            Draw(added, material);
        }

        public void SetColors(uint bodyColor, uint eyeColor)
        {
            FreeMaterials();
            _bodyColor = Conversions.FromRgb(bodyColor);
            foreach (var part in _parts)
            {
                var color =
                    part.Role == "body" ? Conversions.FromRgb(bodyColor)
                    : part.Role == "eyes" ? Conversions.FromRgb(eyeColor)
                    : part.Own;
                part.Opaque = new Material(part.Template) { color = color };
                Draw(part, part.Opaque);
                part.Renderer.enabled = true;
            }

            _faded = 0;
            ColorSilhouette();
        }

        public void ShowThroughWalls(Material mask, Material silhouette)
        {
            if (_silhouette != null)
            {
                Destroy(_silhouette);
            }

            _mask = mask;
            _silhouetteTemplate = silhouette;
            _silhouette = mask != null && silhouette != null ? new Material(silhouette) : null;
            foreach (var part in _parts)
            {
                Draw(part, part.Renderer.sharedMaterial);
            }

            ColorSilhouette();
        }

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

        private void Draw(Part part, Material material)
        {
            if (_silhouette == null)
            {
                part.Alone[0] = material;
                part.Renderer.sharedMaterials = part.Alone;
                return;
            }

            part.WithSilhouette[0] = material;
            part.WithSilhouette[1] = _mask;
            part.WithSilhouette[2] = _silhouette;
            part.Renderer.sharedMaterials = part.WithSilhouette;
        }

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

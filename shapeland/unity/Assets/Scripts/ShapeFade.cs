using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// A shape's body and eye materials, in its colours. While it fades it swaps to transparent copies (made the
    /// first time), which cast no shadow; fully visible, it's drawn opaque as usual.
    /// </summary>
    public sealed class ShapeFade
    {
        private readonly MeshRenderer _body;
        private readonly MeshRenderer _eyes;
        private readonly Material _bodyOpaque;
        private readonly Material _eyesOpaque;
        private readonly Material _bodyFadeTemplate;
        private readonly Material _eyesFadeTemplate;
        private Material _bodyFade;
        private Material _eyesFade;

        public ShapeFade(MeshRenderer body, MeshRenderer eyes, Material bodyFadeTemplate, Material eyesFadeTemplate)
        {
            _body = body;
            _eyes = eyes;
            _bodyOpaque = body.sharedMaterial;
            _eyesOpaque = eyes.sharedMaterial;
            _bodyFadeTemplate = bodyFadeTemplate;
            _eyesFadeTemplate = eyesFadeTemplate;
        }

        /// <summary>How far the shape has faded: 0 fully visible, 1 gone.</summary>
        public float Faded { get; private set; }

        public void Set(float faded)
        {
            faded = Mathf.Clamp01(faded);
            if (faded == Faded)
            {
                return;
            }

            Faded = faded;
            var visible = faded < 1;
            _body.enabled = visible;
            _eyes.enabled = visible;
            if (faded <= 0)
            {
                Use(_bodyOpaque, _eyesOpaque);
                return;
            }

            if (_bodyFade == null)
            {
                _bodyFade = new Material(_bodyFadeTemplate) { color = _bodyOpaque.color };
                _eyesFade = new Material(_eyesFadeTemplate) { color = _eyesOpaque.color };
            }

            SetAlpha(_bodyFade, 1 - faded);
            SetAlpha(_eyesFade, 1 - faded);
            Use(_bodyFade, _eyesFade);
        }

        /// <summary>Destroys the materials made for this shape.</summary>
        public void Destroy()
        {
            Object.Destroy(_bodyOpaque);
            Object.Destroy(_eyesOpaque);
            if (_bodyFade != null)
            {
                Object.Destroy(_bodyFade);
                Object.Destroy(_eyesFade);
            }
        }

        private void Use(Material body, Material eyes)
        {
            _body.sharedMaterial = body;
            _eyes.sharedMaterial = eyes;
        }

        private static void SetAlpha(Material material, float alpha)
        {
            var colour = material.color;
            colour.a = alpha;
            material.color = colour;
        }
    }
}

using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// A player's drawn shape, on the object at their feet: the body and eyes in the player's colors, and how far the
    /// shape has faded. While it fades it swaps to transparent copies of its materials (made the first time), which
    /// cast no shadow; fully visible, it's drawn opaque as usual. The materials it makes are freed with it.
    /// </summary>
    public sealed class ShapeBody : MonoBehaviour
    {
        [SerializeField] private MeshRenderer body;
        [SerializeField] private MeshRenderer eyes;
        [Tooltip("The transparent material the body fades with; its color is set from the body's.")]
        [SerializeField] private Material bodyFadeMaterial;
        [Tooltip("The transparent material the eyes fade with; their color is set from the eyes'.")]
        [SerializeField] private Material eyesFadeMaterial;

        private Material _bodyTemplate;
        private Material _eyesTemplate;
        private Material _bodyOpaque;
        private Material _eyesOpaque;
        private Material _bodyFade;
        private Material _eyesFade;
        private float _faded;

        /// <summary>The middle of the top of the body, in world space, where a name tag goes above.</summary>
        public Vector3 Top => new Vector3(transform.position.x, body.bounds.max.y, transform.position.z);

        /// <summary>How far the shape has faded: 0 fully visible, 1 gone.</summary>
        public float Faded
        {
            get => _faded;
            set => SetFaded(Mathf.Clamp01(value));
        }

        /// <summary>Sets the parts for a shape built in code; prefabs set them in the editor.</summary>
        public void Setup(MeshRenderer bodyRenderer, MeshRenderer eyesRenderer, Material bodyFade, Material eyesFade)
        {
            body = bodyRenderer;
            eyes = eyesRenderer;
            bodyFadeMaterial = bodyFade;
            eyesFadeMaterial = eyesFade;
        }

        /// <summary>Colors the body and eyes, each as 0xRRGGBB.</summary>
        public void SetColors(uint bodyColor, uint eyeColor)
        {
            if (_bodyTemplate == null)
            {
                _bodyTemplate = body.sharedMaterial;
                _eyesTemplate = eyes.sharedMaterial;
            }

            FreeMaterials();
            _bodyOpaque = new Material(_bodyTemplate) { color = GameView.Color(bodyColor) };
            _eyesOpaque = new Material(_eyesTemplate) { color = GameView.Color(eyeColor) };
            body.sharedMaterial = _bodyOpaque;
            eyes.sharedMaterial = _eyesOpaque;
            body.enabled = eyes.enabled = true;
            _faded = 0;
        }

        /// <summary>Turns off shadows and moves the shape's parts to <paramref name="layer"/>, as the join card's preview does.</summary>
        public void ShowOnLayer(int layer)
        {
            foreach (var part in new[] { body, eyes })
            {
                part.gameObject.layer = layer;
                part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private void SetFaded(float faded)
        {
            if (faded == _faded)
            {
                return;
            }

            _faded = faded;
            body.enabled = eyes.enabled = faded < 1;
            if (faded <= 0)
            {
                body.sharedMaterial = _bodyOpaque;
                eyes.sharedMaterial = _eyesOpaque;
                return;
            }

            if (_bodyFade == null)
            {
                _bodyFade = new Material(bodyFadeMaterial) { color = _bodyOpaque.color };
                _eyesFade = new Material(eyesFadeMaterial) { color = _eyesOpaque.color };
            }

            SetAlpha(_bodyFade, 1 - faded);
            SetAlpha(_eyesFade, 1 - faded);
            body.sharedMaterial = _bodyFade;
            eyes.sharedMaterial = _eyesFade;
        }

        private static void SetAlpha(Material material, float alpha)
        {
            var color = material.color;
            color.a = alpha;
            material.color = color;
        }

        private void FreeMaterials()
        {
            foreach (var material in new[] { _bodyOpaque, _eyesOpaque, _bodyFade, _eyesFade })
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }

            _bodyFade = _eyesFade = null;
        }

        private void OnDestroy() => FreeMaterials();
    }
}

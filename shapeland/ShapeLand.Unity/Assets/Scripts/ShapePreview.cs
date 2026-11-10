using ShapeLand.Shared.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    public sealed class ShapePreview : MonoBehaviour
    {
        [SerializeField]
        private Camera previewCamera;

        [SerializeField]
        private float degreesPerSecond = 40;

        [Tooltip("The render texture's size in pixels.")]
        [SerializeField]
        private int textureSize = 256;

        private RenderTexture _texture;
        private ShapeBody _shape;
        private Quaternion _facing = Quaternion.Euler(0, 200, 0);

        public void ShowIn(VisualElement target)
        {
            _texture = new RenderTexture(textureSize, textureSize, 24, RenderTextureFormat.ARGB32) { name = "Shape preview" };
            previewCamera.targetTexture = _texture;
            target.style.backgroundImage = Background.FromRenderTexture(_texture);
        }

        public void Show(GameView view, Shape shape, uint color, uint eyeColor)
        {
            if (_shape != null)
            {
                _facing = _shape.transform.localRotation;
                Destroy(_shape.gameObject);
            }

            _shape = view.BuildShape(shape, color, eyeColor, transform);
            _shape.transform.localRotation = _facing;
            _shape.ShowOnLayer(GameView.PreviewLayer);
        }

        private void Update()
        {
            if (_shape != null)
            {
                _shape.transform.Rotate(0, degreesPerSecond * Time.unscaledDeltaTime, 0);
            }
        }

        private void OnDestroy()
        {
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }
    }
}

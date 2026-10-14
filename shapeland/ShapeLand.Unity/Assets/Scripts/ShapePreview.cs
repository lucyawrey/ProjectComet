using ShapeLand.Shared.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// The join card's turning shape (<c>Assets/Prefabs/ShapePreview.prefab</c>): the chosen shape, built like any
    /// player's, in front of its own camera on a layer the main camera doesn't draw (<see cref="GameView.PreviewLayer"/>),
    /// far below the island, and shown on the card through a render texture.
    /// </summary>
    public sealed class ShapePreview : MonoBehaviour
    {
        [SerializeField] private Camera previewCamera;
        [SerializeField] private float degreesPerSecond = 40;
        [Tooltip("The render texture's size in pixels.")]
        [SerializeField] private int textureSize = 256;

        private RenderTexture _texture;
        private ShapeBody _shape;
        private Quaternion _facing = Quaternion.Euler(0, 200, 0);

        /// <summary>Draws into <paramref name="target"/>'s background from now on.</summary>
        public void ShowIn(VisualElement target)
        {
            _texture = new RenderTexture(textureSize, textureSize, 24, RenderTextureFormat.ARGB32) { name = "Shape preview" };
            previewCamera.targetTexture = _texture;
            target.style.backgroundImage = Background.FromRenderTexture(_texture);
        }

        /// <summary>Shows <paramref name="shape"/> in the given colors, keeping the way the last one faced.</summary>
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

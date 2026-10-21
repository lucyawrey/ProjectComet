using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ShapeLand.Client
{
    /// <summary>
    /// Touch controls for phones, on a uGUI canvas over the HUD: a floating stick on the left half of the screen
    /// (<see cref="FloatingStick"/>), a camera drag on the right half (<see cref="TouchLook"/>) and a jump button.
    /// The stick and button are Input System on-screen controls, a virtual gamepad, so the game's actions read them
    /// as they read a real one. They show while the player can move and the screen has been touched (from the start
    /// on a phone page), and hide when a keyboard or a real gamepad is used away from a phone.
    /// </summary>
    public sealed class TouchControls : MonoBehaviour
    {
        // Canvas units: the canvas is this tall whatever the screen, so sizes are about CSS pixels on a landscape phone.
        private const float ReferenceHeight = 400;
        private const float StickRange = 60;
        private const float BaseSize = 2 * StickRange + 24;
        private const float KnobSize = 56;
        private const float JumpSize = 88;
        private const float Margin = 28;

        private GameObject _canvas;
        private bool _touched;

        /// <summary>Whether the player can move now (in the game, not typing or on the look card): set each frame by the HUD.</summary>
        public bool Allowed { get; set; }

        /// <summary>Builds the controls (hidden until allowed and touched), turning <paramref name="camera"/> with drags.</summary>
        public void Begin(OrbitCamera camera, string jumpLabel)
        {
            _touched = WebPage.IsPhone;
            if (EventSystem.current == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }

            _canvas = new GameObject("Touch controls", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas.SetActive(false);
            _canvas.transform.SetParent(transform, false);
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2 * ReferenceHeight, ReferenceHeight);
            scaler.matchWidthOrHeight = 1;

            var look = Region("Camera drag", new Vector2(0.5f, 0), Vector2.one).AddComponent<TouchLook>();
            look.Camera = camera;

            var stickRegion = Region("Stick", Vector2.zero, new Vector2(0.5f, 1));
            var stick = stickRegion.AddComponent<FloatingStick>();
            stick.Range = StickRange;
            stick.Base = Circle("Base", stickRegion.transform, BaseSize, new Color(1, 1, 1, 0.18f), ring: true);
            stick.Knob = Circle("Knob", stickRegion.transform, KnobSize, new Color(1, 1, 1, 0.55f), ring: false);
            stick.Base.gameObject.SetActive(false);
            stick.Knob.gameObject.SetActive(false);

            var jump = Circle("Jump", _canvas.transform, JumpSize, new Color(1, 1, 1, 0.3f), ring: false);
            jump.anchorMin = jump.anchorMax = new Vector2(1, 0);
            jump.anchoredPosition = new Vector2(-Margin - JumpSize / 2, Margin + JumpSize / 2);
            jump.GetComponent<Image>().raycastTarget = true;
            jump.gameObject.AddComponent<OnScreenButton>().controlPath = "<Gamepad>/buttonSouth";
            Label(jump, jumpLabel);
        }

        private void Update()
        {
            if (_canvas == null)
            {
                return;
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                _touched = true;
            }
            else if (!WebPage.IsPhone && (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame || RealGamepadUsed()))
            {
                _touched = false;
            }

            var show = _touched && Allowed;
            if (_canvas.activeSelf != show)
            {
                _canvas.SetActive(show);
            }
        }

        // A real gamepad's button or stick, not the virtual one the touch controls drive.
        private static bool RealGamepadUsed()
        {
            foreach (var gamepad in Gamepad.all)
            {
                if (gamepad.native && (gamepad.wasUpdatedThisFrame && (gamepad.buttonSouth.isPressed || gamepad.startButton.isPressed
                    || gamepad.leftStick.ReadValue().sqrMagnitude > 0.25f || gamepad.rightStick.ReadValue().sqrMagnitude > 0.25f)))
                {
                    return true;
                }
            }

            return false;
        }

        // A clear, touchable region of the canvas between two anchors.
        private GameObject Region(string name, Vector2 from, Vector2 to)
        {
            var region = new GameObject(name, typeof(RectTransform), typeof(Image));
            region.transform.SetParent(_canvas.transform, false);
            var rect = (RectTransform)region.transform;
            rect.anchorMin = from;
            rect.anchorMax = to;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            region.GetComponent<Image>().color = Color.clear;
            return region;
        }

        // A disc or ring of `size` canvas units, centered on its anchors; it doesn't catch touches unless told to.
        private static RectTransform Circle(string name, Transform parent, float size, Color color, bool ring)
        {
            var circle = new GameObject(name, typeof(RectTransform), typeof(Image));
            circle.transform.SetParent(parent, false);
            var image = circle.GetComponent<Image>();
            image.sprite = ring ? Ring : Disc;
            image.color = color;
            image.raycastTarget = false;
            var rect = (RectTransform)circle.transform;
            rect.sizeDelta = new Vector2(size, size);
            return rect;
        }

        private static void Label(RectTransform parent, string text)
        {
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(parent, false);
            var rect = (RectTransform)label.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var content = label.GetComponent<Text>();
            content.text = text;
            content.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            content.fontSize = 18;
            content.fontStyle = FontStyle.Bold;
            content.alignment = TextAnchor.MiddleCenter;
            content.color = new Color(1, 1, 1, 0.9f);
            content.raycastTarget = false;
        }

        private static Sprite _disc;
        private static Sprite _ring;
        private static Sprite Disc => _disc ??= CircleSprite(1);
        private static Sprite Ring => _ring ??= CircleSprite(0.08f);

        // A white circle drawn into a texture, smoothed at its edges: filled, or a ring `thickness` of its radius wide.
        private static Sprite CircleSprite(float thickness)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            var radius = size / 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                    var outer = Mathf.Clamp01(radius - distance);
                    var inner = thickness >= 1 ? 1 : Mathf.Clamp01(distance - radius * (1 - thickness));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * outer * inner));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}

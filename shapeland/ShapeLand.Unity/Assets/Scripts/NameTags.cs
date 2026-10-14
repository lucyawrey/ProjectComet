using System.Collections.Generic;
using Comet.Client;
using ShapeLand.Shared.Messages;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// A name tag over each other player's shape and a speech bubble with anyone's latest chat line, the player's
    /// own included. They're placed over the shapes every frame in screen space, so they keep one readable size.
    /// </summary>
    [DefaultExecutionOrder(100)] // after the camera has moved
    [RequireComponent(typeof(Hud))]
    public sealed class NameTags : MonoBehaviour
    {
        /// <summary>Tags and bubbles further away than this, in meters, aren't shown.</summary>
        private const float TagDistance = 45;

        // How far above a shape's top its name tag sits, in meters, and the bubble above the tag, in panel pixels.
        private const float TagLift = 0.25f;
        private const float BubbleGap = 26;

        private readonly Dictionary<uint, Tag> _tags = new Dictionary<uint, Tag>();
        private Hud _hud;
        private VisualElement _layer;

        private void OnEnable()
        {
            _hud = GetComponent<Hud>();
            _layer = GetComponent<UIDocument>().rootVisualElement.Q("tags");
            _hud.Join.ChatReceived += OnChat;
            _hud.Join.OtherLeft += OnLeft;
            _hud.Join.ConnectionLost += OnConnectionLost;
        }

        private void OnDisable()
        {
            _hud.Join.ChatReceived -= OnChat;
            _hud.Join.OtherLeft -= OnLeft;
            _hud.Join.ConnectionLost -= OnConnectionLost;
        }

        private void OnConnectionLost(CloseReason reason)
        {
            foreach (var tag in _tags.Values)
            {
                tag.Remove();
            }

            _tags.Clear();
        }

        private void OnChat(ChatMessage message)
        {
            if (_hud.View.Spawns.TryGetValue(message.EntityId, out var speaker))
            {
                TagFor(message.EntityId, speaker).Say(message.Text, Time.unscaledTime);
            }
        }

        private void OnLeft(uint entityId)
        {
            if (_tags.Remove(entityId, out var tag))
            {
                tag.Remove();
            }
        }

        private Tag TagFor(uint entityId, PlayerSpawn spawn)
        {
            if (!_tags.TryGetValue(entityId, out var tag))
            {
                var own = entityId == _hud.Join.Session.EntityId;
                tag = new Tag(_layer, own ? null : spawn.Name);
                _tags[entityId] = tag;
            }
            else
            {
                tag.Rename(spawn.Name); // they may have changed it
            }

            return tag;
        }

        // Puts each player's tag and bubble over their shape, or hides them when behind the camera, too far or faded out.
        private void LateUpdate()
        {
            var panel = _layer.panel;
            if (!_hud.Shown || panel == null)
            {
                return;
            }

            var view = _hud.View;
            var camera = view.Camera;
            foreach (var pair in view.SpawnTable)
            {
                var tag = TagFor(pair.Key, pair.Value);
                var shape = view.ShapeOf(pair.Key);
                if (shape == null)
                {
                    tag.Hide();
                    continue;
                }

                var screen = camera.WorldToScreenPoint(shape.Top + Vector3.up * TagLift);
                if (screen.z <= 0 || screen.z > TagDistance || shape.Faded >= 1)
                {
                    tag.Hide();
                    continue;
                }

                var at = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
                tag.Place(at, 1 - shape.Faded, Time.unscaledTime);
            }
        }

        /// <summary>The bubble text showing over a player now, or null.</summary>
        public string BubbleOf(uint entityId) => _tags.TryGetValue(entityId, out var tag) ? tag.Bubble(Time.unscaledTime) : null;

        /// <summary>A player's name tag (none for the player's own shape) and speech bubble.</summary>
        private sealed class Tag
        {
            private readonly Label _name;
            private readonly VisualElement _bubble;
            private readonly Label _text;
            private float _bubbleUntil = float.NegativeInfinity;

            /// <summary>A tag on <paramref name="layer"/>; a null <paramref name="name"/> makes a bubble without a name.</summary>
            public Tag(VisualElement layer, string name)
            {
                if (name != null)
                {
                    _name = new Label(name) { pickingMode = PickingMode.Ignore };
                    _name.AddToClassList("name-tag");
                    layer.Add(_name);
                }

                _bubble = new VisualElement { pickingMode = PickingMode.Ignore };
                _bubble.AddToClassList("bubble");
                _text = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
                var tail = new VisualElement { pickingMode = PickingMode.Ignore };
                tail.AddToClassList("bubble-tail");
                _bubble.Add(_text);
                _bubble.Add(tail);
                _bubble.style.display = DisplayStyle.None;
                layer.Add(_bubble);
            }

            public void Rename(string name)
            {
                if (_name != null && _name.text != name)
                {
                    _name.text = name;
                }
            }

            /// <summary>Shows <paramref name="text"/> in the bubble, longer for longer lines.</summary>
            public void Say(string text, float now)
            {
                _text.text = text;
                _bubbleUntil = now + Mathf.Min(10, 4 + 0.06f * text.Length);
            }

            /// <summary>The bubble's text while it shows, else null.</summary>
            public string Bubble(float now) => now < _bubbleUntil ? _text.text : null;

            /// <summary>Shows the tag (and the bubble, while it lasts) at <paramref name="at"/> in panel coordinates.</summary>
            public void Place(Vector2 at, float opacity, float now)
            {
                if (_name != null)
                {
                    _name.style.display = DisplayStyle.Flex;
                    _name.style.left = at.x;
                    _name.style.top = at.y;
                    _name.style.opacity = opacity;
                }

                var talking = now < _bubbleUntil;
                _bubble.style.display = talking ? DisplayStyle.Flex : DisplayStyle.None;
                if (talking)
                {
                    _bubble.style.left = at.x;
                    _bubble.style.top = _name != null ? at.y - BubbleGap : at.y;
                    _bubble.style.opacity = opacity;
                }
            }

            public void Hide()
            {
                if (_name != null)
                {
                    _name.style.display = DisplayStyle.None;
                }

                _bubble.style.display = DisplayStyle.None;
            }

            /// <summary>Takes the tag off the screen for good.</summary>
            public void Remove()
            {
                _name?.RemoveFromHierarchy();
                _bubble.RemoveFromHierarchy();
            }
        }
    }
}

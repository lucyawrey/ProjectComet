using UnityEngine;

namespace Comet.Unity
{
    [CreateAssetMenu(fileName = "Collision Colors", menuName = "Comet/Collision Colors")]
    public sealed class CollisionColors : ScriptableObject
    {
        public Color terrain = new Color(0.8f, 0.72f, 0.45f, 0.45f);
        public Color spawn = new Color(0.4f, 0.85f, 0.4f, 0.7f);
        public Color teleporter = new Color(0.4f, 0.7f, 1, 0.7f);
        public Color trigger = new Color(1, 0.6f, 0.25f, 0.7f);

        [Tooltip("Players' bodies.")]
        public Color body = new Color(1, 0.9f, 0.3f, 0.6f);

        [Tooltip("The body left where the server snapped a player back.")]
        public Color snapBack = new Color(1, 0.25f, 0.25f, 0.8f);

        private static CollisionColors? _defaults;

        public static System.Func<CollisionColors?>? Find { get; set; }

        public static CollisionColors Editing
        {
            get
            {
                if (Find?.Invoke() is { } found)
                {
                    return found;
                }

                if (_defaults == null)
                {
                    _defaults = CreateInstance<CollisionColors>();
                    _defaults.hideFlags = HideFlags.DontSave;
                }

                return _defaults;
            }
        }

        public event System.Action? Changed;

        private void OnValidate() => Changed?.Invoke();
    }
}

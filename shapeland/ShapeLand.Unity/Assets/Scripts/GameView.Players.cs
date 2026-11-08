using Comet.Client;
using Comet.Content;
using Comet.Protocol.Messages;
using Comet.Unity;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using UnityEngine;

namespace ShapeLand.Client
{
    public sealed partial class GameView
    {
        // On joining, the player and their shape; on a change of look, a new shape where they stand, so the motor and
        // the camera carry on as they were (the server's snap-back has already put the player where it has them).
        private void OnOwnSpawned(PlayerSpawn spawn)
        {
            WarmUpFades();
            _spawns[spawn.EntityId] = spawn;
            var shape = _join.Content.Shapes[spawn.Shape];
            if (Player == null)
            {
                Player = new GameObject().AddComponent<LocalPlayer>();
                Player.Begin(
                    _join.Session,
                    _world,
                    _join.Content.Movement,
                    shape,
                    new Vector3(spawn.X, spawn.Y, spawn.Z),
                    spawn.Facing,
                    orbitCamera,
                    _controls
                );
                orbitCamera.Follow(Player.transform, _controls, _world);
                _join.Session.Corrected += OnCorrected;
                OnCorrected(
                    new PositionCorrection
                    {
                        X = spawn.X,
                        Y = spawn.Y,
                        Z = spawn.Z,
                    }
                ); // joining on a spawn doesn't claim it
            }
            else
            {
                Destroy(_playerShape.gameObject);
                Player.ChangeShape(shape);
            }

            Player.name = $"{spawn.Name} (you)";
            _playerShape = BuildShape(shape, _content.ColorRgb(spawn.Color), _content.ColorRgb(spawn.EyeColor), Player.transform);
            ApplyShowThroughWalls();
        }

        // Draws the fade and silhouette materials once, out of sight, so their shaders are ready (compiled) before a
        // shape first fades or hides behind something, rather than hitching then. (A freshly opened editor still
        // shows its cyan placeholder on the first fade.)
        private void WarmUpFades()
        {
            _warmUpMesh ??= new Mesh
            {
                name = "Fade warm-up",
                vertices = new[] { Vector3.zero, Vector3.right * 0.001f, Vector3.up * 0.001f },
                triangles = new[] { 0, 1, 2 },
            };
            var place = Matrix4x4.Translate(Camera.transform.position + Camera.transform.forward * (Camera.nearClipPlane * 2));
            foreach (var material in new[] { shapeFadeMaterial, eyesFadeMaterial, shapeMaskMaterial, shapeSilhouetteMaterial })
            {
                if (material != null)
                {
                    Graphics.RenderMesh(new RenderParams(material) { camera = Camera }, _warmUpMesh, 0, place);
                }
            }
        }

        // A player coming into view fades in; one already in view (a new look) is swapped at once.
        private void OnOtherSpawned(PlayerSpawn spawn)
        {
            var track = _otherFades.TryGetValue(spawn.EntityId, out var known) ? known : new OtherFade();
            if (known == null)
            {
                track.Fade.StartHidden();
            }

            if (_others.Remove(spawn.EntityId, out var old))
            {
                Destroy(old.gameObject);
            }

            _spawns[spawn.EntityId] = spawn;
            var other = BuildShape(
                _join.Content.Shapes[spawn.Shape],
                _join.Content.ColorRgb(spawn.Color),
                _join.Content.ColorRgb(spawn.EyeColor),
                transform
            );
            other.name = $"{spawn.Name} ({spawn.EntityId})";
            var position = new Vector3(spawn.X, spawn.Y, spawn.Z);
            other.transform.SetPositionAndRotation(position, Quaternion.Euler(0, spawn.Facing * Mathf.Rad2Deg, 0));
            other.Faded = track.Fade.Faded;
            track.Last = position;
            _others[spawn.EntityId] = other;
            _otherFades[spawn.EntityId] = track;
        }

        // Everyone leaves the world, this player too, and the camera goes back to orbiting the island.
        private void OnConnectionLost(CloseReason reason)
        {
            foreach (var other in _others.Values)
            {
                Destroy(other.gameObject);
            }

            ClearLeaving();
            _others.Clear();
            _otherFades.Clear();

            if (Player != null)
            {
                orbitCamera.Release();
                Destroy(Player.gameObject);
                Player = null;
                _playerShape = null;
            }

            _spawns.Clear(); // this player's own entry too, or a rejoin would tag someone with the old name
        }

        private void ClearLeaving()
        {
            foreach (var (shape, _) in _leaving)
            {
                Destroy(shape.gameObject);
            }

            _leaving.Clear();
        }

        // A player leaving view fades out where they were last drawn.
        private void OnOtherLeft(uint entityId)
        {
            if (_others.Remove(entityId, out var other))
            {
                _spawns.Remove(entityId);
                var fade = _otherFades.Remove(entityId, out var track) ? track.Fade : new ShapeFade();
                fade.Depart(Time.unscaledTime);
                _leaving.Add((other, fade));
            }
        }

        /// <summary>
        /// A player's shape in their colors (0xRRGGBB), standing at its parent's origin: its look (a group of
        /// primitives) at its scale and hover in content.
        /// </summary>
        public ShapeBody BuildShape(Shape shape, uint color, uint eyeColor, Transform parent) =>
            BuildShape(_content, shape, color, eyeColor, parent, shapeMaterial, eyesMaterial, shapeFadeMaterial, eyesFadeMaterial);

        /// <summary><see cref="BuildShape(Shape, uint, uint, Transform)"/> with its content and materials given, as tests build shapes.</summary>
        public static ShapeBody BuildShape(
            ShapeLandContent content,
            Shape shape,
            uint color,
            uint eyeColor,
            Transform parent,
            Material shapeMaterial,
            Material eyesMaterial,
            Material shapeFadeMaterial,
            Material eyesFadeMaterial
        )
        {
            var drawn = ShapeBody.Create(shape.DisplayName, parent, shape.Scale, shape.Hover);
            foreach (var part in Groups.Expand(new Placement { Group = shape.Look }, id => content.Groups.TryGet(id, out var group) ? group : null))
            {
                if (part.Primitive == null)
                {
                    continue; // looks are built from primitives
                }

                var r = part.Rotation;
                var eyes = part.Role == "eyes";
                drawn.AddPart(
                    PrimitiveMeshes.For(part, content.Settings.Primitives),
                    Conversions.ToUnity(part.Position),
                    Quaternion.Euler(r.X, r.Y, r.Z),
                    part.Role,
                    Conversions.FromRgb(part.Color ?? content.Settings.World.PlacedColor),
                    eyes ? eyesMaterial : shapeMaterial,
                    eyes ? eyesFadeMaterial : shapeFadeMaterial
                );
            }

            drawn.SetColors(color, eyeColor);
            return drawn;
        }
    }
}

using System;
using Comet.Content;
using Comet.Simulation;
using UnityEngine;
using static Comet.Unity.Conversions;

namespace Comet.Unity
{
    /// <summary>
    /// Draws a zone in a game: its terrain, and its placed objects. Primitives without a tag are drawn as one mesh per
    /// chunk; tagged ones stay apart for game code to find, and blockout ones for their own material.
    /// </summary>
    public sealed class ZoneDrawing
    {
        private readonly Material _terrain;
        private readonly Material _placed;
        private readonly Material? _blockout;
        private readonly PropLibrary _props;
        private readonly PolygonBudget _budget;
        private readonly uint _placedColor;
        private readonly uint _groundColor;
        private readonly uint _underColor;

        /// <param name="placedColor">Placed objects' color where neither they nor their prop set one, as 0xRRGGBB.</param>
        /// <param name="groundColor">The color of unpainted ground, for terrain pieces without their own.</param>
        /// <param name="underColor">The color of unpainted undersides (skirt walls and bottoms), for terrain pieces without their own.</param>
        public ZoneDrawing(
            Material terrain,
            Material placed,
            Material? blockout,
            PropLibrary props,
            PolygonBudget budget,
            uint placedColor,
            uint groundColor,
            uint underColor
        )
        {
            _terrain = terrain;
            _placed = placed;
            _blockout = blockout;
            _props = props;
            _budget = budget;
            _placedColor = placedColor;
            _groundColor = groundColor;
            _underColor = underColor;
        }

        /// <summary>
        /// Draws <paramref name="zone"/> under <paramref name="parent"/> and returns its object, with the terrain of
        /// <paramref name="world"/> under "Terrain" and placed objects under <paramref name="placed"/>. Each tagged
        /// object goes to <paramref name="onTagged"/> as it's drawn.
        /// </summary>
        public Transform Draw(Zone zone, CollisionWorld world, Transform parent, Action<Placement, GameObject> onTagged, out Transform placed)
        {
            var drawn = new GameObject(zone.Id).transform;
            drawn.SetParent(parent, false);
            var terrain = new GameObject("Terrain").transform;
            terrain.SetParent(drawn, false);
            foreach (var chunk in TerrainMesher.Build(world.Terrain!, _groundColor, _underColor))
            {
                var name = $"Chunk {chunk.Key}";
                var mesh = AddMesh(name, TerrainMeshes.Build(name, chunk.Value), _terrain, terrain);
                mesh.transform.localPosition = ToUnity(chunk.Key.Origin);
            }

            placed = new GameObject("Placed").transform;
            placed.SetParent(drawn, false);
            foreach (var chunk in zone.Chunks)
            {
                var primitives = new PrimitiveMeshes.Builder();
                var toChunk = System.Numerics.Matrix4x4.CreateTranslation(-chunk.Coord.Origin);
                foreach (var placement in chunk.Placements)
                {
                    if (placement.Primitive != null && placement.Tag == null && !placement.Blockout)
                    {
                        primitives.Add(PrimitiveMesher.For(placement, _budget), placement.Matrix() * toChunk, placement.Color ?? _placedColor);
                        continue;
                    }

                    var thing = placement.Primitive != null ? PlacePrimitive(placement, placed) : Place(placement, placed);
                    if (thing != null && placement.Tag != null)
                    {
                        onTagged(placement, thing);
                    }
                }

                if (primitives.VertexCount > 0)
                {
                    var name = $"Primitives {chunk.Coord}";
                    var mesh = new Mesh { name = name };
                    primitives.Fill(mesh);
                    AddMesh(name, mesh, _terrain, placed).transform.localPosition = ToUnity(chunk.Coord.Origin);
                }
            }

            return drawn;
        }

        /// <summary>A primitive on its own, in the placed objects' material (or blockout's), so game code can find and color it.</summary>
        public GameObject PlacePrimitive(Placement placement, Transform parent)
        {
            var thing = AddMesh(placement.Tag ?? "Blockout", PrimitiveMeshes.For(placement, _budget), MaterialFor(placement), parent);
            var rotation = placement.Rotation;
            thing.transform.SetLocalPositionAndRotation(ToUnity(placement.Position), Quaternion.Euler(rotation.X, rotation.Y, rotation.Z));
            PlacedColors.Apply(thing, Conversions.FromRgb(placement.Color ?? _placedColor));
            return thing;
        }

        // A prop's model where a zone places it, in the placed objects' material; its collision objects are hidden.
        private GameObject? Place(Placement placement, Transform parent)
        {
            var model = _props.Model(placement.Prop);
            if (model == null)
            {
                Debug.LogWarning($"No model for {placement.Prop} in the prop library; is the content up to date in the editor?");
                return null;
            }

            var thing = UnityEngine.Object.Instantiate(model, parent, false);
            thing.name = placement.Prop;
            var rotation = placement.Rotation;
            thing.transform.SetLocalPositionAndRotation(ToUnity(placement.Position), Quaternion.Euler(rotation.X, rotation.Y, rotation.Z));
            thing.transform.localScale = ToUnity(placement.Scale);
            PlacedProps.HideCollision(thing);
            PlacedProps.SetMaterial(thing, MaterialFor(placement));

            var own = placement.Color is { } rgb ? Conversions.FromRgb(rgb) : (Color?)null;
            var prop = _props.TryGet(placement.Prop, out var entry) && entry.hasColor ? entry.color : (Color?)null;
            PlacedColors.Apply(thing, own ?? prop ?? Conversions.FromRgb(_placedColor));
            return thing;
        }

        private Material MaterialFor(Placement placement) => placement.Blockout && _blockout != null ? _blockout : _placed;

        private static GameObject AddMesh(string name, Mesh mesh, Material material, Transform parent)
        {
            var thing = new GameObject(name);
            thing.transform.SetParent(parent, false);
            thing.AddComponent<MeshFilter>().sharedMesh = mesh;
            thing.AddComponent<MeshRenderer>().sharedMaterial = material;
            return thing;
        }
    }
}

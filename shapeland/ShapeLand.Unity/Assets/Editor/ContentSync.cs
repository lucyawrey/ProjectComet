using System.IO;
using System.Linq;
using Comet.Unity;
using Comet.Unity.Editor;
using ShapeLand.Shared.Content;
using UnityEditor;
using UnityEngine;

namespace ShapeLand.Client.Editor
{
    /// <summary>
    /// Keeps what the editor holds from ShapeLand's content in step with it each time Comet copies the content in
    /// (<see cref="ContentBuild"/>): the asset library (<see cref="AssetLibrary"/>) follows the content's asset
    /// entries, and the Comet settings take the game's terrain colors, body height, spawn markers and polygon budget.
    /// </summary>
    [InitializeOnLoad]
    public static class ContentSync
    {
        /// <summary>Where the asset library lives; the game scene refers to it.</summary>
        public const string LibraryPath = "Assets/Models/AssetLibrary.asset";

        static ContentSync()
        {
            ContentBuild.Copied += content =>
            {
                RefreshLibrary(content);
                RefreshCometSettings(content);
            };
        }

        // The Comet settings that come from the game's content: terrain colors, the body height, spawn markers and the
        // polygon budget.
        private static void RefreshCometSettings(byte[] contentFile)
        {
            var settings = CometSettings.Find(quiet: true);
            if (settings == null)
            {
                return;
            }

            ShapeLandContent content;
            try
            {
                content = ShapeLandContent.Load(new MemoryStream(contentFile));
            }
            catch (Comet.Content.ContentException)
            {
                return; // RefreshLibrary has said so
            }

            var game = content.Settings;
            var ground = GameView.Color(game.World.GroundColor);
            var under = GameView.Color(game.World.UnderColor);
            var placed = GameView.Color(game.World.PlacedColor);
            if (settings.groundColor == ground && settings.underColor == under && settings.placedColor == placed && settings.bodyHeight == game.Movement.BodyHeight
                && settings.spawnMarkerAsset == game.Spawns.Marker && settings.spawnMarkerHeight == game.Spawns.MarkerHeight
                && settings.spawnMarkerColor == GameView.Color(game.Spawns.UnsetColor)
                && settings.facetLength == game.Primitives.FacetLength && settings.minSides == game.Primitives.MinSides && settings.maxSides == game.Primitives.MaxSides)
            {
                return;
            }

            settings.groundColor = ground;
            settings.underColor = under;
            settings.placedColor = placed;
            settings.bodyHeight = game.Movement.BodyHeight;
            settings.spawnMarkerAsset = game.Spawns.Marker;
            settings.spawnMarkerHeight = game.Spawns.MarkerHeight;
            settings.spawnMarkerColor = GameView.Color(game.Spawns.UnsetColor);
            settings.facetLength = game.Primitives.FacetLength;
            settings.minSides = game.Primitives.MinSides;
            settings.maxSides = game.Primitives.MaxSides;
            ZonePrimitiveEditing.Preview(settings);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            Debug.Log("Updated the Comet settings from the content.");
        }

        // Points each asset key at the model its entry names (a path under Assets/), saving only if something changed.
        private static void RefreshLibrary(byte[] contentFile)
        {
            ShapeLandContent content;
            try
            {
                content = ShapeLandContent.Load(new MemoryStream(contentFile));
            }
            catch (Comet.Content.ContentException e)
            {
                Debug.LogWarning($"Can't read the content to update the asset library: {e.Message}");
                return;
            }

            var library = AssetDatabase.LoadAssetAtPath<AssetLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<AssetLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var entries = content.Assets.All.Select(asset =>
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/" + asset.Model);
                if (model == null)
                {
                    Debug.LogWarning($"{asset.Id}'s model Assets/{asset.Model} isn't in the project.");
                }

                var min = new Vector3(asset.BoundsMin.X, asset.BoundsMin.Y, asset.BoundsMin.Z);
                var max = new Vector3(asset.BoundsMax.X, asset.BoundsMax.Y, asset.BoundsMax.Z);
                return new AssetLibrary.Entry
                {
                    key = asset.Id,
                    model = model,
                    uniformScale = asset.UniformScale,
                    bounds = new Bounds((min + max) / 2, max - min),
                    hasColor = asset.Color != null,
                    color = asset.Color is { } color ? PlacedColors.FromRgb(color) : default,
                };
            }).ToList();
            if (entries.SequenceEqual(library.Entries))
            {
                return;
            }

            library.SetEntries(entries);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            Debug.Log($"Updated the asset library ({entries.Count} assets).");
        }
    }
}

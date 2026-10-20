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
    /// (<see cref="ContentBuild"/>): the prop library (<see cref="PropLibrary"/>) follows the content's prop
    /// entries, and the Comet settings take the game's terrain colors, body height, defaults for new spawn points,
    /// teleporters and triggers, and polygon budget.
    /// </summary>
    [InitializeOnLoad]
    public static class ContentSync
    {
        /// <summary>Where the prop library lives; the game scene refers to it.</summary>
        public const string LibraryPath = "Assets/Models/PropLibrary.asset";

        static ContentSync()
        {
            ContentBuild.Copied += content =>
            {
                RefreshLibrary(content);
                RefreshCometSettings(content);
            };
        }

        // The Comet settings that come from the game's content: terrain colors, the body height, trigger defaults and the
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
                && settings.spawnPad == game.Spawns.Pad && settings.spawnPadSize.y == game.Spawns.PadHeight
                && settings.spawnPadColor == GameView.Color(game.Spawns.UnsetColor) && settings.spawnStandSeconds == game.Spawns.StandSeconds
                && settings.teleporterPad == game.Teleporters.Pad && settings.teleporterPadSize.y == game.Teleporters.PadHeight
                && settings.teleporterPadColor == GameView.Color(game.Teleporters.PadColor) && settings.teleporterStandSeconds == game.Teleporters.StandSeconds
                && settings.triggerStandSeconds == game.Triggers.StandSeconds
                && settings.facetLength == game.Primitives.FacetLength && settings.minSides == game.Primitives.MinSides && settings.maxSides == game.Primitives.MaxSides)
            {
                return;
            }

            settings.groundColor = ground;
            settings.underColor = under;
            settings.placedColor = placed;
            settings.bodyHeight = game.Movement.BodyHeight;
            settings.spawnPad = game.Spawns.Pad;
            settings.spawnPadSize.y = game.Spawns.PadHeight;
            settings.spawnPadColor = GameView.Color(game.Spawns.UnsetColor);
            settings.spawnStandSeconds = game.Spawns.StandSeconds;
            settings.teleporterPad = game.Teleporters.Pad;
            settings.teleporterPadSize.y = game.Teleporters.PadHeight;
            settings.teleporterPadColor = GameView.Color(game.Teleporters.PadColor);
            settings.teleporterStandSeconds = game.Teleporters.StandSeconds;
            settings.triggerStandSeconds = game.Triggers.StandSeconds;
            settings.facetLength = game.Primitives.FacetLength;
            settings.minSides = game.Primitives.MinSides;
            settings.maxSides = game.Primitives.MaxSides;
            ZonePrimitiveEditing.Preview(settings);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            Debug.Log("Updated the Comet settings from the content.");
        }

        // Points each prop id at the model its entry names (a path under Assets/), saving only if something changed.
        private static void RefreshLibrary(byte[] contentFile)
        {
            ShapeLandContent content;
            try
            {
                content = ShapeLandContent.Load(new MemoryStream(contentFile));
            }
            catch (Comet.Content.ContentException e)
            {
                Debug.LogWarning($"Can't read the content to update the prop library: {e.Message}");
                return;
            }

            var library = AssetDatabase.LoadAssetAtPath<PropLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<PropLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var entries = content.Props.All.Select(prop =>
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/" + prop.Model);
                if (model == null)
                {
                    Debug.LogWarning($"{prop.Id}'s model Assets/{prop.Model} isn't in the project.");
                }

                var min = new Vector3(prop.BoundsMin.X, prop.BoundsMin.Y, prop.BoundsMin.Z);
                var max = new Vector3(prop.BoundsMax.X, prop.BoundsMax.Y, prop.BoundsMax.Z);
                return new PropLibrary.Entry
                {
                    key = prop.Id,
                    model = model,
                    uniformScale = prop.UniformScale,
                    bounds = new Bounds((min + max) / 2, max - min),
                    hasColor = prop.Color != null,
                    color = prop.Color is { } color ? PlacedColors.FromRgb(color) : default,
                };
            }).ToList();
            if (entries.SequenceEqual(library.Entries))
            {
                return;
            }

            library.SetEntries(entries);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            Debug.Log($"Updated the prop library ({entries.Count} props).");
        }
    }
}

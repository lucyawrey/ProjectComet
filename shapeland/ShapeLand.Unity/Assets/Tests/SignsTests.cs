using System.Collections;
using System.Linq;
using Comet.Content;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace ShapeLand.Client.Tests
{
    // Signs over the zone: shown within their distance and in sight of the camera, in their own colors.
    public class SignsTests : GameSceneTest
    {
        [UnityTest]
        public IEnumerator SignsShowInRangeAndInSight()
        {
            yield return StartGame();
            var eye = Camera.transform.position;
            var ahead = eye + Camera.transform.forward * 4;
            var right = Camera.transform.right * 1.5f;
            var worldNear = ahead + Vector3.up;
            var worldFar = eye + Camera.transform.forward * 8 + Camera.transform.up;
            var feet = View.Player.transform.position;
            var chunk = Join.Zone.Chunks[0];
            chunk.Signs = chunk.Signs.Concat(new[]
            {
                new Sign { Position = Vectors.ToNumerics(ahead), Text = "Near", BackgroundColor = 0x12161E, BackgroundOpacity = 0.75f, ScreenSized = true, FontSize = 20, Arrow = true },
                new Sign { Position = Vectors.ToNumerics(ahead + right), Text = "Plain", TextColor = 0xFFEE00, Arrow = true },
                new Sign { Position = Vectors.ToNumerics(ahead - right), Text = "Far", ShowDistance = 2 },
                new Sign { Position = Vectors.ToNumerics(feet + Vector3.down * 3), Text = "Buried" },
                new Sign { Position = Vectors.ToNumerics(worldNear), Text = "World", TextHeight = 0.3f },
                new Sign { Position = Vectors.ToNumerics(worldFar), Text = "World far", TextHeight = 0.3f },
            }).ToArray();

            // The signs are read as a zone is entered; turning them off and on reads the zone again.
            var signs = Object.FindAnyObjectByType<Signs>();
            signs.enabled = false;
            signs.enabled = true;
            yield return new WaitForSecondsRealtime(0.5f);

            var panels = Hud.Root.Q("tags").Query(className: "sign").ToList();
            VisualElement Of(string text) => panels.Single(p => p.Q<Label>().text == text);
            bool Showing(string text) => Of(text).resolvedStyle.display == DisplayStyle.Flex && Of(text).resolvedStyle.opacity > 0.9f;
            Assert.That(Showing("Near"), Is.True, "A sign in range and in sight isn't shown.");
            Assert.That(Showing("Plain"), Is.True);
            Assert.That(Showing("Far"), Is.False, "A sign past its distance is shown.");
            Assert.That(Showing("Buried"), Is.False, "A sign behind the ground is shown.");
            Assert.That(Of("Near").resolvedStyle.backgroundColor.a, Is.EqualTo(0.75f).Within(0.01f));
            Assert.That(Of("Plain").resolvedStyle.backgroundColor.a, Is.EqualTo(0f), "A sign without a background has one.");
            Assert.That(Of("Plain").Q<Label>().resolvedStyle.color, Is.EqualTo(GameView.Color(0xFFEE00)));
            Assert.That(Of("Near").Q<Label>().resolvedStyle.fontSize, Is.EqualTo(20f));
            Assert.That(Of("Near").Q(className: "sign-arrow"), Is.Not.Null, "The sign has no arrow.");
            Assert.That(Of("Plain").Q(className: "sign-arrow"), Is.Null, "A sign without a background has an arrow.");

            // World-sized signs shrink with their depth from the camera, measured now since the camera may have moved
            // since they were placed. Screen-sized ones aren't scaled.
            Assert.That(Showing("World") && Showing("World far"), Is.True);
            float Depth(Vector3 at) => View.Camera.WorldToScreenPoint(at).z;
            var nearDepth = Depth(worldNear);
            var farDepth = Depth(worldFar);
            Assert.That(farDepth, Is.GreaterThan(nearDepth * 1.2f));
            var near = Of("World").resolvedStyle.scale.value.x;
            Assert.That(Of("World far").resolvedStyle.scale.value.x, Is.EqualTo(near * nearDepth / farDepth).Within(near * 0.05f), "A world-sized sign isn't smaller further away.");
            Assert.That(Of("Near").resolvedStyle.scale.value.x, Is.EqualTo(1f), "A screen-sized sign is scaled.");
        }
    }
}

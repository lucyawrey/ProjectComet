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
            var feet = View.Player.transform.position;
            var chunk = Join.Zone.Chunks[0];
            chunk.Signs = chunk.Signs.Concat(new[]
            {
                new Sign { Position = Vectors.ToNumerics(ahead), Text = "Near", BackgroundColor = 0x12161E, BackgroundOpacity = 0.75f, FontSize = 20, Arrow = true },
                new Sign { Position = Vectors.ToNumerics(ahead + right), Text = "Plain", TextColor = 0xFFEE00, Arrow = true },
                new Sign { Position = Vectors.ToNumerics(ahead - right), Text = "Far", ShowDistance = 2 },
                new Sign { Position = Vectors.ToNumerics(feet + Vector3.down * 3), Text = "Buried" },
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
        }
    }
}

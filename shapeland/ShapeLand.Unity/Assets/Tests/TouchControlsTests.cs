using NUnit.Framework;
using UnityEngine;

namespace ShapeLand.Client.Tests
{
    // The touch controls' math: the floating stick's value and the camera drag's turn.
    public class TouchControlsTests
    {
        [Test]
        public void TheStickIsStillInsideItsDeadZone()
        {
            Assert.That(TouchControls.StickValue(new Vector2(5, 0), 60), Is.EqualTo(Vector2.zero));
            Assert.That(TouchControls.StickValue(Vector2.zero, 60), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void TheStickRisesEvenlyToFullAtItsRange()
        {
            var half = TouchControls.StickValue(new Vector2(0, 33), 60);
            Assert.That(half.x, Is.EqualTo(0).Within(1e-5f));
            Assert.That(half.y, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(TouchControls.StickValue(new Vector2(60, 0), 60).x, Is.EqualTo(1).Within(1e-5f));
            Assert.That(TouchControls.StickValue(new Vector2(-300, 400), 60).magnitude, Is.EqualTo(1).Within(1e-5f));
        }

        [Test]
        public void ADragTurnsByTheShareOfTheScreenItCrosses()
        {
            Assert.That(TouchControls.DragTurn(new Vector2(400, -200), 800, 180), Is.EqualTo(new Vector2(90, -45)));
            Assert.That(TouchControls.DragTurn(new Vector2(10, 10), 0, 180), Is.EqualTo(Vector2.zero));
        }
    }
}

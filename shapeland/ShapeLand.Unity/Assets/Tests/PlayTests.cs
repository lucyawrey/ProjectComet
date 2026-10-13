using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace ShapeLand.Client.Tests
{
    // Playing in the Game scene: the player walks and jumps with no snap-backs and the camera follows; the speed
    // cheat is snapped back with a blend; walking off the island darkens the screen before the respawn and lightens
    // it after; real bots are drawn moving and removed when they leave; F3 shows the network numbers.
    public class PlayTests : GameSceneTest
    {
        [UnityTest]
        public IEnumerator WalksAndJumpsWithoutSnapBacks()
        {
            yield return StartGame();
            var player = View.Player;
            var start = player.transform.position;

            Press(Key.W);
            yield return Wait(1.5f);
            var walked = Vector3.ProjectOnPlane(player.transform.position - start, Vector3.up).magnitude;
            Assert.That(walked, Is.GreaterThan(2f), "Didn't walk forward.");

            // Stop, then jump in place, so the hills don't change the height reached.
            Press();
            yield return Wait(0.5f);
            var ground = player.Motor.Position.Y;
            Press(Key.Space);
            yield return null;
            Press();
            var highest = ground;
            var deadline = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < deadline)
            {
                highest = Mathf.Max(highest, player.Motor.Position.Y);
                yield return null;
            }

            Assert.That(highest - ground, Is.GreaterThan(0.5f), "Didn't jump.");
            yield return Wait(1.5f);
            Assert.That(player.SnapBacks, Is.EqualTo(0), "The server snapped the player back.");
            Assert.That(player.Respawns, Is.EqualTo(0), "The player fell off.");
            Assert.That(Vector3.Distance(Camera.transform.position, player.transform.position), Is.LessThan(10f), "The camera didn't follow.");
            Debug.Log($"Walked {walked:0.0} m and jumped {highest - ground:0.00} m; {Join.Session.StatesReceived} states received.");
        }

        [UnityTest]
        public IEnumerator SpeedCheatIsSnappedBackWithABlend()
        {
            yield return StartGame();
            var player = View.Player;

            Press(Key.W, Key.C);
            yield return TestGameServer.WaitFor(() => player.SnapBacks > 0, 5);
            Assert.That(player.SnapBacks, Is.GreaterThan(0), "The speed cheat was never snapped back.");
            var trailing = player.BlendDistance;
            Press();

            // The drawn shape trails the corrected position, then catches up within the blend.
            Assert.That(trailing, Is.GreaterThan(0.05f), "The snap-back wasn't blended.");
            yield return Wait(0.3f);
            Assert.That(player.BlendDistance, Is.EqualTo(0), "The blend didn't finish.");
            Debug.Log($"Snapped back {player.SnapBacks} times; the drawn shape trailed by {trailing:0.00} m after the first.");
        }

        [UnityTest]
        public IEnumerator FallingOffDarkensBeforeTheRespawnAndLightensAfter()
        {
            yield return StartGame();
            var player = View.Player;

            // Walk straight on until off the edge, noting how dark the screen was just before the respawn arrived.
            Press(Key.W);
            var darknessBefore = 0f;
            var deadline = Time.realtimeSinceStartup + 20;
            while (player.Respawns == 0 && Time.realtimeSinceStartup < deadline)
            {
                darknessBefore = Hud.Darkness;
                yield return null;
            }

            Press();
            Assert.That(player.Respawns, Is.EqualTo(1), "Never fell off.");
            Assert.That(darknessBefore, Is.GreaterThan(0.99f), "The screen wasn't dark before the respawn arrived.");

            yield return Wait(RespawnFade.FadeInSeconds + 0.3f);
            Assert.That(Hud.Darkness, Is.EqualTo(0), "The screen didn't lighten again.");
            Assert.That(player.Motor.Position.Y, Is.GreaterThan(0), "Not back on the island.");
            Assert.That(player.SnapBacks, Is.EqualTo(0), "The server snapped the player back.");
        }

        [UnityTest]
        public IEnumerator DrawsOtherPlayersMovingAndLeaving()
        {
            yield return StartGame();
            yield return Server.StartBots(2, 8);

            yield return TestGameServer.WaitFor(() => View.Others.Count == 2, 15);
            Assert.That(View.Others.Count, Is.EqualTo(2), "The bots never appeared.");
            var others = new List<ShapeBody>(View.Others.Values);
            var starts = others.ConvertAll(o => o.transform.position);
            yield return Wait(2);
            var moved = 0f;
            for (var i = 0; i < others.Count; i++)
            {
                moved = Mathf.Max(moved, Vector3.Distance(starts[i], others[i].transform.position));
            }

            Assert.That(moved, Is.GreaterThan(1f), "The bots weren't drawn moving.");

            yield return TestGameServer.WaitFor(() => View.Others.Count == 0, 30);
            Assert.That(View.Others.Count, Is.EqualTo(0), "The bots weren't removed after leaving.");
            Debug.Log($"Saw 2 bots; the furthest moved {moved:0.0} m in 2 s.");
        }

        [UnityTest]
        public IEnumerator F3ShowsNetworkStats()
        {
            yield return StartGame();
            yield return Server.StartBots(2, 20);
            Assert.That(NetStats.Panel.ClassListContains("hidden"), Is.True, "The stats show before F3.");

            Press(Key.F3);
            yield return null;
            yield return null;
            Press();
            yield return null;
            Assert.That(NetStats.Shown, Is.True, "F3 didn't show the stats.");
            Assert.That(NetStats.Panel.ClassListContains("hidden"), Is.False);

            // A ping a second, and the bots moving for the delay and holds.
            yield return Wait(3);
            string Number(string name) => NetStats.Panel.Q<Label>(name).text;
            // Under a millisecond on loopback, so the number itself is 0; the clock must have had a pong.
            Assert.That(Join.Session.Clock.Synced, Is.True, "No pong yet.");
            Assert.That(double.Parse(Number("ping-best")), Is.GreaterThanOrEqualTo(0));
            Assert.That(double.Parse(Number("delay")), Is.GreaterThanOrEqualTo(133), "The delay is under its floor of two report intervals.");
            Assert.That(double.Parse(Number("holds")), Is.InRange(0, 100));
            Assert.That(Number("snaps"), Is.EqualTo("0"));
            Assert.That(NetStats.Panel.Q("snaps-dot").ClassListContains("bad"), Is.False);
            Debug.Log($"Stats: ping {Number("ping")} (best {Number("ping-best")}) ms, delay {Number("delay")} ms, holds {Number("holds")}%.");
        }
    }
}

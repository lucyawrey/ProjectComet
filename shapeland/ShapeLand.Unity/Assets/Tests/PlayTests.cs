using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace ShapeLand.Client.Tests
{
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
        public IEnumerator MovingTooFastIsSnappedBackWithABlend()
        {
            yield return StartGame();
            var player = View.Player;

            player.TestSpeedScale = 2.5f;
            Press(Key.W);
            yield return TestGameServer.WaitFor(() => player.SnapBacks > 0, 5);
            Assert.That(player.SnapBacks, Is.GreaterThan(0), "Moving too fast was never snapped back.");
            var trailing = player.BlendDistance;
            Press();
            player.TestSpeedScale = 1;

            Assert.That(trailing, Is.GreaterThan(0.05f), "The snap-back wasn't blended.");
            yield return Wait(0.3f);
            Assert.That(player.BlendDistance, Is.EqualTo(0), "The blend didn't finish.");
            Debug.Log($"Snapped back {player.SnapBacks} times; the drawn shape trailed by {trailing:0.00} m after the first.");
        }

        [UnityTest]
        public IEnumerator FallingOffFadesTheShapeBeforeTheRespawnAndBackAfter()
        {
            yield return StartGame();
            var player = View.Player;

            Press(Key.W);
            var fadedBefore = 0f;
            var deadline = Time.realtimeSinceStartup + 20;
            while (player.Respawns == 0 && Time.realtimeSinceStartup < deadline)
            {
                fadedBefore = player.Faded;
                yield return null;
            }

            Press();
            Assert.That(player.Respawns, Is.EqualTo(1), "Never fell off.");
            Assert.That(fadedBefore, Is.GreaterThan(0.99f), "The shape wasn't faded before the respawn arrived.");

            yield return Wait(1 + ShapeFade.FadeInSeconds + 0.3f);
            Assert.That(player.Faded, Is.EqualTo(0), "The shape didn't fade back in.");
            Assert.That(player.Motor.Position.Y, Is.GreaterThan(-1), "Not back on the ground.");
            Assert.That(player.SnapBacks, Is.EqualTo(0), "The server snapped the player back.");
        }

        [UnityTest, Category("Accounts")]
        public IEnumerator DrawsOtherPlayersMovingAndLeaving()
        {
            yield return StartGame(accounts: true);
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

        [UnityTest, Category("Accounts")]
        public IEnumerator F3ShowsNetworkStats()
        {
            yield return StartGame(accounts: true);
            yield return Server.StartBots(2, 20);
            Assert.That(NetStats.Panel.ClassListContains("hidden"), Is.True, "The stats show before F3.");

            Press(Key.F3);
            yield return null;
            yield return null;
            Press();
            yield return null;
            Assert.That(NetStats.Shown, Is.True, "F3 didn't show the stats.");
            Assert.That(NetStats.Panel.ClassListContains("hidden"), Is.False);

            yield return Wait(3);
            string Number(string name) => NetStats.Panel.Q<Label>(name).text;
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

using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace ShapeLand.Client.Tests
{
    // Chat in the Game scene: lines go round to the log and a bubble, chat stays open after sending, Enter opens the input and stops the shape,
    // others' lines arrive, and sending too fast is explained.
    public class ChatTests : GameSceneTest
    {
        [UnityTest]
        public IEnumerator ChatGoesRoundWithALogLineAndABubble()
        {
            yield return StartGame();
            var player = View.Player;
            Chat.OpenInput();
            Assert.That(player.InputEnabled, Is.False, "The shape can still move while typing.");
            Chat.Typed = "  hello island  ";
            Chat.Send();
            Assert.That(Chat.Open, Is.True, "Chat closed after sending.");
            Assert.That(Chat.Typed, Is.Empty, "The sent line is still in the input.");
            Chat.Send();
            Assert.That(Chat.Open, Is.False, "Sending an empty line didn't close chat.");
            Assert.That(player.InputEnabled, Is.True);

            yield return TestGameServer.WaitFor(() => Chat.LogLines().Count > 0, 10);
            Assert.That(Chat.LogLines(), Is.EqualTo(new[] { "Unity test hello island" }));
            Assert.That(Tags.BubbleOf(Join.Session.EntityId), Is.EqualTo("hello island"), "No bubble over the player's own shape.");
            yield return Wait(0.6f);
            Assert.That(Chat.Opacity, Is.EqualTo(1), "The log isn't showing after a message.");
        }

        [UnityTest]
        public IEnumerator EnterOpensChatAndTheShapeStaysPut()
        {
            yield return StartGame();
            var player = View.Player;
            Press(Key.Enter);
            yield return null;
            yield return null;
            Press();
            yield return Wait(0.3f);
            Assert.That(Chat.Open, Is.True, "Enter didn't open chat.");

            var start = player.transform.position;
            Press(Key.W);
            yield return Wait(1);
            Assert.That(Vector3.Distance(start, player.transform.position), Is.LessThan(0.1f), "The shape moved while typing.");

            Chat.CloseInput();
            yield return Wait(1);
            Assert.That(Vector3.Distance(start, player.transform.position), Is.GreaterThan(1f), "The shape didn't move after chat closed.");
        }

        [UnityTest]
        public IEnumerator OthersChatInTheLogAndInBubbles()
        {
            yield return StartGame();
            yield return Server.StartBots(1, 20);
            yield return TestGameServer.WaitFor(() => Chat.LogLines().Any(line => line.StartsWith("Bot 01 ")), 15);
            var line = Chat.LogLines().FirstOrDefault(l => l.StartsWith("Bot 01 "));
            Assert.That(line, Is.Not.Null, "The bot's chat never reached the log.");
            var bot = View.Spawns.First(p => p.Value.Name == "Bot 01").Key;
            Assert.That("Bot 01 " + Tags.BubbleOf(bot), Is.EqualTo(line), "The bot's bubble doesn't show its line.");
        }

        [UnityTest]
        public IEnumerator ChattingTooFastIsExplained()
        {
            yield return StartGame();
            Chat.OpenInput();
            for (var i = 0; i < 5; i++)
            {
                Chat.Typed = $"line {i}";
                Chat.Send();
            }

            var tooFast = Strings.Get("chat.too_fast");
            yield return TestGameServer.WaitFor(() => Chat.LogLines().Contains(tooFast), 10);
            Assert.That(Chat.LogLines().Count(l => l.StartsWith("Unity test line")), Is.EqualTo(3), "The server's burst of three wasn't delivered.");
            Assert.That(Chat.LogLines(), Does.Contain(tooFast), "Sending too fast wasn't explained.");
        }
    }
}

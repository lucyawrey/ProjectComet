using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace ShapeLand.Client.Tests
{
    // The account screens, with the Login server: the card's Sign In, the Account page reached from the Esc menu, and
    // the session kept for each Login server.
    [Category("Accounts")]
    public class AccountTests : GameSceneTest
    {
        private const string Password = "a long test password";
        private const string NewPassword = "another long password";

        // Sign-ins count as recent this long, so a test can wait it out and have the page ask for the password again.
        private const string Recent = "--Login:RecentSignIn=00:00:03";

        private VisualElement Page => Hud.Root.Q("account");

        private bool AtStep(string step) => Hud.Root.Q(step).ClassListContains("shown");

        private string Message => Hud.Root.Q<Label>("account-message") is { } label && label.text.Length > 0 ? label.text : null;

        private IEnumerator OpenAccount()
        {
            Hud.OpenAccount();
            yield return TestGameServer.WaitFor(() => !Page.ClassListContains("hidden") && Join.Account != null, 10);
            Assert.That(Page.ClassListContains("hidden"), Is.False, "The Account page didn't open.");
        }

        // The page's rows take clicks rather than being buttons.
        private static void ClickRow(VisualElement row)
        {
            using var click = ClickEvent.GetPooled();
            click.target = row;
            row.SendEvent(click);
        }

        private IEnumerator SubmitStep(string waitForStep, float seconds = 10)
        {
            Click(Hud.Root.Q<Button>("account-submit"));
            yield return TestGameServer.WaitFor(() => AtStep(waitForStep) || Message != null, seconds);
            Assert.That(Message, Is.Null);
            Assert.That(AtStep(waitForStep), Is.True, $"Didn't reach {waitForStep}.");
        }

        private IEnumerator SetPassword(string password)
        {
            yield return TestGameServer.WaitFor(() => AtStep("account-password"), 10);
            Hud.Root.Q<TextField>("account-password-field").value = password;
            Hud.Root.Q<TextField>("account-password-again").value = password;
            yield return SubmitStep("account-rows");
        }

        // The confirm panel's choice with this text, wherever its host is.
        private void Choose(string text)
        {
            var choice = Hud.Root.Query<Button>(className: "menu-entry").ToList().Concat(Screen.Root.Query<Button>(className: "menu-entry").ToList())
                .FirstOrDefault(b => b.text == text && b.GetFirstAncestorOfType<VisualElement>() != null && b.parent.ClassListContains("menu-confirm") && b.worldBound.width > 0);
            Assert.That(choice, Is.Not.Null, $"No \"{text}\" to choose.");
            Click(choice);
        }

        private IEnumerator SignOutFromThePage()
        {
            Click(Hud.Root.Q<Button>("account-sign-out"));
            yield return null;
            Choose(Strings.Get("account.sign_out"));
            yield return TestGameServer.WaitFor(() => Screen.Showing && !Join.SignedIn, 10);
            Assert.That(Join.SignedIn, Is.False, "Still signed in.");
            Assert.That(Screen.Showing, Is.True, "The card didn't come back after signing out.");
        }

        [UnityTest]
        public IEnumerator AGuestSetsAPasswordThenSignsInWithIt()
        {
            yield return StartGame(accounts: true);
            yield return OpenAccount();
            ClickRow(Hud.Root.Q("account-password-row")); // a guest's first password asks for nothing first
            yield return SetPassword(Password);
            Assert.That(Join.Account.Guest, Is.False, "Still a guest with a password.");

            yield return SignOutFromThePage();
            Click(Screen.Root.Q<Button>("sign-in-button"));
            yield return null;
            Field("sign-in-username").value = "unittest"; // in other capitals
            Field("sign-in-password").value = Password;
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => View.Player != null || Screen.Error != null, 15);
            Assert.That(Screen.Error, Is.Null);
            Assert.That(Join.Spawn.Value.Name, Is.EqualTo("UnityTest"));
        }

        [UnityTest]
        public IEnumerator ChangesAskForThePasswordAgainOnceTheSignInIsOld()
        {
            yield return StartGame(accounts: true, loginArguments: Recent);
            yield return OpenAccount();
            ClickRow(Hud.Root.Q("account-password-row"));
            yield return SetPassword(Password);

            // A new password, once the sign-in isn't recent: the old one first.
            yield return Wait(4);
            ClickRow(Hud.Root.Q("account-password-row"));
            yield return TestGameServer.WaitFor(() => AtStep("account-confirm"), 10);
            Assert.That(AtStep("account-confirm"), Is.True, "Changing the password didn't ask for it again.");
            Hud.Root.Q<TextField>("account-confirm-field").value = Password;
            yield return SubmitStep("account-password");
            yield return SetPassword(NewPassword);

            // A recovery code, the same way, then signing in with it.
            yield return Wait(4);
            ClickRow(Hud.Root.Q("account-code-row"));
            yield return TestGameServer.WaitFor(() => AtStep("account-confirm"), 10);
            Assert.That(AtStep("account-confirm"), Is.True, "Making a recovery code didn't ask for the password again.");
            Hud.Root.Q<TextField>("account-confirm-field").value = NewPassword;
            yield return SubmitStep("account-code");
            var code = Hud.Root.Q<Label>("account-code-text").text;
            Assert.That(code, Is.Not.Empty, "No recovery code shown.");
            yield return SubmitStep("account-rows");

            yield return SignOutFromThePage();
            Click(Screen.Root.Q<Button>("sign-in-button"));
            yield return null;
            Click(Screen.Root.Q<Button>("sign-in-use-code"));
            Field("sign-in-code").value = code;
            Click(Screen.Root.Q<Button>("join-button"));
            yield return TestGameServer.WaitFor(() => View.Player != null || Screen.Error != null, 15);
            Assert.That(Screen.Error, Is.Null);
            Assert.That(Join.Spawn.Value.Name, Is.EqualTo("UnityTest"));
        }

        [UnityTest]
        public IEnumerator ARenameShowsAtOnce()
        {
            yield return StartGame(accounts: true);
            var id = Join.Session.EntityId;
            yield return OpenAccount();
            ClickRow(Hud.Root.Q("account-username-row"));
            yield return TestGameServer.WaitFor(() => AtStep("account-rename"), 10);
            Hud.Root.Q<TextField>("account-rename-field").value = "Renamed";
            yield return SubmitStep("account-rows");
            Assert.That(Join.PlayerName, Is.EqualTo("Renamed"));
            yield return TestGameServer.WaitFor(() => View.Spawns[id].Name == "Renamed", 10);
            Assert.That(View.Spawns[id].Name, Is.EqualTo("Renamed"), "The game server didn't take the new name.");
        }

        [UnityTest]
        public IEnumerator AGuestSigningOutIsWarnedFirst()
        {
            yield return StartGame(accounts: true);
            yield return OpenAccount();
            Click(Hud.Root.Q<Button>("account-sign-out"));
            yield return null;
            Assert.That(Hud.Root.Query<Label>(className: "menu-confirm-title").ToList().Any(l => l.text == Strings.Get("account.sign_out_guest_title")),
                Is.True, "A guest signing out wasn't warned.");
            Choose(Strings.Get("account.sign_out"));
            yield return TestGameServer.WaitFor(() => Screen.Showing && !Join.SignedIn, 10);
            Assert.That(Join.SignedIn, Is.False);
            Assert.That(Screen.SignedInCard, Is.False, "The card isn't the new player's.");
        }

        [UnityTest]
        public IEnumerator BackFromTheAccountPageLandsOnItsMenuEntry()
        {
            yield return StartGame(accounts: true);
            yield return OpenAccount();
            Click(Hud.Root.Q<Button>("account-back"));
            yield return null;
            Assert.That(Page.ClassListContains("hidden"), Is.True, "The Account page stayed up.");
            Assert.That(Hud.Root.Q("menu-account").ClassListContains("selected"), Is.True, "Back didn't land on the Account entry.");
        }

        [UnityTest]
        public IEnumerator EachLoginServerKeepsItsOwnSession()
        {
            yield return StartGame(accounts: true);
            Assert.That(Join.SignedIn, Is.True);
            Join.Leave();
            Join.UseLoginServer("127.0.0.1:9"); // another Login server: nobody's signed in there
            Assert.That(Join.SignedIn, Is.False, "A session showed up on another Login server.");
            Join.UseLoginServer(Server.Address);
            Assert.That(Join.SignedIn, Is.True, "Switching back lost the session.");
            JoinClient.ForgetToken("127.0.0.1:9");
        }
    }
}

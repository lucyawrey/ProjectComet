using Comet.Content;
using Comet.Protocol.Login;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    public sealed partial class JoinScreen
    {
        // The Sign in page in place of the new player's rows, or back; filled in with a taken name it was pointed to from.
        private void SetSigningIn(bool signingIn)
        {
            _root.EnableInClassList("signing-in", signingIn);
            _signInButton.RemoveFromClassList("pointed");
            MarkFieldRows();
            SetButton(true, signingIn ? "account.sign_in" : "join.play");
            ShowError(null);
            if (signingIn)
            {
                _signInUsername.value = _takenName ?? _signInUsername.value;
                _takenName = null;
                SetWithCode(false);
                (_signInUsername.value.Length > 0 ? _signInPassword : _signInUsername).Focus();
            }
        }

        // A recovery code in place of the username and password, or back.
        private void SetWithCode(bool withCode)
        {
            _withCode = withCode;
            _signIn.EnableInClassList("with-code", withCode);
            MarkFieldRows();
            _useCode.text = Text(withCode ? "account.use_password" : "account.use_code");
            _pageName.text = Text(withCode ? "account.sign_in_code_page" : "account.sign_in");
        }

        // The signed-in card's Set password page (a guest keeping their character), or back to the card.
        private void SetSettingPassword(bool setting)
        {
            _root.EnableInClassList("setting-password", setting);
            if (setting)
            {
                _root.RemoveFromClassList("signed-in");
                _pageName.text = Text("account.set_password");
                _setPasswordUsername.text = _join.PlayerName;
                _setPassword.value = "";
                _setPasswordAgain.value = "";
                CloseServerField();
                MarkFieldRows();
                SetButton(true, "account.save_password");
                ShowError(null);
                _setPassword.Focus();
                return;
            }

            SetSignedIn(true);
        }

        // Back on the card's own pages.
        private void PageBack()
        {
            if (SigningIn)
            {
                SetSigningIn(false);
            }
            else if (SettingPassword)
            {
                SetSettingPassword(false);
            }
        }

        // The signed-in card: who the player is, beside their shape as it was last saved, in place of the new player's rows.
        private void SetSignedIn(bool signedIn)
        {
            _root.EnableInClassList("signed-in", signedIn);
            _root.RemoveFromClassList("setting-password");
            if (signedIn)
            {
                var account = _join.Account;
                _whoName.text = account?.Username ?? "";
                var guest = account?.Guest ?? false;
                _whoGuest.EnableInClassList("hidden", !guest);
                _whoGuestNote.EnableInClassList("hidden", !guest);
                if (account?.Character is CharacterView character && _content.Shapes.TryGet(character.Shape, out var shape))
                {
                    _shape = shape;
                    _color = character.Color;
                    _eyeColor = character.EyeColor;
                }

                SetButton(true, "join.play");
            }

            MarkFieldRows();
        }

        // A returning player, joining behind the small pop-up (the card stays down); on failure the pop-up says why, with
        // Retry, and Cancel brings up the signed-in card. A refused token shows the new player's card (through SignedOut).
        private async void JoinFromPop()
        {
            var attempt = ++_popAttempt;
            _popBusy = true;
            if (!JoinClient.Accounts)
            {
                _join.DevJoin(_devName, _devLook);
                ShowPop();
                return;
            }

            ShowPop();
            var error = _join.Account == null ? await _join.LoadAccountAsync() : null;
            if (attempt != _popAttempt)
            {
                return;
            }

            if (error == null)
            {
                ShowPop(); // with the name, now that the account has loaded
                error = await _join.PlayAsync();
                if (attempt != _popAttempt)
                {
                    return;
                }
            }

            if (error == null)
            {
                return; // connecting; the spawn, a refusal or a failure comes next
            }

            _popBusy = false;
            if (error == LoginErrors.SignedOut)
            {
                HidePop();
            }
            else if (_rejoinUntil == null)
            {
                FailPop(LoginMessage(_content.Strings, error));
            }
        }

        // Join on launch off: the signed-in card, once the account has loaded.
        private async void ShowSignedInOnLaunch()
        {
            var error = await _join.LoadAccountAsync();
            if (error != LoginErrors.SignedOut)
            {
                ShowSignedIn(error == null ? null : LoginMessage(_content.Strings, error));
            }
        }

        private void ShowPop()
        {
            var name = _join.PlayerName;
            _popMain.text = Text(name.Length > 0 ? "account.joining_as" : "account.joining");
            _popSub.text = Text("account.connecting");
            _pop.RemoveFromClassList("failed");
            _pop.RemoveFromClassList("hidden");
        }

        private void FailPop(string message)
        {
            _popBusy = false;
            _popMain.text = message;
            _popSub.text = "";
            _pop.AddToClassList("failed");
            _pop.RemoveFromClassList("hidden");
        }

        private void HidePop()
        {
            _popAttempt++;
            _popBusy = false;
            _pop.AddToClassList("hidden");
        }

        // The pop-up's Cancel: stops the join and brings up the signed-in card.
        private void CancelPop()
        {
            _rejoinUntil = null;
            HidePop();
            _join.CancelJoin();
            ShowCardAgain(null);
        }

        // Signing out from the signed-in card asks first; a guest would lose the character, so Set password comes first.
        private void AskToSignOut()
        {
            var strings = _content.Strings;
            if (_join.Account?.Guest ?? true)
            {
                _confirm.Show(
                    strings.Get("account.sign_out_guest_title"),
                    strings.Get("account.sign_out_guest_note"),
                    null,
                    (Text("account.set_password"), false, () => SetSettingPassword(true)),
                    (Text("account.stay"), false, null),
                    (Text("account.sign_out"), true, SignOut)
                );
            }
            else
            {
                _confirm.Show(
                    Text("account.sign_out_title"),
                    Text("account.sign_out_note"),
                    null,
                    (Text("account.sign_out"), true, SignOut),
                    (Text("account.stay"), false, null)
                );
            }
        }

        private async void SignOut() => await _join.SignOutAsync(); // SignedOut brings up the new player's card

        // After a join that didn't happen or a game that ended: the signed-in card, or with accounts off (no account to
        // show), the new player's card with its choices kept.
        private void ShowCardAgain(string message)
        {
            if (JoinClient.Accounts)
            {
                ShowSignedIn(message);
                return;
            }

            if (Changing)
            {
                Close();
            }

            HidePop();
            SetSignedIn(false);
            Show();
            Retry(message);
        }

        // The card for a signed-in player who isn't playing, with a message: off the Sign In page, if it was last on it.
        private void ShowSignedIn(string message)
        {
            if (Changing)
            {
                Close();
            }

            HidePop();
            SetSigningIn(false);
            SetSignedIn(true);
            Show();
            ShowError(message);
        }

        // The token was refused (expired, revoked, a deleted guest) or the player signed out: the new player's card.
        private void OnSignedOut()
        {
            if (_content == null)
            {
                return;
            }

            if (Changing)
            {
                Close();
            }

            HidePop();
            _confirm.Close();
            StartFresh();
            SetSignedIn(false);
            SetSigningIn(false);
            Show();
        }

        // The signed-in card's Set password page; back to the card once it's saved.
        private async System.Threading.Tasks.Task SavePassword()
        {
            if (_setPassword.value != _setPasswordAgain.value)
            {
                ShowError(Text("account.passwords_differ"));
                return;
            }

            SetButton(false, "account.saving");
            ShowError(null);
            var error = await _join.SetPasswordAsync(_setPassword.value);
            _setPassword.value = "";
            _setPasswordAgain.value = "";
            if (error != null)
            {
                ShowError(LoginMessage(_content.Strings, error));
                SetButton(true, "account.save_password");
                return;
            }

            SetSettingPassword(false);
        }

        // The Sign in page: a username and password, or a recovery code alone.
        private async System.Threading.Tasks.Task SignIn()
        {
            SetButton(false, "account.signing_in");
            ShowError(null);
#if !UNITY_WEBGL || UNITY_EDITOR
            _join.UseLoginServer(ServerAddress());
#endif
            var error = _withCode
                ? await _join.SignInWithCodeAsync(_signInCode.value)
                : await _join.SignInAsync(_signInUsername.value.Trim(), _signInPassword.value);
            _signInPassword.value = "";
            if (error != null)
            {
                ShowError(LoginMessage(_content.Strings, error));
                SetButton(true, "account.sign_in");
                return;
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            PlayerPrefs.SetString(ServerKey, ServerAddress()); // the next launch goes back to the server signed in to
            PlayerPrefs.Save();
#endif
            _signInCode.value = "";
        }
    }
}

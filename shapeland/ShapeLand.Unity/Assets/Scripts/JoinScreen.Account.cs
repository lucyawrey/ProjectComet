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

        private void SetWithCode(bool withCode)
        {
            _withCode = withCode;
            _signIn.EnableInClassList("with-code", withCode);
            MarkFieldRows();
            _useCode.text = Text(withCode ? "account.use_password" : "account.use_code");
            _pageName.text = Text(withCode ? "account.sign_in_code_page" : "account.sign_in");
        }

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
                ShowPop();
                error = await _join.PlayAsync();
                if (attempt != _popAttempt)
                {
                    return;
                }
            }

            if (error == null)
            {
                return;
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

        private void CancelPop()
        {
            _rejoinUntil = null;
            HidePop();
            _join.CancelJoin();
            ShowCardAgain(null);
        }

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

        private async void SignOut() => await _join.SignOutAsync();

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
            PlayerPrefs.SetString(ServerKey, ServerAddress());
            PlayerPrefs.Save();
#endif
            _signInCode.value = "";
        }
    }
}

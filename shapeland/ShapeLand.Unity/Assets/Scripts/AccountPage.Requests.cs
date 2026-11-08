using Comet.Protocol.Login;
using ShapeLand.Shared.World;
using UnityEngine;

namespace ShapeLand.Client
{
    public sealed partial class AccountPage
    {
        // The step's button: save the username or password, check the password, or finish with the code.
        private void Submit()
        {
            switch (_step)
            {
                case Step.Rename:
                    Rename();
                    break;
                case Step.Password:
                    SetPassword();
                    break;
                case Step.Confirm:
                    Confirm();
                    break;
                case Step.NewCode:
                    MakeCode();
                    break;
                case Step.Code:
                    ShowStep(Step.Rows);
                    break;
            }
        }

        private async void Rename()
        {
            if (_busy)
            {
                return;
            }

            if (!ShapeLandRules.TryNormalizeName(_renameField.value, out var name))
            {
                Message(Text("join.name_rules"));
                return;
            }

            if (name == _hud.Join.PlayerName)
            {
                ShowStep(Step.Rows);
                return;
            }

            Busy(true);
            var error = await _hud.Join.RenameAsync(name);
            Busy(false);
            if (error != null)
            {
                Message(JoinScreen.LoginMessage(_hud.Join.Content.Strings, error, name));
                return;
            }

            ShowStep(Step.Rows);
        }

        // Changing a password needs a recent sign-in, so it's asked for before the new password is typed (a guest's
        // first password needs none).
        private void OpenPassword()
        {
            _resubmitPassword = false;
            OpenRecent(Step.Password, needsRecent: !(_hud.Join.Account?.Guest ?? true));
        }

        // Making a recovery code needs a recent sign-in too, asked for before its page; the code comes with its button.
        private void OpenCode()
        {
            if (!(_hud.Join.Account?.Guest ?? true))
            {
                OpenRecent(Step.NewCode, needsRecent: true);
            }
        }

        // Opens the step, first asking for the password again when it needs a recent sign-in and the session isn't.
        private async void OpenRecent(Step step, bool needsRecent)
        {
            if (_busy)
            {
                return;
            }

            if (needsRecent)
            {
                Busy(true);
                await _hud.Join.RefreshAccountAsync(); // whether the sign-in is still recent now, not when the page opened
                Busy(false);
                if (!(_hud.Join.Account?.RecentSignIn ?? false))
                {
                    AskToConfirm(step);
                    return;
                }
            }

            ShowStep(step);
        }

        private async void SetPassword()
        {
            if (_busy)
            {
                return;
            }

            if (_passwordField.value != _passwordAgain.value)
            {
                Message(Text("account.passwords_differ"));
                return;
            }

            Busy(true);
            var error = await _hud.Join.SetPasswordAsync(_passwordField.value);
            Busy(false);
            if (error == LoginErrors.SignInAgain)
            {
                // The sign-in got old while the new password was typed: confirm, then go on with it.
                _resubmitPassword = true;
                AskToConfirm(Step.Password);
                return;
            }

            if (error != null)
            {
                Message(Error(error));
                return;
            }

            ShowStep(Step.Rows);
        }

        private async void MakeCode()
        {
            if (_busy || (_hud.Join.Account?.Guest ?? true))
            {
                return;
            }

            Busy(true);
            var (code, error) = await _hud.Join.MakeRecoveryCodeAsync();
            Busy(false);
            if (error == LoginErrors.SignInAgain)
            {
                AskToConfirm(Step.Code);
                return;
            }

            if (error != null)
            {
                Message(Error(error));
                return;
            }

            ShowStep(Step.Code);
            _codeText.text = code;
        }

        // The change needs a recent sign-in: the password again, then the change goes on.
        private void AskToConfirm(Step then)
        {
            _afterConfirm = then;
            ShowStep(Step.Confirm);
        }

        private async void Confirm()
        {
            if (_busy)
            {
                return;
            }

            Busy(true);
            var error = await _hud.Join.ConfirmPasswordAsync(_confirmField.value);
            Busy(false);
            _confirmField.value = "";
            if (error != null)
            {
                Message(Error(error));
                return;
            }

            if (_afterConfirm == Step.Code)
            {
                MakeCode();
            }
            else if (_afterConfirm == Step.Password && _resubmitPassword)
            {
                // Back to the change with the new password still typed in, and on with it.
                _resubmitPassword = false;
                var (typed, again) = (_passwordField.value, _passwordAgain.value);
                ShowStep(Step.Password);
                _passwordField.value = typed;
                _passwordAgain.value = again;
                SetPassword();
            }
            else
            {
                ShowStep(_afterConfirm);
            }
        }

        // Signing out asks first; a guest would lose the character, so Set password comes first.
        private void AskToSignOut()
        {
            var strings = _hud.Join.Content.Strings;
            if (_hud.Join.Account?.Guest ?? true)
            {
                _confirm.Show(
                    strings.Get("account.sign_out_guest_title"),
                    strings.Get("account.sign_out_guest_note"),
                    null,
                    (Text("account.set_password"), false, () => ShowStep(Step.Password)),
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

        private async void SignOut()
        {
            Close();
            await _hud.Join.SignOutAsync();
        }
    }
}

using System;
using Comet.Protocol.Login;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// The Esc menu's Account page (temporary interface, to be redone): set or change the password, make a recovery
    /// code (shown once, with Copy), and sign out (a guest is warned first). Changing the password or making a code
    /// needs a recent sign-in; when the Login server says so, the page asks for the password again. Pointer and touch
    /// only for now; Esc or Back returns to the menu.
    /// </summary>
    public sealed class AccountPage
    {
        private readonly Hud _hud;
        private readonly VisualElement _page;
        private readonly Label _note;
        private readonly VisualElement _confirmRow;
        private readonly TextField _current;
        private readonly TextField _password;
        private readonly Button _setPassword;
        private readonly VisualElement _codeRow;
        private readonly Label _code;
        private readonly Button _copy;
        private readonly Label _status;
        private readonly Label _signOutWarning;
        private readonly Button _signOut;
        private Action _back;
        private Action _afterConfirm;
        private bool _busy;

        public AccountPage(Hud hud, VisualElement root)
        {
            _hud = hud;
            _page = root.Q("account");
            _note = root.Q<Label>("account-note");
            _confirmRow = root.Q("account-confirm-row");
            _current = root.Q<TextField>("account-current");
            _password = root.Q<TextField>("account-password");
            _setPassword = root.Q<Button>("account-set-password");
            _codeRow = root.Q("account-code-row");
            _code = root.Q<Label>("account-code");
            _copy = root.Q<Button>("account-copy");
            _status = root.Q<Label>("account-status");
            _signOutWarning = root.Q<Label>("account-sign-out-warning");
            _signOut = root.Q<Button>("account-sign-out");
            root.Q<Button>("account-back").clicked += Back;
            _setPassword.clicked += SetPassword;
            root.Q<Button>("account-make-code").clicked += MakeCode;
            root.Q<Button>("account-confirm").clicked += Confirm;
            _copy.clicked += () =>
            {
                GUIUtility.systemCopyBuffer = _code.text;
                Status(Text("account.copied"));
            };
            _signOut.clicked += SignOut;
        }

        /// <summary>True while the page is up.</summary>
        public bool Open => !_page.ClassListContains("hidden");

        /// <summary>True while a field on the page has the keyboard.</summary>
        public bool Typing => Open && _page.focusController?.focusedElement is VisualElement focused
            && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);

        /// <summary>Brings the page up; <paramref name="back"/> runs on Back (the menu comes back).</summary>
        public async void Show(Action back)
        {
            _back = back;
            _code.AddToClassList("hidden");
            _copy.AddToClassList("hidden");
            _confirmRow.AddToClassList("hidden");
            _signOutWarning.AddToClassList("hidden");
            _signOut.text = Text("account.sign_out");
            _password.value = "";
            _current.value = "";
            Status(null);
            Render();
            _page.RemoveFromClassList("hidden");
            await _hud.Join.RefreshAccountAsync();
            Render();
        }

        /// <summary>Closes the page and goes back to the menu. The code shown goes with it: it can't be shown again.</summary>
        public void Back()
        {
            if (!Open)
            {
                return;
            }

            Close();
            _back?.Invoke();
        }

        public void Close()
        {
            _page.AddToClassList("hidden");
            _code.text = "";
            _page.focusController?.focusedElement?.Blur();
        }

        private void Render()
        {
            var account = _hud.Join.Account;
            var guest = account == null || account.Guest;
            _note.text = guest ? Text("account.guest_note") : Format("account.claimed_note", ("name", account.Username));
            _setPassword.text = Text(guest ? "account.set_password" : "account.change_password");
            _codeRow.EnableInClassList("hidden", guest);
        }

        private async void SetPassword()
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            var error = await _hud.Join.SetPasswordAsync(_password.value);
            _busy = false;
            if (error == LoginErrors.SignInAgain)
            {
                AskToConfirm(SetPassword);
                return;
            }

            if (error == null)
            {
                _password.value = "";
                Render();
            }

            Status(error == null ? Text("account.password_saved") : Error(error));
        }

        private async void MakeCode()
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            var (code, error) = await _hud.Join.MakeRecoveryCodeAsync();
            _busy = false;
            if (error == LoginErrors.SignInAgain)
            {
                AskToConfirm(MakeCode);
                return;
            }

            if (error != null)
            {
                Status(Error(error));
                return;
            }

            _code.text = code;
            _code.RemoveFromClassList("hidden");
            _copy.RemoveFromClassList("hidden");
            Status(Format("account.code_shown", ("code", code)));
        }

        // The change needs a recent sign-in: the password again, then the change is tried once more.
        private void AskToConfirm(Action then)
        {
            _afterConfirm = then;
            _confirmRow.RemoveFromClassList("hidden");
            Status(Text("account.sign_in_again"));
        }

        private async void Confirm()
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            var error = await _hud.Join.ConfirmPasswordAsync(_current.value);
            _busy = false;
            _current.value = "";
            if (error != null)
            {
                Status(Error(error));
                return;
            }

            _confirmRow.AddToClassList("hidden");
            var then = _afterConfirm;
            _afterConfirm = null;
            then?.Invoke();
        }

        // A guest is warned first: signing out loses the character.
        private async void SignOut()
        {
            var guest = _hud.Join.Account?.Guest ?? true;
            if (guest && _signOutWarning.ClassListContains("hidden"))
            {
                _signOutWarning.RemoveFromClassList("hidden");
                _signOut.text = Text("account.sign_out_anyway");
                return;
            }

            Close();
            await _hud.Join.SignOutAsync();
        }

        private void Status(string message) => _status.text = message ?? "";

        private string Error(string code) => JoinScreen.LoginMessage(_hud.Join.Content.Strings, code);

        private string Text(string key) => _hud.Join.Content?.Strings.Get(key) ?? "";

        private string Format(string key, params (string Name, object Value)[] values) => _hud.Join.Content?.Strings.Format(key, values) ?? "";
    }
}

using System;
using Comet.Protocol.Login;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// The Esc menu's Account page: rows like Settings (Username, Set or Change password, Recovery code, Sign out), each
    /// opening its own step in the same window. Changing the password or making a code needs a recent sign-in; when the
    /// Login server says so, the Confirm it's you step asks for the password, then the change goes on. A new recovery
    /// code is shown once, and leaving it any way but "I saved it" asks first. Signing out asks first, and a guest is
    /// offered Set password. Pointer and touch only for now; Esc or Back goes back a step, then to the menu.
    /// </summary>
    public sealed class AccountPage
    {
        private enum Step
        {
            Rows,
            Rename,
            Password,
            Confirm,
            Code,
        }

        private readonly Hud _hud;
        private readonly VisualElement _page;
        private readonly Label _backText;
        private readonly Label _pageName;
        private readonly VisualElement _rows;
        private readonly Label _username;
        private readonly VisualElement _guestTag;
        private readonly Label _guestInfo;
        private readonly Label _passwordName;
        private readonly VisualElement _codeRow;
        private readonly Label _codeNote;
        private readonly VisualElement _codeValue;
        private readonly VisualElement _renameStep;
        private readonly TextField _renameField;
        private readonly Label _renameFree;
        private readonly VisualElement _passwordStep;
        private readonly Label _passwordUsername;
        private readonly TextField _passwordField;
        private readonly TextField _passwordAgain;
        private readonly Label _passwordSignsOut;
        private readonly VisualElement _confirmStep;
        private readonly TextField _confirmField;
        private readonly VisualElement _codeStep;
        private readonly Label _codeIntro;
        private readonly Label _codeText;
        private readonly Button _copy;
        private readonly Label _message;
        private readonly Button _submit;
        private readonly ConfirmPanel _confirm;
        private Step _step;
        private Action _back;
        // The step Confirm it's you goes on to once the password checks out.
        private Step _afterConfirm;
        private bool _busy;

        public AccountPage(Hud hud, VisualElement root)
        {
            _hud = hud;
            _page = root.Q("account");
            _backText = root.Q<Label>("account-back-text");
            _pageName = root.Q<Label>("account-page-name");
            _rows = root.Q("account-rows");
            _username = root.Q<Label>("account-username");
            _guestTag = root.Q("account-guest-tag");
            _guestInfo = root.Q<Label>("account-guest-info");
            _passwordName = root.Q<Label>("account-password-name");
            _codeRow = root.Q("account-code-row");
            _codeNote = root.Q<Label>("account-code-note");
            _codeValue = root.Q("account-code-value");
            _renameStep = root.Q("account-rename");
            _renameField = root.Q<TextField>("account-rename-field");
            _renameFree = root.Q<Label>("account-rename-free");
            _passwordStep = root.Q("account-password");
            _passwordUsername = root.Q<Label>("account-password-username");
            _passwordField = root.Q<TextField>("account-password-field");
            _passwordAgain = root.Q<TextField>("account-password-again");
            _passwordSignsOut = root.Q<Label>("account-password-signs-out");
            _confirmStep = root.Q("account-confirm");
            _confirmField = root.Q<TextField>("account-confirm-field");
            _codeStep = root.Q("account-code");
            _codeIntro = root.Q<Label>("account-code-intro");
            _codeText = root.Q<Label>("account-code-text");
            _copy = root.Q<Button>("account-copy");
            _message = root.Q<Label>("account-message");
            _submit = root.Q<Button>("account-submit");
            _confirm = new ConfirmPanel(_page.parent);

            root.Q<Button>("account-back").clicked += Back;
            _submit.clicked += Submit;
            _copy.clicked += () =>
            {
                GUIUtility.systemCopyBuffer = _codeText.text;
                _copy.text = Text("account.copied");
            };
            root.Q("account-username-row").RegisterCallback<ClickEvent>(_ => ShowStep(Step.Rename));
            root.Q("account-password-row").RegisterCallback<ClickEvent>(_ => ShowStep(Step.Password));
            _codeRow.RegisterCallback<ClickEvent>(_ => MakeCode());
            root.Q("account-sign-out-row").RegisterCallback<ClickEvent>(_ => AskToSignOut());
            foreach (var field in new[] { _renameField, _passwordField, _passwordAgain, _confirmField })
            {
                field.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                    {
                        Submit();
                        e.StopPropagation();
                    }
                }, TrickleDown.TrickleDown);
            }
        }

        /// <summary>True while the page (or one of its questions) is up.</summary>
        public bool Open => !_page.ClassListContains("hidden") || _confirm.Open;

        /// <summary>True while a field on the page has the keyboard.</summary>
        public bool Typing => Open && _page.focusController?.focusedElement is VisualElement focused
            && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);

        /// <summary>Brings the page up; <paramref name="back"/> runs on Back from its rows (the menu comes back).</summary>
        public void Show(Action back) => Show(back, Step.Rows);

        /// <summary>Brings the page up at its Set password step (the guest reminder's button).</summary>
        public void ShowSetPassword(Action back) => Show(back, Step.Password);

        private async void Show(Action back, Step step)
        {
            _back = back;
            _page.RemoveFromClassList("hidden");
            ShowStep(step);
            await _hud.Join.RefreshAccountAsync();
            if (Open && _step == Step.Rows)
            {
                Render();
            }
        }

        /// <summary>
        /// Esc or Back: closes the question if one is up, else goes back a step, else closes the page and goes back to
        /// the menu. A recovery code still showing asks first, since it can't be shown again.
        /// </summary>
        public void Back()
        {
            if (_confirm.Open)
            {
                _confirm.Cancel();
                return;
            }

            if (!Open || _busy)
            {
                return;
            }

            switch (_step)
            {
                case Step.Code:
                    AskToLeaveCode();
                    return;
                case Step.Rows:
                    Close();
                    _back?.Invoke();
                    return;
                default:
                    ShowStep(Step.Rows);
                    return;
            }
        }

        public void Close()
        {
            _confirm.Close();
            _page.AddToClassList("hidden");
            _codeText.text = "";
            _page.focusController?.focusedElement?.Blur();
        }

        private void ShowStep(Step step)
        {
            if (_busy || (step == Step.Rename && _hud.Join.Account == null))
            {
                return;
            }

            _step = step;
            var guest = _hud.Join.Account?.Guest ?? true;
            var strings = _hud.Join.Content.Strings;
            var name = ("name", (object)_hud.Join.PlayerName);
            _rows.EnableInClassList("shown", step == Step.Rows);
            _renameStep.EnableInClassList("shown", step == Step.Rename);
            _passwordStep.EnableInClassList("shown", step == Step.Password);
            _confirmStep.EnableInClassList("shown", step == Step.Confirm);
            _codeStep.EnableInClassList("shown", step == Step.Code);
            _submit.EnableInClassList("hidden", step == Step.Rows);
            _backText.text = Text(step == Step.Rows ? "menu.back" : "account.page");
            _pageName.text = step switch
            {
                Step.Rename => Text("account.username"),
                Step.Password => Text(guest ? "account.set_password" : "account.change_password"),
                Step.Confirm => Text("account.confirm_page"),
                Step.Code => Text("account.recovery_code"),
                _ => Text("account.page"),
            };
            Message(null);
            _page.focusController?.focusedElement?.Blur();
            switch (step)
            {
                case Step.Rows:
                    Render();
                    break;
                case Step.Rename:
                    _renameField.value = _hud.Join.PlayerName;
                    _renameFree.text = strings.Format("account.rename_free", name);
                    SetSubmit();
                    _renameField.Focus();
                    break;
                case Step.Password:
                    _passwordUsername.text = _hud.Join.PlayerName;
                    _passwordField.value = "";
                    _passwordAgain.value = "";
                    _passwordSignsOut.text = guest ? "" : strings.Format("account.password_signs_out", name);
                    _passwordSignsOut.EnableInClassList("hidden", guest);
                    SetSubmit();
                    _passwordField.Focus();
                    break;
                case Step.Confirm:
                    _confirmField.value = "";
                    SetSubmit();
                    _confirmField.Focus();
                    break;
                case Step.Code:
                    _codeIntro.text = strings.Format("account.code_intro", name);
                    _copy.text = Text("account.copy");
                    SetSubmit();
                    break;
            }
        }

        private void Render()
        {
            var guest = _hud.Join.Account?.Guest ?? true;
            _username.text = _hud.Join.PlayerName;
            _guestTag.EnableInClassList("hidden", !guest);
            _guestInfo.text = _hud.Join.Content.Strings.Format("account.guest_note", ("name", _hud.Join.PlayerName));
            _guestInfo.EnableInClassList("hidden", !guest);
            _passwordName.text = Text(guest ? "account.set_password" : "account.change_password");
            // A guest has no recovery code until there's a password to recover.
            _codeRow.EnableInClassList("disabled", guest);
            _codeNote.text = Text(guest ? "account.recovery_code_later" : "account.recovery_code_note");
            _codeValue.EnableInClassList("hidden", guest);
        }

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
            else
            {
                ShowStep(_afterConfirm);
            }
        }

        private void AskToLeaveCode() =>
            _confirm.Show(Text("account.leave_code_title"), Text("account.leave_code_note"), null,
                (Text("account.show_code"), false, null),
                (Text("account.leave"), true, () => ShowStep(Step.Rows)));

        // Signing out asks first; a guest would lose the character, so Set password comes first.
        private void AskToSignOut()
        {
            var strings = _hud.Join.Content.Strings;
            var name = ("name", (object)_hud.Join.PlayerName);
            if (_hud.Join.Account?.Guest ?? true)
            {
                _confirm.Show(strings.Format("account.sign_out_guest_title", name), strings.Format("account.sign_out_guest_note", name), null,
                    (Text("account.set_password"), false, () => ShowStep(Step.Password)),
                    (Text("account.stay"), false, null),
                    (Text("account.delete_sign_out"), true, SignOut));
            }
            else
            {
                _confirm.Show(Text("account.sign_out_title"), Text("account.sign_out_note"), null,
                    (Text("account.stay"), false, null),
                    (Text("account.sign_out"), true, SignOut));
            }
        }

        private async void SignOut()
        {
            Close();
            await _hud.Join.SignOutAsync();
        }

        // The step's button, ready.
        private void SetSubmit()
        {
            _submit.SetEnabled(true);
            _submit.text = Text(_step switch
            {
                Step.Rename => "account.save_username",
                Step.Password => "account.save_password",
                Step.Confirm => "account.continue",
                _ => "account.saved_it",
            });
        }

        // While a request is out the button shows it's working and the page holds still.
        private void Busy(bool busy)
        {
            _busy = busy;
            if (!busy)
            {
                SetSubmit();
                return;
            }

            _submit.SetEnabled(false);
            _submit.text = Text("account.saving");
            Message(null);
        }

        private void Message(string message) => _message.text = message ?? "";

        private string Error(string code) => JoinScreen.LoginMessage(_hud.Join.Content.Strings, code);

        private string Text(string key) => _hud.Join.Content?.Strings.Get(key) ?? "";
    }
}

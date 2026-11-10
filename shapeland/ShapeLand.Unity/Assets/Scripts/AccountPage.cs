using System;
using System.Collections.Generic;
using Comet.Protocol.Login;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    public sealed class AccountPage
    {
        private enum Step
        {
            Rows,
            Rename,
            Password,
            Confirm,
            NewCode,
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
        private readonly VisualElement _newCodeStep;
        private readonly Label _newCodeStatus;
        private readonly Label _codeIntro;
        private readonly Label _codeText;
        private readonly Button _copy;
        private readonly Label _message;
        private readonly Button _submit;
        private readonly Button _signOut;
        private readonly ConfirmPanel _confirm;
        private Step _step;
        private Action _back;
        private readonly Button _backButton;
        private readonly VisualElement _usernameRow;
        private readonly VisualElement _passwordRow;
        private readonly VisualElement _confirmHost;

        private int _selected;
        private bool _onBack;
        private readonly MenuNavigation _navigation = new();
        private int _shownOnFrame;

        private Step _afterConfirm;
        private bool _resubmitPassword;
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
            _newCodeStep = root.Q("account-new-code");
            _newCodeStatus = root.Q<Label>("account-new-code-status");
            _codeStep = root.Q("account-code");
            _codeIntro = root.Q<Label>("account-code-intro");
            _codeText = root.Q<Label>("account-code-text");
            _copy = root.Q<Button>("account-copy");
            _message = root.Q<Label>("account-message");
            _submit = root.Q<Button>("account-submit");
            _confirmHost = root.Q("account-safe").parent;
            _confirm = new ConfirmPanel(_confirmHost);
            _usernameRow = root.Q("account-username-row");
            _passwordRow = root.Q("account-password-row");
            _backButton = root.Q<Button>("account-back");
            _backButton.clicked += Back;
            _page.RegisterCallback<PointerMoveEvent>(_ => Navigating = false, TrickleDown.TrickleDown);
            _page.RegisterCallback<PointerDownEvent>(_ => Navigating = false, TrickleDown.TrickleDown);
            _submit.clicked += Submit;
            _copy.clicked += CopyCode;
            _usernameRow.RegisterCallback<ClickEvent>(_ => ShowStep(Step.Rename));
            _passwordRow.RegisterCallback<ClickEvent>(_ => OpenPassword());
            _codeRow.RegisterCallback<ClickEvent>(_ => OpenCode());
            _signOut = root.Q<Button>("account-sign-out");
            _signOut.clicked += AskToSignOut;
            foreach (var field in new[] { _renameField, _passwordField, _passwordAgain, _confirmField })
            {
                field.RegisterCallback<KeyDownEvent>(
                    e =>
                    {
                        if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                        {
                            Submit();
                            e.StopPropagation();
                        }
                        else if (e.keyCode == KeyCode.Tab)
                        {
                            Navigating = true;
                            field.Blur();
                            Move(e.shiftKey ? -1 : 1);
                            FocusSelectedField();
                            e.StopPropagation();
                        }
                    },
                    TrickleDown.TrickleDown
                );
            }
        }

        public bool Open => !_page.ClassListContains("hidden") || _confirm.Open;

        public bool Typing =>
            Open
            && _page.focusController?.focusedElement is VisualElement focused
            && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);

        public void Show(Action back, bool navigating = false) => Show(back, Step.Rows, navigating);

        public void ShowSetPassword(Action back) => Show(back, Step.Password, false);

        private async void Show(Action back, Step step, bool navigating)
        {
            _back = back;
            _shownOnFrame = Time.frameCount;
            Navigating = navigating;
            _page.RemoveFromClassList("hidden");
            ShowStep(step);
            await _hud.Join.RefreshAccountAsync();
            if (Open && _step == Step.Rows)
            {
                Render();
            }
        }

        public void Back()
        {
            if (_confirm.Open)
            {
                _confirm.Close();
                return;
            }

            if (!Open || _busy)
            {
                return;
            }

            switch (_step)
            {
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
            _rows.EnableInClassList("shown", step == Step.Rows);
            _renameStep.EnableInClassList("shown", step == Step.Rename);
            _passwordStep.EnableInClassList("shown", step == Step.Password);
            _confirmStep.EnableInClassList("shown", step == Step.Confirm);
            _newCodeStep.EnableInClassList("shown", step == Step.NewCode);
            _codeStep.EnableInClassList("shown", step == Step.Code);
            _submit.EnableInClassList("hidden", step == Step.Rows);
            _signOut.EnableInClassList("hidden", step != Step.Rows);
            _backText.text = Text(step == Step.Rows ? "menu.back" : "account.page");
            _pageName.text = step switch
            {
                Step.Rename => Text("account.change_username"),
                Step.Password => Text(guest ? "account.set_password" : "account.change_password"),
                Step.Confirm => Text("account.confirm_page"),
                Step.NewCode or Step.Code => Text("account.recovery_code"),
                _ => Text("account.page"),
            };
            Message(null);
            _page.focusController?.focusedElement?.Blur();
            _selected = 0;
            _onBack = false;
            switch (step)
            {
                case Step.Rows:
                    Render();
                    break;
                case Step.Rename:
                    _renameField.value = _hud.Join.PlayerName;
                    _renameFree.text = strings.Get("account.rename_free");
                    SetSubmit();
                    FocusUnlessPad(_renameField);
                    break;
                case Step.Password:
                    _passwordUsername.text = _hud.Join.PlayerName;
                    _passwordField.value = "";
                    _passwordAgain.value = "";
                    _passwordSignsOut.text = guest ? "" : strings.Get("account.password_signs_out");
                    _passwordSignsOut.EnableInClassList("hidden", guest);
                    SetSubmit();
                    FocusUnlessPad(_passwordField);
                    break;
                case Step.Confirm:
                    _confirmField.value = "";
                    SetSubmit();
                    FocusUnlessPad(_confirmField);
                    break;
                case Step.NewCode:
                    _newCodeStatus.text = Text(_hud.Join.Account?.HasRecoveryCode ?? false ? "account.new_code_has" : "account.new_code_none");
                    SetSubmit();
                    break;
                case Step.Code:
                    _codeIntro.text = strings.Get("account.code_intro");
                    _copy.text = Text("account.copy");
                    SetSubmit();
                    break;
            }

            Mark();
        }

        public bool Navigating
        {
            get => _page.ClassListContains("navigating");
            private set
            {
                _page.EnableInClassList("navigating", value);
                _confirmHost.EnableInClassList("navigating", value);
            }
        }

        private bool _padInput;

        public void Update(ShapeLandControls.MenuActions actions)
        {
            if (!Open || Time.frameCount == _shownOnFrame)
            {
                return;
            }

            var fromPad = actions.Navigate.activeControl?.device is Gamepad || actions.Submit.activeControl?.device is Gamepad;
            if (actions.Navigate.WasPerformedThisFrame() || actions.Submit.WasPressedThisFrame() || actions.Back.WasPressedThisFrame())
            {
                _padInput = fromPad;
            }

            if (_confirm.Open)
            {
                _confirm.Navigate(_navigation, actions, () => Navigating = true);
                return;
            }

            var typing = Typing;
            var step = _navigation.StepVertical(typing && !fromPad ? MenuNavigation.TypingArrows() : actions.Navigate.ReadValue<Vector2>());
            var submit = actions.Submit.WasPressedThisFrame() && (!typing || fromPad);
            if (!Navigating && step != Vector2Int.zero && !typing)
            {
                Navigating = true;
                Mark();
                return;
            }

            if (submit || actions.Back.WasPressedThisFrame())
            {
                Navigating = true;
            }

            if (step.y != 0)
            {
                Navigating = true;
                _page.focusController?.focusedElement?.Blur();
                Move(step.y);
                if (!fromPad)
                {
                    FocusSelectedField();
                }
            }

            if (actions.Back.WasPressedThisFrame())
            {
                if (typing)
                {
                    _page.focusController?.focusedElement?.Blur();
                }
                else
                {
                    Back();
                }
            }
            else if (submit)
            {
                if (typing)
                {
                    _page.focusController?.focusedElement?.Blur();
                }
                else
                {
                    Activate();
                }
            }
        }

        private List<VisualElement> Items() =>
            _step switch
            {
                Step.Rows => (_hud.Join.Account?.Guest ?? true)
                    ? new List<VisualElement> { _usernameRow, _passwordRow, _signOut }
                    : new List<VisualElement> { _usernameRow, _passwordRow, _codeRow, _signOut },
                Step.Rename => new List<VisualElement> { _renameField, _submit },
                Step.Password => new List<VisualElement> { _passwordField, _passwordAgain, _submit },
                Step.Confirm => new List<VisualElement> { _confirmField, _submit },
                Step.NewCode => new List<VisualElement> { _submit },
                _ => new List<VisualElement> { _copy, _submit },
            };

        private void Move(int by)
        {
            var count = Items().Count;
            if (_onBack)
            {
                _onBack = by < 0;
            }
            else if (_selected + by < 0)
            {
                _onBack = true;
            }
            else
            {
                _selected = Math.Clamp(_selected + by, 0, count - 1);
            }

            Mark();
        }

        private void Mark()
        {
            var items = Items();
            foreach (
                var item in new VisualElement[]
                {
                    _usernameRow,
                    _passwordRow,
                    _codeRow,
                    _signOut,
                    _renameField,
                    _passwordField,
                    _passwordAgain,
                    _confirmField,
                    _copy,
                    _submit,
                }
            )
            {
                item.EnableInClassList("selected", !_onBack && items.IndexOf(item) == _selected);
            }

            _backButton.EnableInClassList("selected", _onBack);
        }

        private void Activate()
        {
            if (_onBack)
            {
                Back();
                return;
            }

            var item = Items()[Math.Clamp(_selected, 0, Items().Count - 1)];
            if (item is TextField field)
            {
                field.Focus();
            }
            else if (item == _usernameRow)
            {
                ShowStep(Step.Rename);
            }
            else if (item == _passwordRow)
            {
                OpenPassword();
            }
            else if (item == _codeRow)
            {
                OpenCode();
            }
            else if (item == _signOut)
            {
                AskToSignOut();
            }
            else if (item == _copy)
            {
                CopyCode();
            }
            else
            {
                Submit();
            }
        }

        private void FocusSelectedField()
        {
            if (!_onBack && Items()[_selected] is TextField field)
            {
                field.Focus();
            }
        }

        private void FocusUnlessPad(TextField field)
        {
            if (!(_padInput && Navigating))
            {
                field.Focus();
            }
        }

        private void CopyCode()
        {
            GUIUtility.systemCopyBuffer = _codeText.text;
            _copy.text = Text("account.copied");
        }

        private void Render()
        {
            var guest = _hud.Join.Account?.Guest ?? true;
            _username.text = _hud.Join.PlayerName;
            _guestTag.EnableInClassList("hidden", !guest);
            _guestInfo.text = Text("account.guest_note");
            _guestInfo.EnableInClassList("hidden", !guest);
            _passwordName.text = Text(guest ? "account.row_set_password" : "account.row_change_password");
            _codeRow.EnableInClassList("disabled", guest);
            _codeNote.text = Text(guest ? "account.row_recovery_code_later" : "account.row_recovery_code_note");
            _codeValue.EnableInClassList("hidden", guest);
        }

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

        private void OpenPassword()
        {
            _resubmitPassword = false;
            OpenRecent(Step.Password, needsRecent: !(_hud.Join.Account?.Guest ?? true));
        }

        private void OpenCode()
        {
            if (!(_hud.Join.Account?.Guest ?? true))
            {
                OpenRecent(Step.NewCode, needsRecent: true);
            }
        }

        private async void OpenRecent(Step step, bool needsRecent)
        {
            if (_busy)
            {
                return;
            }

            if (needsRecent)
            {
                Busy(true);
                await _hud.Join.RefreshAccountAsync();
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

        private void AskToSignOut() =>
            _confirm.AskToSignOut(_hud.Join.Content.Strings, _hud.Join.Account?.Guest ?? true, () => ShowStep(Step.Password), SignOut);

        private async void SignOut()
        {
            Close();
            await _hud.Join.SignOutAsync();
        }

        private void SetSubmit()
        {
            _submit.SetEnabled(true);
            _submit.text = Text(
                _step switch
                {
                    Step.Rename => "account.save_username",
                    Step.Password => "account.save_password",
                    Step.Confirm => "account.continue",
                    Step.NewCode => "account.create_code",
                    _ => "account.done",
                }
            );
        }

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

        private void Message(string message)
        {
            _message.text = message ?? "";
            _message.EnableInClassList("shown", message != null);
        }

        private string Error(string code) => JoinScreen.LoginMessage(_hud.Join.Content.Strings, code);

        private string Text(string key) => _hud.Join.Content?.Strings.Get(key) ?? "";
    }
}

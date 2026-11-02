using System;
using Comet.Protocol.Login;
using ShapeLand.Shared.World;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// The Esc menu's Account page: rows like Settings (Username, Set or Change password, Recovery code, Sign out), each
    /// opening its own step in the same window. Changing the password or making a code needs a recent sign-in; when the
    /// Login server says so, the Confirm it's you step asks for the password, then the change goes on. A new recovery
    /// code is shown once; Copy counts as saving it, and leaving before that asks first. Signing out asks first, and a guest is
    /// offered Set password. Keys and a gamepad move a selection through the step's rows, fields and buttons, with Back
    /// above them (<see cref="Update"/>); Esc or B goes back a step, then to the menu.
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
        private readonly Button _signOut;
        private readonly ConfirmPanel _confirm;
        private Step _step;
        private Action _back;
        private readonly Button _backButton;
        private readonly VisualElement _usernameRow;
        private readonly VisualElement _passwordRow;
        private readonly VisualElement _confirmHost;
        // Keys and gamepad: the selected item in the step (or Back above it), and the held direction's repeat.
        private int _selected;
        private bool _onBack;
        private Vector2 _held;
        private float _repeatAt;
        private int _shownOnFrame;
        private const float RepeatDelay = 0.4f, RepeatInterval = 0.1f;
        // The step Confirm it's you goes on to once the password checks out.
        private Step _afterConfirm;
        private bool _busy;
        // Whether the code shown was copied, which is as close to "saved" as the page can tell.
        private bool _copied;

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
            _confirmHost = root.Q("account-safe").parent;
            _confirm = new ConfirmPanel(_confirmHost); // over the whole screen, insets included
            _usernameRow = root.Q("account-username-row");
            _passwordRow = root.Q("account-password-row");
            _backButton = root.Q<Button>("account-back");
            _backButton.clicked += Back;
            _page.RegisterCallback<PointerMoveEvent>(_ => Navigating = false, TrickleDown.TrickleDown);
            _page.RegisterCallback<PointerDownEvent>(_ => Navigating = false, TrickleDown.TrickleDown);
            _submit.clicked += Submit;
            _copy.clicked += CopyCode;
            _usernameRow.RegisterCallback<ClickEvent>(_ => ShowStep(Step.Rename));
            _passwordRow.RegisterCallback<ClickEvent>(_ => ShowStep(Step.Password));
            _codeRow.RegisterCallback<ClickEvent>(_ => MakeCode());
            _signOut = root.Q<Button>("account-sign-out");
            _signOut.clicked += AskToSignOut;
            foreach (var field in new[] { _renameField, _passwordField, _passwordAgain, _confirmField })
            {
                field.RegisterCallback<KeyDownEvent>(e =>
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
                }, TrickleDown.TrickleDown);
            }
        }

        /// <summary>True while the page (or one of its questions) is up.</summary>
        public bool Open => !_page.ClassListContains("hidden") || _confirm.Open;

        /// <summary>True while a field on the page has the keyboard.</summary>
        public bool Typing => Open && _page.focusController?.focusedElement is VisualElement focused
            && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);

        /// <summary>
        /// Brings the page up; <paramref name="back"/> runs on Back from its rows (the menu comes back). With
        /// <paramref name="navigating"/> (opened with keys or a gamepad), the selection shows at once.
        /// </summary>
        public void Show(Action back, bool navigating = false) => Show(back, Step.Rows, navigating);

        /// <summary>Brings the page up at its Set password step (the guest reminder's button).</summary>
        public void ShowSetPassword(Action back) => Show(back, Step.Password, false);

        private async void Show(Action back, Step step, bool navigating)
        {
            _back = back;
            _shownOnFrame = Time.frameCount; // the press that opened the page isn't the page's
            Navigating = navigating;
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
                    LeaveCode();
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
            _rows.EnableInClassList("shown", step == Step.Rows);
            _renameStep.EnableInClassList("shown", step == Step.Rename);
            _passwordStep.EnableInClassList("shown", step == Step.Password);
            _confirmStep.EnableInClassList("shown", step == Step.Confirm);
            _codeStep.EnableInClassList("shown", step == Step.Code);
            _submit.EnableInClassList("hidden", step == Step.Rows);
            _signOut.EnableInClassList("hidden", step != Step.Rows); // the rows' bottom corner, as on the signed-in card
            _backText.text = Text(step == Step.Rows ? "menu.back" : "account.page");
            _pageName.text = step switch
            {
                Step.Rename => Text("account.change_username"),
                Step.Password => Text(guest ? "account.set_password" : "account.change_password"),
                Step.Confirm => Text("account.confirm_page"),
                Step.Code => Text("account.recovery_code"),
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
                case Step.Code:
                    _codeIntro.text = strings.Get("account.code_intro");
                    _copy.text = Text("account.copy");
                    _copied = false;
                    SetSubmit();
                    break;
            }

            Mark();
        }

        /// <summary>True while keys or a gamepad move the page's selection, which shows only then.</summary>
        public bool Navigating
        {
            get => _page.ClassListContains("navigating");
            private set
            {
                _page.EnableInClassList("navigating", value);
                _confirmHost.EnableInClassList("navigating", value); // the sign-out and leave-code questions
            }
        }

        // Whether the last move came from a gamepad: a field it lands on waits for A, so a phone's keyboard doesn't
        // pop up by surprise.
        private bool _padInput;

        /// <summary>
        /// Keys and a gamepad on the page, with the Esc menu's Menu actions (Esc itself is the Hud's, through
        /// <see cref="Back"/>): up and down move through the step, Submit uses what's selected, B goes back. While a
        /// field has the keyboard, Up, Down and Tab still move on, and landing on a field with the keyboard types into it.
        /// </summary>
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
                var question = Repeat(actions.Navigate.ReadValue<Vector2>());
                if (question.y != 0)
                {
                    Navigating = true;
                    _confirm.Move(question.y);
                }

                if (actions.Back.WasPressedThisFrame())
                {
                    _confirm.Cancel();
                }
                else if (actions.Submit.WasPressedThisFrame())
                {
                    Navigating = true;
                    _confirm.Choose();
                }

                return;
            }

            var typing = Typing;
            var step = Repeat(typing && !fromPad ? TypingArrows() : actions.Navigate.ReadValue<Vector2>());
            var submit = actions.Submit.WasPressedThisFrame() && (!typing || fromPad); // Space and Enter in a field are the field's
            if (!Navigating && step != Vector2Int.zero && !typing)
            {
                Navigating = true; // the first press only shows where the selection is
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

        // The step's items, top to bottom: the rows and Sign Out; a form's fields and its button; the code's Copy and Done.
        private List<VisualElement> Items() => _step switch
        {
            Step.Rows => (_hud.Join.Account?.Guest ?? true)
                ? new List<VisualElement> { _usernameRow, _passwordRow, _signOut }
                : new List<VisualElement> { _usernameRow, _passwordRow, _codeRow, _signOut },
            Step.Rename => new List<VisualElement> { _renameField, _submit },
            Step.Password => new List<VisualElement> { _passwordField, _passwordAgain, _submit },
            Step.Confirm => new List<VisualElement> { _confirmField, _submit },
            _ => new List<VisualElement> { _copy, _submit },
        };

        // Up from the first item reaches Back; down from Back, the first item again.
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
            foreach (var item in new VisualElement[] { _usernameRow, _passwordRow, _codeRow, _signOut, _renameField, _passwordField, _passwordAgain, _confirmField, _copy, _submit })
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
                ShowStep(Step.Password);
            }
            else if (item == _codeRow)
            {
                MakeCode();
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
            _copied = true;
        }

        // While a field has the keyboard, only Up and Down move on (Left and Right move the caret, letters type).
        private static Vector2 TypingArrows()
        {
            var keyboard = Keyboard.current;
            return keyboard == null ? Vector2.zero
                : new Vector2(0, keyboard.upArrowKey.isPressed ? 1 : keyboard.downArrowKey.isPressed ? -1 : 0);
        }

        // A held direction steps once, then repeats after a pause; down is positive.
        private Vector2Int Repeat(Vector2 value)
        {
            var held = new Vector2(0, Mathf.Abs(value.y) > 0.5f ? -Mathf.Sign(value.y) : 0);
            var now = Time.unscaledTime;
            if (held != _held)
            {
                _held = held;
                _repeatAt = now + RepeatDelay;
                return Vector2Int.RoundToInt(held);
            }

            if (held != Vector2.zero && now >= _repeatAt)
            {
                _repeatAt = now + RepeatInterval;
                return Vector2Int.RoundToInt(held);
            }

            return Vector2Int.zero;
        }

        private void Render()
        {
            var guest = _hud.Join.Account?.Guest ?? true;
            _username.text = _hud.Join.PlayerName;
            _guestTag.EnableInClassList("hidden", !guest);
            _guestInfo.text = Text("account.guest_note");
            _guestInfo.EnableInClassList("hidden", !guest);
            _passwordName.text = Text(guest ? "account.row_set_password" : "account.row_change_password");
            // A guest has no recovery code until there's a password to recover.
            _codeRow.EnableInClassList("disabled", guest);
            _codeNote.text = Text(guest ? "account.row_recovery_code_later" : "account.row_recovery_code_note");
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
                    LeaveCode();
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
            else if (_afterConfirm == Step.Password)
            {
                // Back to the change with the new password still typed in, and on with it.
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

        // Leaving the code: once copied it just goes; before that it asks, since the code can't be shown again.
        private void LeaveCode()
        {
            if (_copied)
            {
                ShowStep(Step.Rows);
                return;
            }

            AskToLeaveCode();
        }

        private void AskToLeaveCode() =>
            _confirm.Show(Text("account.leave_code_title"), Text("account.leave_code_note"), null,
                (Text("account.show_code"), false, null),
                (Text("account.leave"), true, () => ShowStep(Step.Rows)));

        // Signing out asks first; a guest would lose the character, so Set password comes first.
        private void AskToSignOut()
        {
            var strings = _hud.Join.Content.Strings;
            if (_hud.Join.Account?.Guest ?? true)
            {
                _confirm.Show(strings.Get("account.sign_out_guest_title"), strings.Get("account.sign_out_guest_note"), null,
                    (Text("account.set_password"), false, () => ShowStep(Step.Password)),
                    (Text("account.stay"), false, null),
                    (Text("account.sign_out"), true, SignOut));
            }
            else
            {
                _confirm.Show(Text("account.sign_out_title"), Text("account.sign_out_note"), null,
                    (Text("account.sign_out"), true, SignOut),
                    (Text("account.stay"), false, null));
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
                _ => "account.done",
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

        // The page's error, in the join card's error box.
        private void Message(string message)
        {
            _message.text = message ?? "";
            _message.EnableInClassList("shown", message != null);
        }

        private string Error(string code) => JoinScreen.LoginMessage(_hud.Join.Content.Strings, code);

        private string Text(string key) => _hud.Join.Content?.Strings.Get(key) ?? "";
    }
}

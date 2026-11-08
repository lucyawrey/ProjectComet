using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    public sealed partial class AccountPage
    {
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
                var question = _navigation.StepVertical(actions.Navigate.ReadValue<Vector2>());
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
            var step = _navigation.StepVertical(typing && !fromPad ? MenuNavigation.TypingArrows() : actions.Navigate.ReadValue<Vector2>());
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
    }
}

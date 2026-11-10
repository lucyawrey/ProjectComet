using System;
using System.Collections.Generic;
using System.Linq;
using Comet.Content;
using ShapeLand.Shared.Content;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    public sealed partial class JoinScreen
    {
        private void MarkFieldRows()
        {
            var page = SigningIn || SettingPassword;
            foreach (var row in _scroll.Query(className: "nav-row").ToList())
            {
                row.EnableInClassList("hidden", page || SignedInCard || (row == _nameRow && Changing));
            }

            _signInUsernameField.EnableInClassList("hidden", !SigningIn || _withCode);
            _signInPasswordField.EnableInClassList("hidden", !SigningIn || _withCode);
            _signInCodeField.EnableInClassList("hidden", !SigningIn || !_withCode);
            _useCode.EnableInClassList("hidden", !SigningIn);
            _pageRows[4].EnableInClassList("hidden", !SettingPassword);
            _pageRows[5].EnableInClassList("hidden", !SettingPassword);
#if UNITY_WEBGL && !UNITY_EDITOR
            _serverLine.AddToClassList("hidden");
#else
            _serverLine.EnableInClassList("hidden", Changing || SettingPassword || !JoinClient.Accounts);
#endif
            if (_rows != null && !Usable(_rows[_row]))
            {
                SelectRow(FirstRow());
            }
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && Typing)
            {
                Submit();
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Tab && !_confirm.Open)
            {
                Navigating = true;
                _root.focusController?.focusedElement?.Blur();
                MoveRow(e.shiftKey ? -1 : 1);
                FocusRowField();
                e.StopPropagation();
            }
        }

        private void FocusRowField()
        {
            if (!_onBack && _rows[_row].Q<TextField>() is { } field && Usable(_rows[_row]))
            {
                field.Focus();
            }
        }

        private void Navigate(ShapeLandControls.MenuActions actions)
        {
            if (_confirm.Open)
            {
                _confirm.Navigate(_navigation, actions, () => Navigating = true);
                return;
            }

            var typing = Typing;
            var fromPad = actions.Navigate.activeControl?.device is Gamepad;
            var step = _navigation.Step(typing && !fromPad ? MenuNavigation.TypingArrows() : actions.Navigate.ReadValue<Vector2>());
            if (!Navigating && step != Vector2Int.zero && !typing)
            {
                Navigating = true;
                return;
            }

            if (actions.Back.WasPressedThisFrame() || actions.Submit.WasPressedThisFrame() && !typing)
            {
                Navigating = true;
            }

            if (step != Vector2Int.zero && typing)
            {
                _root.focusController?.focusedElement?.Blur();
            }

            if (step.y != 0)
            {
                MoveRow(step.y);
                if (!fromPad)
                {
                    FocusRowField();
                }
            }
            else if (step.x != 0)
            {
                Pick(step.x);
            }

            if (actions.Back.WasPressedThisFrame())
            {
                if (typing)
                {
                    _root.focusController?.focusedElement?.Blur();
                }
                else if (SigningIn || SettingPassword)
                {
                    PageBack();
                }
                else if (FromMenu)
                {
                    Back();
                }
                else
                {
                    Cancel();
                }
            }
            else if (actions.Submit.WasPressedThisFrame() && (!typing || actions.Submit.activeControl?.device is Gamepad))
            {
                SubmitRow(typing);
            }
        }

        private void MoveRow(int by)
        {
            if (_onBack)
            {
                if (by > 0)
                {
                    SelectRow(FirstRow());
                }

                return;
            }

            if (by < 0 && _row == FirstRow() && CurrentBack != null)
            {
                SelectBack();
                return;
            }

            var row = _row;
            do
            {
                row += by;
            } while (row >= 0 && row < _rows.Length && !Usable(_rows[row]));

            if (row >= 0 && row < _rows.Length)
            {
                SelectRow(row);
                if (_scroll.contentContainer.Contains(_rows[row]))
                {
                    _scroll.ScrollTo(_rows[row]);
                }
            }
        }

        private static bool Usable(VisualElement row) => !row.ClassListContains("hidden");

        private int FirstRow() => Math.Max(0, Array.FindIndex(_rows, Usable));

        private Button CurrentBack =>
            SigningIn || SettingPassword ? _pageBack
            : FromMenu ? _backButton
            : null;

        private void SelectBack()
        {
            SelectRow(FirstRow());
            _onBack = true;
            _rows[_row].RemoveFromClassList("selected");
            MarkItem();
            CurrentBack?.AddToClassList("selected");
        }

        private void SelectRow(int row)
        {
            _onBack = false;
            _backButton.RemoveFromClassList("selected");
            _pageBack.RemoveFromClassList("selected");
            _row = row;
            for (var i = 0; i < _rows.Length; i++)
            {
                _rows[i].EnableInClassList("selected", i == row && Items(_rows[i]).Count == 0);
            }

            _buttonIndex = _rows[row] == _buttonsRow ? Items(_buttonsRow).IndexOf(_joinButton) : 0;
            MarkItem();
        }

        private List<VisualElement> Items(VisualElement row)
        {
            if (row == _buttonsRow)
            {
                var signIn = !Changing && !SigningIn && !SignedInCard && !SettingPassword;
                return new VisualElement[] { signIn ? _signInButton : null, SignedInCard ? _signOutButton : null, _cancelButton, _joinButton }
                    .Where(item => item != null && Usable(item))
                    .ToList();
            }

            if (row == _serverLine)
            {
                return _serverLine.ClassListContains("open")
                    ? new List<VisualElement> { _serverField, _serverCancel }
                    : new List<VisualElement> { _serverChange };
            }

            return new List<VisualElement>();
        }

        private VisualElement[] AllItems() =>
            new VisualElement[] { _signInButton, _signOutButton, _cancelButton, _joinButton, _serverChange, _serverField, _serverCancel };

        private void SelectItem(VisualElement item)
        {
            var row = item == _serverChange || item == _serverField || item == _serverCancel ? _serverLine : _buttonsRow;
            _onBack = false;
            _row = Array.IndexOf(_rows, row);
            foreach (var other in _rows)
            {
                other.RemoveFromClassList("selected");
            }

            _buttonIndex = Mathf.Max(0, Items(row).IndexOf(item));
            MarkItem();
        }

        private void MarkItem()
        {
            var items = _onBack ? new List<VisualElement>() : Items(_rows[_row]);
            foreach (var item in AllItems())
            {
                item.EnableInClassList("selected", items.IndexOf(item) == _buttonIndex && items.Contains(item));
            }
        }

        private void Pick(int by)
        {
            var row = _rows[_row];
            if (_onBack)
            {
                return;
            }

            var items = Items(row);
            if (items.Count > 0)
            {
                _buttonIndex = Mathf.Clamp(_buttonIndex + by, 0, items.Count - 1);
                MarkItem();
                return;
            }

            var choices = row.Q(className: "row");
            if (choices == null || choices.childCount == 0)
            {
                return;
            }

            var current = choices.Children().ToList().FindIndex(choice => choice.ClassListContains("on"));
            var next = Mathf.Clamp(current + by, 0, choices.childCount - 1);
            if (next != current && _choose.TryGetValue(choices[next], out var choose))
            {
                choose();
            }
        }

        private void SubmitRow(bool typing)
        {
            var row = _rows[_row];
            if (typing)
            {
                _root.focusController?.focusedElement?.Blur();
            }
            else if (_onBack)
            {
                if (SigningIn || SettingPassword)
                {
                    PageBack();
                }
                else
                {
                    Back();
                }
            }
            else if (Items(row) is { Count: > 0 } items)
            {
                var item = items[Mathf.Clamp(_buttonIndex, 0, items.Count - 1)];
                if (item == _serverField)
                {
                    _server.Focus();
                }
                else if (item == _joinButton)
                {
                    Submit();
                }
                else if (item == _cancelButton)
                {
                    Cancel();
                }
                else if (item == _signInButton)
                {
                    SetSigningIn(true);
                }
                else if (item == _signOutButton)
                {
                    AskToSignOut();
                }
                else if (item == _serverChange)
                {
                    OpenServerField();
                }
                else if (item == _serverCancel)
                {
                    CancelServerField();
                }
            }
            else if (row == _useCode)
            {
                SetWithCode(!_withCode);
            }
            else if (row.Q<TextField>() is { } field)
            {
                field.Focus();
            }
            else
            {
                Submit();
            }
        }
    }
}

using System;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// A question over the dimmed screen, in the Esc menu's Leave game style: a title, a note and a column of buttons,
    /// risky ones red, one after safe ones set apart below them. A confirm leads with the action the player was heading
    /// for, unless it loses something for good. Choosing one closes it. Keys and a gamepad move a selection through
    /// the choices (<see cref="Move"/>, <see cref="Choose"/>), shown only while the host has <c>navigating</c>.
    /// Built in code so the join card and the Account page share it. Pointer and touch only for now.
    /// </summary>
    public sealed class ConfirmPanel
    {
        private readonly VisualElement _overlay;
        private readonly VisualElement _column;
        private readonly VisualElement _host;
        private Action _cancel;
        private readonly System.Collections.Generic.List<(Button Button, Action Chosen)> _choices = new System.Collections.Generic.List<(
            Button,
            Action
        )>();
        private int _selected;

        /// <summary>Adds the (hidden) panel to <paramref name="host"/>, which gets <c>confirming</c> while it's up.</summary>
        public ConfirmPanel(VisualElement host)
        {
            _host = host;
            _overlay = new VisualElement();
            _overlay.AddToClassList("confirm-overlay");
            _overlay.AddToClassList("hidden");
            _column = new VisualElement();
            _column.AddToClassList("menu-confirm");
            _overlay.Add(_column);
            host.Add(_overlay);
        }

        /// <summary>True while the panel is up.</summary>
        public bool Open => !_overlay.ClassListContains("hidden");

        /// <summary>
        /// Asks <paramref name="title"/>. <paramref name="cancel"/> runs on Esc (<see cref="Cancel"/>); each choice runs
        /// its action after the panel closes.
        /// </summary>
        public void Show(string title, string note, Action cancel, params (string Text, bool Risky, Action Chosen)[] choices)
        {
            _cancel = cancel;
            _column.Clear();
            _choices.Clear();
            _column.Add(Label(title, "menu-confirm-title"));
            _column.Add(Label(note, "menu-confirm-note"));
            var first = true;
            var gap = false;
            foreach (var (text, risky, chosen) in choices)
            {
                if (risky && !gap && !first)
                {
                    var spacer = new VisualElement();
                    spacer.AddToClassList("menu-gap");
                    _column.Add(spacer);
                    gap = true;
                }

                var button = new Button(() =>
                {
                    Close();
                    chosen?.Invoke();
                })
                {
                    text = text,
                    focusable = false,
                };
                button.AddToClassList("menu-entry");
                button.EnableInClassList("leave", risky);
                _choices.Add((button, chosen));
                first = false;
                _column.Add(button);
            }

            _selected = 0;
            Mark();
            _overlay.RemoveFromClassList("hidden");
            _host.AddToClassList("confirming");
        }

        /// <summary>Moves the selection up or down the choices.</summary>
        public void Move(int by)
        {
            _selected = Math.Clamp(_selected + by, 0, _choices.Count - 1);
            Mark();
        }

        /// <summary>Chooses the selected choice.</summary>
        public void Choose()
        {
            if (_choices.Count > 0)
            {
                var chosen = _choices[_selected].Chosen;
                Close();
                chosen?.Invoke();
            }
        }

        private void Mark()
        {
            for (var i = 0; i < _choices.Count; i++)
            {
                _choices[i].Button.EnableInClassList("selected", i == _selected);
            }
        }

        /// <summary>Closes the panel as if the player chose to stay.</summary>
        public void Cancel()
        {
            var cancel = _cancel;
            Close();
            cancel?.Invoke();
        }

        public void Close()
        {
            _cancel = null;
            _overlay.AddToClassList("hidden");
            _host.RemoveFromClassList("confirming");
        }

        private static Label Label(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }
    }
}

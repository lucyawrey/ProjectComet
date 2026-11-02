using System;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// A question over the dimmed screen, in the Esc menu's Leave game style: a title, a note and a column of buttons,
    /// the first filled blue as the one to pick, risky ones set apart below in red. Choosing one closes it. Built in
    /// code so the join card and the Account page share it. Pointer and touch only for now.
    /// </summary>
    public sealed class ConfirmPanel
    {
        private readonly VisualElement _overlay;
        private readonly VisualElement _column;
        private readonly VisualElement _host;
        private Action _cancel;

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
            _column.Add(Label(title, "menu-confirm-title"));
            _column.Add(Label(note, "menu-confirm-note"));
            var first = true;
            var gap = false;
            foreach (var (text, risky, chosen) in choices)
            {
                if (risky && !gap)
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
                }) { text = text, focusable = false };
                button.AddToClassList("menu-entry");
                button.EnableInClassList("leave", risky);
                button.EnableInClassList("selected", first);
                first = false;
                _column.Add(button);
            }

            _overlay.RemoveFromClassList("hidden");
            _host.AddToClassList("confirming");
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

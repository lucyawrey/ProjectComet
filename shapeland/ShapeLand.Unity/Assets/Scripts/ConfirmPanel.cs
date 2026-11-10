using System;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
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

        public bool Open => !_overlay.ClassListContains("hidden");

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

        public void Move(int by)
        {
            _selected = Math.Clamp(_selected + by, 0, _choices.Count - 1);
            Mark();
        }

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

using System.Collections.Generic;
using Comet.Content;
using UnityEngine.UIElements;

namespace Comet.Unity
{
    /// <summary>
    /// Fills a UI Toolkit interface's text from the string table: UXML text written as <c>text="#join.title"</c>
    /// is set once the content has loaded, and text with a shared placeholder is refilled when its value changes.
    /// </summary>
    public static class UiText
    {
        public static void Fill(VisualElement root, StringTable strings)
        {
            var withPlaceholders = new List<(TextElement Element, string Key)>();
            root.Query<TextElement>()
                .ForEach(element =>
                {
                    if (element.text.StartsWith("#"))
                    {
                        var key = element.text.Substring(1);
                        element.text = strings.Get(key);
                        if (strings.Raw(key).Contains('{'))
                        {
                            withPlaceholders.Add((element, key));
                        }
                    }
                });

            if (withPlaceholders.Count > 0)
            {
                strings.SharedChanged += () =>
                {
                    foreach (var (element, key) in withPlaceholders)
                    {
                        element.text = strings.Get(key);
                    }
                };
            }
        }
    }
}

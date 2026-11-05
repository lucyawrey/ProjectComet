using System.Collections.Generic;
using Comet.Content;
using UnityEngine.UIElements;

namespace Comet.Unity
{
    /// <summary>
    /// Fills a UI Toolkit interface's text from the game's string table: UXML labels and buttons write a key as
    /// <c>text="#join.title"</c>, and get the text for it here once the content has loaded. Text with a shared
    /// placeholder (<see cref="StringTable.SetShared"/>, like <c>{username}</c>) is filled in again when its value changes.
    /// </summary>
    public static class UiText
    {
        /// <summary>Replaces each <c>#key</c> text under <paramref name="root"/> with its text from <paramref name="strings"/>.</summary>
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

using Comet.Content;
using UnityEngine.UIElements;

namespace Comet.Unity
{
    /// <summary>
    /// Fills a UI Toolkit interface's text from the game's string table: UXML labels and buttons write a key as
    /// <c>text="#join.title"</c>, and get the text for it here once the content has loaded.
    /// </summary>
    public static class UiText
    {
        /// <summary>Replaces each <c>#key</c> text under <paramref name="root"/> with its text from <paramref name="strings"/>.</summary>
        public static void Fill(VisualElement root, StringTable strings)
        {
            root.Query<TextElement>().ForEach(element =>
            {
                if (element.text.StartsWith("#"))
                {
                    element.text = strings.Get(element.text.Substring(1));
                }
            });
        }
    }
}

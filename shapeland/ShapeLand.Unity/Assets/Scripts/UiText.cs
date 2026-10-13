using Comet.Content;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// Fills a UI's text from the content's string table (<c>content/strings/en.toml</c>): UXML labels and buttons
    /// write a key as <c>text="#join.title"</c>, and get the text for it here once the content has loaded.
    /// </summary>
    public static class UiText
    {
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

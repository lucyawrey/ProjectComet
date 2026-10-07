namespace Comet.ContentBuild.Tests;

// A throwaway content folder with widgets in widgets/.
internal sealed class ContentFolder : IDisposable
{
    public ContentFolder() => Directory.CreateDirectory(Path.Combine(Root, "widgets"));

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "comet-content-" + Guid.NewGuid().ToString("N"));

    public string Registry => Path.Combine(Root, "ids.toml");

    public void Write(string relativePath, string text)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public ContentBuildResult Build(Action<Widget, ContentValidation>? validate = null) =>
        new ContentBuilder(Root).Add("widget", "widgets", TestToml.Default.Widget, validate).Build();

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

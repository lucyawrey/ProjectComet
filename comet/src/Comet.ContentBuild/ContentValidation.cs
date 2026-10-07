using Tomlyn.Syntax;

namespace Comet.ContentBuild;

/// <summary>Collects a content file's validation errors, placed at the field they're about.</summary>
public sealed class ContentValidation
{
    private readonly string _file;
    private readonly DocumentSyntax _document;
    private readonly List<ContentError> _errors = new();

    internal ContentValidation(string file, DocumentSyntax document)
    {
        _file = file;
        _document = document;
    }

    internal IReadOnlyList<ContentError> Errors => _errors;

    /// <summary>Records an error about a top-level field, given by its C# property name (<c>nameof(Shape.MaxSpeed)</c>).</summary>
    public void Error(string propertyName, string message)
    {
        var key = ContentJson.KeyFor(propertyName);
        var keyValue = _document.KeyValues.FirstOrDefault(kv => ContentBuilder.KeyText(kv.Key!) == key);
        _errors.Add(keyValue is null
            ? new ContentError(_file, 1, 1, $"{key}: {message}")
            : ContentBuilder.ErrorAt(_file, keyValue.Value!.Span, $"{key}: {message}"));
    }
}

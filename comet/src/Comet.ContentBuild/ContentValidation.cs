using Tomlyn.Syntax;

namespace Comet.ContentBuild;

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

    public void Error(string propertyName, string message)
    {
        var key = ContentJson.KeyFor(propertyName);
        var keyValue = _document.KeyValues.FirstOrDefault(kv => ContentBuilder.KeyText(kv.Key!) == key);
        _errors.Add(
            keyValue is null
                ? new ContentError(_file, 1, 1, $"{key}: {message}")
                : ContentBuilder.ErrorAt(_file, keyValue.Value!.Span, $"{key}: {message}")
        );
    }

    public void Error(string tablePropertyName, string propertyName, string message)
    {
        var table = ContentJson.KeyFor(tablePropertyName);
        var key = ContentJson.KeyFor(propertyName);
        var label = $"{table}.{key}";
        var inTable = _document
            .Tables.Where(t => t is not TableArraySyntax && ContentBuilder.KeyText(t.Name!) == table)
            .SelectMany(t => t.Items.OfType<KeyValueSyntax>())
            .FirstOrDefault(kv => ContentBuilder.KeyText(kv.Key!) == key);
        var dotted = _document.KeyValues.FirstOrDefault(kv => ContentBuilder.KeyText(kv.Key!) == label);
        var inline = _document.KeyValues.FirstOrDefault(kv => ContentBuilder.KeyText(kv.Key!) == table);
        var inInline = (inline?.Value as InlineTableSyntax)
            ?.Items.Select(item => item.KeyValue)
            .FirstOrDefault(kv => kv is not null && ContentBuilder.KeyText(kv.Key!) == key);

        var at =
            (inTable ?? dotted ?? inInline)?.Value
            ?? (SyntaxNode?)_document.Tables.FirstOrDefault(t => ContentBuilder.KeyText(t.Name!) == table)?.Name
            ?? inline?.Key;
        _errors.Add(
            at is null ? new ContentError(_file, 1, 1, $"{label}: {message}") : ContentBuilder.ErrorAt(_file, at.Span, $"{label}: {message}")
        );
    }
}

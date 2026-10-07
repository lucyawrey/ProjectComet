using Comet.Content;

namespace Comet.ContentBuild;

/// <summary>The outcome of <see cref="ContentBuilder.Build"/>: every error found, or the checked entries with their numbers.</summary>
public sealed class ContentBuildResult
{
    private readonly IReadOnlyList<ContentBuilder.IReadContent> _types;

    internal ContentBuildResult(IReadOnlyList<ContentError> errors, IReadOnlyList<ContentBuilder.IReadContent> types)
    {
        Errors = errors;
        _types = types;
    }

    public IReadOnlyList<ContentError> Errors { get; }

    public bool Succeeded => Errors.Count == 0;

    /// <summary>The entries of one registered type, in number order.</summary>
    public IReadOnlyList<T> Entries<T>()
        where T : class, IContentEntry
    {
        if (!Succeeded)
        {
            throw new InvalidOperationException("The content build failed; see Errors.");
        }

        return _types.OfType<ContentBuilder.ReadContent<T>>().Single().Entries;
    }
}

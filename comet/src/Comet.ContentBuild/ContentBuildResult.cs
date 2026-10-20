using Comet.Content;

namespace Comet.ContentBuild;

/// <summary>The outcome of <see cref="ContentBuilder.Build"/>: every error found, or the checked entries with their numbers.</summary>
public sealed class ContentBuildResult
{
    private readonly IReadOnlyList<ContentBuilder.IReadContent> _types;

    internal ContentBuildResult(IReadOnlyList<ContentError> errors, IReadOnlyList<ContentError> warnings, IReadOnlyList<ContentBuilder.IReadContent> types, StringTable? strings, IReadOnlyList<Zone> zones, IReadOnlyList<Group> groups)
    {
        Zones = zones;
        Groups = groups;
        Errors = errors;
        Warnings = warnings;
        _types = types;
        Strings = strings;
    }

    /// <summary>Every problem found, with its file, line and column; empty if the build succeeded.</summary>
    public IReadOnlyList<ContentError> Errors { get; }

    /// <summary>What doesn't stop the build but should be dealt with before a release (blockout placements); errors in a release build.</summary>
    public IReadOnlyList<ContentError> Warnings { get; }

    public bool Succeeded => Errors.Count == 0;

    /// <summary>The interface text, if the game registered a string file (<see cref="ContentBuilder.AddStrings"/>).</summary>
    public StringTable? Strings { get; }

    /// <summary>The zones, in number order, if the game registered a zones folder (<see cref="ContentBuilder.AddZones"/>).</summary>
    public IReadOnlyList<Zone> Zones { get; }

    /// <summary>The groups, in number order, if the game registered a groups folder (<see cref="ContentBuilder.AddGroups"/>); zones hold their copies already expanded.</summary>
    public IReadOnlyList<Group> Groups { get; }

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

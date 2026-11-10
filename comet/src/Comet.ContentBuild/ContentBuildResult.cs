using Comet.Content;

namespace Comet.ContentBuild;

public sealed class ContentBuildResult
{
    private readonly IReadOnlyList<ContentBuilder.IReadContent> _types;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> _keyNumbers;

    internal ContentBuildResult(
        IReadOnlyList<ContentError> errors,
        IReadOnlyList<ContentError> warnings,
        IReadOnlyList<ContentBuilder.IReadContent> types,
        StringTable? strings,
        IReadOnlyList<Zone> zones,
        IReadOnlyList<Group> groups,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> keyNumbers
    )
    {
        _keyNumbers = keyNumbers;
        Zones = zones;
        Groups = groups;
        Errors = errors;
        Warnings = warnings;
        _types = types;
        Strings = strings;
    }

    public IReadOnlyList<ContentError> Errors { get; }

    /// <summary>Doesn't stop the build (blockout placements); errors in a release build.</summary>
    public IReadOnlyList<ContentError> Warnings { get; }

    public bool Succeeded => Errors.Count == 0;

    /// <summary>Null unless <see cref="ContentBuilder.AddStrings"/> was called.</summary>
    public StringTable? Strings { get; }

    /// <summary>In number order; empty unless <see cref="ContentBuilder.AddZones"/> was called.</summary>
    public IReadOnlyList<Zone> Zones { get; }

    public IReadOnlyList<Group> Groups { get; }

    public IReadOnlyDictionary<string, int> Numbers(string kind)
    {
        if (!Succeeded)
        {
            throw new InvalidOperationException("The content build failed; see Errors.");
        }

        return _keyNumbers[kind];
    }

    /// <summary>In number order.</summary>
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

namespace Comet.Content
{
    /// <summary>One content definition, authored as one TOML file and compiled into the content file.</summary>
    public interface IContentEntry
    {
        /// <summary>The namespaced string key (<c>shape.cube</c>). Code looks content up by key.</summary>
        string Id { get; }

        /// <summary>The permanent number from the ID registry, used by the database, network and compiled content.</summary>
        int Number { get; set; }
    }
}

namespace Comet.Content
{
    /// <summary>One content definition, authored as a TOML file.</summary>
    public interface IContentEntry
    {
        /// <summary>The namespaced string key (<c>shape.cube</c>).</summary>
        string Id { get; }

        /// <summary>The permanent number from the ID registry.</summary>
        int Number { get; set; }
    }
}

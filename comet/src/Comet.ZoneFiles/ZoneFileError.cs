namespace Comet.ZoneFiles
{
    /// <summary>A problem in a zone's files: the file it's in and what's wrong, naming the id where there is one.</summary>
    public sealed class ZoneFileError
    {
        public ZoneFileError(string file, string message)
        {
            File = file;
            Message = message;
        }

        /// <summary>The file's path, relative to the zone's folder (<c>chunks/0_-1.toml</c>).</summary>
        public string File { get; }

        public string Message { get; }

        public override string ToString() => $"{File}: {Message}";
    }
}

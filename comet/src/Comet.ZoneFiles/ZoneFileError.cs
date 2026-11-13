namespace Comet.ZoneFiles
{
    public sealed class ZoneFileError
    {
        public ZoneFileError(string file, string message)
        {
            File = file;
            Message = message;
        }

        /// <summary>Relative to the zone's folder (<c>chunks/0_-1/chunk.toml</c>).</summary>
        public string File { get; }

        public string Message { get; }

        public override string ToString() => $"{File}: {Message}";
    }
}

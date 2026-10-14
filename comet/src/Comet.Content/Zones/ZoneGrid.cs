namespace Comet.Content
{
    /// <summary>
    /// The fixed world grid every zone uses: square chunks with terrain samples every meter. Chunk (x, z) covers
    /// world X from <c>x * ChunkSize</c> to <c>(x + 1) * ChunkSize</c>, and likewise Z.
    /// </summary>
    public static class ZoneGrid
    {
        /// <summary>A chunk's side, in meters.</summary>
        public const int ChunkSize = 64;

        /// <summary>Meters between terrain samples.</summary>
        public const float SampleSpacing = 1f;

        /// <summary>
        /// Samples along a chunk's side, edges included: a chunk's last row and column repeat its neighbors' first,
        /// so the terrain meets without seams.
        /// </summary>
        public const int ChunkSamples = ChunkSize + 1;
    }
}

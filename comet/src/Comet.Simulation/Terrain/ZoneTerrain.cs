using System.Collections.Generic;
using Comet.Content;

namespace Comet.Simulation
{
    /// <summary>
    /// A zone's terrain pieces, each seen whole across its chunk tiles: heights by world sample, where sample
    /// (x, z) sits at world (x, z) × <see cref="ZoneGrid.SampleSpacing"/>. Neighboring tiles repeat their shared
    /// edge samples (the zone files check they match), so either tile can answer for them.
    /// </summary>
    public sealed class ZoneTerrain
    {
        private readonly Dictionary<string, Piece> _pieces = new Dictionary<string, Piece>();
        private readonly List<Piece> _order = new List<Piece>();

        /// <summary>The terrain of <paramref name="zone"/>.</summary>
        public ZoneTerrain(Zone zone)
        {
            foreach (var settings in zone.Pieces)
            {
                var piece = new Piece(settings);
                _pieces[settings.Name] = piece;
                _order.Add(piece);
            }

            foreach (var chunk in zone.Chunks)
            {
                foreach (var tile in chunk.Tiles)
                {
                    if (_pieces.TryGetValue(tile.Piece, out var piece))
                    {
                        piece.Add(chunk.Coord, tile);
                    }
                }
            }
        }

        /// <summary>The pieces, in the zone's order.</summary>
        public IReadOnlyList<Piece> Pieces => _order;

        /// <summary>The piece named <paramref name="name"/>.</summary>
        public Piece this[string name] => _pieces[name];

        /// <summary>One terrain piece, its tiles joined into one grid of world samples.</summary>
        public sealed class Piece
        {
            private readonly Dictionary<ChunkCoord, TerrainTile> _tiles = new Dictionary<ChunkCoord, TerrainTile>();

            internal Piece(TerrainPiece settings) => Settings = settings;

            public TerrainPiece Settings { get; }

            /// <summary>Each chunk's tile of this piece.</summary>
            public IReadOnlyDictionary<ChunkCoord, TerrainTile> Tiles => _tiles;

            /// <summary>The lowest and highest world samples any tile covers, on each axis.</summary>
            public (int MinX, int MinZ, int MaxX, int MaxZ) Extent { get; private set; } = (int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);

            /// <summary>The ground height at world sample (<paramref name="x"/>, <paramref name="z"/>), or false over a hole or past the piece.</summary>
            public bool TryGetHeight(int x, int z, out float height) => TryGet(x, z, underside: false, out height);

            /// <summary>The underside height at a world sample, or false where the piece has no underside map there.</summary>
            public bool TryGetUnderside(int x, int z, out float height) => TryGet(x, z, underside: true, out height);

            /// <summary>The painted color at a world sample as 0xRRGGBB, or <paramref name="fallback"/> where nothing is painted.</summary>
            public uint ColorAt(int x, int z, uint fallback)
            {
                for (var i = 0; i < 4; i++)
                {
                    if (TileAt(x, z, i, out var tile, out var tx, out var tz))
                    {
                        return tile.Colors is { } colors ? colors[tx, tz] : fallback;
                    }
                }

                return fallback;
            }

            internal void Add(ChunkCoord chunk, TerrainTile tile)
            {
                _tiles[chunk] = tile;
                var x = chunk.X * ZoneGrid.ChunkSize + tile.OriginX;
                var z = chunk.Z * ZoneGrid.ChunkSize + tile.OriginZ;
                var (minX, minZ, maxX, maxZ) = Extent;
                Extent = (System.Math.Min(minX, x), System.Math.Min(minZ, z), System.Math.Max(maxX, x + tile.Heights.SizeX - 1), System.Math.Max(maxZ, z + tile.Heights.SizeZ - 1));
            }

            private bool TryGet(int x, int z, bool underside, out float height)
            {
                for (var i = 0; i < 4; i++)
                {
                    if (TileAt(x, z, i, out var tile, out var tx, out var tz))
                    {
                        var map = underside ? tile.Underside : tile.Heights;
                        if (map != null && map.TryGetHeight(tx, tz, out height))
                        {
                            return true;
                        }
                    }
                }

                height = 0;
                return false;
            }

            // The world sample's place in the tile of candidate chunk `which` (0 to 3): its own chunk, then the chunks
            // to the west, south and southwest, whose tiles repeat it when it's on their shared edge or corner.
            private bool TileAt(int x, int z, int which, out TerrainTile tile, out int tx, out int tz)
            {
                var chunkX = FloorDiv(x, ZoneGrid.ChunkSize) - (which & 1);
                var chunkZ = FloorDiv(z, ZoneGrid.ChunkSize) - (which >> 1);
                tx = tz = 0;
                if (!_tiles.TryGetValue(new ChunkCoord(chunkX, chunkZ), out tile!))
                {
                    return false;
                }

                tx = x - chunkX * ZoneGrid.ChunkSize - tile.OriginX;
                tz = z - chunkZ * ZoneGrid.ChunkSize - tile.OriginZ;
                return tx >= 0 && tz >= 0 && tx < tile.Heights.SizeX && tz < tile.Heights.SizeZ;
            }

            private static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;
        }
    }
}

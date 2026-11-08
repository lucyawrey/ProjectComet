using System;
using System.Collections.Generic;
using System.Linq;
using Comet.Content;

namespace Comet.ZoneFiles
{
    public static partial class ZoneValidation
    {
        private static void CheckTiles(ZoneChunk chunk, float heightBase, HashSet<string> pieces, string file, Action<string, string> error)
        {
            var seen = new HashSet<string>();
            foreach (var tile in chunk.Tiles)
            {
                if (!pieces.Contains(tile.Piece))
                {
                    error(file, $"tile: there's no piece '{tile.Piece}' in {ZoneFolder.ZoneFile}.");
                    continue;
                }

                if (!seen.Add(tile.Piece))
                {
                    error(file, $"tile: piece '{tile.Piece}' has two tiles in this chunk.");
                }

                var heights = tile.Heights;
                if (
                    tile.OriginX < 0
                    || tile.OriginZ < 0
                    || tile.OriginX + heights.SizeX > ZoneGrid.ChunkSamples
                    || tile.OriginZ + heights.SizeZ > ZoneGrid.ChunkSamples
                )
                {
                    error(
                        file,
                        $"tile {tile.Piece}: {heights.SizeX}×{heights.SizeZ} samples from [{tile.OriginX}, {tile.OriginZ}] reach past the chunk's {ZoneGrid.ChunkSamples}×{ZoneGrid.ChunkSamples}."
                    );
                }

                if (heights.Offset != heightBase || (tile.Underside?.Offset is { } bottom && bottom != heightBase))
                {
                    error(file, $"tile {tile.Piece}: its height offset isn't the zone's height_base ({heightBase}).");
                }

                if (tile.Underside is { } under && (under.SizeX != heights.SizeX || under.SizeZ != heights.SizeZ))
                {
                    error(
                        file,
                        $"tile {tile.Piece}: the underside is {under.SizeX}×{under.SizeZ} samples; the heights are {heights.SizeX}×{heights.SizeZ}."
                    );
                }

                if (tile.Colors is { } colors && (colors.SizeX != heights.SizeX || colors.SizeZ != heights.SizeZ))
                {
                    error(
                        file,
                        $"tile {tile.Piece}: the colors are {colors.SizeX}×{colors.SizeZ} samples; the heights are {heights.SizeX}×{heights.SizeZ}."
                    );
                }

                if (tile.UndersideColors is { } underColors)
                {
                    if (tile.Underside is null)
                    {
                        error(file, $"tile {tile.Piece}: has underside colors but no underside (delete its .under.color file).");
                    }
                    else if (underColors.SizeX != heights.SizeX || underColors.SizeZ != heights.SizeZ)
                    {
                        error(
                            file,
                            $"tile {tile.Piece}: the underside colors are {underColors.SizeX}×{underColors.SizeZ} samples; the heights are {heights.SizeX}×{heights.SizeZ}."
                        );
                    }
                }
            }
        }

        // Neighboring chunks repeat the samples on their shared edges (and corners), so where two tiles of one piece
        // both cover a sample, they must agree on whether it's ground and, within half a height step, how high.
        private static void CheckSharedEdges(Zone zone, Action<string, string> error)
        {
            var tiles = zone.Chunks.SelectMany(c => c.Tiles.Select(t => (Chunk: c.Coord, Tile: t))).GroupBy(t => t.Tile.Piece);
            foreach (var piece in tiles)
            {
                var list = piece.ToList();
                for (var i = 0; i < list.Count; i++)
                {
                    for (var j = i + 1; j < list.Count; j++)
                    {
                        var (a, b) = (list[i], list[j]);
                        if (a.Chunk.Equals(b.Chunk) || Math.Abs(a.Chunk.X - b.Chunk.X) > 1 || Math.Abs(a.Chunk.Z - b.Chunk.Z) > 1)
                        {
                            continue;
                        }

                        var mismatch = FirstMismatch(a.Chunk, a.Tile, b.Chunk, b.Tile);
                        if (mismatch is { } at)
                        {
                            var (first, second) =
                                string.CompareOrdinal(a.Chunk.ToString(), b.Chunk.ToString()) < 0 ? (a.Chunk, b.Chunk) : (b.Chunk, a.Chunk);
                            error(
                                ZoneFolder.ChunkFile(first),
                                $"tile {piece.Key}: the terrain doesn't match chunk {second}'s where they meet (world sample [{at.X}, {at.Z}])."
                            );
                        }
                    }
                }
            }
        }

        private static (int X, int Z)? FirstMismatch(ChunkCoord chunkA, TerrainTile a, ChunkCoord chunkB, TerrainTile b)
        {
            // Both tiles in world sample coordinates (a chunk's first sample is at chunk × ChunkSize).
            int ax = chunkA.X * ZoneGrid.ChunkSize + a.OriginX,
                az = chunkA.Z * ZoneGrid.ChunkSize + a.OriginZ;
            int bx = chunkB.X * ZoneGrid.ChunkSize + b.OriginX,
                bz = chunkB.Z * ZoneGrid.ChunkSize + b.OriginZ;
            int minX = Math.Max(ax, bx),
                maxX = Math.Min(ax + a.Heights.SizeX, bx + b.Heights.SizeX) - 1;
            int minZ = Math.Max(az, bz),
                maxZ = Math.Min(az + a.Heights.SizeZ, bz + b.Heights.SizeZ) - 1;
            var tolerance = Heightmap.Scale / 2;
            for (var z = minZ; z <= maxZ; z++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var groundA = a.Heights.TryGetHeight(x - ax, z - az, out var heightA);
                    var groundB = b.Heights.TryGetHeight(x - bx, z - bz, out var heightB);
                    if (groundA != groundB || (groundA && Math.Abs(heightA - heightB) > tolerance))
                    {
                        return (x, z);
                    }
                }
            }

            return null;
        }
    }
}

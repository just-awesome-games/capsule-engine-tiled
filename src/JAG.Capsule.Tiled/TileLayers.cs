using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using Capsule.Tiles;

namespace JAG.Capsule.Tiled;

internal static class TileLayers
{
    // Tiled packs flip and rotation into the top nibble of a gid. It applies the anti-diagonal flip
    // first, then the horizontal, then the vertical, the order TileTransform applies Transpose, FlipX
    // and FlipY.
    internal const uint OrientationBits = 0xF000_0000u;
    private const uint FlippedHorizontally = 0x8000_0000u;
    private const uint FlippedVertically = 0x4000_0000u;
    private const uint FlippedDiagonally = 0x2000_0000u;
    private const uint RotatedHexagonal120 = 0x1000_0000u;

    internal static SceneDocumentEntry ToEntry(TiledLayer layer, TiledMap map, Tileset[] tilesets) =>
        TiledImporter.Within($"tile layer '{layer.Name}'", () =>
        {
            TiledProperties properties = new(layer.Properties);
            EntitySpawn spawn = new(Vector2.Zero) { ZIndex = properties.TakeInt("zIndex"), ScrollFactor = layer.ScrollFactor };
            bool collider = properties.TakeBool("collider");
            if (layer.Width != map.Width || layer.Height != map.Height)
            {
                throw new FormatException(
                    $"the layer is {layer.Width}x{layer.Height} but the map is {map.Width}x{map.Height}; resize the map in Map > Resize Map with every layer.");
            }

            (Tileset? painted, int[] tiles, int[]? transforms) = Convert(Decode(layer), tilesets);

            JsonObject members = new() { ["tileSize"] = map.TileWidth, ["width"] = map.Width, ["height"] = map.Height };
            if (painted is not null)
            {
                members["texture"] = painted.Texture;
                members["columns"] = painted.Columns;
            }

            members["tileTypes"] = painted?.Palette.DeepClone() ?? new JsonArray(Tilesets.EmptyTileType());
            members["tiles"] = Ints(tiles);
            if (transforms is not null)
            {
                members["transforms"] = Ints(transforms);
            }

            if (collider)
            {
                members["collider"] = true;
            }

            return new SceneDocumentEntry("tile-map", spawn, TiledJson.ToElement(members));
        });

    private static uint[] Decode(TiledLayer layer)
    {
        if (layer.Encoding is { } encoding && !encoding.Equals("csv", StringComparison.OrdinalIgnoreCase)
            || layer.Compression is { Length: > 0 }
            || layer.Data.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("the layer's tile data is not CSV; set Map > Map Properties > Tile Layer Format to CSV.");
        }

        int count = layer.Data.GetArrayLength();
        if (count != (long)layer.Width * layer.Height)
        {
            throw new FormatException(
                $"the layer holds {count} tiles but {layer.Width}x{layer.Height} needs {(long)layer.Width * layer.Height}; re-save the map in Tiled.");
        }

        uint[] gids = new uint[count];
        int index = 0;
        foreach (JsonElement element in layer.Data.EnumerateArray())
        {
            gids[index] = element.ValueKind == JsonValueKind.Number && element.TryGetUInt32(out uint gid)
                ? gid
                : throw new FormatException($"the tile at index {index} is '{element}', which is not a gid; re-save the map in Tiled.");
            index++;
        }

        return gids;
    }

    private static (Tileset? Painted, int[] Tiles, int[]? Transforms) Convert(uint[] gids, Tileset[] tilesets)
    {
        int[] tiles = new int[gids.Length];
        int[]? transforms = null;
        Tileset? painted = null;
        for (int index = 0; index < gids.Length; index++)
        {
            uint gid = gids[index];
            if ((gid & RotatedHexagonal120) != 0)
            {
                throw new FormatException(
                    $"the tile at index {index} is turned 120 degrees, a turn only hexagonal maps have; clear the tile's rotation in Tiled.");
            }

            if ((gid & OrientationBits) != 0)
            {
                transforms ??= new int[gids.Length];
                transforms[index] = (int)TransformOf(gid);
                gid &= ~OrientationBits;
            }

            if (gid == 0)
            {
                continue;
            }

            Tileset owner = Tilesets.OwnerOf(gid, tilesets)
                ?? throw new FormatException($"the tile at index {index} has gid {gid}, which belongs to no tileset in the map; re-save the map in Tiled.");
            painted ??= owner;
            if (painted != owner)
            {
                throw new FormatException(
                    $"the layer paints from tilesets '{painted.Name}' and '{owner.Name}', and a tile map draws from one texture; split it into one layer per tileset.");
            }

            int tileId = (int)gid - owner.FirstGid;
            tiles[index] = owner.IndexByTileId.TryGetValue(tileId, out int paletteIndex)
                ? paletteIndex
                : throw new FormatException(
                    $"tile {tileId} of tileset '{owner.Name}' is painted at index {index} but has no Class; give every painted tile a Class in Tiled.");
        }

        return (painted, tiles, transforms);
    }

    private static TileTransform TransformOf(uint gid)
    {
        TileTransform transform = TileTransform.None;
        if ((gid & FlippedDiagonally) != 0)
        {
            transform |= TileTransform.Transpose;
        }

        if ((gid & FlippedHorizontally) != 0)
        {
            transform |= TileTransform.FlipX;
        }

        if ((gid & FlippedVertically) != 0)
        {
            transform |= TileTransform.FlipY;
        }

        return transform;
    }

    private static JsonArray Ints(int[] values) => [.. values.Select(static value => JsonValue.Create(value))];
}

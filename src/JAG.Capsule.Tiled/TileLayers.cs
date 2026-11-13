using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Capsule.Scenes;
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

    internal static SceneDocumentEntry ToEntry(TiledLayer layer, TiledMap map, Tileset[] tilesets, PropertyScope scope) =>
        TiledImporter.Within($"tile layer '{layer.Name}'", () =>
        {
            TiledImporter.RequireUnblended(layer);
            TiledProperties properties = new(layer.Properties);
            EntitySpawn spawn = new(Vector2.Zero)
            {
                Name = TiledImporter.NameOf(layer.Name),
                ZIndex = properties.TakeInt("zIndex"),
                ScrollFactor = layer.ScrollFactor,
            };
            bool collider = properties.TakeBool("collider");
            if (layer.Width != map.Width || layer.Height != map.Height)
            {
                throw new FormatException(
                    $"the layer is {layer.Width}x{layer.Height} but the map is {map.Width}x{map.Height}; resize the map in Map > Resize Map with every layer.");
            }

            (Tileset? painted, int[] tiles, int[]? transforms, JsonArray? palette) = Convert(Decode(layer), tilesets);
            if (layer.Offset != Vector2.Zero)
            {
                (tiles, transforms) = Shifted(tiles, transforms, layer, map.TileWidth);
            }

            JsonObject members = new() { ["tileSize"] = map.TileWidth, ["width"] = map.Width, ["height"] = map.Height };
            if (!layer.Visible)
            {
                // A hidden layer draws nothing in the game either, and still collides, as a hidden Godot node does.
                foreach (JsonObject tileType in palette?.Cast<JsonObject>() ?? [])
                {
                    tileType.Remove("cell");
                    tileType.Remove("frames");
                }
            }
            else if (painted is not null)
            {
                members["texture"] = painted.Texture;
                members["columns"] = painted.Columns;
            }

            members["tileTypes"] = palette ?? new JsonArray(Tilesets.EmptyTileType());
            members["tiles"] = Ints(tiles);
            if (transforms is not null)
            {
                members["transforms"] = Ints(transforms);
            }

            if (collider)
            {
                members["collider"] = true;
            }

            // Capsule writes these only when the layer paints or turns a tile, and a property never stands in for them.
            properties.AddToEntry(members, scope, "texture", "columns", "transforms");

            return TiledImporter.Checked(new SceneDocumentEntry(string.IsNullOrWhiteSpace(layer.Class) ? "tile-map" : layer.Class, spawn, TiledJson.ToElement(members)));
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

    private static (Tileset? Painted, int[] Tiles, int[]? Transforms, JsonArray? Palette) Convert(uint[] gids, Tileset[] tilesets)
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
                    $"the tile at index {index} is tile {tileId} of tileset '{owner.Name}', which has {owner.IndexByTileId.Count} tiles; re-save the map in Tiled.");
        }

        return painted is null ? (null, tiles, transforms, null) : (painted, tiles, transforms, (JsonArray)painted.Palette.DeepClone());
    }

    // A tile map sits at the world origin, so a layer's Offset moves its tiles instead, which is what Tiled previews.
    private static (int[] Tiles, int[]? Transforms) Shifted(int[] tiles, int[]? transforms, TiledLayer layer, int tileSize)
    {
        if (layer.OffsetX % tileSize != 0 || layer.OffsetY % tileSize != 0)
        {
            throw new FormatException(string.Create(
                CultureInfo.InvariantCulture,
                $"the layer's Offset is ({layer.OffsetX}, {layer.OffsetY}), which is not a whole number of {tileSize}-pixel tiles, and a tile map's tiles sit on the map's grid; snap the offset to whole tiles."));
        }

        int dx = (int)(layer.OffsetX / tileSize);
        int dy = (int)(layer.OffsetY / tileSize);
        int[] movedTiles = new int[tiles.Length];
        int[]? movedTransforms = transforms is null ? null : new int[tiles.Length];
        for (int index = 0; index < tiles.Length; index++)
        {
            if (tiles[index] == 0)
            {
                continue;
            }

            (int y, int x) = Math.DivRem(index, layer.Width);
            int toX = x + dx;
            int toY = y + dy;
            if (toX < 0 || toX >= layer.Width || toY < 0 || toY >= layer.Height)
            {
                throw new FormatException(
                    $"the layer's Offset moves the tile at ({x}, {y}) to ({toX}, {toY}), off the {layer.Width}x{layer.Height} map; move the tile, or the offset, so every tile stays on the map.");
            }

            int to = toY * layer.Width + toX;
            movedTiles[to] = tiles[index];
            if (movedTransforms is not null)
            {
                movedTransforms[to] = transforms![index];
            }
        }

        return (movedTiles, movedTransforms);
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

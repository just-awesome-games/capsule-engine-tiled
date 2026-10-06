using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Capsule.Build;
using Capsule.Tiles;

namespace JAG.Capsule.Tiled;

internal sealed record Tileset(string Name, int FirstGid, string Texture, int Columns, int TileSize, JsonArray Palette, Dictionary<int, int> IndexByTileId, Func<int, JsonObject> UnclassedTileType);

internal static class Tilesets
{
    internal static Tileset[] Load(TiledMap map, AssetImportContext context)
    {
        string mapDirectory = Path.GetDirectoryName(Path.GetFullPath(context.SourcePath))!;
        List<Tileset> tilesets = [];
        foreach (TiledTileset entry in map.Tilesets)
        {
            (TiledTileset tileset, string directory) = entry.Source is { Length: > 0 } source
                ? TiledImporter.Within($"tileset '{source}'", () => ReadExternal(source, mapDirectory, context))
                : (entry, mapDirectory);
            tileset.FirstGid = entry.FirstGid;
            string name = tileset.Name ?? Path.GetFileNameWithoutExtension(entry.Source) ?? "?";
            tilesets.Add(TiledImporter.Within($"tileset '{name}'", () => Resolve(tileset, name, map.TileWidth, directory, context.AssetRoot)));
        }

        return [.. tilesets.OrderBy(static tileset => tileset.FirstGid)];
    }

    internal static Tileset? OwnerOf(uint gid, Tileset[] tilesets) => tilesets.LastOrDefault(tileset => tileset.FirstGid <= gid);

    internal static JsonObject EmptyTileType() => new() { ["name"] = TileGrid.EmptyTileName };

    private static (TiledTileset Tileset, string Directory) ReadExternal(string source, string mapDirectory, AssetImportContext context)
    {
        if (Path.GetExtension(source).ToLowerInvariant() is ".tsx" or ".tmx")
        {
            throw new FormatException("the tileset is XML; Capsule reads JSON tilesets only. Re-save it from Tiled as .tsj.");
        }

        string path = Path.GetFullPath(Path.Combine(mapDirectory, source));
        TiledTileset tileset = TiledJson.Read<TiledTileset>(context.ReadAllText(path));
        TiledJson.RequireFormat(tileset.Version);

        return (tileset, Path.GetDirectoryName(path)!);
    }

    private static Tileset Resolve(TiledTileset tileset, string name, int mapTileSize, string directory, string assetRoot)
    {
        if (string.IsNullOrEmpty(tileset.Image))
        {
            throw new FormatException("the tileset is a collection of images; Capsule imports image tilesets only. Make it a single-image tileset in Tiled.");
        }

        if (tileset.TileWidth != tileset.TileHeight || tileset.TileWidth != mapTileSize)
        {
            throw new FormatException(
                $"the tileset has {tileset.TileWidth}x{tileset.TileHeight} tiles but the map has {mapTileSize}px square ones; set its Tile Width and Tile Height to {mapTileSize} in Tileset Properties.");
        }

        if (tileset.Columns * tileset.TileWidth != tileset.ImageWidth)
        {
            throw new FormatException(
                $"the tileset declares {tileset.Columns} columns of {tileset.TileWidth}px over a {tileset.ImageWidth}px image; re-save the tileset in Tiled so its columns match its image.");
        }

        string texture = TiledImporter.AssetPathOf(tileset.Image, directory, assetRoot);

        // Unpainted Classes stay in the palette, so painting a new type never renumbers the types a
        // scene's tiles already index.
        JsonArray palette = [EmptyTileType()];
        Dictionary<int, int> indexByTileId = [];
        Dictionary<int, TiledTile> unclassed = [];
        foreach (TiledTile tile in (tileset.Tiles ?? []).OrderBy(static tile => tile.Id))
        {
            if (string.IsNullOrWhiteSpace(tile.Type))
            {
                unclassed[tile.Id] = tile;
                continue;
            }

            indexByTileId[tile.Id] = palette.Count;
            palette.Add(TiledImporter.Within($"tile {tile.Id} (Class '{tile.Type}')", () => TileType(tile, tile.Type, tileset.TileWidth, directory, assetRoot)));
        }

        HashSet<string> classes = [.. palette.Select(static tileType => (string)tileType!["name"]!)];

        // An unclassed tile is named after its cell, as the tileset identifies it. It is converted only
        // where a layer paints it. An unpainted one is never read.
        JsonObject UnclassedTileType(int tileId) => TiledImporter.Within($"tile {tileId}", () =>
        {
            string cellName = UnclassedName(tileId);
            if (classes.Contains(cellName))
            {
                throw new FormatException(
                    $"the tile has no Class, so Capsule names it '{cellName}', which another tile's Class already is; give this tile a Class, or rename the other one.");
            }

            return TileType(unclassed.GetValueOrDefault(tileId) ?? new TiledTile { Id = tileId }, cellName, tileset.TileWidth, directory, assetRoot);
        });

        return new Tileset(name, tileset.FirstGid, texture, tileset.Columns, tileset.TileWidth, palette, indexByTileId, UnclassedTileType);
    }

    private static string UnclassedName(int tileId) => string.Create(CultureInfo.InvariantCulture, $"cell-{tileId}");

    private static JsonObject TileType(TiledTile tile, string name, int tileSize, string directory, string assetRoot)
    {
        TiledProperties properties = new(tile.Properties);
        JsonObject tileType = new() { ["name"] = name, ["cell"] = tile.Id };
        if (properties.TakeString("layer")?.Trim() is { } layer)
        {
            tileType["layer"] = layer;
        }
        else if (tile.ObjectGroup?.Objects is { Length: > 0 })
        {
            throw new FormatException(
                "the tile has a collision shape but no 'layer' property, so it collides as nothing; name its collision layer in a 'layer' property, or clear its collision in the Tile Collision Editor.");
        }

        if (ShapeOf(tile, tileSize) is { } shape)
        {
            tileType["shape"] = shape;
        }

        properties.AddTo(tileType, directory, assetRoot, "shape");

        return tileType;
    }

    private static JsonArray? ShapeOf(TiledTile tile, int tileSize)
    {
        TiledObject[] objects = tile.ObjectGroup?.Objects ?? [];
        if (objects.Length == 0)
        {
            return null;
        }

        if (objects.Length > 1)
        {
            throw new FormatException(
                $"the tile has {objects.Length} objects in its collision; merge them in the Tile Collision Editor into one polygon of 3 or 4 points, or one rectangle.");
        }

        TiledObject drawn = objects[0];
        string? refused = drawn switch
        {
            { Ellipse: true } => "an ellipse",
            { Point: true } => "a point",
            { Polyline: not null } => "a polyline",
            { Text.ValueKind: JsonValueKind.Object } => "a text object",
            { Gid: not null } => "a tile object",
            _ => null,
        };

        if (refused is not null)
        {
            throw new FormatException(
                $"the tile collides as {refused}; draw its collision in the Tile Collision Editor as one polygon of 3 or 4 points, or one rectangle.");
        }

        if (drawn.Rotation != 0)
        {
            throw new FormatException(string.Create(
                CultureInfo.InvariantCulture,
                $"the tile's collision shape is rotated {drawn.Rotation} degrees; set its Rotation to 0 and place its points where the rotated shape sat."));
        }

        Vector2 origin = new((float)drawn.X, (float)drawn.Y);
        Vector2[] points;
        if (drawn.Polygon is { } polygon)
        {
            points = [.. polygon.Select(point => origin + new Vector2((float)point.X, (float)point.Y))];
        }
        else
        {
            // A rectangle covering the whole tile is no shape. The tile then collides as a whole tile.
            Vector2 far = origin + new Vector2((float)drawn.Width, (float)drawn.Height);
            if (origin == Vector2.Zero && far == new Vector2(tileSize))
            {
                return null;
            }

            points = [origin, new Vector2(far.X, origin.Y), far, new Vector2(origin.X, far.Y)];
        }

        return [.. points.Select(static point => new JsonArray(point.X, point.Y))];
    }
}

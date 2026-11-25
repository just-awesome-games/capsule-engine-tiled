using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Capsule;
using Capsule.Build;
using Capsule.Physics;
using Capsule.Tiles;

namespace JAG.Capsule.Tiled;

internal sealed record Tileset(string Name, int FirstGid, string Texture, int Columns, int TileSize, JsonArray Palette, Dictionary<int, int> IndexByTileId, Dictionary<int, string> RuleNames);

internal static class Tilesets
{
    // Tiled times a frame in milliseconds and Capsule in fixed steps. The build cannot see a game's runtime
    // step rate, so a frame is counted at the engine's default.
    private const long StepsPerSecond = StepContext.DefaultStepHertz;

    // The sides a rule joins by the slot of a wang id that names each, whose slots Tiled stores clockwise from the top.
    private static readonly (int Slot, string Side)[] Sides =
    [
        (0, "up"), (2, "right"), (4, "down"), (6, "left"), (1, "upRight"), (3, "downRight"), (5, "downLeft"), (7, "upLeft"),
    ];

    internal static Tileset[] Load(TiledMap map, AssetImportContext context, PropertyScope scope)
    {
        List<Tileset> tilesets = [];
        foreach (TiledTileset entry in map.Tilesets)
        {
            (TiledTileset tileset, string directory) = entry.Source is { Length: > 0 } source
                ? TiledImporter.Within($"tileset '{source}'", () => ReadExternal(source, scope.Directory, context))
                : (entry, scope.Directory);
            tileset.FirstGid = entry.FirstGid;
            string name = tileset.Name ?? Path.GetFileNameWithoutExtension(entry.Source) ?? "?";
            tilesets.Add(TiledImporter.Within($"tileset '{name}'", () => Resolve(tileset, name, map.TileWidth, scope with { Directory = directory })));
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

    private static Tileset Resolve(TiledTileset tileset, string name, int mapTileSize, PropertyScope scope)
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

        string texture = TiledImporter.AssetPathOf(tileset.Image, scope.Directory, scope.AssetRoot);

        // Every tile of the tileset is a tile type, painted or not, so a game can set any of them in a tile map at
        // run time. Classes come first, then the wang sets, then the unclassed tiles in ID order. A wang tile is no
        // tile type of its own: it is one look of its set's rule tile.
        TiledWangSet[] wangSets = tileset.WangSets ?? [];
        Dictionary<int, string> ruleNames = RuleNames(wangSets, tileset.TileCount);
        JsonArray palette = [EmptyTileType()];
        Dictionary<int, int> indexByTileId = [];
        Dictionary<int, TiledTile> unclassed = [];
        foreach (TiledTile tile in (tileset.Tiles ?? []).OrderBy(static tile => tile.Id))
        {
            if (ruleNames.TryGetValue(tile.Id, out string? rule))
            {
                TiledImporter.Within($"tile {tile.Id}", () => RefuseOwnLook(tile, rule));
                continue;
            }

            if (string.IsNullOrWhiteSpace(tile.Type))
            {
                unclassed[tile.Id] = tile;
                continue;
            }

            indexByTileId[tile.Id] = palette.Count;
            palette.Add((JsonNode)TiledImporter.Within($"tile {tile.Id} (Class '{tile.Type}')", () => TileType(tile, tile.Type, tileset, scope)));
        }

        HashSet<string> names = [.. palette.Select(static tileType => (string)tileType!["name"]!)];
        foreach (TiledWangSet wangSet in wangSets)
        {
            int index = palette.Count;
            palette.Add((JsonNode)TiledImporter.Within($"wang set '{wangSet.Name}'", () => names.Add(wangSet.Name ?? string.Empty)
                ? RuleType(wangSet, scope)
                : throw new FormatException("the set shares its name with another tile's Class or wang set; rename it in the Terrain Sets view.")));
            foreach (TiledWangTile wangTile in wangSet.WangTiles ?? [])
            {
                indexByTileId[wangTile.TileId] = index;
            }
        }

        for (int tileId = 0; tileId < tileset.TileCount; tileId++)
        {
            if (indexByTileId.ContainsKey(tileId))
            {
                continue;
            }

            // An unclassed tile is named after its cell, as the tileset identifies it.
            string cellName = UnclassedName(tileId);
            int id = tileId;
            indexByTileId[tileId] = palette.Count;
            palette.Add((JsonNode)TiledImporter.Within($"tile {tileId}", () => names.Contains(cellName)
                ? throw new FormatException(
                    $"the tile has no Class, so Capsule names it '{cellName}', which another tile's Class or a wang set's name already is; give this tile a Class, or rename the other one.")
                : TileType(unclassed.GetValueOrDefault(id) ?? new TiledTile { Id = id }, cellName, tileset, scope)));
        }

        return new Tileset(name, tileset.FirstGid, texture, tileset.Columns, tileset.TileWidth, palette, indexByTileId, ruleNames);
    }

    // A wang tile belongs to one set, and a set is a rule tile of one terrain: an edge or mixed set with one colour, the
    // colour its rules join. A corner set picks its look from corners alone, which a rule tile has no side for.
    private static Dictionary<int, string> RuleNames(TiledWangSet[] wangSets, int tileCount)
    {
        Dictionary<int, string> ruleNames = [];
        foreach (TiledWangSet wangSet in wangSets)
        {
            TiledImporter.Within($"wang set '{wangSet.Name}'", () =>
            {
                if (wangSet.Type is not ("edge" or "mixed"))
                {
                    throw new FormatException(
                        $"the set is a {wangSet.Type} set, and Capsule imports edge and mixed sets as rule tiles; change its type in the Terrain Sets view, or delete it.");
                }

                int colors = wangSet.Colors?.Length ?? 0;
                if (colors != 1)
                {
                    throw new FormatException(
                        $"the set has {colors} colors, and a rule tile joins one terrain; give it exactly one color in the Terrain Sets view, and one set to each terrain.");
                }

                foreach (TiledWangTile wangTile in wangSet.WangTiles ?? [])
                {
                    if ((uint)wangTile.TileId >= (uint)tileCount)
                    {
                        throw new FormatException(string.Create(
                            CultureInfo.InvariantCulture,
                            $"wang tile {wangTile.TileId} is past the tileset's {tileCount} tiles; remove it from the set in the Terrain Sets view, or re-save the tileset in Tiled."));
                    }

                    if (!ruleNames.TryAdd(wangTile.TileId, wangSet.Name ?? string.Empty))
                    {
                        throw new FormatException(string.Create(
                            CultureInfo.InvariantCulture,
                            $"tile {wangTile.TileId} is also a tile of wang set '{ruleNames[wangTile.TileId]}', and a tile is one look of one rule tile; remove it from one of the sets."));
                    }
                }
            });
        }

        return ruleNames;
    }

    private static void RefuseOwnLook(TiledTile tile, string rule)
    {
        if (!string.IsNullOrWhiteSpace(tile.Type) || tile.Properties is { Length: > 0 } || tile.Animation is { Length: > 0 } || tile.ObjectGroup?.Objects is { Length: > 0 })
        {
            throw new FormatException(
                $"the tile is a look of rule tile '{rule}', so it takes no Class, property, collision or animation of its own; clear them on the tile, and set a property on its wang set instead.");
        }
    }

    private static JsonObject RuleType(TiledWangSet wangSet, PropertyScope scope)
    {
        TiledProperties properties = new(wangSet.Properties);
        JsonArray rules = [];
        foreach (TiledWangTile wangTile in (wangSet.WangTiles ?? []).OrderBy(static wangTile => wangTile.TileId))
        {
            int[] wangId = wangTile.WangId is { Length: 8 } id
                ? id
                : throw new FormatException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"wang tile {wangTile.TileId} holds {wangTile.WangId?.Length ?? 0} color slots, and Tiled saves 8; re-save the tileset in Tiled."));
            JsonObject rule = new() { ["cell"] = wangTile.TileId };
            string joined = string.Join(", ", Sides.Where(side => wangId[side.Slot] != 0).Select(static side => side.Side));
            if (joined.Length > 0)
            {
                rule["joined"] = joined;
            }

            rules.Add((JsonNode)rule);
        }

        JsonObject tileType = new() { ["name"] = wangSet.Name, ["joins"] = wangSet.Colors![0].Name, ["rules"] = rules };
        if (properties.TakeString("layer")?.Trim() is { } layer)
        {
            tileType["layer"] = layer;
        }

        properties.AddTo(tileType, scope);

        return tileType;
    }

    private static string UnclassedName(int tileId) => string.Create(CultureInfo.InvariantCulture, $"cell-{tileId}");

    // An animated tile is one tile type whatever its frames show. Its own properties and collision apply,
    // as in Tiled, and no frame tile's do.
    private static JsonObject TileType(TiledTile tile, string name, TiledTileset tileset, PropertyScope scope)
    {
        TiledProperties properties = new(tile.Properties);
        JsonObject tileType = new() { ["name"] = name };
        if (tile.Animation is { Length: > 0 } animation)
        {
            tileType["frames"] = FramesOf(animation, tileset.TileCount);
        }
        else
        {
            tileType["cell"] = tile.Id;
        }

        if (properties.TakeString("layer")?.Trim() is { } layer)
        {
            tileType["layer"] = layer;
        }
        else if (tile.ObjectGroup?.Objects is { Length: > 0 })
        {
            throw new FormatException(
                "the tile has a collision shape but no 'layer' property, so it collides as nothing; name its collision layer in a 'layer' property, or clear its collision in the Tile Collision Editor.");
        }

        if (ShapeOf(tile, tileset.TileWidth) is { } shape)
        {
            tileType["shape"] = shape;
        }

        properties.AddTo(tileType, scope, "shape", "cell", "frames");

        return tileType;
    }

    // A frame is held the nearest whole step, half a step rounding up, and at least one.
    private static JsonArray FramesOf(TiledFrame[] animation, int tileCount)
    {
        JsonArray frames = [];
        for (int i = 0; i < animation.Length; i++)
        {
            TiledFrame frame = animation[i];
            if (frame.TileId < 0 || frame.TileId >= tileCount)
            {
                throw new FormatException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"the tile's animation frame at index {i} shows tile {frame.TileId}, which is not one of the tileset's {tileCount} tiles; pick a tile of this tileset for it in the Tile Animation Editor."));
            }

            if (frame.Duration <= 0)
            {
                throw new FormatException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"the tile's animation frame at index {i} lasts {frame.Duration} ms; give it a duration of at least 1 ms in the Tile Animation Editor."));
            }

            int ticks = Math.Max(1, (int)(((frame.Duration * StepsPerSecond) + 500) / 1000));
            frames.Add((JsonNode)new JsonObject { ["cell"] = frame.TileId, ["ticks"] = ticks });
        }

        return frames;
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
                $"the tile has {objects.Length} objects in its collision; merge them in the Tile Collision Editor into one convex polygon of 3 to {Shape2D.MaxPoints} points, or one rectangle.");
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
                $"the tile collides as {refused}; draw its collision in the Tile Collision Editor as one convex polygon of 3 to {Shape2D.MaxPoints} points, or one rectangle.");
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

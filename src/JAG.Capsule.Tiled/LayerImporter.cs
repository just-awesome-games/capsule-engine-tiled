using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using Capsule.Tiles;

namespace JAG.Capsule.Tiled;

internal static class LayerImporter
{
    internal const string ZIndexProperty = "zIndex";
    internal const string ColliderProperty = "collider";
    private const string SizeProperty = "size";
    private const string PathProperty = "path";

    private const string TileMapType = "tile-map";

    // Tiled packs flip and rotation into the top nibble of a gid. It applies the anti-diagonal flip
    // first, then the horizontal, then the vertical, the order TileTransform applies Transpose, FlipX
    // and FlipY.
    private const uint OrientationFlags = 0xF000_0000u;
    private const uint FlippedHorizontally = 0x8000_0000u;
    private const uint FlippedVertically = 0x4000_0000u;
    private const uint FlippedDiagonally = 0x2000_0000u;
    private const uint RotatedHexagonal120 = 0x1000_0000u;

    // Layers dispatch on type, never on name. Entries keep the authored layer order.
    internal static List<SceneDocumentEntry> Read(
        TiledMap map,
        ResolvedTileset[] tilesets,
        string mapDirectory,
        string assetRoot)
    {
        List<SceneDocumentEntry> entries = [];

        foreach (TiledLayer layer in map.Layers)
        {
            switch (layer.Type)
            {
                case "tilelayer":
                    entries.Add(TileLayer(layer, map, tilesets));
                    break;

                case "objectgroup":
                    entries.AddRange(ObjectLayer(layer, tilesets, new FileRoots(mapDirectory, assetRoot)));
                    break;

                default:
                    throw new TiledImportException(
                        $"unsupported layer type '{layer.Type}' (layer '{layer.Name}'); Capsule imports tile layers and object layers only.");
            }
        }

        return entries;
    }

    private static SceneDocumentEntry TileLayer(TiledLayer layer, TiledMap map, ResolvedTileset[] tilesets)
    {
        TiledProperties properties = new(layer.Properties, $"tile layer '{layer.Name}'");

        EntitySpawn spawn = new(Vector2.Zero) { ZIndex = properties.Int(ZIndexProperty), ScrollFactor = ScrollFactorOf(layer) };

        return new SceneDocumentEntry(TileMapType, spawn, TileMapMembers(layer, map, tilesets, properties.Bool(ColliderProperty)));
    }

    private static SceneDocumentEntry[] ObjectLayer(TiledLayer layer, ResolvedTileset[] tilesets, FileRoots files)
    {
        TiledProperties properties = new(layer.Properties, $"object layer '{layer.Name}'");
        int? layerZIndex = properties.Int(ZIndexProperty);

        // An object's collision is its class's own. A layer switch that reached none would mislead.
        if (properties.Bool(ColliderProperty))
        {
            throw new TiledImportException(
                $"{properties.Owner} sets '{ColliderProperty}'; Capsule reads it on tile layers only, and an object collides as its class does. Remove the property.");
        }

        Vector2? scrollFactor = ScrollFactorOf(layer);

        return [.. (layer.Objects ?? []).Select(placed => Placement(placed, layer, tilesets, files, layerZIndex, scrollFactor))];
    }

    // A tile object's size over its tileset's tile size is its scale. A rectangle's or ellipse's
    // extent is its size member, a polyline's or polygon's points are its path member, and a
    // point or zero-size marker imports its position alone. Tiled turns an object about its own
    // position, which is the entry's. Every object keeps its Tiled id, which an object property names
    // it by.
    private static SceneDocumentEntry Placement(
        TiledObject placed,
        TiledLayer layer,
        ResolvedTileset[] tilesets,
        FileRoots files,
        int? layerZIndex,
        Vector2? scrollFactor)
    {
        string owner = $"object {placed.Id} on layer '{layer.Name}'";
        RequireNoTemplate(placed, owner);
        if (string.IsNullOrWhiteSpace(placed.Type))
        {
            throw new TiledImportException($"{owner} has no Class; every object is typed by its Class.");
        }

        RequirePlaceableShape(placed, owner);
        TiledProperties properties = new(placed.Properties, owner);

        // An object's own zIndex overrides its layer's.
        EntitySpawn spawn = new(new Vector2((float)placed.X, (float)placed.Y))
        {
            Rotation = float.DegreesToRadians((float)placed.Rotation),
            ZIndex = properties.Int(ZIndexProperty) ?? layerZIndex,
            ScrollFactor = scrollFactor,
        };

        if (placed.Gid is not { } gid)
        {
            bool sized = placed.Width > 0 && placed.Height > 0;

            return new SceneDocumentEntry(placed.Type, spawn, EntityMembers(properties, files, sized ? (placed.Width, placed.Height) : null, PathOf(placed, owner)))
            {
                Id = placed.Id,
            };
        }

        if ((gid & OrientationFlags) != 0)
        {
            throw new TiledImportException(
                $"{owner} is a flipped or rotated tile object; Capsule places tile objects unflipped, and flips only the tiles of a tile layer. Clear the object's flip in Tiled and face the entity in its own code.");
        }

        ResolvedTileset drawn = OwnerOf(gid, tilesets)
            ?? throw new TiledImportException($"{owner} has tile gid {gid}, which belongs to no tileset in the map.");

        spawn = spawn with { Scale = new Vector2((float)(placed.Width / drawn.TileSize), (float)(placed.Height / drawn.TileSize)) };

        return new SceneDocumentEntry(placed.Type, spawn, EntityMembers(properties, files, null, null)) { Id = placed.Id };
    }

    // A template instance carries only what it overrides, its Class included, and the importer
    // reads no .tx file.
    private static void RequireNoTemplate(TiledObject placed, string owner)
    {
        if (placed.Template is { } template)
        {
            throw new TiledImportException(
                $"{owner} is an instance of template '{template}'; Capsule reads no templates. Detach the object from its template in Tiled.");
        }
    }

    private static void RequirePlaceableShape(TiledObject placed, string owner)
    {
        if (placed.Text.ValueKind == JsonValueKind.Object)
        {
            throw new TiledImportException(
                $"{owner} is a text object; Capsule places points, rectangles, ellipses, polylines, polygons and tile objects. Replace it with one of those.");
        }

        if (placed.Gid is null && (placed.Width > 0) != (placed.Height > 0))
        {
            throw new TiledImportException(string.Create(
                CultureInfo.InvariantCulture,
                $"{owner} is {placed.Width}x{placed.Height}, an extent with no area; Capsule imports a rectangle's or ellipse's extent as its size. Give it both a width and a height, or click-place it with neither."));
        }
    }

    // A polyline's points, or a polygon's closed by repeating its first point, relative to the
    // object's position. An entity's turn only affects its presentation, so a turned path would lie
    // where Tiled does not draw it.
    private static TiledPoint[]? PathOf(TiledObject placed, string owner)
    {
        TiledPoint[]? path = placed.Polygon is { Length: > 0 } polygon ? [.. polygon, polygon[0]] : placed.Polyline;
        if (path is not null && placed.Rotation != 0)
        {
            throw new TiledImportException(string.Create(
                CultureInfo.InvariantCulture,
                $"{owner} is a {(placed.Polygon is null ? "polyline" : "polygon")} turned {placed.Rotation} degrees; Capsule imports its points unturned. Set its Rotation to 0 and move the points where the turn put them."));
        }

        return path;
    }

    // The extent or path comes first, then the custom properties in Tiled's order.
    private static JsonElement? EntityMembers(
        TiledProperties properties,
        FileRoots files,
        (double Width, double Height)? size,
        TiledPoint[]? path) =>
        TiledProperties.ObjectOf(writer =>
        {
            if (size is (double width, double height))
            {
                writer.WriteStartArray(SizeProperty);
                writer.WriteNumberValue(width);
                writer.WriteNumberValue(height);
                writer.WriteEndArray();
            }

            if (path is not null)
            {
                writer.WriteStartArray(PathProperty);
                foreach (TiledPoint point in path)
                {
                    writer.WriteStartArray();
                    writer.WriteNumberValue(point.X);
                    writer.WriteNumberValue(point.Y);
                    writer.WriteEndArray();
                }

                writer.WriteEndArray();
            }

            properties.WriteValues(
                writer,
                [ZIndexProperty],
                name => SceneDocumentKeys.Entry.Contains(name) || (name == SizeProperty && size is not null) || (name == PathProperty && path is not null),
                files.MapDirectory,
                files.AssetRoot);
        });

    // A layer's Parallax Factor. Tiled's default of 1, 1 is absent, as the document writes it.
    private static Vector2? ScrollFactorOf(TiledLayer layer) =>
        layer.ParallaxX == 1 && layer.ParallaxY == 1
            ? null
            : new Vector2((float)layer.ParallaxX, (float)layer.ParallaxY);

    // The tile map's members. A grid cuts its cells from one texture, and a layer paints from one
    // tileset. A layer that paints nothing keeps the empty palette and names no texture.
    private static JsonElement? TileMapMembers(TiledLayer layer, TiledMap map, ResolvedTileset[] tilesets, bool collider)
    {
        RequireReadableTileData(layer, map);

        int[] tiles = new int[(long)map.Width * map.Height];
        int[]? transforms = null;
        ResolvedTileset? painted = null;
        int index = 0;

        foreach (JsonElement element in layer.Data.EnumerateArray())
        {
            if (index == tiles.Length)
            {
                throw TileCountMismatch(layer, map, tiles.Length, "more than");
            }

            if (!element.TryGetUInt32(out uint gid))
            {
                throw new TiledImportException($"tile layer '{layer.Name}' has a non-numeric tile at index {index}.");
            }

            if ((gid & RotatedHexagonal120) != 0)
            {
                throw new TiledImportException(
                    $"tile layer '{layer.Name}' has a tile turned 120 degrees at index {index}; that turn exists only on hexagonal maps and Capsule imports orthogonal maps only.");
            }

            if ((gid & OrientationFlags) != 0)
            {
                transforms ??= new int[tiles.Length];
                transforms[index] = (int)TransformOf(gid);
                gid &= ~OrientationFlags;
            }

            if (gid != 0)
            {
                ResolvedTileset owner = OwnerOf(gid, tilesets)
                    ?? throw new TiledImportException(
                        $"tile gid {gid} on layer '{layer.Name}' belongs to no tileset in the map.");

                painted ??= owner;
                if (!ReferenceEquals(painted, owner))
                {
                    throw new TiledImportException(
                        $"tile layer '{layer.Name}' paints from tilesets '{painted.Name}' and '{owner.Name}'; a layer draws from one texture, so split it into one layer per tileset.");
                }

                tiles[index] = owner.IndexByGid.TryGetValue((int)gid, out int cell)
                    ? cell
                    : throw new TiledImportException(
                        $"tile {(int)gid - owner.FirstGid} of tileset '{owner.Name}' is painted at index {index} on layer '{layer.Name}' but has no Class; give every painted tile a Class in Tiled.");
            }

            index++;
        }

        if (index != tiles.Length)
        {
            throw TileCountMismatch(layer, map, index, "only");
        }

        return TiledProperties.ObjectOf(writer =>
        {
            writer.WriteNumber("tileSize", map.TileWidth);
            writer.WriteNumber("width", map.Width);
            writer.WriteNumber("height", map.Height);
            if (painted is not null)
            {
                writer.WriteString("texture", painted.Texture);
                writer.WriteNumber("columns", painted.Columns);
            }

            writer.WriteStartArray("tileTypes");
            foreach (JsonElement tileType in painted?.Palette ?? [TilesetImporter.EmptyTileType])
            {
                tileType.WriteTo(writer);
            }

            writer.WriteEndArray();
            WriteInts(writer, "tiles", tiles);
            if (transforms is not null)
            {
                WriteInts(writer, "transforms", transforms);
            }

            if (collider)
            {
                writer.WriteBoolean(ColliderProperty, true);
            }
        });
    }

    private static void WriteInts(Utf8JsonWriter writer, string name, int[] values)
    {
        writer.WriteStartArray(name);
        foreach (int value in values)
        {
            writer.WriteNumberValue(value);
        }

        writer.WriteEndArray();
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

    private static void RequireReadableTileData(TiledLayer layer, TiledMap map)
    {
        if (layer.Width != map.Width || layer.Height != map.Height)
        {
            throw new TiledImportException(
                $"tile layer '{layer.Name}' is {layer.Width}x{layer.Height} but the map is {map.Width}x{map.Height}.");
        }

        if (layer.Encoding is { } encoding && !encoding.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new TiledImportException(
                $"tile layer '{layer.Name}' uses '{encoding}' tile data; set Map > Map Properties > Tile Layer Format to CSV.");
        }

        if (layer.Compression is { Length: > 0 } compression)
        {
            throw new TiledImportException(
                $"tile layer '{layer.Name}' is '{compression}'-compressed; set Map > Map Properties > Tile Layer Format to CSV.");
        }

        if (layer.Data.ValueKind != JsonValueKind.Array)
        {
            throw new TiledImportException(
                $"tile layer '{layer.Name}' has no plain tile data; set Map > Map Properties > Tile Layer Format to CSV.");
        }
    }

    private static TiledImportException TileCountMismatch(TiledLayer layer, TiledMap map, int count, string qualifier) =>
        new($"tile layer '{layer.Name}' carries {qualifier} {count} tiles but {map.Width}x{map.Height} requires {map.Width * map.Height}.");

    // Tilesets are in ascending firstgid order. The owner is the last one starting at or below the gid.
    private static ResolvedTileset? OwnerOf(uint gid, ResolvedTileset[] tilesets)
    {
        ResolvedTileset? owner = null;
        foreach (ResolvedTileset tileset in tilesets)
        {
            if (tileset.FirstGid > (int)gid)
            {
                break;
            }

            owner = tileset;
        }

        return owner;
    }

    // Where a file property resolves from, and the root its asset key is taken under.
    private readonly record struct FileRoots(string MapDirectory, string AssetRoot);
}

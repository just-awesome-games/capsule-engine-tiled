using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Capsule.Scenes.Documents;
using Capsule.Tiles;

namespace JAG.Capsule.Tiled;

internal static class LayerImporter
{
    internal const string ZIndexProperty = "zIndex";
    internal const string ColliderProperty = "collider";
    private const string SizeProperty = "size";
    private const string PathProperty = "path";

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
        string assetRoot,
        ref int nextEntityId)
    {
        List<SceneDocumentEntry> entries = [];

        foreach (TiledLayer layer in map.Layers)
        {
            switch (layer.Type)
            {
                case "tilelayer":
                    entries.Add(TileLayer(layer, map, tilesets, nextEntityId++));
                    break;

                case "objectgroup":
                    foreach (EntityPlacement placed in ObjectLayer(layer, tilesets, new FileRoots(mapDirectory, assetRoot)))
                    {
                        entries.Add(placed);
                    }

                    break;

                default:
                    throw new TiledImportException(
                        $"unsupported layer type '{layer.Type}' (layer '{layer.Name}'); Capsule imports tile layers and object layers only.");
            }
        }

        return entries;
    }

    private static TileMapPlacement TileLayer(TiledLayer layer, TiledMap map, ResolvedTileset[] tilesets, int id)
    {
        string owner = $"tile layer '{layer.Name}'";
        TileGrid grid = ReadGrid(layer, map, tilesets);
        Vector2? scrollFactor = ScrollFactorOf(layer);
        TiledProperties properties = new(layer.Properties, owner);
        bool collider = properties.Bool(ColliderProperty);

        // A layer's palette is its tileset's every Class, painted or not, so one layered tile anywhere in
        // the tileset lets the layer collide.
        if (collider && CollidingTile(grid) is null)
        {
            throw new TiledImportException(
                $"{owner} sets '{ColliderProperty}' but paints from a tileset with no colliding tile. Give a tile in the tileset a '{TilesetImporter.LayerProperty}' string property, or remove '{ColliderProperty}' from the layer.");
        }

        // The engine refuses a scrolled grid that collides.
        if (collider && scrollFactor is not null)
        {
            throw new TiledImportException(
                $"{owner} has a Parallax Factor and sets '{ColliderProperty}'; a layer that collides cannot scroll apart from the camera. Remove '{ColliderProperty}' from the layer, or set its Parallax Factor to 1, 1.");
        }

        return new TileMapPlacement(id, grid, properties.Int(ZIndexProperty), scrollFactor, collider);
    }

    private static EntityPlacement[] ObjectLayer(TiledLayer layer, ResolvedTileset[] tilesets, FileRoots files)
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
    // extent is its size property, a polyline's or polygon's points are its path property, and a
    // point or zero-size marker imports its position alone. Tiled turns an object about its own
    // position, which is the placement's.
    private static EntityPlacement Placement(
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
        int? zIndex = properties.Int(ZIndexProperty) ?? layerZIndex;
        float rotation = (float)placed.Rotation;

        if (placed.Gid is not { } gid)
        {
            bool sized = placed.Width > 0 && placed.Height > 0;
            if (sized && properties.Has(SizeProperty))
            {
                throw new TiledImportException(
                    $"{owner} has both an extent and a '{SizeProperty}' property; Capsule imports the extent as '{SizeProperty}'. Remove the property, or click-place the object with no extent.");
            }

            TiledPoint[]? path = PathOf(placed, owner);
            if (path is not null && properties.Has(PathProperty))
            {
                throw new TiledImportException(
                    $"{owner} has both points and a '{PathProperty}' property; Capsule imports the points as '{PathProperty}'. Remove the property.");
            }

            return new EntityPlacement(
                placed.Id,
                placed.Type,
                (float)placed.X,
                (float)placed.Y,
                ZIndex: zIndex,
                ScrollFactor: scrollFactor,
                RotationDegrees: rotation,
                Properties: EntityProperties(properties, files, sized ? (placed.Width, placed.Height) : null, path));
        }

        if ((gid & OrientationFlags) != 0)
        {
            throw new TiledImportException(
                $"{owner} is a flipped or rotated tile object; Capsule places tile objects unflipped, and flips only the tiles of a tile layer. Clear the object's flip in Tiled and face the entity in its own code.");
        }

        ResolvedTileset drawn = OwnerOf(gid, tilesets)
            ?? throw new TiledImportException($"{owner} has tile gid {gid}, which belongs to no tileset in the map.");

        return new EntityPlacement(
            placed.Id,
            placed.Type,
            (float)placed.X,
            (float)placed.Y,
            (float)(placed.Width / drawn.TileSize),
            (float)(placed.Height / drawn.TileSize),
            zIndex,
            scrollFactor,
            rotation,
            EntityProperties(properties, files, null, null));
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

    // The extent or path comes first, then the custom properties in Tiled's order. A placement with
    // none of them carries no properties object.
    private static JsonElement? EntityProperties(
        TiledProperties properties,
        FileRoots files,
        (double Width, double Height)? size,
        TiledPoint[]? path)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
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

            properties.WriteValues(writer, [ZIndexProperty], files.MapDirectory, files.AssetRoot);
            writer.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);

        return document.RootElement.GetPropertyCount() == 0 ? null : document.RootElement.Clone();
    }

    private static TileType? CollidingTile(TileGrid grid)
    {
        foreach (TileType tileType in grid.TileTypes)
        {
            if (tileType.Layer is not null)
            {
                return tileType;
            }
        }

        return null;
    }

    // A layer's Parallax Factor. Tiled's default of 1, 1 is absent, as the document writes it.
    private static Vector2? ScrollFactorOf(TiledLayer layer) =>
        layer.ParallaxX == 1 && layer.ParallaxY == 1
            ? null
            : new Vector2((float)layer.ParallaxX, (float)layer.ParallaxY);

    // A grid cuts its cells from one texture, and a layer paints from one tileset. A layer that
    // paints nothing keeps the empty palette and names no texture.
    private static TileGrid ReadGrid(TiledLayer layer, TiledMap map, ResolvedTileset[] tilesets)
    {
        RequireReadableTileData(layer, map);

        int[] tiles = new int[(long)map.Width * map.Height];
        TileTransform[]? transforms = null;
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
                transforms ??= new TileTransform[tiles.Length];
                transforms[index] = TransformOf(gid);
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

        try
        {
            return painted is null
                ? new TileGrid(map.TileWidth, map.Width, map.Height, [TileGrid.EmptyTile], tiles)
                : new TileGrid(map.TileWidth, map.Width, map.Height, painted.Palette, tiles, painted.Texture, painted.Columns, transforms, painted.Authored);
        }
        catch (ArgumentException ex)
        {
            throw new TiledImportException($"tile layer '{layer.Name}' imports to an invalid tile map: {ex.Message}", ex);
        }
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

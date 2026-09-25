using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Tiles;

namespace Capsule.Tiled;

internal static class TilesetImporter
{
    internal const string LayerProperty = "layer";
    private const string OneWayProperty = "oneWay";
    private const string SolidSidesProperty = "solidSides";

    // Every tileset the map names, in ascending firstgid order. External tilesets feed the source hash.
    internal static ResolvedTileset[] Load(TiledMap map, string mapPath, string assetRoot, IncrementalHash sourceHash)
    {
        string mapDirectory = Path.GetDirectoryName(Path.GetFullPath(mapPath))!;
        List<(TiledTileset Tileset, string Directory)> loaded = [];
        foreach (TiledTileset entry in map.Tilesets)
        {
            if (string.IsNullOrEmpty(entry.Source))
            {
                loaded.Add((entry, mapDirectory));
                continue;
            }

            string owner = $"tileset '{entry.Source}'";
            string extension = Path.GetExtension(entry.Source);
            if (extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".tmx", StringComparison.OrdinalIgnoreCase))
            {
                throw new TiledImportException(
                    $"{owner} is XML; Capsule reads JSON tilesets only. Re-save it from Tiled as .tsj.");
            }

            string path = Path.GetFullPath(Path.Combine(mapDirectory, entry.Source));
            if (!IsWithin(path, assetRoot))
            {
                throw new TiledImportException(
                    $"{owner} resolves outside the asset root '{assetRoot}'; move it under that root so the build can track it.");
            }

            if (!File.Exists(path))
            {
                throw new TiledImportException($"{owner} is missing (expected at '{path}').");
            }

            byte[] tilesetBytes = File.ReadAllBytes(path);
            AppendLengthPrefixed(sourceHash, tilesetBytes);
            TiledTileset tileset = TiledImporter.Deserialize(tilesetBytes, owner, TiledJsonContext.Default.TiledTileset);
            TiledImporter.RequireSupportedFormat(tileset.Version, owner);
            tileset.FirstGid = entry.FirstGid;
            tileset.Name ??= Path.GetFileNameWithoutExtension(entry.Source);
            loaded.Add((tileset, Path.GetDirectoryName(path)!));
        }

        loaded.Sort(static (left, right) => left.Tileset.FirstGid.CompareTo(right.Tileset.FirstGid));

        // A tile type is an identity. Class names are unique across the whole map.
        Dictionary<string, string> tilesetByClass = new(StringComparer.Ordinal);
        ResolvedTileset[] tilesets = new ResolvedTileset[loaded.Count];
        for (int i = 0; i < tilesets.Length; i++)
        {
            tilesets[i] = Resolve(loaded[i].Tileset, loaded[i].Directory, map, assetRoot, tilesetByClass);
        }

        return tilesets;
    }

    private static ResolvedTileset Resolve(
        TiledTileset tileset,
        string tilesetDirectory,
        TiledMap map,
        string assetRoot,
        Dictionary<string, string> tilesetByClass)
    {
        string name = tileset.Name ?? "?";

        if (string.IsNullOrEmpty(tileset.Image))
        {
            throw new TiledImportException(
                $"tileset '{name}' is a collection of images; Capsule imports image tilesets only. Make it a single-image tileset in Tiled.");
        }

        if (tileset.Columns < 1)
        {
            throw new TiledImportException(
                $"tileset '{name}' declares {tileset.Columns} columns; an image tileset is at least one tile across.");
        }

        if (tileset.TileWidth != tileset.TileHeight || tileset.TileWidth != map.TileWidth)
        {
            throw new TiledImportException(
                $"tileset '{name}' has {tileset.TileWidth}x{tileset.TileHeight} tiles but the map has {map.TileWidth}px square ones; a tile map draws one tileset cell per grid cell.");
        }

        if (tileset.Columns * tileset.TileWidth != tileset.ImageWidth)
        {
            throw new TiledImportException(
                $"tileset '{name}' declares {tileset.Columns} columns of {tileset.TileWidth}px over a {tileset.ImageWidth}px image; re-save the tileset in Tiled so its columns match its image.");
        }

        return new ResolvedTileset(
            name,
            tileset.FirstGid,
            TextureOf(tileset.Image, name, tilesetDirectory, assetRoot),
            tileset.Columns,
            tileset.TileWidth,
            BuildPalette(tileset, name, tilesetByClass, out Dictionary<int, int> indexByGid),
            indexByGid);
    }

    // The handle's name is the atlas's path under the asset root, directories included.
    private static TextureHandle TextureOf(string authored, string name, string tilesetDirectory, string assetRoot)
    {
        string image = Path.GetFullPath(Path.Combine(tilesetDirectory, authored));

        if (!IsWithin(image, assetRoot))
        {
            throw new TiledImportException(
                $"tileset '{name}' draws from '{authored}', which resolves to '{image}'; a scene document names a texture by its path under '{assetRoot}', so move the image under that root.");
        }

        string extension = Path.GetExtension(image);
        if (extension.Length == 0)
        {
            throw new TiledImportException(
                $"tileset '{name}' draws from '{authored}'; a scene document names a texture by its path, extension included, so the image needs one.");
        }

        return new TextureHandle(Path.GetRelativePath(assetRoot, image).Replace('\\', '/')[..^extension.Length], extension);
    }

    private static bool IsWithin(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
            && !string.Equals(relative, "..", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void AppendLengthPrefixed(IncrementalHash hash, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    // Every Class in the tileset enters the palette in tile-id order, painted or not. Painting a new
    // type never renumbers the types a scene's tiles already index.
    private static TileDefinition[] BuildPalette(
        TiledTileset tileset,
        string tilesetName,
        Dictionary<string, string> tilesetByClass,
        out Dictionary<int, int> indexByGid)
    {
        List<TileDefinition> palette = [TileGrid.EmptyTile];
        indexByGid = [];

        foreach (TiledTile tile in (tileset.Tiles ?? []).OrderBy(tile => tile.Id))
        {
            if (string.IsNullOrWhiteSpace(tile.Type))
            {
                continue;
            }

            if (string.Equals(tile.Type, TileGrid.EmptyTileType, StringComparison.Ordinal))
            {
                throw new TiledImportException(
                    $"tileset '{tilesetName}' tile {tile.Id} has Class '{TileGrid.EmptyTileType}', which is reserved for the absence of a tile; rename it.");
            }

            if (!tilesetByClass.TryAdd(tile.Type, tilesetName))
            {
                throw new TiledImportException(
                    $"Class '{tile.Type}' is defined by more than one tile (tilesets '{tilesetByClass[tile.Type]}' and '{tilesetName}'); a Class must name exactly one tile.");
            }

            indexByGid[tileset.FirstGid + tile.Id] = palette.Count;
            palette.Add(DefinitionOf(tile, tile.Type, tilesetName, tileset.TileWidth));
        }

        return [.. palette];
    }

    private static TileDefinition DefinitionOf(TiledTile tile, string tileClass, string tilesetName, int tileSize)
    {
        TiledProperties properties = new(tile.Properties, $"tileset '{tilesetName}' tile {tile.Id} (Class '{tileClass}')");
        string? layer = LayerOf(properties);

        // Checked on the authored objects. A whole-tile rectangle writes no shape and still collides.
        if (layer is null && tile.ObjectGroup?.Objects is { Length: > 0 })
        {
            throw new TiledImportException(
                $"{properties.Owner} has a collision shape but no '{LayerProperty}' property, so it collides as nothing; name the collision layer the tile is on in a '{LayerProperty}' property, or clear its collision in the Tile Collision Editor.");
        }

        // The engine refuses oneWay on a tile with no layer, and solidSides on one that is not oneWay.
        return new TileDefinition(
            tileClass,
            tile.Id,
            layer,
            ShapeOf(tile, properties.Owner, tileSize),
            OneWay: properties.Bool(OneWayProperty),
            SolidSides: properties.Bool(SolidSidesProperty));
    }

    // The collision layer a tile is on, trimmed of the whitespace Tiled's property editor leaves.
    private static string? LayerOf(TiledProperties properties) => properties.String(LayerProperty)?.Trim() switch
    {
        null => null,

        // Read as absent, a blank layer would ship a tile that never collides.
        "" => throw properties.Invalid(LayerProperty, string.Empty, "one collision layer name"),
        { } layer => layer,
    };

    // The one polygon or rectangle a tile's Tile Collision Editor holds. None is the whole tile, and so
    // is a rectangle covering it, which the document writes as no shape. Shape2D owns convexity and
    // winding.
    private static Shape2D? ShapeOf(TiledTile tile, string owner, int tileSize)
    {
        TiledObject[] objects = tile.ObjectGroup?.Objects ?? [];
        if (objects.Length == 0)
        {
            return null;
        }

        if (objects.Length > 1)
        {
            throw new TiledImportException(
                $"{owner} has {objects.Length} objects in its collision; a tile collides as one shape. Merge them in the Tile Collision Editor into one polygon of 3 or 4 points, or one rectangle.");
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
            throw new TiledImportException(
                $"{owner} collides as {refused}; draw its collision in the Tile Collision Editor as one polygon of 3 or 4 points, or one rectangle.");
        }

        if (drawn.Rotation != 0)
        {
            throw new TiledImportException(string.Create(
                CultureInfo.InvariantCulture,
                $"{owner} has a collision shape rotated {drawn.Rotation} degrees; set its Rotation to 0 and place its points where the rotated shape sat."));
        }

        Vector2 origin = new((float)drawn.X, (float)drawn.Y);
        Span<Vector2> corners = stackalloc Vector2[Shape2D.MaxPoints];
        int count;
        if (drawn.Polygon is { } polygon)
        {
            if (polygon.Length is < 3 or > Shape2D.MaxPoints)
            {
                throw new TiledImportException(
                    $"{owner} has a collision polygon of {polygon.Length} points; a tile collides as 3 or {Shape2D.MaxPoints}. Redraw it in the Tile Collision Editor.");
            }

            for (int i = 0; i < polygon.Length; i++)
            {
                corners[i] = origin + new Vector2((float)polygon[i].X, (float)polygon[i].Y);
            }

            count = polygon.Length;
        }
        else
        {
            Vector2 far = origin + new Vector2((float)drawn.Width, (float)drawn.Height);
            corners[0] = origin;
            corners[1] = new Vector2(far.X, origin.Y);
            corners[2] = far;
            corners[3] = new Vector2(origin.X, far.Y);
            count = 4;
        }

        Shape2D shape;
        try
        {
            shape = Shape2D.Polygon(corners[..count]);
        }
        catch (ArgumentException ex)
        {
            throw new TiledImportException(
                $"{owner} has a collision shape Capsule cannot collide as: {ex.Message} Redraw it as a convex polygon of 3 or 4 separate points, or a rectangle with a width and a height.",
                ex);
        }

        Aabb2D bounds = shape.Bounds;
        if (bounds.Min.X < 0f || bounds.Min.Y < 0f || bounds.Max.X > tileSize || bounds.Max.Y > tileSize)
        {
            throw new TiledImportException(
                $"{owner} has a collision shape reaching outside its {tileSize}px tile; keep every point within the tile in the Tile Collision Editor.");
        }

        return shape.Kind == ShapeKind2D.Box && bounds.Min == Vector2.Zero && bounds.Max == new Vector2(tileSize)
            ? null
            : shape;
    }
}

// One tileset as a layer consumes it: the atlas it names, how that atlas is cut, and the palette a
// layer painted from it takes whole.
internal sealed record ResolvedTileset(
    string Name,
    int FirstGid,
    TextureHandle Texture,
    int Columns,
    int TileSize,
    TileDefinition[] Palette,
    Dictionary<int, int> IndexByGid);

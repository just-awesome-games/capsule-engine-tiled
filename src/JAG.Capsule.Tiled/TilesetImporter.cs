using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Capsule.Tiles;

namespace JAG.Capsule.Tiled;

internal static class TilesetImporter
{
    internal const string LayerProperty = "layer";

    // The tile type keys the importer writes itself.
    private const string NameKey = "name";
    private const string CellKey = "cell";
    private const string ShapeKey = "shape";

    // The palette entry every unpainted cell points at.
    internal static readonly JsonElement EmptyTileType =
        TiledProperties.ObjectOf(static writer => writer.WriteString(NameKey, TileGrid.EmptyTileName))!.Value;

    // Every tileset the map names, in ascending firstgid order.
    internal static ResolvedTileset[] Load(TiledMap map, string mapPath, string assetRoot, Func<string, byte[]> read, Func<string, bool> exists)
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

            if (!exists(path))
            {
                throw new TiledImportException($"{owner} is missing (expected at '{path}').");
            }

            TiledTileset tileset = MapImporter.Deserialize(read(path), owner, TiledJsonContext.Default.TiledTileset);
            MapImporter.RequireSupportedFormat(tileset.Version, owner);
            tileset.FirstGid = entry.FirstGid;
            tileset.Name ??= Path.GetFileNameWithoutExtension(entry.Source);
            loaded.Add((tileset, Path.GetDirectoryName(path)!));
        }

        loaded.Sort(static (left, right) => left.Tileset.FirstGid.CompareTo(right.Tileset.FirstGid));

        ResolvedTileset[] tilesets = new ResolvedTileset[loaded.Count];
        for (int i = 0; i < tilesets.Length; i++)
        {
            tilesets[i] = Resolve(loaded[i].Tileset, loaded[i].Directory, map, assetRoot);
        }

        return tilesets;
    }

    private static ResolvedTileset Resolve(
        TiledTileset tileset,
        string tilesetDirectory,
        TiledMap map,
        string assetRoot)
    {
        string name = tileset.Name ?? "?";

        if (string.IsNullOrEmpty(tileset.Image))
        {
            throw new TiledImportException(
                $"tileset '{name}' is a collection of images; Capsule imports image tilesets only. Make it a single-image tileset in Tiled.");
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

        JsonElement[] palette = BuildPalette(tileset, name, tilesetDirectory, assetRoot, out Dictionary<int, int> indexByGid);

        return new ResolvedTileset(
            name,
            tileset.FirstGid,
            TextureOf(tileset.Image, name, tilesetDirectory, assetRoot),
            tileset.Columns,
            tileset.TileWidth,
            palette,
            indexByGid);
    }

    // The atlas's path under the asset root, directories and extension included.
    private static string TextureOf(string authored, string name, string tilesetDirectory, string assetRoot)
    {
        string image = Path.GetFullPath(Path.Combine(tilesetDirectory, authored));

        if (!IsWithin(image, assetRoot))
        {
            throw new TiledImportException(
                $"tileset '{name}' draws from '{authored}', which resolves to '{image}'; a scene document names a texture by its path under '{assetRoot}', so move the image under that root.");
        }

        return Path.GetRelativePath(assetRoot, image).Replace('\\', '/');
    }

    internal static bool IsWithin(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
            && !string.Equals(relative, "..", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    // Every Class in the tileset enters the palette in tile-id order, painted or not. Painting a new
    // type never renumbers the types a scene's tiles already index.
    private static JsonElement[] BuildPalette(
        TiledTileset tileset,
        string tilesetName,
        string tilesetDirectory,
        string assetRoot,
        out Dictionary<int, int> indexByGid)
    {
        List<JsonElement> palette = [EmptyTileType];
        indexByGid = [];

        foreach (TiledTile tile in (tileset.Tiles ?? []).OrderBy(tile => tile.Id))
        {
            if (string.IsNullOrWhiteSpace(tile.Type))
            {
                continue;
            }

            indexByGid[tileset.FirstGid + tile.Id] = palette.Count;
            TiledProperties properties = new(tile.Properties, $"tileset '{tilesetName}' tile {tile.Id} (Class '{tile.Type}')");
            palette.Add(TileTypeOf(tile, properties, tileset.TileWidth, tilesetDirectory, assetRoot));
        }

        return [.. palette];
    }

    // The tile's Class is its name and its tile id its cell. Every other tile property, a "type" naming a
    // TileType subclass among them, sets the member of its name. Tiled writes a tile's file property
    // relative to its tileset's directory.
    private static JsonElement TileTypeOf(TiledTile tile, TiledProperties properties, int tileSize, string tilesetDirectory, string assetRoot)
    {
        // Trimmed of the whitespace Tiled's property editor leaves. An untrimmed layer would never match
        // the layer a mover collides with.
        string? layer = properties.String(LayerProperty)?.Trim();

        // Checked on the authored objects. A whole-tile rectangle writes no shape and still collides.
        if (layer is null && tile.ObjectGroup?.Objects is { Length: > 0 })
        {
            throw new TiledImportException(
                $"{properties.Owner} has a collision shape but no '{LayerProperty}' property, so it collides as nothing; name the collision layer the tile is on in a '{LayerProperty}' property, or clear its collision in the Tile Collision Editor.");
        }

        Vector2[]? shape = ShapeOf(tile, properties.Owner, tileSize);

        return TiledProperties.ObjectOf(writer =>
        {
            writer.WriteString(NameKey, tile.Type);
            writer.WriteNumber(CellKey, tile.Id);
            if (layer is not null)
            {
                writer.WriteString(LayerProperty, layer);
            }

            if (shape is not null)
            {
                writer.WriteStartArray(ShapeKey);
                foreach (Vector2 point in shape)
                {
                    writer.WriteStartArray();
                    writer.WriteNumberValue(point.X);
                    writer.WriteNumberValue(point.Y);
                    writer.WriteEndArray();
                }

                writer.WriteEndArray();
            }

            properties.WriteValues(writer, [LayerProperty], [NameKey, CellKey, ShapeKey], tilesetDirectory, assetRoot);
        })!.Value;
    }

    // The points of the one polygon or rectangle a tile's Tile Collision Editor holds. None is the whole
    // tile, and so is a rectangle covering it, which the document writes as no shape. The engine checks
    // that the points make a convex polygon inside the tile.
    private static Vector2[]? ShapeOf(TiledTile tile, string owner, int tileSize)
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
        if (drawn.Polygon is { } polygon)
        {
            return [.. polygon.Select(point => origin + new Vector2((float)point.X, (float)point.Y))];
        }

        Vector2 far = origin + new Vector2((float)drawn.Width, (float)drawn.Height);

        return origin == Vector2.Zero && far == new Vector2(tileSize)
            ? null
            : [origin, new Vector2(far.X, origin.Y), far, new Vector2(origin.X, far.Y)];
    }
}

// One tileset as a layer consumes it: the atlas it names, how that atlas is cut, and the palette a
// layer painted from it takes whole, each entry a tile type object.
internal sealed record ResolvedTileset(
    string Name,
    int FirstGid,
    string Texture,
    int Columns,
    int TileSize,
    JsonElement[] Palette,
    Dictionary<int, int> IndexByGid);

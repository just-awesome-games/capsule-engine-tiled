using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes.Documents;
using Capsule.Tiles;

namespace Capsule.Tiled;

internal static class TiledImporter
{
    internal const string ToolName = "tiled";

    private const string ColorProperty = "color";

    private const string LayerProperty = "layer";

    private const string OneWayProperty = "oneWay";
    private const string SolidSidesProperty = "solidSides";

    private const string CollidableFacesProperty = "collidableFaces";

    private const string CollisionProperty = "collision";

    private const string ZIndexProperty = "zIndex";

    private const string BaseSceneProperty = "baseScene";

    private const string CameraProperty = "camera";

    private const string AmbientProperty = "ambient";

    private const string SamplingProperty = "sampling";

    private const string ColorPropertyType = "color";

    // Tiled omits the type when it writes a string property.
    private const string StringPropertyType = "string";

    private const string IntPropertyType = "int";

    private const string BoolPropertyType = "bool";

    // Tiled packs flip and rotation into the top nibble of a gid.
    private const uint OrientationFlags = 0xF000_0000u;

    internal static SceneDocument Import(string mapPath, int? tileSize = null, string? dependencyRoot = null)
    {
        byte[] mapBytes = File.ReadAllBytes(mapPath);
        TiledMap map = Deserialize(mapBytes, mapPath, TiledJsonContext.Default.TiledMap);

        RequireSupportedMap(map, mapPath, tileSize);

        string mapDirectory = Path.GetDirectoryName(Path.GetFullPath(mapPath)) ?? Directory.GetCurrentDirectory();
        string? resolvedDependencyRoot = dependencyRoot is null ? null : Path.GetFullPath(dependencyRoot);
        using IncrementalHash sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sourceHash.AppendData(mapBytes);
        Tileset[] tilesets = LoadTilesets(map, mapDirectory, resolvedDependencyRoot, sourceHash);

        // Tiled mints ids for objects only. Tile layers continue from its next object id, and the
        // document keeps one id space.
        int nextEntityId = map.NextObjectId;
        List<SceneDocumentEntry> entries = ReadEntries(map, tilesets, ref nextEntityId);

        SceneDocumentSource source = new(
            ToolName,
            mapPath.Replace('\\', '/'),
            Convert.ToHexStringLower(sourceHash.GetHashAndReset()));

        // The document authors no size. A map's size is its tiles.
        SceneSettings settings = new()
        {
            BaseScene = MapStringPropertyOf(map.Properties, BaseSceneProperty),
            Camera = MapStringPropertyOf(map.Properties, CameraProperty),
            ClearColor = OpaqueColor(map.BackgroundColor, mapPath, "Background Color", "The background"),
            Ambient = MapColorPropertyOf(map.Properties, AmbientProperty, mapPath),
            Sampling = MapSamplingOf(map.Properties, mapPath),
        };

        try
        {
            return new SceneDocument(entries, nextEntityId, source, settings);
        }
        catch (Exception ex) when (ex is SceneDocumentFormatException or ArgumentException)
        {
            throw new TiledImportException($"'{mapPath}' imports to an invalid scene: {ex.Message}", ex);
        }
    }

    private static void RequireSupportedMap(TiledMap map, string mapPath, int? tileSize)
    {
        if (!string.Equals(map.Orientation, "orthogonal", StringComparison.Ordinal))
        {
            throw new TiledImportException(
                $"'{mapPath}' is a '{map.Orientation}' map; Capsule imports orthogonal maps only.");
        }

        if (map.Infinite)
        {
            throw new TiledImportException(
                $"'{mapPath}' is an infinite map; turn off Infinite in Map > Map Properties.");
        }

        if (map.TileWidth != map.TileHeight)
        {
            throw new TiledImportException(
                $"'{mapPath}' has {map.TileWidth}x{map.TileHeight} tiles; Capsule imports square tiles only.");
        }

        if (tileSize is { } declared && map.TileWidth != declared)
        {
            throw new TiledImportException(
                $"'{mapPath}' has {map.TileWidth}px tiles but the game declares {declared}px; set Map > Map Properties > Tile Width and Tile Height to {declared}, or change CapsuleTileSize.");
        }

        // Widened to long. A wrapped int product would size the tile array instead of failing here.
        long area = (long)map.Width * map.Height;
        if (map.Width <= 0 || map.Height <= 0 || area > Array.MaxLength)
        {
            throw new TiledImportException(
                $"'{mapPath}' is {map.Width}x{map.Height}, which is not a grid Capsule can hold.");
        }
    }

    private static Tileset[] LoadTilesets(
        TiledMap map,
        string mapDirectory,
        string? dependencyRoot,
        IncrementalHash sourceHash)
    {
        List<(TiledTileset Tileset, string Directory)> resolved = [];
        foreach (TiledTileset entry in map.Tilesets)
        {
            if (string.IsNullOrEmpty(entry.Source))
            {
                resolved.Add((entry, mapDirectory));
                continue;
            }

            string extension = Path.GetExtension(entry.Source);
            if (extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".tmx", StringComparison.OrdinalIgnoreCase))
            {
                throw new TiledImportException(
                    $"tileset '{entry.Source}' is XML; Capsule reads JSON tilesets only. Re-save it from Tiled as .tsj.");
            }

            string path = Path.GetFullPath(Path.Combine(mapDirectory, entry.Source));
            if (dependencyRoot is not null && !IsWithin(path, dependencyRoot))
            {
                throw new TiledImportException(
                    $"tileset '{entry.Source}' resolves outside the tracked asset source root '{dependencyRoot}'; move it under that root so the build can track it.");
            }

            if (!File.Exists(path))
            {
                throw new TiledImportException($"tileset '{entry.Source}' is missing (expected at '{path}').");
            }

            byte[] tilesetBytes = File.ReadAllBytes(path);
            AppendLengthPrefixed(sourceHash, tilesetBytes);
            TiledTileset tileset = Deserialize(tilesetBytes, path, TiledJsonContext.Default.TiledTileset);
            tileset.FirstGid = entry.FirstGid;
            tileset.Name ??= Path.GetFileNameWithoutExtension(entry.Source);
            resolved.Add((tileset, Path.GetDirectoryName(path) ?? mapDirectory));
        }

        resolved.Sort(static (left, right) => left.Tileset.FirstGid.CompareTo(right.Tileset.FirstGid));

        // A tile type is an identity. Class names are unique across the whole map.
        Dictionary<string, string> tilesetByClass = new(StringComparer.Ordinal);
        Tileset[] tilesets = new Tileset[resolved.Count];
        for (int i = 0; i < tilesets.Length; i++)
        {
            tilesets[i] = Describe(resolved[i].Tileset, resolved[i].Directory, map, dependencyRoot, tilesetByClass);
        }

        return tilesets;
    }

    private static Tileset Describe(
        TiledTileset tileset,
        string tilesetDirectory,
        TiledMap map,
        string? dependencyRoot,
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

        return new Tileset(
            name,
            tileset.FirstGid,
            TextureOf(tileset, name, tilesetDirectory, dependencyRoot),
            tileset.Columns,
            tileset.TileWidth,
            BuildPalette(tileset, name, tilesetByClass, out Dictionary<int, int> indexByGid),
            indexByGid);
    }

    // The handle's name is the atlas's path under the asset root, directories included.
    private static TextureHandle TextureOf(
        TiledTileset tileset,
        string name,
        string tilesetDirectory,
        string? dependencyRoot)
    {
        string image = Path.GetFullPath(Path.Combine(tilesetDirectory, tileset.Image!));

        if (dependencyRoot is not null && !IsWithin(image, dependencyRoot))
        {
            throw new TiledImportException(
                $"tileset '{name}' draws from '{tileset.Image}', which resolves to '{image}'; a scene document names a texture by its path under '{dependencyRoot}', so move the image under that root.");
        }

        string extension = Path.GetExtension(image);
        if (extension.Length == 0)
        {
            throw new TiledImportException(
                $"tileset '{name}' draws from '{tileset.Image}'; a scene document names a texture by its path, extension included, so the image needs one.");
        }

        // A run with no dependency root names the atlas by its stem. The build always passes a root.
        if (dependencyRoot is null)
        {
            return new TextureHandle(Path.GetFileNameWithoutExtension(image), extension);
        }

        return new TextureHandle(Path.GetRelativePath(dependencyRoot, image).Replace('\\', '/')[..^extension.Length], extension);
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
            string? tileClass = tile.ResolvedClass;
            if (string.IsNullOrWhiteSpace(tileClass))
            {
                continue;
            }

            if (string.Equals(tileClass, TileGrid.EmptyTileType, StringComparison.Ordinal))
            {
                throw new TiledImportException(
                    $"tileset '{tilesetName}' tile {tile.Id} has Class '{TileGrid.EmptyTileType}', which is reserved for the absence of a tile; rename it.");
            }

            if (!tilesetByClass.TryAdd(tileClass, tilesetName))
            {
                throw new TiledImportException(
                    $"Class '{tileClass}' is defined by more than one tile (tilesets '{tilesetByClass[tileClass]}' and '{tilesetName}'); a Class must name exactly one tile.");
            }

            AuthoredTile authored = new(tile, tileClass, tilesetName);
            RequireNoRetiredProperty(authored);

            string? layer = LayerOf(authored);
            indexByGid[tileset.FirstGid + tile.Id] = palette.Count;
            palette.Add(new TileDefinition(
                tileClass,
                tile.Id,
                layer,
                OneWay: BoolOf(authored, OneWayProperty),
                SolidSides: BoolOf(authored, SolidSidesProperty)));
        }

        return [.. palette];
    }

    private static void RequireNoRetiredProperty(AuthoredTile authored)
    {
        if (authored.Property(CollisionProperty) is not null)
        {
            throw new TiledImportException(
                $"{authored} has a '{CollisionProperty}' property, which Capsule no longer reads; name the collision layer the tile is on in a '{LayerProperty}' property.");
        }

        if (authored.Property(CollidableFacesProperty) is not null)
        {
            throw new TiledImportException(
                $"{authored} has a '{CollidableFacesProperty}' property, which Capsule no longer reads; a tile that blocks only from above takes a bool '{OneWayProperty}' property set to true instead.");
        }

        if (authored.Property(ColorProperty) is not null)
        {
            throw new TiledImportException(
                $"{authored} has a '{ColorProperty}' property, which Capsule no longer reads; a tile draws the cell of the tileset's image it occupies, so remove the property and paint the tile itself.");
        }
    }

    // The collision layer a tile is on, trimmed of the whitespace Tiled's property editor leaves.
    private static string? LayerOf(AuthoredTile authored)
    {
        if (!TryListOf(authored, LayerProperty, "one collision layer name", out string[] names))
        {
            return null;
        }

        return names.Length switch
        {
            // An empty property is an error. Read as absent, it would ship a tile that never collides.
            0 => throw new TiledImportException(
                $"{authored} has a '{LayerProperty}' property naming nothing; give it one collision layer name, or remove the property."),
            1 => names[0],
            _ => throw new TiledImportException(
                $"{authored} has '{LayerProperty}' naming {names.Length} layers; a tile is on one layer."),
        };
    }

    // A tile's bool property, oneWay or solidSides. Absent is false. The engine refuses oneWay on a
    // tile with no layer, and solidSides on one that is not oneWay.
    private static bool BoolOf(AuthoredTile authored, string propertyName)
    {
        if (authored.Property(propertyName) is not { } property)
        {
            return false;
        }

        string declared = property.Type ?? StringPropertyType;
        if (!string.Equals(declared, BoolPropertyType, StringComparison.Ordinal)
            || property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new TiledImportException(
                $"{authored} declares '{propertyName}' as a '{declared}' property; it has to be a bool property, or be left off entirely.");
        }

        return property.Value.GetBoolean();
    }

    // A comma-separated string property, trimmed, with blank entries dropped. Returns whether the
    // property was present. The caller owns the default for an absent one.
    private static bool TryListOf(AuthoredTile authored, string propertyName, string expected, out string[] names)
    {
        names = [];

        TiledProperty? property = authored.Property(propertyName);
        if (property is null)
        {
            return false;
        }

        if (property.Type is { } declared && !string.Equals(declared, StringPropertyType, StringComparison.Ordinal))
        {
            throw new TiledImportException(
                $"{authored} declares '{propertyName}' as a '{declared}' property; it has to be a string property of {expected}, or be left off entirely.");
        }

        string? value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
        if (value is null)
        {
            throw new TiledImportException(
                $"{authored} has a '{propertyName}' property that holds no text; it has to be a string property of {expected}, or be left off entirely.");
        }

        List<string> parsed = [];
        foreach (string part in value.Split(','))
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                parsed.Add(trimmed);
            }
        }

        names = [.. parsed];

        return true;
    }

    // Layers dispatch on type, never on name. Entries keep the authored layer order.
    private static List<SceneDocumentEntry> ReadEntries(TiledMap map, Tileset[] tilesets, ref int nextEntityId)
    {
        List<SceneDocumentEntry> entries = [];

        foreach (TiledLayer layer in map.Layers)
        {
            switch (layer.Type)
            {
                case "tilelayer":
                    entries.Add(new TileMapPlacement(
                        nextEntityId++,
                        ReadGrid(layer, map, tilesets),
                        ZIndexOf(layer.Properties, $"tile layer '{layer.Name}'")));
                    break;

                case "objectgroup":
                    int? layerBand = ZIndexOf(layer.Properties, $"object layer '{layer.Name}'");
                    foreach (TiledObject placed in layer.Objects ?? [])
                    {
                        string? objectClass = placed.ResolvedClass;
                        if (string.IsNullOrWhiteSpace(objectClass))
                        {
                            throw new TiledImportException(
                                $"object {placed.Id} on layer '{layer.Name}' has no Class; every object is typed by its Class.");
                        }

                        entries.Add(Placement(placed, objectClass, layer, tilesets, layerBand));
                    }

                    break;

                default:
                    throw new TiledImportException(
                        $"unsupported layer type '{layer.Type}' (layer '{layer.Name}'); Capsule imports tile layers and object layers only.");
            }
        }

        return entries;
    }

    // A tile object's size over its tileset's tile size is its scale. A point or rectangle imports
    // its position alone.
    private static EntityPlacement Placement(
        TiledObject placed,
        string objectClass,
        TiledLayer layer,
        Tileset[] tilesets,
        int? layerBand)
    {
        // An object's own band overrides its layer's.
        int? zIndex = ZIndexOf(placed.Properties, $"object {placed.Id} on layer '{layer.Name}'") ?? layerBand;

        if (placed.Gid is not { } gid)
        {
            return new EntityPlacement(placed.Id, objectClass, (float)placed.X, (float)placed.Y, ZIndex: zIndex);
        }

        if ((gid & OrientationFlags) != 0)
        {
            throw new TiledImportException(
                $"object {placed.Id} on layer '{layer.Name}' is a flipped or rotated tile object; Capsule imports unflipped tiles only.");
        }

        Tileset drawn = OwnerOf(gid, tilesets)
            ?? throw new TiledImportException(
                $"object {placed.Id} on layer '{layer.Name}' has tile gid {gid}, which belongs to no tileset in the map.");

        return new EntityPlacement(
            placed.Id,
            objectClass,
            (float)placed.X,
            (float)placed.Y,
            (float)(placed.Width / drawn.TileSize),
            (float)(placed.Height / drawn.TileSize),
            zIndex);
    }

    // The draw band a placement authors. An absent property is null, never 0. The entity's class
    // owns the default band.
    private static int? ZIndexOf(TiledProperty[]? properties, string owner)
    {
        if (TiledProperties.Find(properties, ZIndexProperty) is not { } property)
        {
            return null;
        }

        string declared = property.Type ?? StringPropertyType;
        if (!string.Equals(declared, IntPropertyType, StringComparison.Ordinal))
        {
            throw new TiledImportException(
                $"{owner} declares '{ZIndexProperty}' as a '{declared}' property; it has to be an int property, or be left off entirely.");
        }

        if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out int zIndex))
        {
            throw new TiledImportException(
                $"{owner} has a '{ZIndexProperty}' property of '{property.Value}'; it has to be a whole number an int holds, or be left off entirely.");
        }

        return zIndex;
    }

    // An optional map string, null when absent. The engine refuses a value that is not a key.
    private static string? MapStringPropertyOf(TiledProperty[]? properties, string name)
    {
        if (TiledProperties.Find(properties, name) is not { } property)
        {
            return null;
        }

        string declared = property.Type ?? StringPropertyType;
        if (!string.Equals(declared, StringPropertyType, StringComparison.Ordinal))
        {
            throw new TiledImportException(
                $"the map declares '{name}' as a '{declared}' property; set Map > Map Properties > {name} to a string property, or leave it off entirely.");
        }

        if (property.Value.ValueKind != JsonValueKind.String)
        {
            throw new TiledImportException(
                $"the map has a '{name}' property of '{property.Value}'; set Map > Map Properties > {name} to the key text, or leave it off entirely.");
        }

        return property.Value.GetString();
    }

    // Tiled writes a map colour property as "#aarrggbb".
    private static ColorRgba? MapColorPropertyOf(TiledProperty[]? properties, string name, string mapPath)
    {
        if (TiledProperties.Find(properties, name) is not { } property)
        {
            return null;
        }

        string declared = property.Type ?? StringPropertyType;
        if (!string.Equals(declared, ColorPropertyType, StringComparison.Ordinal))
        {
            throw new TiledImportException(
                $"the map declares '{name}' as a '{declared}' property; set Map > Map Properties > {name} to a color property, or leave it off entirely.");
        }

        string? color = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();

        return OpaqueColor(color, mapPath, $"'{name}' colour", $"The '{name}' colour");
    }

    // A typed setting cannot carry a misspelling to the engine's check. The two spellings are the
    // document format's.
    private static TextureSampling? MapSamplingOf(TiledProperty[]? properties, string mapPath) =>
        MapStringPropertyOf(properties, SamplingProperty) switch
        {
            null => null,
            "linear" => TextureSampling.Linear,
            "point" => TextureSampling.Point,
            { } other => throw new TiledImportException(
                $"'{mapPath}' has a '{SamplingProperty}' property of '{other}'; set it to \"linear\" or \"point\", or leave it off entirely."),
        };

    // Tiled writes "#rrggbb" for an opaque colour and "#aarrggbb" otherwise. The alpha moves last for
    // ColorRgba.FromHex. A scene's colours are opaque.
    private static ColorRgba? OpaqueColor(string? color, string mapPath, string owner, string subject)
    {
        if (color is null)
        {
            return null;
        }

        ColorRgba parsed;
        try
        {
            parsed = ColorRgba.FromHex(color.Length == 9 && color[0] == '#'
                ? string.Concat("#".AsSpan(), color.AsSpan(3), color.AsSpan(1, 2))
                : color);
        }
        catch (FormatException ex)
        {
            throw new TiledImportException(
                $"'{mapPath}' has a {owner} of '{color}', which is not a colour Tiled writes; pick one in Tiled's colour picker, or leave it off entirely.",
                ex);
        }

        if (parsed.A != byte.MaxValue)
        {
            throw new TiledImportException(
                $"'{mapPath}' has a translucent {owner} '{color}'. {subject} must be opaque: set its alpha to 255.");
        }

        return parsed;
    }

    // A grid cuts its cells from one texture, and a layer paints from one tileset. A layer that
    // paints nothing keeps the empty palette and names no texture. Only the cell array is held.
    private static TileGrid ReadGrid(TiledLayer layer, TiledMap map, Tileset[] tilesets)
    {
        RequireReadableTileData(layer, map);

        int[] tiles = new int[(long)map.Width * map.Height];
        Tileset? painted = null;
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

            if ((gid & OrientationFlags) != 0)
            {
                throw new TiledImportException(
                    $"tile layer '{layer.Name}' has a flipped or rotated tile at index {index}; Capsule imports unflipped tiles only.");
            }

            if (gid != 0)
            {
                Tileset owner = OwnerOf(gid, tilesets)
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

        return painted is null
            ? new TileGrid(map.TileWidth, map.Width, map.Height, [TileGrid.EmptyTile], tiles)
            : new TileGrid(
                map.TileWidth,
                map.Width,
                map.Height,
                painted.Palette,
                tiles,
                painted.Texture,
                painted.Columns);
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
    private static Tileset? OwnerOf(uint gid, Tileset[] tilesets)
    {
        Tileset? owner = null;
        foreach (Tileset tileset in tilesets)
        {
            if (tileset.FirstGid > (int)gid)
            {
                break;
            }

            owner = tileset;
        }

        return owner;
    }

    private static T Deserialize<T>(byte[] utf8, string path, JsonTypeInfo<T> typeInfo)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        ReadOnlySpan<byte> bytes = utf8;
        if (bytes.StartsWith(bom))
        {
            bytes = bytes[bom.Length..];
        }

        T? document;
        try
        {
            document = JsonSerializer.Deserialize(bytes, typeInfo);
        }
        catch (JsonException ex)
        {
            throw new TiledImportException(
                string.Create(CultureInfo.InvariantCulture, $"'{path}' is not readable Tiled JSON: {ex.Message}"),
                ex);
        }

        return document ?? throw new TiledImportException($"'{path}' is empty.");
    }

    // A tile and the identity every message about it names.
    private readonly record struct AuthoredTile(TiledTile Tile, string Class, string Tileset)
    {
        internal TiledProperty? Property(string name) => Tile.Property(name);

        public override string ToString() => $"tileset '{Tileset}' tile {Tile.Id} (Class '{Class}')";
    }

    // One tileset as a layer consumes it: the atlas it names, how that atlas is cut, and the
    // palette a layer painted from it takes whole.
    private sealed record Tileset(
        string Name,
        int FirstGid,
        TextureHandle Texture,
        int Columns,
        int TileSize,
        TileDefinition[] Palette,
        Dictionary<int, int> IndexByGid);
}

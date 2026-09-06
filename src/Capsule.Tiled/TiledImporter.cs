using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Capsule.Assets;
using Capsule.Collision;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Tiles;

namespace Capsule.Tiled;

public static class TiledImporter
{
    public const string ToolName = "tiled";

    private const string ColorProperty = "color";

    private const string LayerProperty = "layer";

    private const string CollidableFacesProperty = "collidableFaces";

    private const string CollisionProperty = "collision";

    private const string ZIndexProperty = "zIndex";

    // The asset-source domain a tileset's atlas is filed under; a document names its texture by the
    // path under it.
    private const string TextureDirectory = "textures";

    // Tiled's name for the String property type, which it omits when writing one.
    private const string StringPropertyType = "string";

    // Tiled's name for the Int property type, which it always writes.
    private const string IntPropertyType = "int";

    // Tiled packs flip and rotation into the top nibble of a gid.
    private const uint OrientationFlags = 0xF000_0000u;

    public static SceneDocument Import(string mapPath, int? tileSize = null, string? dependencyRoot = null)
    {
        byte[] mapBytes = File.ReadAllBytes(mapPath);
        TiledMap map = Deserialize(mapBytes, mapPath, TiledJsonContext.Default.TiledMap);

        RequireSupportedMap(map, mapPath, tileSize);

        string mapDirectory = Path.GetDirectoryName(Path.GetFullPath(mapPath)) ?? Directory.GetCurrentDirectory();
        string? resolvedDependencyRoot = dependencyRoot is null ? null : Path.GetFullPath(dependencyRoot);
        using IncrementalHash sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sourceHash.AppendData(mapBytes);
        Tileset[] tilesets = LoadTilesets(map, mapDirectory, resolvedDependencyRoot, sourceHash);

        // Tiled mints ids for objects alone; tile layers take theirs from the next one Tiled would
        // hand out, so the document keeps one collision-free id space.
        int nextEntityId = map.NextObjectId;
        List<SceneDocumentEntry> entries = ReadEntries(map, tilesets, ref nextEntityId);

        SceneDocumentSource source = new(
            ToolName,
            mapPath.Replace('\\', '/'),
            Convert.ToHexStringLower(sourceHash.GetHashAndReset()));

        try
        {
            return new SceneDocument(entries, nextEntityId, source);
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

        // Widened before anything is sized off it: an int product wraps, and the wrapped value
        // would size the tile array rather than fail here.
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
                    $"tileset '{entry.Source}' is XML; Capsule reads JSON tilesets only — re-save it from Tiled as .tsj.");
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

        // Class names are unique across the whole map, not per tileset: a tile type is identity.
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
                $"tileset '{name}' is a collection of images; Capsule imports image tilesets only — make it a single-image tileset in Tiled.");
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

    // The handle's name is the atlas's path under the textures root, directories included.
    private static TextureHandle TextureOf(
        TiledTileset tileset,
        string name,
        string tilesetDirectory,
        string? dependencyRoot)
    {
        string image = Path.GetFullPath(Path.Combine(tilesetDirectory, tileset.Image!));
        string? texturesRoot = dependencyRoot is null ? null : Path.Combine(dependencyRoot, TextureDirectory);

        if (texturesRoot is not null && !IsWithin(image, texturesRoot))
        {
            throw new TiledImportException(
                $"tileset '{name}' draws from '{tileset.Image}', which resolves to '{image}'; a scene document names a texture by its path under '{texturesRoot}', so move the image under that root.");
        }

        string extension = Path.GetExtension(image);
        if (extension.Length == 0)
        {
            throw new TiledImportException(
                $"tileset '{name}' draws from '{tileset.Image}'; a scene document names a texture by its path under '{TextureDirectory}', extension included, so the image needs one.");
        }

        // With no dependency root there is no textures root to measure against, so a bare importer
        // run keeps the atlas's stem. The build always passes one.
        string handle = texturesRoot is null
            ? Path.GetFileNameWithoutExtension(image)
            : Path.GetRelativePath(texturesRoot, image).Replace('\\', '/')[..^extension.Length];

        return new TextureHandle(handle, extension);
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

    // Every Class in the tileset enters the palette, painted or not, in tile-id order: painting a
    // new type must not renumber the types a scene's tiles already index.
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
            palette.Add(new TileDefinition(tileClass, tile.Id, layer, FacesOf(authored, layer)));
        }

        return [.. palette];
    }

    private static void RequireNoRetiredProperty(AuthoredTile authored)
    {
        if (authored.Property(CollisionProperty) is not null)
        {
            throw new TiledImportException(
                $"{authored} has a '{CollisionProperty}' property, which Capsule no longer reads; name the collision layer the tile is on in a '{LayerProperty}' property, and which of its sides collide in a '{CollidableFacesProperty}' one.");
        }

        if (authored.Property(ColorProperty) is not null)
        {
            throw new TiledImportException(
                $"{authored} has a '{ColorProperty}' property, which Capsule no longer reads; a tile draws the cell of the tileset's image it occupies, so remove the property and paint the tile itself.");
        }
    }

    // The collision layer a tile is on. Trimmed: the whitespace Tiled's property editor leaves
    // behind is not part of the name.
    private static string? LayerOf(AuthoredTile authored)
    {
        if (!TryListOf(authored, LayerProperty, "one collision layer name", out string[] names))
        {
            return null;
        }

        return names.Length switch
        {
            // Authored and empty is not absent: read as absent it would ship a tile that silently
            // never collides.
            0 => throw new TiledImportException(
                $"{authored} has a '{LayerProperty}' property naming nothing; give it one collision layer name, or remove the property."),
            1 => names[0],
            _ => throw new TiledImportException(
                $"{authored} has '{LayerProperty}' naming {names.Length} layers; a tile is on one layer."),
        };
    }

    private static CellFaces2D FacesOf(AuthoredTile authored, string? layer)
    {
        string expected = $"a comma-separated list of {string.Join(", ", TileFaceNames.All)}";
        if (!TryListOf(authored, CollidableFacesProperty, expected, out string[] names))
        {
            return CellFaces2D.All;
        }

        // Asked of the property's presence, not of what it holds: an empty one on a tile with no
        // layer is refused here rather than passing as absent.
        if (layer is null)
        {
            throw new TiledImportException(
                $"{authored} has '{CollidableFacesProperty}' but no '{LayerProperty}'; a tile that collides as nothing has no sides to declare.");
        }

        if (names.Length == 0)
        {
            throw new TiledImportException(
                $"{authored} has a '{CollidableFacesProperty}' property naming nothing; give it at least one of {string.Join(", ", TileFaceNames.All)}, or remove the property to collide on every side.");
        }

        CellFaces2D faces = CellFaces2D.None;
        foreach (string name in names)
        {
            faces |= TileFaceNames.TryParse(name, out CellFaces2D one)
                ? one
                : throw new TiledImportException(
                    $"{authored} has '{CollidableFacesProperty}' naming '{name}'; it has to be {expected}, or be left off entirely.");
        }

        return faces;
    }

    // A comma-separated string property, trimmed, with blank entries dropped. Returns whether the
    // property was present, not whether it named anything: only the caller knows which default an
    // absent one takes. Tiled omits the type of a string property, so an absent type is that type.
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

    // Dispatched on layer type, never layer name: what a layer is called is a game's convention.
    // Entries keep the authored layer order, so a foreground tile layer stays above the object
    // layers it follows.
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

    // A tile object's box over the cell it was dragged from is its scale. Every other object is a
    // point or a rectangle, whose size Capsule has no meaning for, so only its position imports.
    private static EntityPlacement Placement(
        TiledObject placed,
        string objectClass,
        TiledLayer layer,
        Tileset[] tilesets,
        int? layerBand)
    {
        // The object's own band wins over the one its layer bands the whole group at.
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

    // The draw band a placement authors. An absent property is null, never 0: zero is a band like
    // any other, and reading absence as zero would band every unbanded placement there, overriding
    // the default its class owns with a value the document never authored.
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

    // One layer paints from one tileset, because a grid cuts its cells from one texture. A layer
    // that paints nothing keeps the empty palette and names no texture at all. Each gid is resolved
    // as it is read, so nothing but the cell array is held for the layer.
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

    // The tilesets are in ascending firstgid order, so the owner is the last one starting at or
    // below the gid, and the first one starting above it ends the search.
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
                string.Create(CultureInfo.InvariantCulture, $"'{path}' is not readable Tiled JSON — {ex.Message}"),
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

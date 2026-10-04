using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Capsule.Scenes.Documents;

namespace JAG.Capsule.Tiled;

internal static class MapImporter
{
    private const string BaseSceneProperty = SceneDocumentKeys.Document.BaseScene;
    private const string CameraProperty = "camera";
    private const string BackgroundColor = "Background Color";

    // The scene members the importer writes itself.
    private const string ClearColorKey = "clearColor";
    private const string ScrollCenterKey = "scrollCenter";

    private const string MapOwner = "the map";

    // Tiled 1.9 writes a tile's and an object's Class as "class". Tiled 1.10 writes it as "type".
    private static readonly Version OldestFormat = new(1, 10);

    // The build reads the map and probes and reads its tilesets through the import context, which makes
    // each an input of the import. A test reads them from disk.
    internal static SceneDocument Import(
        string mapPath,
        string assetRoot,
        int? tileSize = null,
        Func<string, byte[]>? read = null,
        Func<string, bool>? exists = null)
    {
        read ??= File.ReadAllBytes;
        exists ??= File.Exists;
        TiledMap map = Deserialize(read(mapPath), MapOwner, TiledJsonContext.Default.TiledMap);
        RequireSupportedFormat(map.Version, MapOwner);
        RequireSupportedMap(map, tileSize);

        string fullAssetRoot = Path.GetFullPath(assetRoot);
        ResolvedTileset[] tilesets = TilesetImporter.Load(map, mapPath, fullAssetRoot, read, exists);
        string mapDirectory = Path.GetDirectoryName(Path.GetFullPath(mapPath))!;
        List<SceneDocumentEntry> entries = LayerImporter.Read(map, tilesets, mapDirectory, fullAssetRoot);
        TiledProperties properties = new(map.Properties, MapOwner);

        try
        {
            return new SceneDocument(
                entries,
                SceneMembers(map, properties, mapDirectory, fullAssetRoot),
                properties.String(BaseSceneProperty));
        }
        catch (ArgumentException ex)
        {
            throw new TiledImportException($"the map imports to an invalid scene: {ex.Message}", ex);
        }
    }

    internal static void RequireSupportedFormat(string? version, string owner)
    {
        if (!Version.TryParse(version, out Version? format) || format < OldestFormat)
        {
            throw new TiledImportException(
                $"{owner} has format version '{version}'; re-save it with Tiled 1.10 or later.");
        }
    }

    internal static T Deserialize<T>(byte[] utf8, string owner, JsonTypeInfo<T> typeInfo)
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
            throw new TiledImportException($"{owner} is not readable Tiled JSON: {ex.Message}", ex);
        }

        return document ?? throw new TiledImportException($"{owner} is empty.");
    }

    private static void RequireSupportedMap(TiledMap map, int? tileSize)
    {
        if (!string.Equals(map.Orientation, "orthogonal", StringComparison.Ordinal))
        {
            throw new TiledImportException(
                $"the map is '{map.Orientation}'; Capsule imports orthogonal maps only.");
        }

        if (map.Infinite)
        {
            throw new TiledImportException(
                "the map is infinite; turn off Infinite in Map > Map Properties.");
        }

        if (map.TileWidth != map.TileHeight)
        {
            throw new TiledImportException(
                $"the map has {map.TileWidth}x{map.TileHeight} tiles; Capsule imports square tiles only.");
        }

        if (tileSize is { } declared && map.TileWidth != declared)
        {
            throw new TiledImportException(
                $"the map has {map.TileWidth}px tiles but the game declares {declared}px; set Map > Map Properties > Tile Width and Tile Height to {declared}, or change WithTileSize in the build project.");
        }

        // Widened to long. A wrapped int product would size the tile array instead of failing here.
        long area = (long)map.Width * map.Height;
        if (map.Width <= 0 || map.Height <= 0 || area > Array.MaxLength)
        {
            throw new TiledImportException(
                $"the map is {map.Width}x{map.Height}, which is not a grid Capsule can hold.");
        }
    }

    // The scene's members. The document authors no size, because a map's size is its tiles. Every map
    // property but baseScene and camera sets the member of its name.
    private static JsonElement? SceneMembers(TiledMap map, TiledProperties properties, string mapDirectory, string assetRoot) =>
        TiledProperties.ObjectOf(writer =>
        {
            if (map.BackgroundColor is { } background)
            {
                writer.WriteString(ClearColorKey, properties.ColorText(BackgroundColor, background));
            }

            string? camera = properties.String(CameraProperty);
            (double X, double Y)? scrollCenter = ScrollCenterOf(map);
            if (camera is not null || scrollCenter is not null)
            {
                writer.WriteStartObject(CameraProperty);
                if (camera is not null)
                {
                    writer.WriteString(SceneDocumentKeys.MemberObject.Type, camera);
                }

                if (scrollCenter is (double x, double y))
                {
                    writer.WriteStartArray(ScrollCenterKey);
                    writer.WriteNumberValue(x);
                    writer.WriteNumberValue(y);
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            properties.WriteValues(
                writer,
                [BaseSceneProperty, CameraProperty],
                static name => name == ClearColorKey || SceneDocumentKeys.Document.Contains(name),
                mapDirectory,
                assetRoot);
        });

    // Tiled's renderer adds the Parallax Origin to the view centre (mapscene.cpp), so an origin O
    // previews as a scroll centre of -O. A map with a parallax layer writes even 0, 0, because the
    // camera's own default would draw those layers away from where Tiled previews them. Subtracting
    // from zero keeps a zero origin from writing -0.
    private static (double X, double Y)? ScrollCenterOf(TiledMap map) =>
        map.ParallaxOriginX != 0 || map.ParallaxOriginY != 0 || map.Layers.Any(layer => layer.ParallaxX != 1 || layer.ParallaxY != 1)
            ? (0 - map.ParallaxOriginX, 0 - map.ParallaxOriginY)
            : null;
}

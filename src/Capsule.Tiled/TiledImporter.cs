using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Capsule.Rendering;
using Capsule.Scenes.Documents;

namespace Capsule.Tiled;

internal static class TiledImporter
{
    internal const string ToolName = "tiled";

    private const string BaseSceneProperty = "baseScene";
    private const string CameraProperty = "camera";
    private const string AmbientProperty = "ambient";
    private const string SamplingProperty = "sampling";
    private const string BackgroundColor = "Background Color";

    private const string MapOwner = "the map";

    // Tiled 1.9 writes a tile's and an object's Class as "class". Tiled 1.10 writes it as "type".
    private static readonly Version OldestFormat = new(1, 10);

    internal static SceneDocument Import(string mapPath, string assetRoot, int? tileSize = null)
    {
        byte[] mapBytes = File.ReadAllBytes(mapPath);
        TiledMap map = Deserialize(mapBytes, MapOwner, TiledJsonContext.Default.TiledMap);
        RequireSupportedFormat(map.Version, MapOwner);
        RequireSupportedMap(map, tileSize);

        using IncrementalHash sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sourceHash.AppendData(mapBytes);
        ResolvedTileset[] tilesets = TilesetImporter.Load(map, mapPath, Path.GetFullPath(assetRoot), sourceHash);

        // Tiled mints ids for objects only. Tile layers continue from its next object id, and the
        // document keeps one id space.
        int nextEntityId = map.NextObjectId;
        List<SceneDocumentEntry> entries = LayerImporter.Read(map, tilesets, ref nextEntityId);

        SceneDocumentSource source = new(
            ToolName,
            mapPath.Replace('\\', '/'),
            Convert.ToHexStringLower(sourceHash.GetHashAndReset()));

        try
        {
            return new SceneDocument(entries, nextEntityId, source, SettingsOf(map));
        }
        catch (Exception ex) when (ex is SceneDocumentFormatException or ArgumentException)
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
                $"the map has {map.TileWidth}px tiles but the game declares {declared}px; set Map > Map Properties > Tile Width and Tile Height to {declared}, or change CapsuleTileSize.");
        }

        // Widened to long. A wrapped int product would size the tile array instead of failing here.
        long area = (long)map.Width * map.Height;
        if (map.Width <= 0 || map.Height <= 0 || area > Array.MaxLength)
        {
            throw new TiledImportException(
                $"the map is {map.Width}x{map.Height}, which is not a grid Capsule can hold.");
        }
    }

    // The document authors no size. A map's size is its tiles.
    private static SceneSettings SettingsOf(TiledMap map)
    {
        TiledProperties properties = new(map.Properties, MapOwner);

        return new SceneSettings
        {
            BaseScene = properties.String(BaseSceneProperty),
            Camera = properties.String(CameraProperty),
            ClearColor = map.BackgroundColor is { } background ? properties.OpaqueColor(BackgroundColor, background) : null,
            Ambient = properties.Color(AmbientProperty),
            ScrollCenter = ScrollCenterOf(map),

            // A typed setting cannot carry a misspelling to the engine's check. The two spellings are
            // the document format's.
            Sampling = properties.String(SamplingProperty) switch
            {
                null => null,
                "linear" => TextureSampling.Linear,
                "point" => TextureSampling.Point,
                { } other => throw properties.Invalid(SamplingProperty, other, "linear or point"),
            },
        };
    }

    // Tiled's Parallax Origin and the scene's scroll centre are both the view centre at which every
    // layer sits as authored. A map with a parallax layer writes even 0, 0, because the camera's own
    // default would draw those layers away from where Tiled previews them.
    private static Vector2? ScrollCenterOf(TiledMap map) =>
        map.ParallaxOriginX != 0 || map.ParallaxOriginY != 0 || map.Layers.Any(layer => layer.ParallaxX != 1 || layer.ParallaxY != 1)
            ? new Vector2((float)map.ParallaxOriginX, (float)map.ParallaxOriginY)
            : null;
}

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace JAG.Capsule.Tiled;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TiledMap))]
[JsonSerializable(typeof(TiledTileset))]
[JsonSerializable(typeof(TiledProperty[]))]
internal sealed partial class TiledJson : JsonSerializerContext
{
    // Tiled 1.9 writes a tile's and an object's Class as "class". Tiled 1.10 writes it as "type".
    private static readonly Version OldestFormat = new(1, 10);

    internal static T Read<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, (JsonTypeInfo<T>)Default.GetTypeInfo(typeof(T))!)
                ?? throw new FormatException("the file holds no Tiled document; re-save it from Tiled.");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"the file is not readable Tiled JSON ({ex.Message}); re-save it from Tiled.", ex);
        }
    }

    internal static void RequireFormat(string? version)
    {
        if (!Version.TryParse(version, out Version? format) || format < OldestFormat)
        {
            throw new FormatException($"format version '{version}' is older than 1.10; re-save the file with Tiled 1.10 or later.");
        }
    }

    internal static TiledProperty[] ReadItems(JsonElement list) =>
        list.Deserialize(Default.TiledPropertyArray) ?? [];

    internal static JsonElement? ToElement(JsonObject members) =>
        members.Count == 0 ? null : System.Text.Json.JsonElement.Parse(members.ToJsonString());
}

internal sealed class TiledMap
{
    public string? Version { get; set; }
    public string? Orientation { get; set; }
    public bool Infinite { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int TileWidth { get; set; }
    public int TileHeight { get; set; }
    public string? BackgroundColor { get; set; }
    public double ParallaxOriginX { get; set; }
    public double ParallaxOriginY { get; set; }
    public TiledLayer[] Layers { get; set; } = [];
    public TiledTileset[] Tilesets { get; set; } = [];
    public TiledProperty[]? Properties { get; set; }
}

internal sealed class TiledLayer
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Encoding { get; set; }
    public string? Compression { get; set; }
    public double ParallaxX { get; set; } = 1;
    public double ParallaxY { get; set; } = 1;
    public JsonElement Data { get; set; }
    public TiledObject[]? Objects { get; set; }
    public TiledProperty[]? Properties { get; set; }

    internal Vector2? ScrollFactor =>
        ParallaxX == 1 && ParallaxY == 1 ? null : new Vector2((float)ParallaxX, (float)ParallaxY);
}

internal sealed class TiledTileset
{
    public int FirstGid { get; set; }
    public string? Source { get; set; }
    public string? Version { get; set; }
    public string? Name { get; set; }
    public string? Image { get; set; }
    public int ImageWidth { get; set; }
    public int Columns { get; set; }
    public int TileWidth { get; set; }
    public int TileHeight { get; set; }
    public TiledTile[]? Tiles { get; set; }
}

internal sealed class TiledTile
{
    public int Id { get; set; }
    public string? Type { get; set; }
    public TiledProperty[]? Properties { get; set; }
    public TiledLayer? ObjectGroup { get; set; }
}

internal sealed class TiledProperty
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? PropertyType { get; set; }
    public JsonElement Value { get; set; }
}

internal sealed class TiledObject
{
    public int Id { get; set; }
    public string? Type { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Rotation { get; set; }
    public TiledPoint[]? Polygon { get; set; }
    public TiledPoint[]? Polyline { get; set; }
    public bool Ellipse { get; set; }
    public bool Point { get; set; }
    public JsonElement Text { get; set; }
    public uint? Gid { get; set; }
    public string? Template { get; set; }
    public TiledProperty[]? Properties { get; set; }
}

internal sealed class TiledPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

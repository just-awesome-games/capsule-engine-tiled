using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capsule.Tiled;

// Only the fields the importer reads. Tiled writes many more and adds them between versions,
// so unmapped members are skipped here — the opposite of Capsule's own format, which we own
// and therefore hold strictly.
internal sealed class TiledMap
{
    [JsonPropertyName("orientation")]
    public string? Orientation { get; set; }

    [JsonPropertyName("infinite")]
    public bool Infinite { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("tilewidth")]
    public int TileWidth { get; set; }

    [JsonPropertyName("tileheight")]
    public int TileHeight { get; set; }

    [JsonPropertyName("nextobjectid")]
    public int NextObjectId { get; set; }

    [JsonPropertyName("layers")]
    public TiledLayer[] Layers { get; set; } = [];

    [JsonPropertyName("tilesets")]
    public TiledTileset[] Tilesets { get; set; } = [];
}

internal sealed class TiledLayer
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("encoding")]
    public string? Encoding { get; set; }

    [JsonPropertyName("compression")]
    public string? Compression { get; set; }

    // An array for CSV/plain data and a string for base64; only the former is supported, and
    // the difference is not visible to a typed member.
    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }

    [JsonPropertyName("objects")]
    public TiledObject[]? Objects { get; set; }
}

// One shape for both cases: the map's tilesets array carries firstgid plus either an inline
// tileset or a source pointing at a .tsj, and an external .tsj is the same document without them.
internal sealed class TiledTileset
{
    [JsonPropertyName("firstgid")]
    public int FirstGid { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    // An image tileset's atlas, relative to the tileset document. A collection tileset has none
    // and writes columns 0, which is how the two are told apart.
    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("imagewidth")]
    public int ImageWidth { get; set; }

    [JsonPropertyName("columns")]
    public int Columns { get; set; }

    [JsonPropertyName("tilewidth")]
    public int TileWidth { get; set; }

    [JsonPropertyName("tileheight")]
    public int TileHeight { get; set; }

    [JsonPropertyName("tiles")]
    public TiledTile[]? Tiles { get; set; }
}

internal sealed class TiledTile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    // Tiled 1.9 writes "class"; 1.10 reverted to "type". Both are read so a map authored in
    // either version imports without the author knowing which.
    [JsonPropertyName("class")]
    public string? Class { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("properties")]
    public TiledProperty[]? Properties { get; set; }

    public string? ResolvedClass => string.IsNullOrWhiteSpace(Class) ? Type : Class;

    public TiledProperty? Property(string name)
    {
        foreach (TiledProperty property in Properties ?? [])
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                return property;
            }
        }

        return null;
    }
}

internal sealed class TiledProperty
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    // A tile carries every custom property an author gave it, of any type, so a typed member
    // here would fail the whole import on a neighbouring bool or int.
    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }
}

internal sealed class TiledObject
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("class")]
    public string? Class { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    // Present only on a tile object — one placed from a tileset — and carrying Tiled's flip bits in
    // its top nibble. Its absence is what tells a point or a rectangle from a tile.
    [JsonPropertyName("gid")]
    public uint? Gid { get; set; }

    public string? ResolvedClass => string.IsNullOrWhiteSpace(Class) ? Type : Class;
}

using System.Text.Json;

namespace Capsule.Tiled;

// Only the fields the importer reads. Unmapped members are skipped. TiledJsonContext matches names
// case-insensitively, and the C# name is the mapping.
internal sealed class TiledMap
{
    public string? Orientation { get; set; }
    public bool Infinite { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int TileWidth { get; set; }
    public int TileHeight { get; set; }
    public int NextObjectId { get; set; }

    // "#rrggbb" or "#aarrggbb", and absent when the map sets no Background Color.
    public string? BackgroundColor { get; set; }
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

    // An array for CSV data and a string for base64. Only CSV is supported.
    public JsonElement Data { get; set; }
    public TiledObject[]? Objects { get; set; }
    public TiledProperty[]? Properties { get; set; }
}

// A map's tileset entry carries firstgid and either an inline tileset or a .tsj source. An external
// .tsj is the same shape without them.
internal sealed class TiledTileset
{
    public int FirstGid { get; set; }
    public string? Source { get; set; }
    public string? Name { get; set; }

    // An image tileset's atlas, relative to the tileset document. A collection tileset has none.
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

    // Tiled 1.9 writes "class" and 1.10 writes "type". Both are read.
    public string? Class { get; set; }
    public string? Type { get; set; }
    public TiledProperty[]? Properties { get; set; }

    public string? ResolvedClass => string.IsNullOrWhiteSpace(Class) ? Type : Class;

    public TiledProperty? Property(string name) => TiledProperties.Find(Properties, name);
}

// One lookup by name for the custom properties of a tile, a layer or an object.
internal static class TiledProperties
{
    internal static TiledProperty? Find(TiledProperty[]? properties, string name)
    {
        foreach (TiledProperty property in properties ?? [])
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
    public string? Name { get; set; }
    public string? Type { get; set; }

    // Untyped. A typed member would fail the import on a neighbouring property of another type.
    public JsonElement Value { get; set; }
}

internal sealed class TiledObject
{
    public int Id { get; set; }
    public string? Class { get; set; }
    public string? Type { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    // Present only on a tile object, with Tiled's flip bits in its top nibble. A point or rectangle
    // has none.
    public uint? Gid { get; set; }
    public TiledProperty[]? Properties { get; set; }

    public string? ResolvedClass => string.IsNullOrWhiteSpace(Class) ? Type : Class;
}

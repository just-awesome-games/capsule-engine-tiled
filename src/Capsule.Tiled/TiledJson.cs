using System.Text.Json;

namespace Capsule.Tiled;

// Only the fields the importer reads. Tiled writes many more and adds them between versions, so
// unmapped members are skipped. Tiled spells every name in lower case and TiledJsonContext matches
// case-insensitively, so the C# name is the mapping.
internal sealed class TiledMap
{
    public string? Orientation { get; set; }
    public bool Infinite { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int TileWidth { get; set; }
    public int TileHeight { get; set; }
    public int NextObjectId { get; set; }
    public TiledLayer[] Layers { get; set; } = [];
    public TiledTileset[] Tilesets { get; set; } = [];
}

internal sealed class TiledLayer
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Encoding { get; set; }
    public string? Compression { get; set; }

    // An array for CSV/plain data and a string for base64; only the former is supported, and the
    // difference is not visible to a typed member.
    public JsonElement Data { get; set; }
    public TiledObject[]? Objects { get; set; }
}

// One shape for both cases: the map's tilesets array carries firstgid plus either an inline tileset
// or a source pointing at a .tsj, and an external .tsj is the same document without them.
internal sealed class TiledTileset
{
    public int FirstGid { get; set; }
    public string? Source { get; set; }
    public string? Name { get; set; }

    // An image tileset's atlas, relative to the tileset document. A collection tileset has none and
    // writes columns 0, which is how the two are told apart.
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

    // Tiled 1.9 writes "class"; 1.10 reverted to "type". Both are read, so a map authored in either
    // version imports.
    public string? Class { get; set; }
    public string? Type { get; set; }
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
    public string? Name { get; set; }
    public string? Type { get; set; }

    // A tile carries every custom property an author gave it, of any type, so a typed member here
    // would fail the whole import on a neighbouring bool or int.
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

    // Present only on a tile object — one placed from a tileset — and carrying Tiled's flip bits in
    // its top nibble. Its absence is what tells a point or a rectangle from a tile.
    public uint? Gid { get; set; }

    public string? ResolvedClass => string.IsNullOrWhiteSpace(Class) ? Type : Class;
}

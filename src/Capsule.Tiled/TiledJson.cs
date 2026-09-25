using System.Text.Json;

namespace Capsule.Tiled;

// Only the fields the importer reads. Unmapped members are skipped. TiledJsonContext matches names
// case-insensitively, and the C# name is the mapping.
internal sealed class TiledMap
{
    public string? Version { get; set; }
    public string? Orientation { get; set; }
    public bool Infinite { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int TileWidth { get; set; }
    public int TileHeight { get; set; }
    public int NextObjectId { get; set; }

    // "#rrggbb" or "#aarrggbb", and absent when the map sets no Background Color.
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

    // Tiled omits a factor of 1.
    public double ParallaxX { get; set; } = 1;
    public double ParallaxY { get; set; } = 1;

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

    // An external tileset's own format version. An inline one has the map's.
    public string? Version { get; set; }
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
    public string? Type { get; set; }
    public TiledProperty[]? Properties { get; set; }

    // The Tile Collision Editor's shapes, in pixels from the tile's top-left corner.
    public TiledLayer? ObjectGroup { get; set; }
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
    public string? Type { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    // Degrees clockwise about the object's origin.
    public double Rotation { get; set; }

    // A rectangle carries none of these. Each other shape carries its own.
    public TiledPoint[]? Polygon { get; set; }
    public TiledPoint[]? Polyline { get; set; }
    public bool Ellipse { get; set; }
    public bool Point { get; set; }
    public JsonElement Text { get; set; }

    // Present only on a tile object, with Tiled's flip bits in its top nibble. A point or rectangle
    // has none.
    public uint? Gid { get; set; }
    public TiledProperty[]? Properties { get; set; }
}

// A polygon or polyline point, relative to its object's position.
internal sealed class TiledPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

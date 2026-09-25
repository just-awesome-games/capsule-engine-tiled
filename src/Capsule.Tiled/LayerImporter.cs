using System.Numerics;
using System.Text.Json;
using Capsule.Scenes.Documents;
using Capsule.Tiles;

namespace Capsule.Tiled;

internal static class LayerImporter
{
    internal const string ZIndexProperty = "zIndex";

    // Tiled packs flip and rotation into the top nibble of a gid. It applies the anti-diagonal flip
    // first, then the horizontal, then the vertical, the order TileTransform applies Transpose, FlipX
    // and FlipY.
    private const uint OrientationFlags = 0xF000_0000u;
    private const uint FlippedHorizontally = 0x8000_0000u;
    private const uint FlippedVertically = 0x4000_0000u;
    private const uint FlippedDiagonally = 0x2000_0000u;
    private const uint RotatedHexagonal120 = 0x1000_0000u;

    // Layers dispatch on type, never on name. Entries keep the authored layer order.
    internal static List<SceneDocumentEntry> Read(TiledMap map, ResolvedTileset[] tilesets, ref int nextEntityId)
    {
        List<SceneDocumentEntry> entries = [];

        foreach (TiledLayer layer in map.Layers)
        {
            switch (layer.Type)
            {
                case "tilelayer":
                    entries.Add(TileLayer(layer, map, tilesets, nextEntityId++));
                    break;

                case "objectgroup":
                    foreach (EntityPlacement placed in ObjectLayer(layer, tilesets))
                    {
                        entries.Add(placed);
                    }

                    break;

                default:
                    throw new TiledImportException(
                        $"unsupported layer type '{layer.Type}' (layer '{layer.Name}'); Capsule imports tile layers and object layers only.");
            }
        }

        return entries;
    }

    private static TileMapPlacement TileLayer(TiledLayer layer, TiledMap map, ResolvedTileset[] tilesets, int id)
    {
        string owner = $"tile layer '{layer.Name}'";
        TileGrid grid = ReadGrid(layer, map, tilesets);
        Vector2? scrollFactor = ScrollFactorOf(layer);

        // The engine refuses a scrolled grid whose palette collides, and a layer's palette is its
        // tileset's every Class, painted or not.
        if (scrollFactor is not null && CollidingTile(grid) is { } colliding)
        {
            throw new TiledImportException(
                $"{owner} has a Parallax Factor but paints from a tileset with colliding tiles ('{colliding.Type}' has a '{TilesetImporter.LayerProperty}' property); a layer that collides cannot scroll apart from the camera. Split the colliding tiles into their own tileset painted on a layer whose Parallax Factor is 1, 1.");
        }

        return new TileMapPlacement(id, grid, new TiledProperties(layer.Properties, owner).Int(ZIndexProperty), scrollFactor);
    }

    private static EntityPlacement[] ObjectLayer(TiledLayer layer, ResolvedTileset[] tilesets)
    {
        int? layerZIndex = new TiledProperties(layer.Properties, $"object layer '{layer.Name}'").Int(ZIndexProperty);
        Vector2? scrollFactor = ScrollFactorOf(layer);

        return [.. (layer.Objects ?? []).Select(placed => Placement(placed, layer, tilesets, layerZIndex, scrollFactor))];
    }

    // A tile object's size over its tileset's tile size is its scale. A point or rectangle imports
    // its position alone.
    private static EntityPlacement Placement(
        TiledObject placed,
        TiledLayer layer,
        ResolvedTileset[] tilesets,
        int? layerZIndex,
        Vector2? scrollFactor)
    {
        string owner = $"object {placed.Id} on layer '{layer.Name}'";
        if (string.IsNullOrWhiteSpace(placed.Type))
        {
            throw new TiledImportException($"{owner} has no Class; every object is typed by its Class.");
        }

        // An object's own zIndex overrides its layer's.
        int? zIndex = new TiledProperties(placed.Properties, owner).Int(ZIndexProperty) ?? layerZIndex;

        if (placed.Gid is not { } gid)
        {
            return new EntityPlacement(placed.Id, placed.Type, (float)placed.X, (float)placed.Y, ZIndex: zIndex, ScrollFactor: scrollFactor);
        }

        if ((gid & OrientationFlags) != 0)
        {
            throw new TiledImportException(
                $"{owner} is a flipped or rotated tile object; Capsule places tile objects unflipped, and flips only the tiles of a tile layer. Clear the object's flip in Tiled and face the entity in its own code.");
        }

        ResolvedTileset drawn = OwnerOf(gid, tilesets)
            ?? throw new TiledImportException($"{owner} has tile gid {gid}, which belongs to no tileset in the map.");

        return new EntityPlacement(
            placed.Id,
            placed.Type,
            (float)placed.X,
            (float)placed.Y,
            (float)(placed.Width / drawn.TileSize),
            (float)(placed.Height / drawn.TileSize),
            zIndex,
            scrollFactor);
    }

    private static TileDefinition? CollidingTile(TileGrid grid)
    {
        foreach (TileDefinition definition in grid.TileTypes)
        {
            if (definition.Layer is not null)
            {
                return definition;
            }
        }

        return null;
    }

    // A layer's Parallax Factor. Tiled's default of 1, 1 is absent, as the document writes it.
    private static Vector2? ScrollFactorOf(TiledLayer layer) =>
        layer.ParallaxX == 1 && layer.ParallaxY == 1
            ? null
            : new Vector2((float)layer.ParallaxX, (float)layer.ParallaxY);

    // A grid cuts its cells from one texture, and a layer paints from one tileset. A layer that
    // paints nothing keeps the empty palette and names no texture.
    private static TileGrid ReadGrid(TiledLayer layer, TiledMap map, ResolvedTileset[] tilesets)
    {
        RequireReadableTileData(layer, map);

        int[] tiles = new int[(long)map.Width * map.Height];
        TileTransform[]? transforms = null;
        ResolvedTileset? painted = null;
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

            if ((gid & RotatedHexagonal120) != 0)
            {
                throw new TiledImportException(
                    $"tile layer '{layer.Name}' has a tile turned 120 degrees at index {index}; that turn exists only on hexagonal maps and Capsule imports orthogonal maps only.");
            }

            if ((gid & OrientationFlags) != 0)
            {
                transforms ??= new TileTransform[tiles.Length];
                transforms[index] = TransformOf(gid);
                gid &= ~OrientationFlags;
            }

            if (gid != 0)
            {
                ResolvedTileset owner = OwnerOf(gid, tilesets)
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

        try
        {
            return painted is null
                ? new TileGrid(map.TileWidth, map.Width, map.Height, [TileGrid.EmptyTile], tiles)
                : new TileGrid(map.TileWidth, map.Width, map.Height, painted.Palette, tiles, painted.Texture, painted.Columns, transforms);
        }
        catch (ArgumentException ex)
        {
            throw new TiledImportException($"tile layer '{layer.Name}' imports to an invalid tile map: {ex.Message}", ex);
        }
    }

    private static TileTransform TransformOf(uint gid)
    {
        TileTransform transform = TileTransform.None;
        if ((gid & FlippedDiagonally) != 0)
        {
            transform |= TileTransform.Transpose;
        }

        if ((gid & FlippedHorizontally) != 0)
        {
            transform |= TileTransform.FlipX;
        }

        if ((gid & FlippedVertically) != 0)
        {
            transform |= TileTransform.FlipY;
        }

        return transform;
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
    private static ResolvedTileset? OwnerOf(uint gid, ResolvedTileset[] tilesets)
    {
        ResolvedTileset? owner = null;
        foreach (ResolvedTileset tileset in tilesets)
        {
            if (tileset.FirstGid > (int)gid)
            {
                break;
            }

            owner = tileset;
        }

        return owner;
    }
}

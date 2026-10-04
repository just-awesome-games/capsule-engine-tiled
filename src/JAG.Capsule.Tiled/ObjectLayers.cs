using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace JAG.Capsule.Tiled;

internal static class ObjectLayers
{
    internal static SceneDocumentEntry[] ToEntries(TiledLayer layer, Tileset[] tilesets, string mapDirectory, string assetRoot)
    {
        int? zIndex = TiledImporter.Within($"object layer '{layer.Name}'", () =>
        {
            TiledProperties properties = new(layer.Properties);
            if (properties.TakeBool("collider"))
            {
                throw new FormatException("the layer sets 'collider', which only a tile layer reads, and an object collides as its class does; remove the property.");
            }

            return properties.TakeInt("zIndex");
        });

        return [.. (layer.Objects ?? []).Select(placed => TiledImporter.Within(
            $"object {placed.Id} on layer '{layer.Name}'",
            () => ToEntry(placed, layer, zIndex, tilesets, mapDirectory, assetRoot)))];
    }

    private static SceneDocumentEntry ToEntry(TiledObject placed, TiledLayer layer, int? layerZIndex, Tileset[] tilesets, string mapDirectory, string assetRoot)
    {
        if (placed.Template is { } template)
        {
            throw new FormatException($"the object is an instance of template '{template}', and Capsule reads no templates; detach the object from its template in Tiled.");
        }

        if (string.IsNullOrWhiteSpace(placed.Type))
        {
            throw new FormatException("the object has no Class; give it the Class of the entity it places.");
        }

        RequirePlaceableShape(placed);
        TiledProperties properties = new(placed.Properties);
        EntitySpawn spawn = new(new Vector2((float)placed.X, (float)placed.Y))
        {
            Rotation = float.DegreesToRadians((float)placed.Rotation),
            Scale = placed.Gid is { } gid ? ScaleOf(placed, gid, tilesets) : Vector2.One,
            ZIndex = properties.TakeInt("zIndex") ?? layerZIndex,
            ScrollFactor = layer.ScrollFactor,
        };

        JsonObject members = [];
        if (placed.Gid is null && placed.Width > 0 && placed.Height > 0)
        {
            members["size"] = new JsonArray(placed.Width, placed.Height);
        }

        TiledPoint[]? path = placed.Polygon is { Length: > 0 } polygon ? [.. polygon, polygon[0]] : placed.Polyline;
        if (path is not null)
        {
            members["path"] = new JsonArray([.. path.Select(static point => new JsonArray(point.X, point.Y))]);
        }

        properties.AddTo(members, mapDirectory, assetRoot);

        return new SceneDocumentEntry(placed.Type, spawn, TiledJson.ToElement(members)) { Id = placed.Id };
    }

    private static void RequirePlaceableShape(TiledObject placed)
    {
        if (placed.Text.ValueKind == JsonValueKind.Object)
        {
            throw new FormatException("the object is a text object; replace it with a point, rectangle, ellipse, polyline, polygon or tile object.");
        }

        if (placed.Gid is null && (placed.Width > 0) != (placed.Height > 0))
        {
            throw new FormatException(string.Create(
                CultureInfo.InvariantCulture,
                $"the object is {placed.Width}x{placed.Height}, an extent with no area; give it both a width and a height, or click-place it with neither."));
        }

        // An entity's turn only affects its presentation, so a turned polyline or polygon would lie
        // where Tiled does not draw it.
        if ((placed.Polygon is { Length: > 0 } || placed.Polyline is not null) && placed.Rotation != 0)
        {
            throw new FormatException(string.Create(
                CultureInfo.InvariantCulture,
                $"the {(placed.Polygon is null ? "polyline" : "polygon")} is turned {placed.Rotation} degrees; set its Rotation to 0 and move the points where the turn put them."));
        }
    }

    private static Vector2 ScaleOf(TiledObject placed, uint gid, Tileset[] tilesets)
    {
        if ((gid & TileLayers.OrientationBits) != 0)
        {
            throw new FormatException(
                "the tile object is flipped or rotated, and Capsule flips only the tiles of a tile layer; clear the object's flip in Tiled and face the entity in its own code.");
        }

        Tileset drawn = Tilesets.OwnerOf(gid, tilesets)
            ?? throw new FormatException($"the tile object has gid {gid}, which belongs to no tileset in the map; re-save the map in Tiled.");

        return new Vector2((float)(placed.Width / drawn.TileSize), (float)(placed.Height / drawn.TileSize));
    }
}

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Capsule.Scenes;

namespace JAG.Capsule.Tiled;

internal static class ObjectLayers
{
    internal static SceneDocumentEntry[] ToEntries(TiledLayer layer, Tileset[] tilesets, PropertyScope scope)
    {
        (int? zIndex, bool? unlit) = TiledImporter.Within($"object layer '{layer.Name}'", () =>
        {
            RequireUnblended(layer);
            TiledProperties properties = new(layer.Properties);
            if (properties.TakeBool("collider") is true)
            {
                throw new FormatException("the layer sets 'collider', which only a tile layer reads, and an object collides as its class does; remove the property.");
            }

            (int?, bool?) defaults = (properties.TakeInt("zIndex"), properties.TakeBool("unlit"));
            properties.RefuseEntryKeys();

            return defaults;
        });

        return [.. (layer.Objects ?? []).Select(placed => TiledImporter.Within(
            $"object {placed.Id} on layer '{layer.Name}'",
            () => ToEntry(placed, layer, zIndex, unlit, tilesets, scope)))];
    }

    // An object's own code replaces its tint rather than multiplying into it, the roots it spawns later inherit nothing,
    // and a tint dims its lights and lines. A layer-wide blend would preview in Tiled and not reach the game.
    private static void RequireUnblended(TiledLayer layer)
    {
        const string Reason = "an object's own code replaces its tint, what it spawns inherits none, and a tint dims its lights and lines";
        if (layer.Opacity != 1)
        {
            throw new FormatException(string.Create(
                CultureInfo.InvariantCulture,
                $"the layer's Opacity is {layer.Opacity}, and Capsule fades no object layer: {Reason}; set it to 1 and fade the objects in the game."));
        }

        if (layer.TintColor is { } tint)
        {
            throw new FormatException(
                $"the layer's Tint Color is '{tint}', and Capsule tints no object layer: {Reason}; clear it and colour the objects in the game.");
        }
    }

    private static SceneDocumentEntry ToEntry(
        TiledObject placed, TiledLayer layer, int? layerZIndex, bool? layerUnlit, Tileset[] tilesets, PropertyScope scope)
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
        EntitySpawn spawn = new(layer.Offset + new Vector2((float)placed.X, (float)placed.Y))
        {
            Name = TiledImporter.NameOf(placed.Name),
            Rotation = float.DegreesToRadians((float)placed.Rotation),
            Scale = placed.Gid is { } gid ? ScaleOf(placed, gid, tilesets) : Vector2.One,
            ZIndex = properties.TakeInt("zIndex") ?? layerZIndex,
            ScrollFactor = layer.ScrollFactor,
            Unlit = properties.TakeBool("unlit") ?? layerUnlit,
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

        properties.AddToEntry(members, scope);

        return TiledImporter.Checked(new SceneDocumentEntry(placed.Type, spawn, TiledJson.ToElement(members)) { Id = placed.Id });
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

using System.Text.Json.Nodes;
using Capsule.Build;
using Capsule.Scenes.Documents;

namespace JAG.Capsule.Tiled;

/// <summary>Imports each Tiled map under the asset root into the scene document beside it.</summary>
/// <example>
/// The game's build project adds the importer in its <c>Program.cs</c>:
/// <code>
/// return CapsuleBuild.Configure(args)
///     .AddImporter(new TiledImporter())
///     .WithTileSize(16)
///     .Run();
/// </code>
/// </example>
public sealed class TiledImporter : IAssetImporter
{
    /// <summary>The map extension, <c>.tmj</c>.</summary>
    public IReadOnlyList<string> Extensions { get; } = [".tmj"];

    /// <summary>Imports one map into one scene document.</summary>
    /// <exception cref="FormatException">The map or a tileset it names falls outside the supported Tiled subset.</exception>
    public void Import(AssetImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TiledMap map = TiledJson.Read<TiledMap>(context.ReadAllText(context.SourcePath));
        RequireSupported(map, context.TileSize);

        Tileset[] tilesets = Tilesets.Load(map, context);
        string mapDirectory = Path.GetDirectoryName(Path.GetFullPath(context.SourcePath))!;
        List<SceneDocumentEntry> entries = [];
        foreach (TiledLayer layer in map.Layers)
        {
            switch (layer.Type)
            {
                case "tilelayer":
                    entries.Add(TileLayers.ToEntry(layer, map, tilesets));
                    break;
                case "objectgroup":
                    entries.AddRange(ObjectLayers.ToEntries(layer, tilesets, mapDirectory, context.AssetRoot));
                    break;
                default:
                    throw new FormatException(
                        $"layer '{layer.Name}' has type '{layer.Type}'; Capsule imports tile layers and object layers only. Remove it or move its content to one of those.");
            }
        }

        TiledProperties properties = new(map.Properties);
        string? baseScene = properties.TakeString(SceneDocumentKeys.Document.BaseScene);
        JsonObject members = [];
        if (map.BackgroundColor is { } background)
        {
            members["clearColor"] = TiledProperties.Color(background);
        }

        if (Camera(map, properties.TakeString("camera")) is { } camera)
        {
            members["camera"] = camera;
        }

        properties.AddTo(members, mapDirectory, context.AssetRoot, "clearColor");

        SceneDocument scene;
        try
        {
            scene = new SceneDocument(entries, TiledJson.ToElement(members), baseScene);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"the map imports to an invalid scene: {ex.Message}", ex);
        }

        context.Write(Path.ChangeExtension(context.AssetPath, ".scene.json"), scene.ToJson());
    }

    internal static T Within<T>(string context, Func<T> import)
    {
        try
        {
            return import();
        }
        catch (FormatException ex)
        {
            throw new FormatException($"{context}: {ex.Message}", ex);
        }
    }

    internal static string AssetPathOf(string path, string directory, string assetRoot)
    {
        string root = Path.GetFullPath(assetRoot);
        string full = Path.GetFullPath(Path.Combine(directory, path));
        string below = Path.GetRelativePath(root, full);
        if (Path.IsPathRooted(below) || below == ".." || below.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new FormatException($"'{path}' resolves to '{full}', outside the asset root '{root}'; move the file under that root.");
        }

        return below.Replace('\\', '/');
    }

    private static void RequireSupported(TiledMap map, int? tileSize)
    {
        TiledJson.RequireFormat(map.Version);
        if (map.Orientation != "orthogonal")
        {
            throw new FormatException($"the map is {map.Orientation}; Capsule imports orthogonal maps only. Recreate it as an orthogonal map.");
        }

        if (map.Infinite)
        {
            throw new FormatException("the map is infinite; turn off Infinite in Map > Map Properties.");
        }

        if (map.TileWidth != map.TileHeight)
        {
            throw new FormatException(
                $"the map has {map.TileWidth}x{map.TileHeight} tiles; Capsule imports square tiles only. Set Tile Width and Tile Height in Map > Map Properties to one size.");
        }

        if (tileSize is { } declared && map.TileWidth != declared)
        {
            throw new FormatException(
                $"the map has {map.TileWidth}px tiles but the game declares {declared}px; set Map > Map Properties > Tile Width and Tile Height to {declared}, or change WithTileSize in the build project.");
        }

        if (map.Width <= 0 || map.Height <= 0)
        {
            throw new FormatException($"the map is {map.Width}x{map.Height} tiles; give it a size in Map > Resize Map.");
        }
    }

    // Tiled's renderer adds the Parallax Origin to the view centre, so an origin O previews as a
    // scroll centre of -O. A map with a parallax layer writes even 0, 0, because the camera's own
    // default would draw those layers away from where Tiled previews them. Subtracting from zero keeps
    // a zero origin from writing -0.
    private static JsonObject? Camera(TiledMap map, string? type)
    {
        bool scrolls = map.ParallaxOriginX != 0 || map.ParallaxOriginY != 0 || map.Layers.Any(static layer => layer.ScrollFactor is not null);
        if (type is null && !scrolls)
        {
            return null;
        }

        JsonObject camera = [];
        if (type is not null)
        {
            camera["type"] = type;
        }

        if (scrolls)
        {
            camera["scrollCenter"] = new JsonArray(0 - map.ParallaxOriginX, 0 - map.ParallaxOriginY);
        }

        return camera;
    }
}

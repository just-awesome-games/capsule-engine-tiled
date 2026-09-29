using Capsule.Build;
using Capsule.Scenes.Documents;

namespace JAG.Capsule.Tiled;

/// <summary>Imports Tiled maps into Capsule scene documents during a game's asset build.</summary>
/// <remarks>
/// <para>
/// Each <c>.tmj</c> map under the asset root becomes a scene document at the same path with a
/// <c>.scene.json</c> extension. <c>Assets/Scenes/Highway/Room02.tmj</c> becomes the scene
/// keyed <c>scenes/highway/room-02</c>. A map reads its <c>.tsj</c> tilesets from under the asset root,
/// and draws from their images there. An edited tileset imports again only the maps that name it. A
/// map outside the supported Tiled subset, or whose tile size differs from the one
/// <see cref="CapsuleBuild.WithTileSize"/> configures, fails the build naming the map.
/// </para>
/// </remarks>
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
    private const string DocumentExtension = ".scene.json";

    /// <summary>The map extension, <c>.tmj</c>.</summary>
    /// <remarks>Tilesets are read through the maps that name them, and no tileset is imported on its own.</remarks>
    public IReadOnlyList<string> Extensions { get; } = [".tmj"];

    /// <summary>Imports one map into one scene document.</summary>
    /// <exception cref="FormatException">The map or a tileset it names falls outside the supported Tiled subset.</exception>
    public void Import(AssetImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        SceneDocument document = MapImporter.Import(
            context.SourcePath, context.AssetRoot, context.TileSize, context.ReadAllBytes, context.Exists);
        context.Write(Path.ChangeExtension(context.AssetPath, DocumentExtension), SceneDocumentFile.ToJson(document));
    }
}

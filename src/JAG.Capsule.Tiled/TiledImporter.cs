using Capsule.Build;
using Capsule.Scenes.Documents;

namespace JAG.Capsule.Tiled;

/// <summary>Imports Tiled maps into Capsule scene documents during a game's asset build.</summary>
/// <remarks>
/// <para>
/// Each <c>.tmj</c> map under the asset root becomes a scene document at the same path with a
/// <c>.scene.json</c> extension. <c>Assets/Scenes/Highway/Room02.tmj</c> becomes the scene
/// keyed <c>scenes/highway/room-02</c>. A map reads its <c>.tsj</c> tilesets and their images from under
/// the asset root. A map outside the supported Tiled subset, or whose tile size differs from the one
/// <see cref="CapsuleBuild.WithTileSize"/> configures, fails the build naming the map.
/// </para>
/// <para>
/// The first map a build imports seeds a Tiled project, named for the logic project's directory, at the
/// asset root while no <c>.tiled-project</c> file exists anywhere under it. The project carries the
/// <c>CapsuleLayer</c> class. The build never overwrites the file.
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

    private const string ProjectExtension = ".tiled-project";

    // The embedded template's LogicalName in the project file.
    private const string ProjectTemplate = "capsule.tiled-project";

    private bool _projectSeeded;

    /// <summary>The map extension, <c>.tmj</c>.</summary>
    /// <remarks>Tilesets are read through the maps that name them, and no tileset is imported on its own.</remarks>
    public IReadOnlyList<string> Extensions { get; } = [".tmj"];

    /// <summary>Imports one map into one scene document.</summary>
    /// <exception cref="FormatException">The map or a tileset it names falls outside the supported Tiled subset.</exception>
    public void Import(AssetImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // One build is one importer instance, so the asset root is searched once per build.
        if (!_projectSeeded)
        {
            _projectSeeded = true;
            SeedProject(context.AssetRoot);
        }

        SceneDocument document = MapImporter.Import(context.SourcePath, context.AssetRoot, context.TileSize);
        context.Write(Path.ChangeExtension(context.AssetPath, DocumentExtension), SceneDocumentFile.ToJson(document));
    }

    // The seed sits at the asset root so that it holds every map wherever the game files it. The build
    // runs in the logic project's directory, which names the file.
    private static void SeedProject(string assetRoot)
    {
        if (Directory.EnumerateFiles(assetRoot, "*" + ProjectExtension, SearchOption.AllDirectories).Any())
        {
            return;
        }

        string project = Path.Combine(assetRoot, new DirectoryInfo(Environment.CurrentDirectory).Name + ProjectExtension);
        using Stream template = typeof(TiledImporter).Assembly.GetManifestResourceStream(ProjectTemplate)!;
        using FileStream seed = new(project, FileMode.CreateNew, FileAccess.Write);
        template.CopyTo(seed);
    }
}

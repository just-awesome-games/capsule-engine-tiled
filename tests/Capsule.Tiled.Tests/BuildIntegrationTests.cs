using Capsule.Scenes.Documents;

namespace Capsule.Tiled.Tests;

// The whole seam, end to end: this project's asset-sources/scenes/room.tmj went through the
// package's targets into Capsule's scene-document hook, and out the other side as shipped content.
public sealed class BuildIntegrationTests
{
    private static string Shipped(string name) =>
        Path.Combine(AppContext.BaseDirectory, "assets", "scenes", name);

    [Fact]
    public void TheBuildShipsAMapAsACanonicalSceneDocument()
    {
        string path = Shipped("room.scene.json");

        Assert.True(File.Exists(path), $"expected the build to ship {path}");
        Assert.Equal(SceneDocumentFile.ToJson(SceneDocumentFile.Load(path)), File.ReadAllText(path));
    }

    [Fact]
    public void TheShippedDocumentKeepsTheMapAsItsProvenance()
    {
        SceneDocument document = SceneDocumentFile.Load(Shipped("room.scene.json"));

        Assert.Equal(TiledImporter.ToolName, document.Source?.Tool);
        Assert.EndsWith("asset-sources/scenes/room.tmj", document.Source?.Path, StringComparison.Ordinal);
    }
}

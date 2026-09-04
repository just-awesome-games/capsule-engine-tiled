using Capsule.Assets;
using Capsule.Scenes.Documents;

namespace Capsule.Tiled.Tests;

// The whole seam, end to end: this project's asset-sources/scenes maps through the package's
// targets, Capsule's scene-document hook, and out as shipped content.
public sealed class BuildIntegrationTests
{
    private static string Shipped(string key) =>
        Path.Combine(AppContext.BaseDirectory, "assets", "scenes", key + ".scene.json");

    [Theory]
    [InlineData("room")]
    [InlineData("halls/room")]
    public void TheBuildShipsAMapAsACanonicalSceneDocumentAtItsKey(string key)
    {
        string path = Shipped(key);

        Assert.True(File.Exists(path), $"expected the build to ship {path}");
        Assert.Equal(SceneDocumentFile.ToJson(SceneDocumentFile.Load(path)), File.ReadAllText(path));
    }

    [Theory]
    [InlineData("room", "asset-sources/scenes/room.tmj")]
    [InlineData("halls/room", "asset-sources/scenes/halls/room.tmj")]
    public void TheShippedDocumentKeepsItsMapAsItsProvenance(string key, string source)
    {
        SceneDocument document = SceneDocumentFile.Load(Shipped(key));

        Assert.Equal(TiledImporter.ToolName, document.Source?.Tool);
        Assert.EndsWith(source, document.Source?.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedDocumentNamesANestedAtlasByItsPathUnderTextures()
    {
        SceneDocument document = SceneDocumentFile.Load(Shipped("halls/room"));

        Assert.Equal(
            new TextureHandle("terrain/tiles", ".png"),
            document.Entries[0].TileMap!.Value.Grid.Texture);
    }
}

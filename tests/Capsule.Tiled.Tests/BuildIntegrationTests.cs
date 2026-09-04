using Capsule.Assets;
using Capsule.Scenes.Documents;

namespace Capsule.Tiled.Tests;

// The whole seam, end to end: this project's asset-sources/scenes maps went through the package's
// targets into Capsule's scene-document hook, and out the other side as shipped content.
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

    // Two maps of one stem in two directories are two keys, and the pair proves it: neither
    // overwrites the other, and the nested one keeps its directory all the way to the output.
    [Theory]
    [InlineData("room", "asset-sources/scenes/room.tmj")]
    [InlineData("halls/room", "asset-sources/scenes/halls/room.tmj")]
    public void TheShippedDocumentKeepsItsMapAsItsProvenance(string key, string source)
    {
        SceneDocument document = SceneDocumentFile.Load(Shipped(key));

        Assert.Equal(TiledImporter.ToolName, document.Source?.Tool);
        Assert.EndsWith(source, document.Source?.Path, StringComparison.Ordinal);
    }

    // The nested map paints from an atlas at asset-sources/textures/terrain/tiles.png, which is the
    // texture name the document carries.
    [Fact]
    public void TheShippedDocumentNamesANestedAtlasByItsPathUnderTextures()
    {
        SceneDocument document = SceneDocumentFile.Load(Shipped("halls/room"));

        Assert.Equal(
            new TextureHandle("terrain/tiles", ".png"),
            document.Entries[0].TileMap!.Value.Grid.Texture);
    }
}

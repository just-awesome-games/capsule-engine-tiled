namespace Capsule.Tiled.Tests;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class TiledSceneToolTests
{
    [Fact]
    public void Import_WritesEachDocumentAtTheKeyItsMapClaims()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room", "halls/room");
        StringWriter output = new();
        StringWriter error = new();

        int exit = TiledSceneTool.Import(
            "out",
            [new TiledSource("room", "room.tmj"), new TiledSource("halls/room", "halls/room.tmj")],
            tileSize: null,
            output,
            error);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        Assert.True(File.Exists("out/room.scene.json"));
        Assert.True(File.Exists("out/halls/room.scene.json"));
    }

    [Fact]
    public void Import_RefusesTwoMapsClaimingOneKey()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room", "halls/room");
        StringWriter error = new();

        int exit = TiledSceneTool.Import(
            "out",
            [new TiledSource("room", "room.tmj"), new TiledSource("room", "halls/room.tmj")],
            tileSize: null,
            new StringWriter(),
            error);

        Assert.Equal(1, exit);
        Assert.Contains("halls/room.tmj: would overwrite", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("keys must be unique", error.ToString(), StringComparison.Ordinal);
    }
}

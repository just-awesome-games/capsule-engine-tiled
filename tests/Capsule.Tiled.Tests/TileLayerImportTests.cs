using Capsule.Collision;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Tiles;

namespace Capsule.Tiled.Tests;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class TileLayerImportTests
{
    [Fact]
    public void Import_ReadsATilesLayerProperty()
    {
        SceneDocument document = ImportWithTileProperty(
            "{\"name\":\"layer\",\"type\":\"string\",\"value\":\" solid \"},");

        Assert.Equal("solid", Palette(document)[1].Layer);
        Assert.Equal(CellFaces2D.All, Palette(document)[1].CollidableFaces);
        Assert.Null(Palette(document)[2].Layer);
    }

    [Fact]
    public void Import_ReadsATilesCollidableFacesProperty()
    {
        SceneDocument document = ImportWithTileProperty(
            "{\"name\":\"layer\",\"type\":\"string\",\"value\":\"platform\"},{\"name\":\"collidableFaces\",\"type\":\"string\",\"value\":\" top , \"},");

        Assert.Equal(CellFaces2D.Top, Palette(document)[1].CollidableFaces);
    }

    [Fact]
    public void Import_LeavesATileWithNoLayerPropertyCollidingWithNothing()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");

        SceneDocument document = TiledImporter.Import("room.tmj");

        Assert.All(Palette(document).ToArray(), definition => Assert.Null(definition.Layer));
    }

    [Fact]
    public void Import_RejectsATileStillCarryingACollisionProperty()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty("{\"name\":\"collision\",\"type\":\"string\",\"value\":\"box\"},"));

        Assert.Contains("no longer reads", error.Message, StringComparison.Ordinal);
        Assert.Contains("'layer'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'collidableFaces'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsALayerPropertyNamingMoreThanOneLayer()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty("{\"name\":\"layer\",\"type\":\"string\",\"value\":\"solid,platform\"},"));

        Assert.Contains("naming 2 layers", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsACollidableFacesPropertyThatSpellsSomethingElse()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty(
                "{\"name\":\"layer\",\"type\":\"string\",\"value\":\"platform\"},{\"name\":\"collidableFaces\",\"type\":\"string\",\"value\":\"sideways\"},"));

        Assert.Contains("naming 'sideways'", error.Message, StringComparison.Ordinal);
        Assert.Contains("left, right, top, bottom", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsCollidableFacesOnATileWithNoLayer()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty("{\"name\":\"collidableFaces\",\"type\":\"string\",\"value\":\"top\"},"));

        Assert.Contains("no 'layer'", error.Message, StringComparison.Ordinal);
    }

    // Present and naming nothing is an authoring mistake, not a default: read as absent, an empty
    // collidableFaces would silently ship a solid tile.
    [Theory]
    [InlineData("")]
    [InlineData(" , , ")]
    public void Import_RejectsACollidableFacesPropertyThatNamesNothing(string authored)
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty(
                $"{{\"name\":\"layer\",\"type\":\"string\",\"value\":\"platform\"}},{{\"name\":\"collidableFaces\",\"type\":\"string\",\"value\":\"{authored}\"}},"));

        Assert.Contains("naming nothing", error.Message, StringComparison.Ordinal);
        Assert.Contains("remove the property", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" , , ")]
    public void AnEmptyCollidableFacesPropertyWithNoLayer_StillReachesTheFacesWithoutLayerRefusal(string authored)
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty(
                $"{{\"name\":\"collidableFaces\",\"type\":\"string\",\"value\":\"{authored}\"}},"));

        Assert.Contains("no 'layer'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" , ")]
    public void Import_RejectsALayerPropertyThatNamesNothing(string authored)
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty(
                $"{{\"name\":\"layer\",\"type\":\"string\",\"value\":\"{authored}\"}},"));

        Assert.Contains("naming nothing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsALayerPropertyNotDeclaredAsAString()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty("{\"name\":\"layer\",\"type\":\"file\",\"value\":\"solid\"},"));

        Assert.Contains("tileset 'terrain' tile 0", error.Message, StringComparison.Ordinal);
        Assert.Contains("Class 'ground'", error.Message, StringComparison.Ordinal);
        Assert.Contains("as a 'file' property", error.Message, StringComparison.Ordinal);
    }

    private static SceneDocument ImportWithTileProperty(string property)
    {
        // The fixture's first tile carries no properties of its own, so this becomes its whole list.
        string tileset = SceneDocumentFixtures.Read("tiles.tsj").Replace(
            "\"id\":0,\n         \"type\":\"ground\"",
            $"\"id\":0,\n         \"properties\":[{property.TrimEnd(',')}],\n         \"type\":\"ground\"",
            StringComparison.Ordinal);

        Assert.NotEqual(SceneDocumentFixtures.Read("tiles.tsj"), tileset);

        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", tileset);

        return TiledImporter.Import(workspace.Write("room.tmj", SceneDocumentFixtures.Read("room.tmj")));
    }

    private static ReadOnlySpan<TileDefinition> Palette(SceneDocument document) =>
        document.Entries[0].TileMap!.Value.Grid.TileTypes;
}

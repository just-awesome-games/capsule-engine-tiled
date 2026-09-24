using Capsule.Scenes.Documents;
using Capsule.Tiles;

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
        Assert.False(Palette(document)[1].OneWay);
        Assert.Null(Palette(document)[2].Layer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Import_ReadsATilesOneWayAndSolidSidesProperties(bool solidSides)
    {
        SceneDocument document = ImportWithTileProperty(
            "{\"name\":\"layer\",\"type\":\"string\",\"value\":\"platform\"},{\"name\":\"oneWay\",\"type\":\"bool\",\"value\":true},"
            + (solidSides ? "{\"name\":\"solidSides\",\"type\":\"bool\",\"value\":true}," : string.Empty));

        Assert.True(Palette(document)[1].OneWay);
        Assert.Equal(solidSides, Palette(document)[1].SolidSides);
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
    }

    [Fact]
    public void Import_RejectsATileStillCarryingACollidableFacesProperty()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty(
                "{\"name\":\"layer\",\"type\":\"string\",\"value\":\"platform\"},{\"name\":\"collidableFaces\",\"type\":\"string\",\"value\":\"top\"},"));

        Assert.Contains("'collidableFaces' property, which Capsule no longer reads", error.Message, StringComparison.Ordinal);
        Assert.Contains("'oneWay'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsALayerPropertyNamingMoreThanOneLayer()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty("{\"name\":\"layer\",\"type\":\"string\",\"value\":\"solid,platform\"},"));

        Assert.Contains("naming 2 layers", error.Message, StringComparison.Ordinal);
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

using System.Numerics;
using Capsule.Physics;
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

    [Fact]
    public void Import_TakesACollisionPolygonAsTheTilesShapeOffsetByItsObject()
    {
        // Tiled places a polygon object at its first point and writes every point relative to it.
        Shape2D shape = Palette(ImportWithCollision(
            "{\"id\":1,\"x\":0,\"y\":16,\"polygon\":[{\"x\":0,\"y\":0},{\"x\":16,\"y\":-16},{\"x\":16,\"y\":0}]}"))[1].Shape!.Value;

        Vector2[] points = [.. Enumerable.Range(0, shape.PointCount).Select(shape.Point)];
        Assert.Equal(3, points.Length);
        Assert.Contains(new Vector2(0, 16), points);
        Assert.Contains(new Vector2(16, 0), points);
        Assert.Contains(new Vector2(16, 16), points);
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(0, false)]
    public void Import_TakesACollisionRectangleAsItsCornersAndOneCoveringTheTileAsTheWholeTile(int top, bool shaped)
    {
        TileDefinition ground = Palette(ImportWithCollision(
            $"{{\"id\":1,\"x\":0,\"y\":{top},\"width\":16,\"height\":{16 - top}}}"))[1];

        Assert.Equal(shaped, ground.Shape is not null);
        if (ground.Shape is { } shape)
        {
            Assert.Equal(new Vector2(0, top), shape.Bounds.Min);
            Assert.Equal(new Vector2(16, 16), shape.Bounds.Max);
        }
    }

    [Fact]
    public void Import_RejectsMoreThanOneCollisionObject()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(() => ImportWithCollision(
            "{\"id\":1,\"width\":8,\"height\":8},{\"id\":2,\"x\":8,\"width\":8,\"height\":8}"));

        Assert.Contains("tileset 'terrain' tile 0 (Class 'ground') has 2 objects in its collision", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsACollisionEllipse()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(() => ImportWithCollision(
            "{\"id\":1,\"width\":16,\"height\":16,\"ellipse\":true}"));

        Assert.Contains("tileset 'terrain' tile 0 (Class 'ground') collides as an ellipse", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsANonConvexCollisionPolygonNamingTheTile()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(() => ImportWithCollision(
            "{\"id\":1,\"polygon\":[{\"x\":0,\"y\":0},{\"x\":16,\"y\":8},{\"x\":0,\"y\":16},{\"x\":4,\"y\":8}]}"));

        Assert.Contains("tileset 'terrain' tile 0 (Class 'ground') has a collision shape Capsule cannot collide as", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(0)]
    public void Import_RejectsACollisionShapeOnATileWithNoLayerNamingTheTile(int top)
    {
        TiledImportException error = Assert.Throws<TiledImportException>(() => ImportWithCollision(
            $"{{\"id\":1,\"y\":{top},\"width\":16,\"height\":{16 - top}}}",
            layer: false));

        Assert.Contains("tileset 'terrain' tile 0 (Class 'ground') has a collision shape but no 'layer' property", error.Message, StringComparison.Ordinal);
    }

    // The fixture's first tile, drawn in the Tile Collision Editor as these objects.
    private static SceneDocument ImportWithCollision(string objects, bool layer = true) => ImportWithTileProperty(
        layer ? "{\"name\":\"layer\",\"type\":\"string\",\"value\":\"solid\"}" : string.Empty,
        $"\"objectgroup\":{{\"draworder\":\"index\",\"objects\":[{objects}],\"type\":\"objectgroup\",\"x\":0,\"y\":0}},");

    private static SceneDocument ImportWithTileProperty(string property, string members = "")
    {
        // The fixture's first tile carries no properties of its own, so this becomes its whole list.
        string tileset = SceneDocumentFixtures.Read("tiles.tsj").Replace(
            "\"id\":0,\n         \"type\":\"ground\"",
            $"\"id\":0,\n         {members}\"properties\":[{property.TrimEnd(',')}],\n         \"type\":\"ground\"",
            StringComparison.Ordinal);

        Assert.NotEqual(SceneDocumentFixtures.Read("tiles.tsj"), tileset);

        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", tileset);

        return TiledImporter.Import(workspace.Write("room.tmj", SceneDocumentFixtures.Read("room.tmj")));
    }

    private static ReadOnlySpan<TileDefinition> Palette(SceneDocument document) =>
        document.Entries[0].TileMap!.Value.Grid.TileTypes;
}

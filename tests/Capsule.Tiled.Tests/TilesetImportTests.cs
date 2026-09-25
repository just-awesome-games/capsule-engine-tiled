using System.Numerics;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Scenes.Documents;
using Capsule.Tiles;

namespace Capsule.Tiled.Tests;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class TilesetImportTests
{
    [Fact]
    public void Import_PutsUnpaintedClassesInThePaletteInTileIdOrder()
    {
        using TiledFixtures.Workspace workspace = TiledFixtures.CopyTiledSources("room");

        SceneDocument document = TiledImporter.Import("room.tmj", ".");

        string[] types = [.. TiledFixtures.TileMapOf(document).Grid.TileTypes.ToArray().Select(static definition => definition.Type)];

        Assert.Equal(["empty", "ground", "wall", "ledge", "hazard"], types);
    }

    [Fact]
    public void Import_TakesEachTilesCellFromItsTiledTileId()
    {
        using TiledFixtures.Workspace workspace = TiledFixtures.CopyTiledSources("room");

        SceneDocument document = TiledImporter.Import("room.tmj", ".");

        Assert.Equal(new TextureHandle("Textures/tiles", ".png"), TiledFixtures.TileMapOf(document).Grid.Texture);
        Assert.Equal(4, TiledFixtures.TileMapOf(document).Grid.Columns);

        Assert.Contains(
            "\"texture\": \"Textures/tiles.png\"",
            SceneDocumentFile.ToJson(document),
            StringComparison.Ordinal);
        Assert.Equal(
            [null, 0, 1, 2, 3],
            TiledFixtures.TileMapOf(document).Grid.TileTypes.ToArray().Select(static definition => definition.Cell));
    }

    [Theory]
    [InlineData("\"image\":\"Textures\\/tiles.png\",", "\"image\":\"\",", "tileset 'terrain' is a collection of images")]
    [InlineData("\"columns\":4,", "\"columns\":0,", "tileset 'terrain' declares 0 columns")]
    [InlineData("\"columns\":4,", "\"columns\":3,", "3 columns of 16px over a 64px image")]
    [InlineData("\"tileheight\":16,", "\"tileheight\":8,", "tileset 'terrain' has 16x8 tiles")]
    [InlineData("\"type\":\"ledge\"", "\"type\":\"wall\"", "more than one tile")]
    [InlineData("\"type\":\"ledge\"", "\"type\":\"empty\"", "reserved")]
    public void Import_RefusesATilesetItCannotRepresent(string from, string to, string expected)
    {
        TiledImportException error = TiledFixtures.ImportFailure(
            TiledFixtures.Read("room.tmj"),
            TiledFixtures.Mutate(TiledFixtures.Read("tiles.tsj"), from, to));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RefusesAnImageOutsideTheAssetRoot()
    {
        using TiledFixtures.Workspace workspace = TilesetAtlas("..\\/art\\/tiles.png");

        TiledImportException error = Assert.Throws<TiledImportException>(
            () => TiledImporter.Import("assets/scenes/room.tmj", "assets"));

        Assert.Contains("move the image under that root", error.Message, StringComparison.Ordinal);
    }

    // An image anywhere under the asset root is named by its path there.
    [Theory]
    [InlineData("Textures\\/terrain\\/cave.png", "Textures/terrain/cave")]
    [InlineData("art\\/terrain\\/cave.png", "art/terrain/cave")]
    public void Import_NamesANestedAtlasByItsPathUnderTheAssetRoot(string image, string key)
    {
        using TiledFixtures.Workspace workspace = TilesetAtlas(image);

        SceneDocument document = TiledImporter.Import("assets/scenes/room.tmj", "assets");

        Assert.Equal(new TextureHandle(key, ".png"), TiledFixtures.TileMapOf(document).Grid.Texture);
        Assert.Contains(
            $"\"texture\": \"{key}.png\"",
            SceneDocumentFile.ToJson(document),
            StringComparison.Ordinal);
    }

    // A map under assets/scenes drawing its tileset from assets/, whose atlas the caller names.
    private static TiledFixtures.Workspace TilesetAtlas(string image)
    {
        TiledFixtures.Workspace workspace = new();
        Directory.CreateDirectory("assets/scenes");
        workspace.Write("assets/tiles.tsj", TiledFixtures.Mutate(
            TiledFixtures.Read("tiles.tsj"),
            "\"image\":\"Textures\\/tiles.png\"",
            $"\"image\":\"{image}\""));
        workspace.Write(
            "assets/scenes/room.tmj",
            TiledFixtures.Mutate(TiledFixtures.Read("room.tmj"), "\"source\":\"tiles.tsj\"", "\"source\":\"../tiles.tsj\""));

        return workspace;
    }

    [Fact]
    public void Import_SourceHashChangesWhenAnExternalTilesetChanges()
    {
        using TiledFixtures.Workspace workspace = new();
        workspace.Write("room.tmj", TiledFixtures.Read("room.tmj"));
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        string first = TiledImporter.Import("room.tmj", ".").Source!.Value.Hash;

        string changed = TiledFixtures.Mutate(TiledFixtures.Read("tiles.tsj"), "\"tilecount\":4", "\"tilecount\":8");
        workspace.Write("tiles.tsj", changed);
        string second = TiledImporter.Import("room.tmj", ".").Source!.Value.Hash;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Import_AcceptsAnExternalTilesetWithinTheTrackedRoot()
    {
        using TiledFixtures.Workspace workspace = new();
        Directory.CreateDirectory("assets/scenes");
        workspace.Write("assets/tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        string map = TiledFixtures.Mutate(TiledFixtures.Read("room.tmj"), "\"source\":\"tiles.tsj\"", "\"source\":\"../tiles.tsj\"");
        workspace.Write("assets/scenes/room.tmj", map);

        SceneDocument imported = TiledImporter.Import("assets/scenes/room.tmj", "assets");

        Assert.Equal("ground", TiledFixtures.TileMapOf(imported).Grid.TileTypes[1].Type);
    }

    [Fact]
    public void Import_RejectsAnExternalTilesetOutsideTheTrackedRoot()
    {
        using TiledFixtures.Workspace workspace = new();
        Directory.CreateDirectory("assets/scenes");
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        string map = TiledFixtures.Mutate(TiledFixtures.Read("room.tmj"), "\"source\":\"tiles.tsj\"", "\"source\":\"../../tiles.tsj\"");
        workspace.Write("assets/scenes/room.tmj", map);

        TiledImportException error = Assert.Throws<TiledImportException>(
            () => TiledImporter.Import("assets/scenes/room.tmj", "assets"));

        Assert.Contains("outside the asset root", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_ReadsATilesLayerProperty()
    {
        SceneDocument document = ImportWithTileProperty(
            "{\"name\":\"layer\",\"type\":\"string\",\"value\":\" solid \"},");

        Assert.Equal("solid", TiledFixtures.Palette(document)[1].Layer);
        Assert.False(TiledFixtures.Palette(document)[1].OneWay);
        Assert.Null(TiledFixtures.Palette(document)[2].Layer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Import_ReadsATilesOneWayAndSolidSidesProperties(bool solidSides)
    {
        SceneDocument document = ImportWithTileProperty(
            "{\"name\":\"layer\",\"type\":\"string\",\"value\":\"platform\"},{\"name\":\"oneWay\",\"type\":\"bool\",\"value\":true},"
            + (solidSides ? "{\"name\":\"solidSides\",\"type\":\"bool\",\"value\":true}," : string.Empty));

        Assert.True(TiledFixtures.Palette(document)[1].OneWay);
        Assert.Equal(solidSides, TiledFixtures.Palette(document)[1].SolidSides);
    }

    [Fact]
    public void Import_LeavesATileWithNoLayerPropertyCollidingWithNothing()
    {
        using TiledFixtures.Workspace workspace = TiledFixtures.CopyTiledSources("room");

        SceneDocument document = TiledImporter.Import("room.tmj", ".");

        Assert.All(TiledFixtures.Palette(document).ToArray(), definition => Assert.Null(definition.Layer));
    }

    [Theory]
    [InlineData("")]
    public void Import_RejectsALayerPropertyThatNamesNothing(string authored)
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty(
                $"{{\"name\":\"layer\",\"type\":\"string\",\"value\":\"{authored}\"}},"));

        Assert.Contains("has 'layer' of ''", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsALayerPropertyNotDeclaredAsAString()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty("{\"name\":\"layer\",\"type\":\"file\",\"value\":\"solid\"},"));

        Assert.Contains("tileset 'terrain' tile 0", error.Message, StringComparison.Ordinal);
        Assert.Contains("Class 'ground'", error.Message, StringComparison.Ordinal);
        Assert.Contains("has 'layer' of type file", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_TakesACollisionPolygonAsTheTilesShapeOffsetByItsObject()
    {
        // Tiled places a polygon object at its first point and writes every point relative to it.
        Shape2D shape = TiledFixtures.Palette(ImportWithCollision(
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
        TileDefinition ground = TiledFixtures.Palette(ImportWithCollision(
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
        string tileset = TiledFixtures.Read("tiles.tsj").Replace(
            "\"id\":0,\n         \"type\":\"ground\"",
            $"\"id\":0,\n         {members}\"properties\":[{property.TrimEnd(',')}],\n         \"type\":\"ground\"",
            StringComparison.Ordinal);

        Assert.NotEqual(TiledFixtures.Read("tiles.tsj"), tileset);

        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", tileset);

        return TiledImporter.Import(workspace.Write("room.tmj", TiledFixtures.Read("room.tmj")), ".");
    }
}

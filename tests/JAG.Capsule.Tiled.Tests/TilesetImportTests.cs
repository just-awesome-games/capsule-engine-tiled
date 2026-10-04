using System.Text.Json;
using Capsule.Scenes.Documents;

namespace JAG.Capsule.Tiled.Tests;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class TilesetImportTests
{
    [Fact]
    public void Import_PutsUnpaintedClassesInThePaletteInTileIdOrder()
    {
        using TiledFixtures.Workspace workspace = TiledFixtures.CopyTiledSources("room");

        SceneDocument document = MapImporter.Import("room.tmj", ".");

        Assert.Equal(
            ["empty", "ground", "wall", "ledge", "hazard"],
            TiledFixtures.Palette(document).Select(static tileType => tileType.GetProperty("name").GetString()));
    }

    [Fact]
    public void Import_TakesEachTilesCellFromItsTiledTileId()
    {
        using TiledFixtures.Workspace workspace = TiledFixtures.CopyTiledSources("room");

        SceneDocument document = MapImporter.Import("room.tmj", ".");

        Assert.Equal("Textures/tiles.png", TiledFixtures.TileMapOf(document).GetProperty("texture").GetString());
        Assert.Equal(4, TiledFixtures.TileMapOf(document).GetProperty("columns").GetInt32());
        Assert.Equal(
            [null, 0, 1, 2, 3],
            TiledFixtures.Palette(document).Select(static tileType => tileType.TryGetProperty("cell", out JsonElement cell) ? cell.GetInt32() : (int?)null));
    }

    [Theory]
    [InlineData("\"image\":\"Textures\\/tiles.png\",", "\"image\":\"\",", "tileset 'terrain' is a collection of images")]
    [InlineData("\"columns\":4,", "\"columns\":3,", "3 columns of 16px over a 64px image")]
    [InlineData("\"tileheight\":16,", "\"tileheight\":8,", "tileset 'terrain' has 16x8 tiles")]
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
            () => MapImporter.Import("assets/scenes/room.tmj", "assets"));

        Assert.Contains("move the image under that root", error.Message, StringComparison.Ordinal);
    }

    // An image anywhere under the asset root is named by its path there.
    [Theory]
    [InlineData("Textures\\/terrain\\/cave.png", "Textures/terrain/cave.png")]
    [InlineData("art\\/terrain\\/cave.png", "art/terrain/cave.png")]
    public void Import_NamesANestedAtlasByItsPathUnderTheAssetRoot(string image, string texture)
    {
        using TiledFixtures.Workspace workspace = TilesetAtlas(image);

        SceneDocument document = MapImporter.Import("assets/scenes/room.tmj", "assets");

        Assert.Equal(texture, TiledFixtures.TileMapOf(document).GetProperty("texture").GetString());
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
    public void Import_AcceptsAnExternalTilesetWithinTheTrackedRoot()
    {
        using TiledFixtures.Workspace workspace = new();
        Directory.CreateDirectory("assets/scenes");
        workspace.Write("assets/tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        string map = TiledFixtures.Mutate(TiledFixtures.Read("room.tmj"), "\"source\":\"tiles.tsj\"", "\"source\":\"../tiles.tsj\"");
        workspace.Write("assets/scenes/room.tmj", map);

        SceneDocument imported = MapImporter.Import("assets/scenes/room.tmj", "assets");

        Assert.Equal("ground", TiledFixtures.Palette(imported)[1].GetProperty("name").GetString());
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
            () => MapImporter.Import("assets/scenes/room.tmj", "assets"));

        Assert.Contains("outside the asset root", error.Message, StringComparison.Ordinal);
    }

    // A layer padded with whitespace would never match the layer a mover collides with.
    [Fact]
    public void Import_TrimsATilesLayerProperty()
    {
        SceneDocument document = ImportWithTileProperty(
            "{\"name\":\"layer\",\"type\":\"string\",\"value\":\" solid \"},");

        Assert.Equal("solid", TiledFixtures.Palette(document)[1].GetProperty("layer").GetString());
    }

    // The Class stays the palette entry's name, a type property names its subclass, and every other
    // property sets the member of its name beside them. Tiles authoring nothing further write nothing more.
    [Fact]
    public void Import_WritesATilesPropertiesAsMembersOfItsTileType()
    {
        SceneDocument document = ImportWithTileProperty(
            "{\"name\":\"type\",\"type\":\"string\",\"value\":\"ice\"},{\"name\":\"grip\",\"type\":\"float\",\"value\":0.1},");

        JsonElement[] palette = TiledFixtures.Palette(document);
        Assert.Equal("""{"name":"ground","cell":0,"type":"ice","grip":0.1}""", palette[1].GetRawText());
        Assert.Equal("""{"name":"wall","cell":1}""", palette[2].GetRawText());
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
        JsonElement ground = TiledFixtures.Palette(ImportWithCollision(
            "{\"id\":1,\"x\":0,\"y\":16,\"polygon\":[{\"x\":0,\"y\":0},{\"x\":16,\"y\":-16},{\"x\":16,\"y\":0}]}"))[1];

        Assert.Equal("[[0,16],[16,0],[16,16]]", ground.GetProperty("shape").GetRawText());
    }

    [Theory]
    [InlineData(8, "[[0,8],[16,8],[16,16],[0,16]]")]
    [InlineData(0, null)]
    public void Import_TakesACollisionRectangleAsItsCornersAndOneCoveringTheTileAsTheWholeTile(int top, string? shape)
    {
        JsonElement ground = TiledFixtures.Palette(ImportWithCollision(
            $"{{\"id\":1,\"x\":0,\"y\":{top},\"width\":16,\"height\":{16 - top}}}"))[1];

        Assert.Equal(shape, ground.TryGetProperty("shape", out JsonElement written) ? written.GetRawText() : null);
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

    // A whole-tile rectangle writes no shape, so only the importer sees that the tile meant to collide.
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

        return MapImporter.Import(workspace.Write("room.tmj", TiledFixtures.Read("room.tmj")), ".");
    }
}

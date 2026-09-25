using Capsule.Rendering;
using Capsule.Scenes.Documents;

namespace Capsule.Tiled.Tests;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class MapImportTests
{
    [Fact]
    public void Import_ReproducesTheCommittedDocumentByteForByte()
    {
        using TiledFixtures.Workspace workspace = TiledFixtures.CopyTiledSources("room");

        SceneDocument document = TiledImporter.Import("room.tmj", ".");

        Assert.Equal(TiledFixtures.Read("room.scene.json"), SceneDocumentFile.ToJson(document));
    }

    [Fact]
    public void Import_RejectsAMapWhoseTileSizeIsNotTheDeclaredOne()
    {
        using TiledFixtures.Workspace workspace = TiledFixtures.CopyTiledSources("room");

        TiledImportException error = Assert.Throws<TiledImportException>(
            () => TiledImporter.Import("room.tmj", ".", tileSize: 8));

        Assert.Contains("has 16px tiles", error.Message, StringComparison.Ordinal);
        Assert.Contains("declares 8px", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_WithNoDeclaredTileSize_TakesTheMapsOwn()
    {
        string map = TiledFixtures.Mutate(TiledFixtures.Read("room.tmj"), "\"tileheight\":16", "\"tileheight\":8");
        map = TiledFixtures.Mutate(map, "\"tilewidth\":16", "\"tilewidth\":8");

        // The tileset is cut at the map's size too, so its cells stay one grid cell each.
        string tileset = TiledFixtures.Mutate(TiledFixtures.Read("tiles.tsj"), "\"imageheight\":16", "\"imageheight\":8");
        tileset = TiledFixtures.Mutate(tileset, "\"imagewidth\":64", "\"imagewidth\":32");
        tileset = TiledFixtures.Mutate(tileset, "\"tileheight\":16", "\"tileheight\":8");
        tileset = TiledFixtures.Mutate(tileset, "\"tilewidth\":16", "\"tilewidth\":8");

        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", tileset);

        Assert.Equal(8, TiledFixtures.TileMapOf(TiledImporter.Import(workspace.Write("room.tmj", map), ".")).Grid.TileSize);
    }

    [Fact]
    public void Import_ForwardSlashesTheSourcePathItStamps()
    {
        using TiledFixtures.Workspace workspace = TiledFixtures.CopyTiledSources("scenes/room");

        SceneDocument document = TiledImporter.Import(Path.Combine("scenes", "room.tmj"), ".");

        Assert.Equal("scenes/room.tmj", document.Source?.Path);
    }

    [Fact]
    public void Import_RefusesAnAbsoluteSourcePath()
    {
        using TiledFixtures.Workspace workspace = TiledFixtures.CopyTiledSources("room");

        TiledImportException error = Assert.Throws<TiledImportException>(
            () => TiledImporter.Import(Path.GetFullPath("room.tmj"), "."));

        Assert.Contains("must be relative", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"orientation\":\"orthogonal\"", "\"orientation\":\"isometric\"", "orthogonal maps only")]
    [InlineData("\"infinite\":false", "\"infinite\":true", "the map is infinite")]
    [InlineData("\"tileheight\":16", "\"tileheight\":8", "square tiles only")]
    [InlineData("\"type\":\"tilelayer\"", "\"type\":\"imagelayer\"", "unsupported layer type")]
    [InlineData("1, 1, 1, 4]", "1, 1, 1, 2147483649]", "flipped or rotated")]
    [InlineData("1, 1, 1, 4]", "1, 1, 1, 5]", "has no Class")]
    [InlineData("1, 1, 1, 4]", "1, 1, 1, 4, 0]", "requires 12")]
    [InlineData("\"type\":\"coin\"", "\"type\":\"\"", "typed by its Class")]
    [InlineData("\"source\":\"tiles.tsj\"", "\"source\":\"tiles.tsx\"", "XML")]
    [InlineData("\"source\":\"tiles.tsj\"", "\"source\":\"missing.tsj\"", "is missing")]
    [InlineData("\"data\":[0, 0, 0, 0, 1, 1, 2, 0, 1, 1, 1, 4],", "\"data\":\"AAAA\",\"encoding\":\"base64\",", "CSV")]
    [InlineData("\"infinite\":false", "\"backgroundcolor\":\"#80101820\",\"infinite\":false", "the map has 'Background Color' of '#80101820'")]
    [InlineData("\"version\":\"1.10\"", "\"version\":\"1.9\"", "re-save it with Tiled 1.10 or later")]
    public void Import_RejectsWhatItCannotRepresent(string from, string to, string expected)
    {
        TiledImportException error = TiledFixtures.ImportFailure(
            TiledFixtures.Mutate(TiledFixtures.Read("room.tmj"), from, to),
            TiledFixtures.Read("tiles.tsj"));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RefusesAParallaxOrigin()
    {
        TiledImportException error = TiledFixtures.ImportFailure(
            TiledFixtures.Mutate(
                TiledFixtures.Read("room.tmj"),
                "\"orientation\":\"orthogonal\",",
                "\"orientation\":\"orthogonal\",\"parallaxoriginx\":0,\"parallaxoriginy\":96,"),
            TiledFixtures.Read("tiles.tsj"));

        Assert.Contains("Parallax Origin of (0, 96)", error.Message, StringComparison.Ordinal);
    }

    // Tiled writes an opaque Background Color as #rrggbb and a color property as #aarrggbb.
    [Fact]
    public void Import_CarriesTheMapsSceneSettingsIntoTheDocument()
    {
        string map = TiledFixtures.Mutate(
            TiledFixtures.Read("room.tmj"),
            "\"orientation\":\"orthogonal\",",
            "\"backgroundcolor\":\"#101820\","
                + "\"properties\":["
                + "{\"name\":\"ambient\",\"type\":\"color\",\"value\":\"#ff484c68\"},"
                + "{\"name\":\"baseScene\",\"type\":\"string\",\"value\":\"jag/rooms/room-scene\"},"
                + "{\"name\":\"camera\",\"type\":\"string\",\"value\":\"jag/rooms/room-camera\"},"
                + "{\"name\":\"sampling\",\"type\":\"string\",\"value\":\"point\"}],"
                + "\"orientation\":\"orthogonal\",");

        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", map), ".");

        Assert.Equal(
            new SceneSettings
            {
                BaseScene = "jag/rooms/room-scene",
                Camera = "jag/rooms/room-camera",
                ClearColor = new ColorRgba(16, 24, 32),
                Ambient = new ColorRgba(72, 76, 104),
                Sampling = TextureSampling.Point,
            },
            document.Settings);
    }

    [Theory]
    [InlineData("baseScene", "int", "1", "the map has 'baseScene' of type int")]
    [InlineData("camera", "int", "1", "the map has 'camera' of type int")]
    [InlineData("baseScene", "string", "7", "the map has 'baseScene' of '7'")]
    [InlineData("ambient", "string", "\"#ff484c68\"", "the map has 'ambient' of type string")]
    public void Import_RejectsAMapPropertyOfTheWrongType(string name, string type, string value, string expected)
    {
        TiledImportException error = TiledFixtures.ImportFailure(
            WithMapProperty(TiledFixtures.Read("room.tmj"), name, type, value),
            TiledFixtures.Read("tiles.tsj"));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    // A custom property on the map itself, as Tiled writes one.
    private static string WithMapProperty(string map, string name, string type, string value) => TiledFixtures.Mutate(
        map,
        "\"orientation\":\"orthogonal\",",
        $"\"properties\":[{{\"name\":\"{name}\",\"type\":\"{type}\",\"value\":{value}}}],\"orientation\":\"orthogonal\",");

    [Fact]
    public void Import_RejectsAGridWhoseAreaOverflowsAnInt()
    {
        string map = TiledFixtures.Mutate(TiledFixtures.Read("room.tmj"), "\"width\":4", "\"width\":65536");
        map = TiledFixtures.Mutate(map, "\"height\":3", "\"height\":65536");

        TiledImportException error = TiledFixtures.ImportFailure(map, TiledFixtures.Read("tiles.tsj"));

        Assert.Contains("65536x65536", error.Message, StringComparison.Ordinal);
    }
}

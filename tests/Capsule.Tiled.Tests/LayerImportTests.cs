using System.Numerics;
using Capsule.Assets;
using Capsule.Scenes.Documents;

namespace Capsule.Tiled.Tests;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class LayerImportTests
{
    [Fact]
    public void Import_PreservesMultipleTileAndObjectLayersInAuthoredOrder()
    {
        string authored = TiledFixtures.Read("room.tmj").ReplaceLineEndings("\n");
        string map = TiledFixtures.Mutate(
            authored,
            "        }],\n \"nextlayerid\":3,",
            """
                    },
                    {
                     "data":[0, 0, 0, 0, 1, 1, 2, 0, 1, 1, 1, 4],
                     "height":3,
                     "id":3,
                     "name":"foreground",
                     "type":"tilelayer",
                     "width":4
                    }],
             "nextlayerid":4,
            """);

        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", map), ".");

        Assert.Collection(
            document.Entries.ToArray(),
            entry => Assert.NotNull(entry.TileMap),
            entry => Assert.Equal("player", entry.Entity!.Value.Type),
            entry => Assert.Equal("coin", entry.Entity!.Value.Type),
            entry => Assert.NotNull(entry.TileMap));
        Assert.Equal(8, document.NextEntityId);
    }

    [Fact]
    public void Import_AllowsAnObjectOnlyMap()
    {
        string map = TiledFixtures.Mutate(TiledFixtures.Read("room.tmj"), "\"type\":\"tilelayer\"", "\"type\":\"objectgroup\"");

        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", map), ".");

        Assert.All(document.Entries.ToArray(), entry => Assert.NotNull(entry.Entity));
    }

    [Fact]
    public void Import_ScalesATileObjectByItsBoxOverTheTilesetCell()
    {
        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));

        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", TileObject("\"gid\":1,")), ".");

        EntityPlacement placed = document.Entries.ToArray()[^1].Entity!.Value;

        Assert.Equal("crate", placed.Type);
        Assert.Equal(2f, placed.ScaleX);
        Assert.Equal(0.5f, placed.ScaleY);

        // A point keeps the identity scale, which the canonical form leaves out.
        Assert.Equal(1f, document.Entries.ToArray()[1].Entity!.Value.ScaleX);
        Assert.Equal(1, SceneDocumentFile.ToJson(document).Split("\"scale\"").Length - 1);
    }

    [Fact]
    public void Import_RefusesAFlippedTileObject()
    {
        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        workspace.Write("room.tmj", TileObject("\"gid\":2147483649,"));

        TiledImportException error = Assert.Throws<TiledImportException>(() => TiledImporter.Import("room.tmj", "."));

        Assert.Contains("flipped or rotated tile object", error.Message, StringComparison.Ordinal);
        Assert.Contains("unflipped tiles only", error.Message, StringComparison.Ordinal);
    }

    // One 32x8 tile object of the 16px tileset, appended to the object layer.
    private static string TileObject(string gid) => TiledFixtures.Mutate(
        TiledFixtures.Read("room.tmj").ReplaceLineEndings("\n"),
        "                 \"x\":40.5,\n                 \"y\":24\n                }],",
        $$"""
                         "x":40.5,
                         "y":24
                        },
                        {
                         "height":8,
                         "id":4,
                         {{gid}}
                         "name":"",
                         "rotation":0,
                         "type":"crate",
                         "visible":true,
                         "width":32,
                         "x":64,
                         "y":48
                        }],
        """);

    [Fact]
    public void Import_BandsOnlyThePlacementsThatAuthorAZIndex()
    {
        string map = WithZIndex(TiledFixtures.Read("room.tmj"), "\"name\":\"terrain\",", "int", "-10");
        map = WithZIndex(map, "\"type\":\"player\",", "int", "5");

        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", map), ".");

        Assert.Equal(-10, TiledFixtures.TileMapOf(document).ZIndex);
        Assert.Equal(5, document.Entries[1].Entity!.Value.ZIndex);

        // The coin authors nothing, so the document says nothing and its class keeps the default.
        Assert.Null(document.Entries[2].Entity!.Value.ZIndex);
    }

    [Fact]
    public void Import_TakesAnObjectLayersZIndexAsTheDefaultItsObjectsOverride()
    {
        string map = WithZIndex(TiledFixtures.Read("room.tmj"), "\"name\":\"things\",", "int", "3");
        map = WithZIndex(map, "\"type\":\"player\",", "int", "7");

        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", map), ".");

        Assert.Null(TiledFixtures.TileMapOf(document).ZIndex);
        Assert.Equal(7, document.Entries[1].Entity!.Value.ZIndex);
        Assert.Equal(3, document.Entries[2].Entity!.Value.ZIndex);
    }

    [Theory]
    [InlineData("\"name\":\"terrain\",", "float", "1.5", "tile layer 'terrain' has 'zIndex' of type float")]
    [InlineData("\"name\":\"things\",", "string", "\"2\"", "object layer 'things' has 'zIndex' of type string")]
    [InlineData("\"type\":\"player\",", "int", "4294967296", "object 3 on layer 'things' has 'zIndex' of '4294967296'")]
    public void Import_RejectsAZIndexThatIsNotAnInt(string anchor, string type, string value, string expected)
    {
        TiledImportException error = TiledFixtures.ImportFailure(
            WithZIndex(TiledFixtures.Read("room.tmj"), anchor, type, value),
            TiledFixtures.Read("tiles.tsj"));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    // A custom property on the layer or object the anchor line opens, as Tiled writes one.
    private static string WithZIndex(string map, string anchor, string type, string value) => TiledFixtures.Mutate(
        map,
        anchor,
        $"\"properties\":[{{\"name\":\"zIndex\",\"type\":\"{type}\",\"value\":{value}}}],{anchor}");

    [Fact]
    public void Import_CarriesALayersParallaxFactorAsTheScrollFactorOfItsPlacements()
    {
        string map = WithParallax(TiledFixtures.Read("room.tmj"), "\"name\":\"terrain\",", "0.5", "1");
        map = WithParallax(map, "\"name\":\"things\",", "0.25", "0.75");

        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", map), ".");

        Assert.Equal(new Vector2(0.5f, 1f), TiledFixtures.TileMapOf(document).ScrollFactor);
        Assert.Equal(new Vector2(0.25f, 0.75f), document.Entries[1].Entity!.Value.ScrollFactor);
        Assert.Equal(new Vector2(0.25f, 0.75f), document.Entries[2].Entity!.Value.ScrollFactor);
    }

    [Fact]
    public void Import_WritesNoScrollFactorForAnAuthoredParallaxOfOne()
    {
        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        SceneDocument document = TiledImporter.Import(workspace.Write(
            "room.tmj",
            WithParallax(TiledFixtures.Read("room.tmj"), "\"name\":\"terrain\",", "1", "1")), ".");

        Assert.Null(TiledFixtures.TileMapOf(document).ScrollFactor);
    }

    [Fact]
    public void Import_RejectsParallaxOnATileLayerWhosePaletteCollides()
    {
        TiledImportException error = TiledFixtures.ImportFailure(
            WithParallax(TiledFixtures.Read("room.tmj"), "\"name\":\"terrain\",", "0.5", "1"),
            TiledFixtures.Mutate(
                TiledFixtures.Read("tiles.tsj"),
                "\"type\":\"hazard\"",
                "\"properties\":[{\"name\":\"layer\",\"type\":\"string\",\"value\":\"solid\"}],\"type\":\"hazard\""));

        Assert.Contains("tile layer 'terrain' has a Parallax Factor", error.Message, StringComparison.Ordinal);
        Assert.Contains("'hazard'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsATileLayerWhosePaletteTheEngineRefuses()
    {
        TiledImportException error = TiledFixtures.ImportFailure(
            TiledFixtures.Read("room.tmj"),
            TiledFixtures.Mutate(
                TiledFixtures.Read("tiles.tsj"),
                "\"type\":\"ground\"",
                "\"properties\":[{\"name\":\"oneWay\",\"type\":\"bool\",\"value\":true}],\"type\":\"ground\""));

        Assert.Contains("tile layer 'terrain' imports to an invalid tile map", error.Message, StringComparison.Ordinal);
    }

    // A layer's Parallax Factor as Tiled writes it.
    private static string WithParallax(string map, string anchor, string x, string y) => TiledFixtures.Mutate(
        map,
        anchor,
        $"\"parallaxx\":{x},\"parallaxy\":{y},{anchor}");

    [Fact]
    public void Import_RejectsALayerPaintedFromTwoTilesets()
    {
        using TiledFixtures.Workspace workspace = TwoTilesets("1, 1, 1, 5]");

        TiledImportException error = Assert.Throws<TiledImportException>(() => TiledImporter.Import("room.tmj", "."));

        Assert.Contains("tile layer 'terrain'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'terrain' and 'props'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_TakesEachLayersPaletteFromTheOneTilesetItPaints()
    {
        using TiledFixtures.Workspace workspace = TwoTilesets("1, 1, 1, 4]");

        SceneDocument document = TiledImporter.Import("room.tmj", ".");

        Assert.Equal(new TextureHandle("Textures/tiles", ".png"), TiledFixtures.TileMapOf(document).Grid.Texture);
        Assert.Equal(
            ["empty", "ground", "wall", "ledge", "hazard"],
            TiledFixtures.TileMapOf(document).Grid.TileTypes.ToArray().Select(static definition => definition.Type));
    }

    [Fact]
    public void Import_GivesAnEmptyLayerNoTextureAndNothingButTheEmptyTileType()
    {
        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        string map = TiledFixtures.Mutate(
            TiledFixtures.Read("room.tmj"),
            "\"data\":[0, 0, 0, 0, 1, 1, 2, 0, 1, 1, 1, 4],",
            "\"data\":[0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],");

        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", map), ".");

        Assert.Null(TiledFixtures.TileMapOf(document).Grid.Texture);
        Assert.Equal(0, TiledFixtures.TileMapOf(document).Grid.Columns);
        Assert.Equal("empty", Assert.Single(TiledFixtures.TileMapOf(document).Grid.TileTypes.ToArray()).Type);
    }

    private static TiledFixtures.Workspace TwoTilesets(string lastRow)
    {
        const string oneTileset = "\"tilesets\":[\n        {\n         \"firstgid\":1,\n         \"source\":\"tiles.tsj\"\n        }],";
        const string twoTilesets = "\"tilesets\":[\n        {\n         \"firstgid\":1,\n         \"source\":\"tiles.tsj\"\n        },\n        {\n         \"firstgid\":5,\n         \"source\":\"props.tsj\"\n        }],";
        const string props = """
            { "columns":1,
             "image":"textures\/props.png",
             "imageheight":16,
             "imagewidth":16,
             "name":"props",
             "tilecount":1,
             "tileheight":16,
             "tiles":[
                    {
                     "id":0,
                     "type":"crate"
                    }],
             "tilewidth":16,
             "type":"tileset",
             "version":"1.10"
            }
            """;

        TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        workspace.Write("props.tsj", props);
        workspace.Write(
            "room.tmj",
            TiledFixtures.Mutate(TiledFixtures.Mutate(TiledFixtures.Read("room.tmj"), oneTileset, twoTilesets), "1, 1, 1, 4]", lastRow));

        return workspace;
    }
}

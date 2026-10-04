using System.Numerics;
using Capsule.Scenes.Documents;

namespace JAG.Capsule.Tiled.Tests;

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
        SceneDocument document = MapImporter.Import(workspace.Write("room.tmj", map), ".");

        Assert.Equal(
            ["tile-map", "player", "coin", "tile-map"],
            document.Entries.ToArray().Select(static entry => entry.Type));
    }

    [Fact]
    public void Import_AllowsAnObjectOnlyMap()
    {
        string map = TiledFixtures.Mutate(TiledFixtures.Read("room.tmj"), "\"type\":\"tilelayer\"", "\"type\":\"objectgroup\"");

        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        SceneDocument document = MapImporter.Import(workspace.Write("room.tmj", map), ".");

        Assert.Equal(["player", "coin"], document.Entries.ToArray().Select(static entry => entry.Type));
    }

    [Fact]
    public void Import_ScalesATileObjectByItsBoxOverTheTilesetCell()
    {
        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));

        SceneDocument document = MapImporter.Import(workspace.Write("room.tmj", TileObject("\"gid\":1,")), ".");

        SceneDocumentEntry placed = document.Entries[^1];

        Assert.Equal("crate", placed.Type);
        Assert.Equal(2f, placed.ScaleX);
        Assert.Equal(0.5f, placed.ScaleY);

        // A point keeps the identity scale, which the canonical form leaves out.
        Assert.Equal(1f, document.Entries[1].ScaleX);
        Assert.Equal(1, SceneDocumentFile.ToJson(document).Split("\"scale\"").Length - 1);
    }

    [Fact]
    public void Import_RefusesAFlippedTileObject()
    {
        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        workspace.Write("room.tmj", TileObject("\"gid\":2147483649,"));

        TiledImportException error = Assert.Throws<TiledImportException>(() => MapImporter.Import("room.tmj", "."));

        Assert.Contains("flipped or rotated tile object", error.Message, StringComparison.Ordinal);
        Assert.Contains("places tile objects unflipped", error.Message, StringComparison.Ordinal);
    }

    // H, V and D are FlipX, FlipY and Transpose on the painted cell. The tile type stays the one
    // unflipped tile, and a gid with no flip bit keeps None.
    [Fact]
    public void Import_CarriesATileLayersFlipBitsAsCellTransforms()
    {
        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        string map = TiledFixtures.Mutate(
            TiledFixtures.Read("room.tmj"),
            "\"data\":[0, 0, 0, 0, 1, 1, 2, 0, 1, 1, 1, 4],",
            "\"data\":[0, 0, 0, 0, 2147483649, 1073741825, 536870914, 0, 3758096385, 1, 1, 4],");

        SceneDocument unflipped = MapImporter.Import(workspace.Write("unflipped.tmj", TiledFixtures.Read("room.tmj")), ".");
        SceneDocument document = MapImporter.Import(workspace.Write("room.tmj", map), ".");

        Assert.Equal(
            TiledFixtures.TileMapOf(unflipped).GetProperty("tiles").GetRawText(),
            TiledFixtures.TileMapOf(document).GetProperty("tiles").GetRawText());
        Assert.False(TiledFixtures.TileMapOf(unflipped).TryGetProperty("transforms", out _));
        Assert.Equal("[0,0,0,0,1,2,4,0,7,0,0,0]", TiledFixtures.TileMapOf(document).GetProperty("transforms").GetRawText());
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
        SceneDocument document = MapImporter.Import(workspace.Write("room.tmj", map), ".");

        Assert.Equal(-10, document.Entries[0].ZIndex);
        Assert.Equal(5, document.Entries[1].ZIndex);

        // A band is the spawn's, never an entry member.
        Assert.Null(document.Entries[1].Properties);

        // The coin authors nothing, so the document says nothing and its class keeps the default.
        Assert.Null(document.Entries[2].ZIndex);
    }

    [Fact]
    public void Import_TakesAnObjectLayersZIndexAsTheDefaultItsObjectsOverride()
    {
        string map = WithZIndex(TiledFixtures.Read("room.tmj"), "\"name\":\"things\",", "int", "3");
        map = WithZIndex(map, "\"type\":\"player\",", "int", "7");

        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        SceneDocument document = MapImporter.Import(workspace.Write("room.tmj", map), ".");

        Assert.Null(document.Entries[0].ZIndex);
        Assert.Equal(7, document.Entries[1].ZIndex);
        Assert.Equal(3, document.Entries[2].ZIndex);
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
        SceneDocument document = MapImporter.Import(workspace.Write("room.tmj", map), ".");

        Assert.Equal(new Vector2(0.5f, 1f), document.Entries[0].ScrollFactor);
        Assert.Equal(new Vector2(0.25f, 0.75f), document.Entries[1].ScrollFactor);
        Assert.Equal(new Vector2(0.25f, 0.75f), document.Entries[2].ScrollFactor);
    }

    [Fact]
    public void Import_WritesNoScrollFactorForAnAuthoredParallaxOfOne()
    {
        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        SceneDocument document = MapImporter.Import(workspace.Write(
            "room.tmj",
            WithParallax(TiledFixtures.Read("room.tmj"), "\"name\":\"terrain\",", "1", "1")), ".");

        Assert.Null(document.Entries[0].ScrollFactor);
    }

    // A layer collides only when it sets collider. A layered tileset alone paints decoration, which may scroll.
    [Fact]
    public void Import_GivesATileLayerAColliderOnlyWhenItSetsOne()
    {
        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", CollidingTileset());
        SceneDocument scrolled = MapImporter.Import(
            workspace.Write("scrolled.tmj", WithParallax(TiledFixtures.Read("room.tmj"), "\"name\":\"terrain\",", "0.5", "1")),
            ".");
        SceneDocument colliding = MapImporter.Import(
            workspace.Write("room.tmj", WithCollider(TiledFixtures.Read("room.tmj"), "\"name\":\"terrain\",")),
            ".");

        Assert.False(TiledFixtures.TileMapOf(scrolled).TryGetProperty("collider", out _));
        Assert.Equal(new Vector2(0.5f, 1f), scrolled.Entries[0].ScrollFactor);
        Assert.True(TiledFixtures.TileMapOf(colliding).GetProperty("collider").GetBoolean());
    }

    // An object collides as its class does, so a collider switch on its layer would reach nothing.
    [Fact]
    public void Import_RejectsAColliderOnAnObjectLayer()
    {
        TiledImportException error = TiledFixtures.ImportFailure(
            WithCollider(TiledFixtures.Read("room.tmj"), "\"name\":\"things\","),
            TiledFixtures.Read("tiles.tsj"));

        Assert.Contains("object layer 'things' sets 'collider'", error.Message, StringComparison.Ordinal);
    }

    private static string CollidingTileset() => TiledFixtures.Mutate(
        TiledFixtures.Read("tiles.tsj"),
        "\"type\":\"hazard\"",
        "\"properties\":[{\"name\":\"layer\",\"type\":\"string\",\"value\":\"solid\"}],\"type\":\"hazard\"");

    private static string WithCollider(string map, string anchor) => TiledFixtures.Mutate(
        map,
        anchor,
        $"\"properties\":[{{\"name\":\"collider\",\"type\":\"bool\",\"value\":true}}],{anchor}");

    // A layer's Parallax Factor as Tiled writes it.
    private static string WithParallax(string map, string anchor, string x, string y) => TiledFixtures.Mutate(
        map,
        anchor,
        $"\"parallaxx\":{x},\"parallaxy\":{y},{anchor}");

    [Fact]
    public void Import_RejectsALayerPaintedFromTwoTilesets()
    {
        using TiledFixtures.Workspace workspace = TwoTilesets("1, 1, 1, 5]");

        TiledImportException error = Assert.Throws<TiledImportException>(() => MapImporter.Import("room.tmj", "."));

        Assert.Contains("tile layer 'terrain'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'terrain' and 'props'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_TakesEachLayersPaletteFromTheOneTilesetItPaints()
    {
        using TiledFixtures.Workspace workspace = TwoTilesets("1, 1, 1, 4]");

        SceneDocument document = MapImporter.Import("room.tmj", ".");

        Assert.Equal("Textures/tiles.png", TiledFixtures.TileMapOf(document).GetProperty("texture").GetString());
        Assert.Equal(
            ["empty", "ground", "wall", "ledge", "hazard"],
            TiledFixtures.Palette(document).Select(static tileType => tileType.GetProperty("name").GetString()));
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

        SceneDocument document = MapImporter.Import(workspace.Write("room.tmj", map), ".");

        Assert.False(TiledFixtures.TileMapOf(document).TryGetProperty("texture", out _));
        Assert.False(TiledFixtures.TileMapOf(document).TryGetProperty("columns", out _));
        Assert.Equal("""{"name":"empty"}""", Assert.Single(TiledFixtures.Palette(document)).GetRawText());
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

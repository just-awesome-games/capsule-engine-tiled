using Capsule.Assets;
using Capsule.Scenes.Documents;

namespace Capsule.Tiled.Tests;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class TiledImportTests
{
    [Fact]
    public void Import_ReproducesTheCommittedDocumentByteForByte()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");

        SceneDocument document = TiledImporter.Import("room.tmj");

        Assert.Equal(SceneDocumentFixtures.Read("room.scene.json"), SceneDocumentFile.ToJson(document));
    }

    [Fact]
    public void Import_PutsUnpaintedClassesInThePaletteInTileIdOrder()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");

        SceneDocument document = TiledImporter.Import("room.tmj");

        string[] types = [.. TileMapOf(document).Grid.TileTypes.ToArray().Select(static definition => definition.Type)];

        Assert.Equal(["empty", "ground", "wall", "ledge", "hazard"], types);
    }

    [Fact]
    public void Import_TakesEachTilesCellFromItsTiledTileId()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");

        SceneDocument document = TiledImporter.Import("room.tmj");

        Assert.Equal(new TextureHandle("tiles", ".png"), TileMapOf(document).Grid.Texture);
        Assert.Equal(4, TileMapOf(document).Grid.Columns);

        Assert.Contains(
            "\"texture\": \"tiles.png\"",
            SceneDocumentFile.ToJson(document),
            StringComparison.Ordinal);
        Assert.Equal(
            [null, 0, 1, 2, 3],
            TileMapOf(document).Grid.TileTypes.ToArray().Select(static definition => definition.Cell));
    }

    [Theory]
    [InlineData("\"image\":\"Textures\\/tiles.png\",", "\"image\":\"\",", "tileset 'terrain' is a collection of images")]
    [InlineData("\"columns\":4,", "\"columns\":0,", "tileset 'terrain' declares 0 columns")]
    [InlineData("\"columns\":4,", "\"columns\":3,", "3 columns of 16px over a 64px image")]
    [InlineData("\"tileheight\":16,", "\"tileheight\":8,", "tileset 'terrain' has 16x8 tiles")]
    [InlineData("\"type\":\"ledge\"", "\"type\":\"wall\"", "more than one tile")]
    [InlineData("\"type\":\"ledge\"", "\"type\":\"empty\"", "reserved")]
    [InlineData("\"name\":\"solid\"", "\"name\":\"color\"", "paint the tile itself")]
    public void Import_RefusesATilesetItCannotRepresent(string from, string to, string expected)
    {
        TiledImportException error = ImportMutated(from, to, mutateTileset: true);

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RefusesAnImageOutsideTheTexturesDomain()
    {
        using SceneDocumentFixtures.Workspace workspace = TilesetAtlas("art\\/tiles.png");

        TiledImportException error = Assert.Throws<TiledImportException>(
            () => TiledImporter.Import("assets/scenes/room.tmj", dependencyRoot: "assets"));

        Assert.Contains("names a texture by its path under", error.Message, StringComparison.Ordinal);
        Assert.Contains("Textures", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_NamesANestedAtlasByItsPathUnderTheTexturesRoot()
    {
        using SceneDocumentFixtures.Workspace workspace = TilesetAtlas("Textures\\/terrain\\/cave.png");

        SceneDocument document = TiledImporter.Import("assets/scenes/room.tmj", dependencyRoot: "assets");

        Assert.Equal(new TextureHandle("terrain/cave", ".png"), TileMapOf(document).Grid.Texture);
        Assert.Contains(
            "\"texture\": \"terrain/cave.png\"",
            SceneDocumentFile.ToJson(document),
            StringComparison.Ordinal);
    }

    // A map under assets/scenes drawing its tileset from assets/, whose atlas the caller names.
    private static SceneDocumentFixtures.Workspace TilesetAtlas(string image)
    {
        SceneDocumentFixtures.Workspace workspace = new();
        Directory.CreateDirectory("assets/scenes");
        workspace.Write("assets/tiles.tsj", Mutate(
            SceneDocumentFixtures.Read("tiles.tsj"),
            "\"image\":\"Textures\\/tiles.png\"",
            $"\"image\":\"{image}\""));
        workspace.Write(
            "assets/scenes/room.tmj",
            Mutate(SceneDocumentFixtures.Read("room.tmj"), "\"source\":\"tiles.tsj\"", "\"source\":\"../tiles.tsj\""));

        return workspace;
    }

    [Fact]
    public void Import_RejectsAMapWhoseTileSizeIsNotTheDeclaredOne()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");

        TiledImportException error = Assert.Throws<TiledImportException>(
            () => TiledImporter.Import("room.tmj", tileSize: 8));

        Assert.Contains("has 16px tiles", error.Message, StringComparison.Ordinal);
        Assert.Contains("declares 8px", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_WithNoDeclaredTileSize_TakesTheMapsOwn()
    {
        string map = Mutate(SceneDocumentFixtures.Read("room.tmj"), "\"tileheight\":16", "\"tileheight\":8");
        map = Mutate(map, "\"tilewidth\":16", "\"tilewidth\":8");

        // The tileset is cut at the map's size too, so its cells stay one grid cell each.
        string tileset = Mutate(SceneDocumentFixtures.Read("tiles.tsj"), "\"imageheight\":16", "\"imageheight\":8");
        tileset = Mutate(tileset, "\"imagewidth\":64", "\"imagewidth\":32");
        tileset = Mutate(tileset, "\"tileheight\":16", "\"tileheight\":8");
        tileset = Mutate(tileset, "\"tilewidth\":16", "\"tilewidth\":8");

        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", tileset);

        Assert.Equal(8, TileMapOf(TiledImporter.Import(workspace.Write("room.tmj", map))).Grid.TileSize);
    }

    [Fact]
    public void Import_ReadsTheClassAndTypeSpellingsAlike()
    {
        SceneDocument golden = SceneDocumentFile.Load(SceneDocumentFixtures.Path("room.scene.json"));
        using SceneDocumentFixtures.Workspace workspace = new();

        SceneDocument document = TiledImporter.Import(
            workspace.Write("room-tiled19.tmj", SceneDocumentFixtures.Read("room-tiled19.tmj")));

        Assert.Equal(TileMapOf(golden).Grid.TileSize, TileMapOf(document).Grid.TileSize);
        Assert.Equal(golden.NextEntityId, document.NextEntityId);
        Assert.Equal(TileMapOf(golden).Grid.TileTypes.ToArray(), TileMapOf(document).Grid.TileTypes.ToArray());
        Assert.Equal(TileMapOf(golden).Grid.Tiles.ToArray(), TileMapOf(document).Grid.Tiles.ToArray());
        Assert.Equal(
            golden.Entries.ToArray().Select(static entry => entry.Entity).Where(static entity => entity is not null),
            document.Entries.ToArray().Select(static entry => entry.Entity).Where(static entity => entity is not null));
    }

    [Fact]
    public void Import_ForwardSlashesTheSourcePathItStamps()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("scenes/room");

        SceneDocument document = TiledImporter.Import(Path.Combine("scenes", "room.tmj"));

        Assert.Equal("scenes/room.tmj", document.Source?.Path);
    }

    [Fact]
    public void Import_SourceHashChangesWhenAnExternalTilesetChanges()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("room.tmj", SceneDocumentFixtures.Read("room.tmj"));
        workspace.Write("tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        string first = TiledImporter.Import("room.tmj").Source!.Value.Hash;

        string changed = Mutate(SceneDocumentFixtures.Read("tiles.tsj"), "\"tilecount\":4", "\"tilecount\":8");
        workspace.Write("tiles.tsj", changed);
        string second = TiledImporter.Import("room.tmj").Source!.Value.Hash;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Import_AcceptsAnExternalTilesetWithinTheTrackedRoot()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        Directory.CreateDirectory("assets/scenes");
        workspace.Write("assets/tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        string map = Mutate(SceneDocumentFixtures.Read("room.tmj"), "\"source\":\"tiles.tsj\"", "\"source\":\"../tiles.tsj\"");
        workspace.Write("assets/scenes/room.tmj", map);

        SceneDocument imported = TiledImporter.Import("assets/scenes/room.tmj", dependencyRoot: "assets");

        Assert.Equal("ground", TileMapOf(imported).Grid.TileTypes[1].Type);
    }

    [Fact]
    public void Import_RejectsAnExternalTilesetOutsideTheTrackedRoot()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        Directory.CreateDirectory("assets/scenes");
        workspace.Write("tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        string map = Mutate(SceneDocumentFixtures.Read("room.tmj"), "\"source\":\"tiles.tsj\"", "\"source\":\"../../tiles.tsj\"");
        workspace.Write("assets/scenes/room.tmj", map);

        TiledImportException error = Assert.Throws<TiledImportException>(
            () => TiledImporter.Import("assets/scenes/room.tmj", dependencyRoot: "assets"));

        Assert.Contains("outside the tracked asset source root", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RefusesAnAbsoluteSourcePath()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");

        TiledImportException error = Assert.Throws<TiledImportException>(
            () => TiledImporter.Import(Path.GetFullPath("room.tmj")));

        Assert.Contains("must be relative", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"orientation\":\"orthogonal\"", "\"orientation\":\"isometric\"", "orthogonal maps only")]
    [InlineData("\"infinite\":false", "\"infinite\":true", "infinite map")]
    [InlineData("\"tileheight\":16", "\"tileheight\":8", "square tiles only")]
    [InlineData("\"type\":\"tilelayer\"", "\"type\":\"imagelayer\"", "unsupported layer type")]
    [InlineData("1, 1, 1, 4]", "1, 1, 1, 2147483649]", "flipped or rotated")]
    [InlineData("1, 1, 1, 4]", "1, 1, 1, 5]", "has no Class")]
    [InlineData("1, 1, 1, 4]", "1, 1, 1, 4, 0]", "requires 12")]
    [InlineData("\"type\":\"coin\"", "\"type\":\"\"", "typed by its Class")]
    [InlineData("\"source\":\"tiles.tsj\"", "\"source\":\"tiles.tsx\"", "XML")]
    [InlineData("\"source\":\"tiles.tsj\"", "\"source\":\"missing.tsj\"", "is missing")]
    [InlineData("\"data\":[0, 0, 0, 0, 1, 1, 2, 0, 1, 1, 1, 4],", "\"data\":\"AAAA\",\"encoding\":\"base64\",", "CSV")]
    public void Import_RejectsWhatItCannotRepresent(string from, string to, string expected)
    {
        TiledImportException error = ImportMutated(from, to, mutateTileset: false);

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_PreservesMultipleTileAndObjectLayersInAuthoredOrder()
    {
        string authored = SceneDocumentFixtures.Read("room.tmj").ReplaceLineEndings("\n");
        string map = Mutate(
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

        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", map));

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
        string map = Mutate(SceneDocumentFixtures.Read("room.tmj"), "\"type\":\"tilelayer\"", "\"type\":\"objectgroup\"");

        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", map));

        Assert.All(document.Entries.ToArray(), entry => Assert.NotNull(entry.Entity));
    }

    [Fact]
    public void Import_ScalesATileObjectByItsBoxOverTheTilesetCell()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));

        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", TileObject("\"gid\":1,")));

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
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        workspace.Write("room.tmj", TileObject("\"gid\":2147483649,"));

        TiledImportException error = Assert.Throws<TiledImportException>(() => TiledImporter.Import("room.tmj"));

        Assert.Contains("flipped or rotated tile object", error.Message, StringComparison.Ordinal);
        Assert.Contains("unflipped tiles only", error.Message, StringComparison.Ordinal);
    }

    // One 32x8 tile object of the 16px tileset, appended to the object layer.
    private static string TileObject(string gid) => Mutate(
        SceneDocumentFixtures.Read("room.tmj").ReplaceLineEndings("\n"),
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
        string map = WithZIndex(SceneDocumentFixtures.Read("room.tmj"), "\"name\":\"terrain\",", "int", "-10");
        map = WithZIndex(map, "\"type\":\"player\",", "int", "5");

        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", map));

        Assert.Equal(-10, TileMapOf(document).ZIndex);
        Assert.Equal(5, document.Entries[1].Entity!.Value.ZIndex);

        // The coin authors nothing, so the document says nothing and its class keeps the default.
        Assert.Null(document.Entries[2].Entity!.Value.ZIndex);
    }

    [Fact]
    public void Import_TakesAnObjectLayersZIndexAsTheDefaultItsObjectsOverride()
    {
        string map = WithZIndex(SceneDocumentFixtures.Read("room.tmj"), "\"name\":\"things\",", "int", "3");
        map = WithZIndex(map, "\"type\":\"player\",", "int", "7");

        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", map));

        Assert.Null(TileMapOf(document).ZIndex);
        Assert.Equal(7, document.Entries[1].Entity!.Value.ZIndex);
        Assert.Equal(3, document.Entries[2].Entity!.Value.ZIndex);
    }

    [Theory]
    [InlineData("\"name\":\"terrain\",", "float", "1.5", "tile layer 'terrain' declares 'zIndex' as a 'float'")]
    [InlineData("\"name\":\"things\",", "string", "\"2\"", "object layer 'things' declares 'zIndex' as a 'string'")]
    [InlineData("\"type\":\"player\",", "int", "4294967296", "object 3 on layer 'things' has a 'zIndex' property of '4294967296'")]
    public void Import_RejectsAZIndexThatIsNotAnInt(string anchor, string type, string value, string expected)
    {
        TiledImportException error = Import(
            WithZIndex(SceneDocumentFixtures.Read("room.tmj"), anchor, type, value),
            SceneDocumentFixtures.Read("tiles.tsj"));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    // A custom property on the layer or object the anchor line opens, as Tiled writes one.
    private static string WithZIndex(string map, string anchor, string type, string value) => Mutate(
        map,
        anchor,
        $"\"properties\":[{{\"name\":\"zIndex\",\"type\":\"{type}\",\"value\":{value}}}],{anchor}");

    [Fact]
    public void Import_RejectsAGridWhoseAreaOverflowsAnInt()
    {
        string map = Mutate(SceneDocumentFixtures.Read("room.tmj"), "\"width\":4", "\"width\":65536");
        map = Mutate(map, "\"height\":3", "\"height\":65536");

        TiledImportException error = Import(map, SceneDocumentFixtures.Read("tiles.tsj"));

        Assert.Contains("65536x65536", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsALayerPaintedFromTwoTilesets()
    {
        using SceneDocumentFixtures.Workspace workspace = TwoTilesets("1, 1, 1, 5]");

        TiledImportException error = Assert.Throws<TiledImportException>(() => TiledImporter.Import("room.tmj"));

        Assert.Contains("tile layer 'terrain'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'terrain' and 'props'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_TakesEachLayersPaletteFromTheOneTilesetItPaints()
    {
        using SceneDocumentFixtures.Workspace workspace = TwoTilesets("1, 1, 1, 4]");

        SceneDocument document = TiledImporter.Import("room.tmj");

        Assert.Equal(new TextureHandle("tiles", ".png"), TileMapOf(document).Grid.Texture);
        Assert.Equal(
            ["empty", "ground", "wall", "ledge", "hazard"],
            TileMapOf(document).Grid.TileTypes.ToArray().Select(static definition => definition.Type));
    }

    [Fact]
    public void Import_GivesAnEmptyLayerNoTextureAndNothingButTheEmptyTileType()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        string map = Mutate(
            SceneDocumentFixtures.Read("room.tmj"),
            "\"data\":[0, 0, 0, 0, 1, 1, 2, 0, 1, 1, 1, 4],",
            "\"data\":[0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],");

        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", map));

        Assert.Null(TileMapOf(document).Grid.Texture);
        Assert.Equal(0, TileMapOf(document).Grid.Columns);
        Assert.Equal("empty", Assert.Single(TileMapOf(document).Grid.TileTypes.ToArray()).Type);
    }

    private static SceneDocumentFixtures.Workspace TwoTilesets(string lastRow)
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
             "type":"tileset"
            }
            """;

        SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        workspace.Write("props.tsj", props);
        workspace.Write(
            "room.tmj",
            Mutate(Mutate(SceneDocumentFixtures.Read("room.tmj"), oneTileset, twoTilesets), "1, 1, 1, 4]", lastRow));

        return workspace;
    }

    private static TiledImportException ImportMutated(string from, string to, bool mutateTileset)
    {
        string map = SceneDocumentFixtures.Read("room.tmj");
        string tileset = SceneDocumentFixtures.Read("tiles.tsj");

        if (mutateTileset)
        {
            tileset = Mutate(tileset, from, to);
        }
        else
        {
            map = Mutate(map, from, to);
        }

        return Import(map, tileset);
    }

    private static TiledImportException Import(string map, string tileset)
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", tileset);
        string mapPath = workspace.Write("room.tmj", map);

        return Assert.Throws<TiledImportException>(() => TiledImporter.Import(mapPath));
    }

    private static TileMapPlacement TileMapOf(SceneDocument document, int index = 0) =>
        document.Entries[index].TileMap!.Value;

    private static string Mutate(string text, string from, string to)
    {
        Assert.Contains(from, text, StringComparison.Ordinal);
        return text.Replace(from, to, StringComparison.Ordinal);
    }
}

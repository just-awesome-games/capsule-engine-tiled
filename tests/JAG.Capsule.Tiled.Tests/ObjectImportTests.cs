using Capsule.Scenes.Documents;

namespace JAG.Capsule.Tiled.Tests;

// An object's turn, extent, points and custom properties, and the objects the importer refuses.
[Collection(SceneWorkspaceCollection.Name)]
public sealed class ObjectImportTests
{
    [Fact]
    public void Import_CarriesAnObjectsRotation()
    {
        SceneDocumentEntry placed = Imported(Crate("\"rotation\":22.5,\"width\":0,\"height\":0"));

        Assert.Equal(float.DegreesToRadians(22.5f), placed.Spawn.Rotation);
    }

    // A click-placed rectangle is the spawn marker existing maps rely on, and keeps no size. A path's
    // points stay relative to the object, and a polygon's closes on its first point.
    [Theory]
    [InlineData("\"width\":32,\"height\":8", "{\"size\":[32,8]}")]
    [InlineData("\"ellipse\":true,\"width\":12.5,\"height\":4", "{\"size\":[12.5,4]}")]
    [InlineData("\"width\":0,\"height\":0", null)]
    [InlineData("\"polyline\":[{\"x\":0,\"y\":0},{\"x\":32,\"y\":-8.5}],\"width\":0,\"height\":0", "{\"path\":[[0,0],[32,-8.5]]}")]
    [InlineData("\"polygon\":[{\"x\":0,\"y\":0},{\"x\":8,\"y\":0},{\"x\":0,\"y\":8}],\"width\":0,\"height\":0", "{\"path\":[[0,0],[8,0],[0,8],[0,0]]}")]
    public void Import_WritesAnObjectsShapeAsItsSizeOrPath(string shape, string? expected)
    {
        SceneDocumentEntry placed = Imported(Crate(shape));

        Assert.Equal(expected, placed.Members?.GetRawText());
    }

    // A custom property named after a key the importer writes would write that key twice.
    [Theory]
    [InlineData("\"width\":32,\"height\":8,\"properties\":[{\"name\":\"size\",\"propertytype\":\"Vector2\",\"type\":\"class\",\"value\":{\"x\":1,\"y\":2}}]", "size")]
    [InlineData("\"polyline\":[{\"x\":0,\"y\":0},{\"x\":8,\"y\":0}],\"width\":0,\"height\":0,\"properties\":[{\"name\":\"path\",\"type\":\"string\",\"value\":\"\"}]", "path")]
    [InlineData("\"width\":0,\"height\":0,\"properties\":[{\"name\":\"x\",\"type\":\"float\",\"value\":1}]", "x")]
    public void Import_RefusesAPropertyNamedAfterAKeyItWrites(string members, string name)
    {
        TiledImportException error = Failure(Crate(members));

        Assert.Contains($"object 4 on layer 'things' has a '{name}' property, but Capsule writes '{name}' itself", error.Message, StringComparison.Ordinal);
    }

    // Tiled writes a colour alpha first. An unset colour is an empty string, and an unset object
    // reference is 0.
    [Theory]
    [InlineData("{\"name\":\"lift\",\"type\":\"object\",\"value\":11}", "{\"lift\":11}")]
    [InlineData("{\"name\":\"lift\",\"type\":\"object\",\"value\":0}", null)]
    [InlineData("{\"name\":\"tint\",\"type\":\"color\",\"value\":\"#80FF8000\"}", "{\"tint\":\"#ff800080\"}")]
    [InlineData("{\"name\":\"tint\",\"type\":\"color\",\"value\":\"#ff00ff00\"}", "{\"tint\":\"#00ff00\"}")]
    [InlineData("{\"name\":\"tint\",\"type\":\"color\",\"value\":\"\"}", null)]
    [InlineData("{\"name\":\"drift\",\"propertytype\":\"Vec\",\"type\":\"class\",\"value\":{\"x\":1.5,\"y\":-2}}", "{\"drift\":[1.5,-2]}")]
    [InlineData("{\"name\":\"bounds\",\"propertytype\":\"Rect\",\"type\":\"class\",\"value\":{\"bottom\":240,\"left\":0,\"right\":320.5,\"top\":-16}}", "{\"bounds\":[0,-16,320.5,240]}")]
    [InlineData("{\"name\":\"biome\",\"propertytype\":\"Biome\",\"type\":\"string\",\"value\":\"iceCave\"}", "{\"biome\":\"iceCave\"}")]
    [InlineData("{\"name\":\"hazards\",\"propertytype\":\"Hazard\",\"type\":\"string\",\"value\":\"spikes,fire\"}", "{\"hazards\":\"spikes,fire\"}")]
    [InlineData("{\"name\":\"music\",\"type\":\"file\",\"value\":\"Music\\/cave.ogg\"}", "{\"music\":\"Music/cave.ogg\"}")]
    [InlineData("{\"name\":\"next\",\"type\":\"file\",\"value\":\"Scenes\\/halls\\/hall.tmj\"}", "{\"next\":\"Scenes/halls/hall\"}")]
    [InlineData("{\"name\":\"music\",\"type\":\"file\",\"value\":\"\"}", null)]
    public void Import_WritesACustomPropertyInTheDocumentsValueForm(string property, string? expected)
    {
        SceneDocumentEntry placed = Imported(Crate($"\"width\":0,\"height\":0,\"properties\":[{property}]"));

        Assert.Equal(expected, placed.Members?.GetRawText());
    }

    [Theory]
    [InlineData(
        "{\"name\":\"loot\",\"propertytype\":\"Loot\",\"type\":\"class\",\"value\":{\"count\":2,\"x\":1}}",
        "has 'loot' of class 'Loot' with members count, x; Capsule converts only a class whose members are the numbers x and y, or the numbers left, top, right and bottom, and imports no other class value.")]
    [InlineData(
        "{\"name\":\"drift\",\"propertytype\":\"Vec\",\"type\":\"class\",\"value\":{\"y\":4}}",
        "has 'drift' of class 'Vec' setting only y; Tiled saves only the members a value sets. Set both x and y even where one is 0")]
    [InlineData(
        "{\"name\":\"drift\",\"propertytype\":\"Vec\",\"type\":\"class\",\"value\":{\"x\":1,\"x\":2}}",
        "has 'drift' of class 'Vec' setting x more than once. Set both x and y once each")]
    [InlineData(
        "{\"name\":\"bounds\",\"propertytype\":\"Rect\",\"type\":\"class\",\"value\":{\"left\":0,\"top\":0,\"right\":3,\"right\":4}}",
        "has 'bounds' of class 'Rect' setting right more than once. Set all of left, top, right and bottom once each")]
    [InlineData(
        "{\"name\":\"bounds\",\"propertytype\":\"Rect\",\"type\":\"class\",\"value\":{\"bottom\":240,\"left\":0,\"right\":320}}",
        "has 'bounds' of class 'Rect' setting only bottom, left, right; Tiled saves only the members a value sets. Set all of left, top, right and bottom even where one is 0")]
    [InlineData(
        "{\"name\":\"bounds\",\"propertytype\":\"Rect\",\"type\":\"class\",\"value\":{\"bottom\":240,\"left\":0,\"right\":320,\"top\":0,\"x\":1}}",
        "has 'bounds' of class 'Rect' with members bottom, left, right, top, x; Capsule converts only a class whose members are the numbers x and y, or the numbers left, top, right and bottom")]
    [InlineData(
        "{\"name\":\"music\",\"type\":\"file\",\"value\":\"..\\/Music\\/cave.ogg\"}",
        "has 'music' at '../Music/cave.ogg', which resolves to")]
    [InlineData(
        "{\"name\":\"biome\",\"propertytype\":\"Biome\",\"type\":\"int\",\"value\":1}",
        "has 'biome' of enum 'Biome' stored as a number; Capsule reads an enum by its member name. Set 'Biome' to save its values as strings")]
    public void Import_RefusesAPropertyItCannotConvert(string property, string expected)
    {
        TiledImportException error = Failure(Crate($"\"width\":0,\"height\":0,\"properties\":[{property}]"));

        Assert.Contains("object 4 on layer 'things' " + expected, error.Message, StringComparison.Ordinal);
    }

    // An entity's turn only affects its presentation, so a turned path would lie where Tiled does
    // not draw it.
    [Theory]
    [InlineData("\"polygon\":[{\"x\":0,\"y\":0},{\"x\":8,\"y\":0},{\"x\":0,\"y\":8}],\"rotation\":90,\"width\":0,\"height\":0", "is a polygon turned 90 degrees")]
    [InlineData("\"text\":{\"text\":\"hi\",\"wrap\":true},\"width\":16,\"height\":8", "is a text object")]
    [InlineData("\"width\":16,\"height\":0", "is 16x0, an extent with no area")]
    public void Import_RefusesAnObjectItCannotPlace(string shape, string expected)
    {
        TiledImportException error = Failure(Crate(shape));

        Assert.Contains("object 4 on layer 'things' " + expected, error.Message, StringComparison.Ordinal);
    }

    // A template instance carries only what it overrides, and its Class comes from the .tx.
    [Fact]
    public void Import_RefusesATemplateInstanceBeforeItsMissingClass()
    {
        TiledImportException error = Failure(Crate("\"template\":\"crate.tx\",\"width\":0,\"height\":0", type: null));

        Assert.Contains("object 4 on layer 'things' is an instance of template 'crate.tx'; Capsule reads no templates", error.Message, StringComparison.Ordinal);
    }

    // A crate object with the given members, appended to the fixture's object layer. A null type
    // leaves the object with no Class.
    private static string Crate(string members, string? type = "crate") => TiledFixtures.Mutate(
        TiledFixtures.Read("room.tmj").ReplaceLineEndings("\n"),
        "                 \"x\":40.5,\n                 \"y\":24\n                }],",
        $$"""
                         "x":40.5,
                         "y":24
                        },
                        {
                         "id":4,
                         {{(type is null ? "" : $"\"type\":\"{type}\",")}}
                         {{members}},
                         "x":64,
                         "y":48
                        }],
        """);

    private static SceneDocumentEntry Imported(string map)
    {
        using TiledFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", TiledFixtures.Read("tiles.tsj"));
        SceneDocument document = MapImporter.Import(workspace.Write("room.tmj", map), ".");

        return document.Entries[^1];
    }

    private static TiledImportException Failure(string map) => TiledFixtures.ImportFailure(map, TiledFixtures.Read("tiles.tsj"));
}

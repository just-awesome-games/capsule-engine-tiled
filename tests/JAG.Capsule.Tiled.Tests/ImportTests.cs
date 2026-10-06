using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Capsule.Build;

namespace JAG.Capsule.Tiled.Tests;

// Imports Fixtures/Scenes/golden.tmj from a copy of Fixtures/ as the asset root. Each refusal row
// edits the copy first. An edit is "path=json", and a path starting "tileset:" edits terrain.tsj.
public sealed class ImportTests : IDisposable
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("capsule-tiled-");

    public ImportTests()
    {
        foreach (string file in Directory.EnumerateFiles(Fixtures, "*", SearchOption.AllDirectories))
        {
            string copy = Path.Combine(_root.FullName, Path.GetRelativePath(Fixtures, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

    private string MapPath => Path.Combine(_root.FullName, "Scenes", "golden.tmj");

    private string TilesetPath => Path.Combine(_root.FullName, "Tilesets", "terrain.tsj");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void TheGoldenMapImportsToTheCommittedScene()
    {
        (string assetPath, byte[] contents) = Assert.Single(Import().Outputs);
        string scene = Encoding.UTF8.GetString(contents);
        JsonNode expected = JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "golden.scene.json")))!;

        Assert.Equal("Scenes/golden.scene.json", assetPath);
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(scene)), scene);
        Assert.Equal(expected.ToJsonString(), scene);
    }

    // The build re-imports a map when any of its inputs changes, so an edited tileset re-imports its maps.
    [Fact]
    public void TheMapAndEachTilesetItReadsAreInputs()
    {
        Assert.Equal([MapPath, TilesetPath], Import().Inputs.Select(Path.GetFullPath));
    }

    // The build reports any IOException against the map.
    [Fact]
    public void AMissingTilesetFailsAsAMissingFile()
    {
        Edit("tilesets.0.source=\"../Tilesets/missing.tsj\"");

        Assert.Throws<FileNotFoundException>(Import);
    }

    [Theory]
    [InlineData("width=\"four\"", "the file is not readable Tiled JSON")]
    [InlineData("=null", "the file holds no Tiled document")]
    [InlineData("version=\"1.9\"", "format version '1.9' is older than 1.10")]
    [InlineData("orientation=\"isometric\"", "the map is isometric; Capsule imports orthogonal maps only")]
    [InlineData("infinite=true", "the map is infinite")]
    [InlineData("tileheight=8", "the map has 16x8 tiles; Capsule imports square tiles only")]
    [InlineData("tilewidth=8; tileheight=8", "the map has 8px tiles but the game declares 16px")]
    [InlineData("width=0", "the map is 0x3 tiles")]
    [InlineData("layers.2.type=\"imagelayer\"", "layer 'empty' has type 'imagelayer'")]
    [InlineData("properties.2.value=\"Not A Key\"", "baseScene is 'Not A Key', which is not a key")]
    [InlineData("layers.3.objects.0.properties.0.name=\"x\"", "has a member 'x', which the format reserves for the entry itself")]
    [InlineData("layers.3.objects.1.properties.0.name=\"size\"", "object 2 on layer 'things': property 'size': Capsule writes 'size' itself")]
    [InlineData("tileset:tiles.0.properties.1={\"name\":\"shape\",\"type\":\"int\",\"value\":7}", "tile 0 (Class 'ground'): property 'shape': Capsule writes 'shape' itself")]
    [InlineData("backgroundcolor=null; properties.8={\"name\":\"clearColor\",\"type\":\"color\",\"value\":\"#ff101820\"}", "property 'clearColor': Capsule writes 'clearColor' itself")]
    [InlineData("layers.0.properties.1.type=\"float\"", "tile layer 'terrain': property 'zIndex' is declared float; Capsule reads it as int")]
    [InlineData("layers.3.objects.0.properties.0.value=4294967296", "object 1 on layer 'things': property 'zIndex' holds '4294967296', which is not a valid int")]
    [InlineData("layers.2.width=5", "tile layer 'empty': the layer is 5x3 but the map is 4x3")]
    [InlineData("layers.2.encoding=\"base64\"", "tile layer 'empty': the layer's tile data is not CSV")]
    [InlineData("width=40000; height=40000; layers.0.width=40000; layers.0.height=40000", "tile layer 'terrain': the layer holds 12 tiles but 40000x40000 needs 1600000000")]
    [InlineData("layers.2.data.0=\"x\"", "tile layer 'empty': the tile at index 0 is 'x', which is not a gid")]
    [InlineData("layers.2.data.0=268435457", "tile layer 'empty': the tile at index 0 is turned 120 degrees")]
    [InlineData("tilesets.0.firstgid=3", "tile layer 'terrain': the tile at index 4 has gid 2, which belongs to no tileset")]
    [InlineData("layers.2.data.0=1; layers.2.data.1=9", "tile layer 'empty': the layer paints from tilesets 'terrain' and 'props'")]
    [InlineData("tileset:tiles.6.type=\"cell-5\"", "tile layer 'terrain': tile 5: the tile has no Class, so Capsule names it 'cell-5'")]
    [InlineData("layers.3.properties.1={\"name\":\"collider\",\"type\":\"bool\",\"value\":true}", "object layer 'things': the layer sets 'collider'")]
    [InlineData("layers.3.objects.0.template=\"player.tx\"", "object 1 on layer 'things': the object is an instance of template 'player.tx'")]
    [InlineData("layers.3.objects.0.type=\"\"", "object 1 on layer 'things': the object has no Class")]
    [InlineData("layers.3.objects.0.text={\"text\":\"hi\"}", "object 1 on layer 'things': the object is a text object")]
    [InlineData("layers.3.objects.1.height=0", "object 2 on layer 'things': the object is 32x0, an extent with no area")]
    [InlineData("layers.3.objects.4.rotation=90", "object 5 on layer 'things': the polygon is turned 90 degrees")]
    [InlineData("layers.3.objects.6.gid=2147483657", "object 7 on layer 'things': the tile object is flipped or rotated")]
    [InlineData("tilesets.0.firstgid=2; layers.0.data.8=0; layers.0.data.9=0; layers.3.objects.6.gid=1", "object 7 on layer 'things': the tile object has gid 1, which belongs to no tileset")]
    [InlineData("layers.3.objects.4.properties.0={\"name\":\"biome\",\"propertytype\":\"Biome\",\"type\":\"int\",\"value\":1}", "property 'biome': enum 'Biome' is stored as a number")]
    [InlineData("layers.3.objects.3.properties.0.value=-1", "property 'lift': '-1' is not an object id")]
    [InlineData("layers.3.objects.5.properties.2.value.1={\"type\":\"object\",\"value\":0}", "property 'ticks': item 1 is unset")]
    [InlineData("layers.3.objects.5.properties.1.value={\"y\":4}", "property 'drift': class 'Vector2' sets y; Capsule imports a class value that sets exactly")]
    [InlineData("layers.3.objects.2.properties.0.value=\"#zz\"", "property 'core': '#zz' is not a #rrggbb or #aarrggbb colour")]
    [InlineData("layers.3.objects.1.properties.1.value=\"../../door.ogg\"", "property 'music': '../../door.ogg' resolves to")]
    [InlineData("tilesets.0.source=\"../Tilesets/terrain.tsx\"", "tileset '../Tilesets/terrain.tsx': the tileset is XML")]
    [InlineData("tileset:version=\"1.9\"", "tileset '../Tilesets/terrain.tsj': format version '1.9' is older than 1.10")]
    [InlineData("tileset:image=null", "tileset 'terrain': the tileset is a collection of images")]
    [InlineData("tileset:tileheight=8", "tileset 'terrain': the tileset has 16x8 tiles but the map has 16px square ones")]
    [InlineData("tileset:columns=3", "tileset 'terrain': the tileset declares 3 columns of 16px over a 64px image")]
    [InlineData("tileset:image=\"../../terrain.png\"", "tileset 'terrain': '../../terrain.png' resolves to")]
    [InlineData("tileset:tiles.0.properties=[]", "tileset 'terrain': tile 0 (Class 'ground'): the tile has a collision shape but no 'layer' property")]
    [InlineData("tileset:tiles.0.objectgroup.objects.1={\"id\":2,\"width\":8,\"height\":8}", "tile 0 (Class 'ground'): the tile has 2 objects in its collision")]
    [InlineData("tileset:tiles.0.objectgroup.objects.0.ellipse=true", "tile 0 (Class 'ground'): the tile collides as an ellipse")]
    [InlineData("tileset:tiles.0.objectgroup.objects.0.rotation=45", "tile 0 (Class 'ground'): the tile's collision shape is rotated 45 degrees")]
    public void TheImporterRefusesWhatItCannotRepresent(string edits, string expected)
    {
        Edit(edits);

        FormatException error = Assert.Throws<FormatException>(Import);

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    private AssetImportContext Import()
    {
        AssetImportContext context = new(MapPath, _root.FullName) { TileSize = 16 };
        new TiledImporter().Import(context);

        return context;
    }

    private void Edit(string edits)
    {
        foreach (string edit in edits.Split("; "))
        {
            int equals = edit.IndexOf('=', StringComparison.Ordinal);
            string path = edit[..equals];
            string file = path.StartsWith("tileset:", StringComparison.Ordinal) ? TilesetPath : MapPath;
            JsonNode? root = Set(JsonNode.Parse(File.ReadAllText(file))!, path.Replace("tileset:", string.Empty, StringComparison.Ordinal), JsonNode.Parse(edit[(equals + 1)..]));
            File.WriteAllText(file, root?.ToJsonString() ?? "null");
        }
    }

    // Sets the member or array element at a dotted path, appending when the index is the array's length.
    private static JsonNode? Set(JsonNode root, string path, JsonNode? value)
    {
        if (path.Length == 0)
        {
            return value;
        }

        string[] segments = path.Split('.');
        JsonNode parent = segments[..^1].Aggregate(root, static (node, segment) => node is JsonArray ? node[Index(segment)]! : node[segment]!);
        if (parent is not JsonArray array)
        {
            parent[segments[^1]] = value;
        }
        else if (Index(segments[^1]) == array.Count)
        {
            array.Add(value);
        }
        else
        {
            array[Index(segments[^1])] = value;
        }

        return root;
    }

    private static int Index(string segment) => int.Parse(segment, CultureInfo.InvariantCulture);
}

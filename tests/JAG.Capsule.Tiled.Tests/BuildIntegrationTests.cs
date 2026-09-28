using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Capsule.Assets;
using Capsule.Scenes.Documents;

namespace JAG.Capsule.Tiled.Tests;

// The seam end to end, from this project's Assets/Scenes maps through its build project's
// TiledImporter to shipped content.
[Collection(SceneWorkspaceCollection.Name)]
public sealed class BuildIntegrationTests
{
    private static string Shipped(string key) =>
        Path.Combine(AppContext.BaseDirectory, "assets", key + ".scene.json.gz");

    // Capsule ships a scene document gzipped.
    private static string Inflated(string key)
    {
        using StreamReader inflated = new(new GZipStream(File.OpenRead(Shipped(key)), CompressionMode.Decompress));

        return inflated.ReadToEnd();
    }

    private static SceneDocument Load(string key) => SceneDocumentFile.Parse(Inflated(key));

    [Theory]
    [InlineData("scenes/room")]
    [InlineData("scenes/halls/room")]
    public void TheBuildShipsAMapAsACanonicalSceneDocumentAtItsKey(string key)
    {
        string path = Shipped(key);

        Assert.True(File.Exists(path), $"expected the build to ship {path}");
        string shipped = Inflated(key);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(shipped),
            JsonNode.Parse(SceneDocumentFile.ToJson(SceneDocumentFile.Parse(shipped)))));
    }

    [Theory]
    [InlineData("scenes/room", "Assets/Scenes/room.tmj")]
    [InlineData("scenes/halls/room", "Assets/Scenes/halls/room.tmj")]
    public void TheShippedDocumentKeepsItsMapAsItsProvenance(string key, string source)
    {
        SceneDocument document = Load(key);

        Assert.Equal(MapImporter.ToolName, document.Source?.Tool);
        Assert.EndsWith(source, document.Source?.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedDocumentNamesANestedAtlasByItsPathUnderAssets()
    {
        SceneDocument document = Load("scenes/halls/room");

        Assert.Equal(
            new TextureHandle("textures/terrain/tiles", ".png"),
            document.Entries[0].TileMap!.Value.Grid.Texture);
    }

    // The module spells key and handle as the authoring tree does. Capsule normalizes both.
    // Assets/Scenes/UpperHalls/Room02.tmj draws Assets/Textures/CaveWalls/CaveTiles.png.
    [Fact]
    public void TheBuildNormalizesAnAuthoredSpellingIntoTheShippedKeyAndHandle()
    {
        SceneDocument document = Load("scenes/upper-halls/room-02");

        Assert.Equal(
            new TextureHandle("textures/cave-walls/cave-tiles", ".png"),
            document.Entries[0].TileMap!.Value.Grid.Texture);
    }

    // Assets/Scenes/dev/ holds a .capsuleignore. Its map ships in every build but a shipping one.
    [Fact]
    public void AMapUnderAMarkedDirectoryShipsOnlyOutsideAShippingBuild()
    {
        string path = Shipped("scenes/dev/scratch");

        Assert.True(File.Exists(path), $"expected the build to ship {path}");

        using Scratch scratch = new();
        string obj = scratch.Subdirectory("obj");
        BuildResult shipping = RunBuild(obj, "-p:CapsuleShipping=true");
        Assert.True(shipping.ExitCode == 0, shipping.Output);

        string assets = Path.Combine(obj, "assets");
        string[] shipped = [.. Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(assets, file).Replace('\\', '/'))];
        Assert.Contains("scenes/room.scene.json.gz", shipped);
        Assert.DoesNotContain("scenes/dev/scratch.scene.json.gz", shipped);
    }

    [Fact]
    public void AMapsDefectFailsTheBuildNamingTheMap()
    {
        using Scratch scratch = new();
        string assets = scratch.Subdirectory("Assets");
        File.WriteAllText(Path.Combine(assets, "broken.tmj"), "{");

        BuildResult build = RunBuild(scratch.Subdirectory("obj"), $"-p:CapsuleAssetSourcesDir={assets}");

        Assert.NotEqual(0, build.ExitCode);
        Assert.Contains("broken.tmj: the map is not readable Tiled JSON", build.Output, StringComparison.Ordinal);
    }

    // The seed lands at the asset root, holding every map wherever it is filed. The build runs in the
    // test project's directory, which names the seed. A second build leaves the edited file alone.
    [Fact]
    public void TheBuildSeedsTheTiledProjectOnceAndNeverOverwritesIt()
    {
        using Scratch scratch = new();
        string assets = scratch.Subdirectory("Assets");
        string levels = Directory.CreateDirectory(Path.Combine(assets, "Levels")).FullName;
        File.Copy(TiledFixtures.Path("room.tmj"), Path.Combine(levels, "room.tmj"));
        File.Copy(TiledFixtures.Path("tiles.tsj"), Path.Combine(levels, "tiles.tsj"));
        string seeded = Path.Combine(assets, "JAG.Capsule.Tiled.Tests.tiled-project");

        BuildResult first = RunBuild(scratch.Subdirectory("first"), $"-p:CapsuleAssetSourcesDir={assets}");
        Assert.True(first.ExitCode == 0, first.Output);
        Assert.Equal(TiledFixtures.Read("capsule.tiled-project"), File.ReadAllText(seeded));

        File.WriteAllText(seeded, "{ \"folders\": [\".\", \"halls\"] }");
        BuildResult second = RunBuild(scratch.Subdirectory("second"), $"-p:CapsuleAssetSourcesDir={assets}");
        Assert.True(second.ExitCode == 0, second.Output);

        Assert.Equal("{ \"folders\": [\".\", \"halls\"] }", File.ReadAllText(seeded));
    }

    private sealed record BuildResult(int ExitCode, string Output);

    // One run of the test project's asset build, through its build project, into a fresh obj
    // directory. The build project then always runs. A build of this whole project would fight the
    // test host for its own bin/ and obj/.
    private static BuildResult RunBuild(string objDirectory, params string[] properties)
    {
        string root = Path.GetFullPath(TiledFixtures.Metadata("RepositoryRoot"));
        ProcessStartInfo start = new("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(Path.Combine(root, "tests", "JAG.Capsule.Tiled.Tests", "JAG.Capsule.Tiled.Tests.csproj"));
        start.ArgumentList.Add("-t:CapsuleRunBuildTool");
        // A reused worker node inherits the redirected pipes and holds them open until its idle
        // timeout. ReadToEnd would block that long, so the nodes exit with this build.
        start.ArgumentList.Add("-nodeReuse:false");
        start.ArgumentList.Add($"-p:CapsuleObjDir={objDirectory}");
        start.ArgumentList.Add($"-p:CapsuleSourcePath={TiledFixtures.Metadata("CapsuleSourcePath")}");
        foreach (string property in properties)
        {
            start.ArgumentList.Add(property);
        }

        using Process msbuild = Process.Start(start)!;
        string output = msbuild.StandardOutput.ReadToEnd();
        string errors = msbuild.StandardError.ReadToEnd();
        msbuild.WaitForExit();

        return new BuildResult(msbuild.ExitCode, output + errors);
    }

    private sealed class Scratch : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("capsule-tiled-build-");

        internal string Subdirectory(string name) => _root.CreateSubdirectory(name).FullName;

        public void Dispose() => _root.Delete(recursive: true);
    }
}

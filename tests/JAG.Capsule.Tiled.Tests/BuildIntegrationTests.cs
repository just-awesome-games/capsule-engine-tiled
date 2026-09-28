using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Capsule.Assets;
using Capsule.Scenes.Documents;

namespace JAG.Capsule.Tiled.Tests;

// The seam end to end, from this project's Assets/Scenes maps to shipped content.
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

        Assert.Equal(TiledImporter.ToolName, document.Source?.Tool);
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

        string[] shipping = ShippedByAShippingRun();
        Assert.Contains("scenes/room.scene.json.gz", shipping);
        Assert.DoesNotContain("scenes/dev/scratch.scene.json.gz", shipping);
    }

    // A shipping run of Capsule's build tool, written to a scratch directory. A shipping build of
    // this project would fight the test host for its own bin/ and obj/.
    private static string[] ShippedByAShippingRun()
    {
        string root = Path.GetFullPath(TiledFixtures.Metadata("RepositoryRoot"));
        DirectoryInfo scratch = Directory.CreateTempSubdirectory("capsule-tiled-shipping-");
        try
        {
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
            start.ArgumentList.Add("-p:CapsuleShipping=true");
            start.ArgumentList.Add($"-p:CapsuleObjDir={scratch.FullName}");
            start.ArgumentList.Add($"-p:CapsuleSourcePath={TiledFixtures.Metadata("CapsuleSourcePath")}");

            using Process msbuild = Process.Start(start)!;
            string output = msbuild.StandardOutput.ReadToEnd();
            string errors = msbuild.StandardError.ReadToEnd();
            msbuild.WaitForExit();

            Assert.True(msbuild.ExitCode == 0, output + errors);

            string assets = Path.Combine(scratch.FullName, "assets");
            return [.. Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(assets, file).Replace('\\', '/'))];
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }
}

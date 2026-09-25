using System.Diagnostics;
using System.Text.Json.Nodes;
using Capsule.Assets;
using Capsule.Scenes.Documents;

namespace Capsule.Tiled.Tests;

// The seam end to end, from this project's Assets/Scenes maps to shipped content.
[Collection(SceneWorkspaceCollection.Name)]
public sealed class BuildIntegrationTests
{
    private static string Shipped(string key) =>
        Path.Combine(AppContext.BaseDirectory, "assets", key + ".scene.json");

    [Theory]
    [InlineData("scenes/room")]
    [InlineData("scenes/halls/room")]
    public void TheBuildShipsAMapAsACanonicalSceneDocumentAtItsKey(string key)
    {
        string path = Shipped(key);

        Assert.True(File.Exists(path), $"expected the build to ship {path}");
        Assert.Equal(SceneDocumentFile.ToJson(SceneDocumentFile.Load(path)), File.ReadAllText(path));
    }

    [Theory]
    [InlineData("scenes/room", "Assets/Scenes/room.tmj")]
    [InlineData("scenes/halls/room", "Assets/Scenes/halls/room.tmj")]
    public void TheShippedDocumentKeepsItsMapAsItsProvenance(string key, string source)
    {
        SceneDocument document = SceneDocumentFile.Load(Shipped(key));

        Assert.Equal(TiledImporter.ToolName, document.Source?.Tool);
        Assert.EndsWith(source, document.Source?.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedDocumentNamesANestedAtlasByItsPathUnderAssets()
    {
        SceneDocument document = SceneDocumentFile.Load(Shipped("scenes/halls/room"));

        Assert.Equal(
            new TextureHandle("textures/terrain/tiles", ".png"),
            document.Entries[0].TileMap!.Value.Grid.Texture);
    }

    // The module spells key and handle as the authoring tree does. Capsule normalizes both.
    // Assets/Scenes/UpperHalls/Room02.tmj draws Assets/Textures/CaveWalls/CaveTiles.png.
    [Fact]
    public void TheBuildNormalizesAnAuthoredSpellingIntoTheShippedKeyAndHandle()
    {
        SceneDocument document = SceneDocumentFile.Load(Shipped("scenes/upper-halls/room-02"));

        Assert.Equal(
            new TextureHandle("textures/cave-walls/cave-tiles", ".png"),
            document.Entries[0].TileMap!.Value.Grid.Texture);
    }

    // Assets/Scenes/dev/ holds a .capsuleignore. Its map reaches Capsule in every build but a
    // shipping one.
    [Fact]
    public void AMapUnderAMarkedDirectoryShipsOnlyOutsideAShippingBuild()
    {
        string path = Shipped("scenes/dev/scratch");

        Assert.True(File.Exists(path), $"expected the build to ship {path}");
        Assert.DoesNotContain("Scenes/dev/scratch", HandedToCapsule(shipping: true));
    }

    // Read off the hand-over target. A shipping build of this project would fight the test host for
    // its own bin/.
    private static IEnumerable<string> HandedToCapsule(bool shipping)
    {
        string root = Path.GetFullPath(TiledFixtures.Metadata("RepositoryRoot"));
        ProcessStartInfo start = new("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(Path.Combine(root, "tests", "Capsule.Tiled.Tests", "Capsule.Tiled.Tests.csproj"));
        start.ArgumentList.Add("-t:CapsuleCollectSceneDocuments");
        start.ArgumentList.Add("-getItem:CapsuleSceneDocument");
        // A reused worker node inherits the redirected pipes and holds them open until its idle
        // timeout. ReadToEnd would block that long, so the nodes exit with this build.
        start.ArgumentList.Add("-nodeReuse:false");
        start.ArgumentList.Add($"-p:CapsuleShipping={(shipping ? "true" : "false")}");
        if (TiledFixtures.Metadata("CapsuleUsePackages").Length > 0)
        {
            start.ArgumentList.Add($"-p:CapsuleUsePackages={TiledFixtures.Metadata("CapsuleUsePackages")}");
        }

        using Process msbuild = Process.Start(start)!;
        string output = msbuild.StandardOutput.ReadToEnd();
        string errors = msbuild.StandardError.ReadToEnd();
        msbuild.WaitForExit();

        Assert.True(msbuild.ExitCode == 0, output + errors);

        return JsonNode.Parse(output)!["Items"]!["CapsuleSceneDocument"]!
            .AsArray()
            .Select(static item => item!["CapsuleDocumentKey"]!.GetValue<string>())
            .ToArray();
    }
}

using System.Diagnostics;
using System.Text.Json.Nodes;
using Capsule.Assets;
using Capsule.Scenes.Documents;

namespace Capsule.Tiled.Tests;

// The whole seam, end to end: this project's Assets/Scenes maps through the package's
// targets, Capsule's scene-document hook, and out as shipped content.
[Collection(SceneWorkspaceCollection.Name)]
public sealed class BuildIntegrationTests
{
    private static string Shipped(string key) =>
        Path.Combine(AppContext.BaseDirectory, "assets", "scenes", key + ".scene.json");

    [Theory]
    [InlineData("room")]
    [InlineData("halls/room")]
    public void TheBuildShipsAMapAsACanonicalSceneDocumentAtItsKey(string key)
    {
        string path = Shipped(key);

        Assert.True(File.Exists(path), $"expected the build to ship {path}");
        Assert.Equal(SceneDocumentFile.ToJson(SceneDocumentFile.Load(path)), File.ReadAllText(path));
    }

    [Theory]
    [InlineData("room", "Assets/Scenes/room.tmj")]
    [InlineData("halls/room", "Assets/Scenes/halls/room.tmj")]
    public void TheShippedDocumentKeepsItsMapAsItsProvenance(string key, string source)
    {
        SceneDocument document = SceneDocumentFile.Load(Shipped(key));

        Assert.Equal(TiledImporter.ToolName, document.Source?.Tool);
        Assert.EndsWith(source, document.Source?.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedDocumentNamesANestedAtlasByItsPathUnderTextures()
    {
        SceneDocument document = SceneDocumentFile.Load(Shipped("halls/room"));

        Assert.Equal(
            new TextureHandle("terrain/tiles", ".png"),
            document.Entries[0].TileMap!.Value.Grid.Texture);
    }

    // The module spells key and handle the way its authoring tree does; Capsule normalizes both.
    // Assets/Scenes/UpperHalls/Room02.tmj draws Assets/Textures/CaveWalls/CaveTiles.png.
    [Fact]
    public void TheBuildNormalizesAnAuthoredSpellingIntoTheShippedKeyAndHandle()
    {
        SceneDocument document = SceneDocumentFile.Load(Shipped("upper-halls/room-02"));

        Assert.Equal(
            new TextureHandle("cave-walls/cave-tiles", ".png"),
            document.Entries[0].TileMap!.Value.Grid.Texture);
    }

    // Capsule's development-only marker binds this module's globs as it binds the engine's:
    // Assets/Scenes/dev/ holds a .capsuleignore, so its map is part of every ordinary build and
    // reaches Capsule in no shipping one.
    [Fact]
    public void AMapUnderAMarkedDirectoryShipsOnlyOutsideAShippingBuild()
    {
        string path = Shipped("dev/scratch");

        Assert.True(File.Exists(path), $"expected the build to ship {path}");
        Assert.DoesNotContain("dev/scratch", HandedToCapsule(shipping: true));
    }

    // The keys the module hands Capsule, read off the hand-over target rather than a build output:
    // a shipping build of this project would fight the test host for its own bin/.
    private static IEnumerable<string> HandedToCapsule(bool shipping)
    {
        string root = Path.GetFullPath(SceneDocumentFixtures.Metadata("RepositoryRoot"));
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
        start.ArgumentList.Add($"-p:CapsuleShipping={(shipping ? "true" : "false")}");
        if (SceneDocumentFixtures.Metadata("CapsuleUsePackages").Length > 0)
        {
            start.ArgumentList.Add($"-p:CapsuleUsePackages={SceneDocumentFixtures.Metadata("CapsuleUsePackages")}");
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

using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Capsule.Tiled.Tests;

// Holds the shipped property types to the importer's names and to each other.
[Collection(SceneWorkspaceCollection.Name)]
public sealed class PropertyTypesTests
{
    private const string TypesFile = "capsule-property-types.json";

    private const string ProjectFile = "capsule.tiled-project";

    [Fact]
    public void TheProjectTemplateCarriesTheSameTypesAsTheStandaloneFile()
    {
        Assert.Equal(Canonical(Types(TypesFile)), Canonical(Project()["propertyTypes"]!.AsArray()));
    }

    [Fact]
    public void TheProjectTemplateOpensOnTheDirectoryItSitsIn()
    {
        Assert.Equal(
            ["."],
            Project()["folders"]!.AsArray().Select(static folder => folder!.GetValue<string>()).ToArray());
    }

    [Fact]
    public void TheLayerClassBandsALayerThroughTheZIndexTheImporterReads()
    {
        JsonNode layer = TypeNamed("CapsuleLayer");

        Assert.Equal("class", layer["type"]!.GetValue<string>());

        // A tile's or an object's Class is already its Capsule type. Only a layer's Class is free.
        Assert.Equal(["layer"], layer["useAs"]!.AsArray().Select(static use => use!.GetValue<string>()).ToArray());

        JsonNode member = Assert.Single(layer["members"]!.AsArray())!;
        Assert.Equal("zIndex", member["name"]!.GetValue<string>());
        Assert.Equal("int", member["type"]!.GetValue<string>());
    }

    // The seed lands where a developer opens it. A second build leaves the edited file alone.
    [Fact]
    public void TheBuildSeedsTheProjectOnceAndNeverOverwritesIt()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        Directory.CreateDirectory("sources/Scenes");
        workspace.Write("sources/Scenes/room.tmj", SceneDocumentFixtures.Read("room.tmj"));
        workspace.Write("sources/Scenes/tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        string sources = Path.GetFullPath("sources");
        string seeded = Path.Combine(sources, "Scenes", "Capsule.Tiled.Tests.tiled-project");

        Seed(sources);
        Assert.Equal(SceneDocumentFixtures.Read(ProjectFile), File.ReadAllText(seeded));

        File.WriteAllText(seeded, "{ \"folders\": [\".\", \"halls\"] }");
        Seed(sources);

        Assert.Equal("{ \"folders\": [\".\", \"halls\"] }", File.ReadAllText(seeded));
    }

    private static void Seed(string assetSourcesDir)
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
        start.ArgumentList.Add("-t:CapsuleTiledSeedProject");
        start.ArgumentList.Add($"-p:CapsuleAssetSourcesDir={assetSourcesDir}");
        if (SceneDocumentFixtures.Metadata("CapsuleUsePackages").Length > 0)
        {
            start.ArgumentList.Add($"-p:CapsuleUsePackages={SceneDocumentFixtures.Metadata("CapsuleUsePackages")}");
        }

        using Process msbuild = Process.Start(start)!;
        string output = msbuild.StandardOutput.ReadToEnd() + msbuild.StandardError.ReadToEnd();
        msbuild.WaitForExit();

        Assert.True(msbuild.ExitCode == 0, output);
    }

    private static JsonNode TypeNamed(string name) => Assert.Single(
        Types(TypesFile),
        type => string.Equals(type!["name"]!.GetValue<string>(), name, StringComparison.Ordinal))!;

    private static JsonArray Types(string file) => JsonNode.Parse(SceneDocumentFixtures.Read(file))!.AsArray();

    private static JsonObject Project() => JsonNode.Parse(SceneDocumentFixtures.Read(ProjectFile))!.AsObject();

    // Compared as text rather than by reference equality, which JsonNode does not define.
    private static string Canonical(JsonArray types) => types.ToJsonString();
}

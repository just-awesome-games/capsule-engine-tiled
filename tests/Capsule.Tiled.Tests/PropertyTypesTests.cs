using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using Capsule.Scenes.Documents;

namespace Capsule.Tiled.Tests;

// The shipped Tiled vocabulary. Its whole job is to spell what the importer reads, so these specs
// hold the two files to the importer's property names and to each other.
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
    public void TheCollidableFacesEnumSpellsExactlyTheFacesTheImporterParses()
    {
        JsonNode faces = TypeNamed("CapsuleCollidableFaces");

        Assert.Equal("enum", faces["type"]!.GetValue<string>());

        // The importer reads a comma-separated string and trims it, which is how Tiled writes a
        // string-storage flags enum. Saved as numbers the value would be a bitfield it cannot read.
        Assert.Equal("string", faces["storageType"]!.GetValue<string>());
        Assert.True(faces["valuesAsFlags"]!.GetValue<bool>());
        Assert.Equal(
            TileFaceNames.All,
            faces["values"]!.AsArray().Select(static value => value!.GetValue<string>()).ToArray());
    }

    [Fact]
    public void TheLayerClassBandsALayerThroughTheZIndexTheImporterReads()
    {
        JsonNode layer = TypeNamed("CapsuleLayer");

        Assert.Equal("class", layer["type"]!.GetValue<string>());

        // A tile's Class is its Capsule tile type and an object's Class is its entry type, so a
        // class scoped to either would be read as that type. Only a layer's Class is Capsule's to
        // spend.
        Assert.Equal(["layer"], layer["useAs"]!.AsArray().Select(static use => use!.GetValue<string>()).ToArray());

        JsonNode member = Assert.Single(layer["members"]!.AsArray())!;
        Assert.Equal("zIndex", member["name"]!.GetValue<string>());
        Assert.Equal("int", member["type"]!.GetValue<string>());
    }

    // The whole seed, driven over a scratch scenes root: it lands where a developer opens it, and
    // the second build leaves the one they have since edited alone.
    [Fact]
    public void TheBuildSeedsTheProjectOnceAndNeverOverwritesIt()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        Directory.CreateDirectory("sources/scenes");
        workspace.Write("sources/scenes/room.tmj", SceneDocumentFixtures.Read("room.tmj"));
        workspace.Write("sources/scenes/tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        string sources = Path.GetFullPath("sources");
        string seeded = Path.Combine(sources, "scenes", "Capsule.Tiled.Tests.tiled-project");

        Seed(sources);
        Assert.Equal(SceneDocumentFixtures.Read(ProjectFile), File.ReadAllText(seeded));

        File.WriteAllText(seeded, "{ \"folders\": [\".\", \"halls\"] }");
        Seed(sources);

        Assert.Equal("{ \"folders\": [\".\", \"halls\"] }", File.ReadAllText(seeded));
    }

    private static void Seed(string assetSourcesDir)
    {
        string root = Path.GetFullPath(Metadata("RepositoryRoot"));
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
        if (Metadata("CapsuleUsePackages").Length > 0)
        {
            start.ArgumentList.Add($"-p:CapsuleUsePackages={Metadata("CapsuleUsePackages")}");
        }

        using Process msbuild = Process.Start(start)!;
        string output = msbuild.StandardOutput.ReadToEnd() + msbuild.StandardError.ReadToEnd();
        msbuild.WaitForExit();

        Assert.True(msbuild.ExitCode == 0, output);
    }

    private static string Metadata(string key) =>
        typeof(PropertyTypesTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value ?? string.Empty;

    private static JsonNode TypeNamed(string name) => Assert.Single(
        Types(TypesFile),
        type => string.Equals(type!["name"]!.GetValue<string>(), name, StringComparison.Ordinal))!;

    private static JsonArray Types(string file) => JsonNode.Parse(SceneDocumentFixtures.Read(file))!.AsArray();

    private static JsonObject Project() => JsonNode.Parse(SceneDocumentFixtures.Read(ProjectFile))!.AsObject();

    // Compared as text rather than by reference equality, which JsonNode does not define.
    private static string Canonical(JsonArray types) => types.ToJsonString();
}

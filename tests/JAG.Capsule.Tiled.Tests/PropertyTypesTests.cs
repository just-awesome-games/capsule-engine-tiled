using System.Text.Json.Nodes;

namespace JAG.Capsule.Tiled.Tests;

// Holds the shipped property types to the importer's names and to each other.
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
    public void TheLayerClassBandsALayerThroughTheZIndexTheImporterReads()
    {
        JsonNode layer = TypeNamed("CapsuleLayer");

        Assert.Equal("class", layer["type"]!.GetValue<string>());

        // A tile's or an object's Class is already its Capsule type. Only a layer's Class is free.
        Assert.Equal(["layer"], layer["useAs"]!.AsArray().Select(static use => use!.GetValue<string>()).ToArray());

        JsonNode member = Assert.Single(layer["members"]!.AsArray())!;
        Assert.Equal(LayerImporter.ZIndexProperty, member["name"]!.GetValue<string>());
        Assert.Equal("int", member["type"]!.GetValue<string>());
    }

    private static JsonNode TypeNamed(string name) => Assert.Single(
        Types(TypesFile),
        type => string.Equals(type!["name"]!.GetValue<string>(), name, StringComparison.Ordinal))!;

    private static JsonArray Types(string file) => JsonNode.Parse(TiledFixtures.Read(file))!.AsArray();

    private static JsonObject Project() => JsonNode.Parse(TiledFixtures.Read(ProjectFile))!.AsObject();

    // Compared as text rather than by reference equality, which JsonNode does not define.
    private static string Canonical(JsonArray types) => types.ToJsonString();
}

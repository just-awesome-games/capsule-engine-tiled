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

    // Tiled writes a class member only when the layer changes it. The class's collider is then false, as
    // the importer reads an absent one.
    [Fact]
    public void TheLayerClassCarriesTheLayerPropertiesTheImporterReads()
    {
        JsonNode layer = TypeNamed("CapsuleLayer");

        Assert.Equal("class", layer["type"]!.GetValue<string>());

        // A tile's or an object's Class is already its Capsule type. Only a layer's Class is free.
        Assert.Equal(["layer"], layer["useAs"]!.AsArray().Select(static use => use!.GetValue<string>()).ToArray());

        Assert.Equal(
            [(LayerImporter.ColliderProperty, "bool", "false"), (LayerImporter.ZIndexProperty, "int", "0")],
            layer["members"]!.AsArray().Select(static member => (
                member!["name"]!.GetValue<string>(),
                member["type"]!.GetValue<string>(),
                member["value"]!.ToJsonString())));
    }

    // Tiled writes a class value's members by name, so the order the importer writes them in is its own.
    [Theory]
    [InlineData("Vector2")]
    [InlineData("Rect")]
    public void EachValueClassCarriesTheNumbersTheImporterConverts(string name)
    {
        JsonNode type = TypeNamed(name);
        string[] converted = name == "Vector2" ? TiledProperties.VectorMembers : TiledProperties.RectMembers;

        Assert.Equal(["property"], type["useAs"]!.AsArray().Select(static use => use!.GetValue<string>()).ToArray());
        JsonNode[] members = [.. type["members"]!.AsArray().Select(static member => member!)];
        Assert.Equal(
            converted.Order(StringComparer.Ordinal),
            members.Select(static member => member["name"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        Assert.All(members, static member => Assert.Equal("float", member["type"]!.GetValue<string>()));
    }

    private static JsonNode TypeNamed(string name) => Assert.Single(
        Types(TypesFile),
        type => string.Equals(type!["name"]!.GetValue<string>(), name, StringComparison.Ordinal))!;

    private static JsonArray Types(string file) => JsonNode.Parse(TiledFixtures.Read(file))!.AsArray();

    private static JsonObject Project() => JsonNode.Parse(TiledFixtures.Read(ProjectFile))!.AsObject();

    // Compared as text rather than by reference equality, which JsonNode does not define.
    private static string Canonical(JsonArray types) => types.ToJsonString();
}

using System.Text.Json.Nodes;

namespace JAG.Capsule.Tiled.Tests;

public sealed class PropertyTypesTests
{
    [Fact]
    public void TheProjectTemplateCarriesTheSameTypesAsTheStandaloneFile()
    {
        JsonNode project = JsonNode.Parse(File.ReadAllText(Fixture("capsule.tiled-project")))!;

        Assert.Equal(Types().ToJsonString(), project["propertyTypes"]!.ToJsonString());
    }

    // Tiled writes a layer's class member only when it differs from the default, so the defaults are
    // what the importer reads when a member is absent.
    [Fact]
    public void TheShippedClassesCarryTheMembersTheImporterReads()
    {
        JsonNode layer = TypeNamed("CapsuleLayer");
        Assert.Equal("class", layer["type"]!.GetValue<string>());
        Assert.Equal(["layer"], UseAs(layer));
        Assert.Equal(
            [("collider", "bool", "false"), ("zIndex", "int", "0")],
            Members(layer).Select(static member => (Name(member), member["type"]!.GetValue<string>(), member["value"]!.ToJsonString())));

        foreach ((string name, string[] members) in new[] { ("Vector2", new[] { "x", "y" }), ("Rect", ["bottom", "left", "right", "top"]) })
        {
            JsonNode type = TypeNamed(name);
            Assert.Equal(["property"], UseAs(type));
            Assert.Equal(members, Members(type).Select(Name).Order(StringComparer.Ordinal));
            Assert.All(Members(type), static member => Assert.Equal("float", member["type"]!.GetValue<string>()));
        }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static JsonArray Types() => JsonNode.Parse(File.ReadAllText(Fixture("capsule-property-types.json")))!.AsArray();

    private static JsonNode TypeNamed(string name) => Assert.Single(Types(), type => Name(type!) == name)!;

    private static string[] UseAs(JsonNode type) => [.. type["useAs"]!.AsArray().Select(static use => use!.GetValue<string>())];

    private static JsonNode[] Members(JsonNode type) => [.. type["members"]!.AsArray().Select(static member => member!)];

    private static string Name(JsonNode node) => node["name"]!.GetValue<string>();
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Capsule.Rendering;

namespace JAG.Capsule.Tiled;

internal sealed class TiledProperties(TiledProperty[]? properties)
{
    private static readonly string[] SceneExtensions = [".scene.json", ".tmj"];
    private static readonly string[] VectorMembers = ["x", "y"];
    private static readonly string[] RectMembers = ["left", "top", "right", "bottom"];

    private readonly List<TiledProperty> _remaining = [.. properties ?? []];

    internal string? TakeString(string name) => Take(name, "string") switch
    {
        null => null,
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        { } value => throw NotA(name, value, "string"),
    };

    internal bool TakeBool(string name) => Take(name, "bool") switch
    {
        null => false,
        { ValueKind: JsonValueKind.True or JsonValueKind.False } value => value.GetBoolean(),
        { } value => throw NotA(name, value, "bool"),
    };

    internal int? TakeInt(string name) => Take(name, "int") switch
    {
        null => null,
        { ValueKind: JsonValueKind.Number } value when value.TryGetInt32(out int number) => number,
        { } value => throw NotA(name, value, "int"),
    };

    internal void AddTo(JsonObject members, string directory, string assetRoot, params ReadOnlySpan<string> reserved)
    {
        foreach (TiledProperty property in _remaining)
        {
            string name = property.Name ?? string.Empty;
            if (members.ContainsKey(name) || reserved.Contains(name))
            {
                throw new FormatException($"property '{name}': Capsule writes '{name}' itself; rename or remove the property.");
            }

            if (TiledImporter.Within($"property '{name}'", () => ValueOf(property, directory, assetRoot)) is { } value)
            {
                members[name] = value;
            }
        }
    }

    internal static string Color(string tiled)
    {
        ColorRgba color;
        try
        {
            color = ColorRgba.FromHex(tiled.Length == 9 && tiled[0] == '#' ? string.Concat("#", tiled.AsSpan(3), tiled.AsSpan(1, 2)) : tiled);
        }
        catch (FormatException)
        {
            throw new FormatException($"'{tiled}' is not a #rrggbb or #aarrggbb colour; pick the colour again in Tiled.");
        }

        return color.A == byte.MaxValue
            ? $"#{color.R:x2}{color.G:x2}{color.B:x2}"
            : $"#{color.R:x2}{color.G:x2}{color.B:x2}{color.A:x2}";
    }

    private static JsonNode? ValueOf(TiledProperty property, string directory, string assetRoot)
    {
        JsonElement value = property.Value;
        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        return (property.Type ?? "string") switch
        {
            "int" when property.PropertyType is { } enumType => throw new FormatException(
                $"enum '{enumType}' is stored as a number; set '{enumType}' to save its values as strings in Tiled's Custom Types Editor."),
            "string" or "int" or "float" or "bool" => JsonValue.Create(value),
            "file" => string.IsNullOrEmpty(text) ? null : AssetKeyOf(text, directory, assetRoot),
            "color" => string.IsNullOrEmpty(text) ? null : Color(text),
            "object" when value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int id) && id >= 0 => id == 0 ? null : id,
            "object" => throw new FormatException($"'{value}' is not an object id; pick the object again in Tiled."),
            "class" => ClassValue(value, property.PropertyType ?? "class"),
            "list" => ListValue(value, directory, assetRoot),
            { } other => throw new FormatException(
                $"Capsule does not import a {other} property; use a string, int, float, bool, color, file, object, enum, x/y class, left/top/right/bottom class or list property."),
        };
    }

    // An item takes the form a property of its type takes. An unset item fails, because leaving it out
    // would move every later item.
    private static JsonArray ListValue(JsonElement value, string directory, string assetRoot)
    {
        TiledProperty[] items = value.ValueKind == JsonValueKind.Array
            ? TiledJson.ReadItems(value)
            : throw new FormatException($"'{value}' is not a list; set the list again in Tiled.");
        JsonArray list = [];
        for (int index = 0; index < items.Length; index++)
        {
            TiledProperty item = items[index];
            list.Add(TiledImporter.Within($"item {index}", () => ValueOf(item, directory, assetRoot))
                ?? throw new FormatException($"item {index} is unset; set it or remove it from the list in Tiled."));
        }

        return list;
    }

    private static string AssetKeyOf(string file, string directory, string assetRoot)
    {
        string key = TiledImporter.AssetPathOf(file, directory, assetRoot);
        string? sceneExtension = Array.Find(SceneExtensions, extension => key.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        return sceneExtension is null ? key : key[..^sceneExtension.Length];
    }

    private static JsonArray ClassValue(JsonElement value, string className)
    {
        JsonProperty[] members = value.ValueKind == JsonValueKind.Object ? [.. value.EnumerateObject()] : [];
        string[] names = [.. members.Select(static member => member.Name)];
        string[]? shape = Array.Find([VectorMembers, RectMembers], shape => names.Length == shape.Length && shape.All(names.Contains));
        if (shape is null || members.Any(static member => member.Value.ValueKind != JsonValueKind.Number))
        {
            throw new FormatException(
                $"class '{className}' sets {(names.Length == 0 ? "no member" : string.Join(", ", names))}; Capsule imports a class value that sets exactly the numbers x and y, or exactly left, top, right and bottom, once each. Tiled saves only the members a value sets, so set every member even where it is 0.");
        }

        return [.. shape.Select(member => JsonValue.Create(value.GetProperty(member)))];
    }

    private JsonElement? Take(string name, string type)
    {
        int index = _remaining.FindIndex(property => property.Name == name);
        if (index < 0)
        {
            return null;
        }

        TiledProperty property = _remaining[index];
        _remaining.RemoveAt(index);
        string declared = property.Type ?? "string";

        return declared == type
            ? property.Value
            : throw new FormatException($"property '{name}' is declared {declared}; Capsule reads it as {type}. Change its type to {type} in Tiled.");
    }

    private static FormatException NotA(string name, JsonElement value, string type) =>
        new($"property '{name}' holds '{value}', which is not a valid {type}; correct the value in Tiled.");
}

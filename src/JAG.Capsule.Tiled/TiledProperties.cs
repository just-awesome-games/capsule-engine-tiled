using System.Text.Json;
using System.Text.Json.Nodes;
using Capsule.Rendering;
using Capsule.Scenes;

namespace JAG.Capsule.Tiled;

// What a property's value resolves against: the directory of the file holding it, the asset root, and the Tiled
// project's classes, or null when the game names no project.
internal sealed record PropertyScope(string Directory, string AssetRoot, TiledClass[]? Classes);

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

    // An entry's members, where a property may not take a key the scene document keeps for the entry itself.
    internal void AddToEntry(JsonObject members, PropertyScope scope, params ReadOnlySpan<string> reserved)
    {
        RefuseEntryKeys();
        AddTo(members, scope, reserved);
    }

    // An object layer makes no entry of its own, and its properties still reach each of its objects' entries.
    internal void RefuseEntryKeys()
    {
        foreach (TiledProperty property in _remaining)
        {
            string name = property.Name ?? string.Empty;
            if (SceneDocumentKeys.Entry.Contains(name))
            {
                throw new FormatException(name == SceneDocumentKeys.Entry.Name
                    ? "property 'name': the scene document reserves 'name' for the entry; set the Name field of the object or tile layer instead and remove the property."
                    : $"property '{name}': the scene document reserves '{name}' for the entry, and Capsule sets it from the object's or layer's own fields; rename or remove the property.");
            }
        }
    }

    internal void AddTo(JsonObject members, PropertyScope scope, params ReadOnlySpan<string> reserved)
    {
        foreach (TiledProperty property in _remaining)
        {
            string name = property.Name ?? string.Empty;
            if (members.ContainsKey(name) || reserved.Contains(name))
            {
                throw new FormatException($"property '{name}': Capsule writes '{name}' itself; rename or remove the property.");
            }

            if (TiledImporter.Within($"property '{name}'", () => ValueOf(property, scope)) is { } value)
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

    private static JsonNode? ValueOf(TiledProperty property, PropertyScope scope)
    {
        JsonElement value = property.Value;
        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        return (property.Type ?? "string") switch
        {
            "int" when property.PropertyType is { } enumType => throw new FormatException(
                $"enum '{enumType}' is stored as a number; set '{enumType}' to save its values as strings in Tiled's Custom Types Editor."),
            "string" or "int" or "float" or "bool" => JsonValue.Create(value),
            "file" => string.IsNullOrEmpty(text) ? null : AssetKeyOf(text, scope),
            "color" => string.IsNullOrEmpty(text) ? null : Color(text),
            "object" when value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int id) && id >= 0 => id == 0 ? null : id,
            "object" => throw new FormatException($"'{value}' is not an object id; pick the object again in Tiled."),
            "class" => ClassValue(value, property.PropertyType ?? "class", scope),
            "list" => ListValue(value, scope),
            { } other => throw new FormatException(
                $"Capsule does not import a {other} property; use a string, int, float, bool, color, file, object, enum, class or list property."),
        };
    }

    // An item takes the form a property of its type takes. An unset item fails, because leaving it out
    // would move every later item.
    private static JsonArray ListValue(JsonElement value, PropertyScope scope)
    {
        TiledProperty[] items = value.ValueKind == JsonValueKind.Array
            ? TiledJson.ReadItems(value)
            : throw new FormatException($"'{value}' is not a list; set the list again in Tiled.");
        JsonArray list = [];
        for (int index = 0; index < items.Length; index++)
        {
            TiledProperty item = items[index];
            list.Add(TiledImporter.Within($"item {index}", () => ValueOf(item, scope))
                ?? throw new FormatException($"item {index} is unset; set it or remove it from the list in Tiled."));
        }

        return list;
    }

    private static string AssetKeyOf(string file, PropertyScope scope)
    {
        string key = TiledImporter.AssetPathOf(file, scope.Directory, scope.AssetRoot);
        string? sceneExtension = Array.Find(SceneExtensions, extension => key.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        return sceneExtension is null ? key : key[..^sceneExtension.Length];
    }

    // A class declaring exactly x and y, or left, top, right and bottom, is the array its member reads. Any other class
    // the project declares is an object of its members.
    private static JsonNode ClassValue(JsonElement value, string className, PropertyScope scope)
    {
        JsonProperty[] members = value.ValueKind == JsonValueKind.Object ? [.. value.EnumerateObject()] : [];
        string[] names = [.. members.Select(static member => member.Name)];
        TiledClass? declared = Array.Find(scope.Classes ?? [], type => type.Name == className);
        string[]? shape = ShapeOf(declared is null ? names : [.. declared.Members.Select(static member => member.Name ?? string.Empty)]);
        if (shape is null && declared is not null)
        {
            return ObjectValue(members, declared, scope);
        }

        if (shape is null || ShapeOf(names) != shape || members.Any(static member => member.Value.ValueKind != JsonValueKind.Number))
        {
            throw new FormatException(
                $"class '{className}' sets {(names.Length == 0 ? "no member" : string.Join(", ", names))}; Capsule imports a class value that sets exactly the numbers x and y, or exactly left, top, right and bottom, once each, or any other class the project in TiledImporter.Project declares. Tiled saves only the members a value sets, so set every member of an x/y or left/top/right/bottom class even where it is 0.");
        }

        return new JsonArray([.. shape.Select(member => JsonValue.Create(value.GetProperty(member)))]);
    }

    private static string[]? ShapeOf(string[] names) =>
        Array.Find([VectorMembers, RectMembers], shape => names.Length == shape.Length && shape.All(names.Contains));

    // A map saves a class value's members with no types, so only the project tells a colour or a file from a string.
    private static JsonObject ObjectValue(JsonProperty[] members, TiledClass declared, PropertyScope scope)
    {
        JsonObject read = [];
        foreach (JsonProperty member in members)
        {
            TiledProperty type = Array.Find(declared.Members, candidate => candidate.Name == member.Name)
                ?? throw new FormatException($"class '{declared.Name}' sets '{member.Name}', which the project does not declare; add the member in Tiled's Custom Types Editor or remove it from the value.");
            TiledProperty typed = new() { Name = member.Name, Type = type.Type, PropertyType = type.PropertyType, Value = member.Value };
            if (TiledImporter.Within($"member '{member.Name}'", () => ValueOf(typed, scope)) is { } value)
            {
                read[member.Name] = value;
            }
        }

        return read;
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

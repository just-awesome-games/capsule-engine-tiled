using System.Text.Json;
using Capsule.Rendering;

namespace JAG.Capsule.Tiled;

// The custom properties of one map, layer, object or tile. Each read holds a property to the type
// Tiled declares for it, and every refusal names the owner.
internal sealed class TiledProperties(TiledProperty[]? properties, string owner)
{
    // Tiled omits the type when it writes a string property.
    private const string StringType = "string";
    private const string BoolType = "bool";
    private const string IntType = "int";
    private const string ColorType = "color";
    private const string FloatType = "float";
    private const string FileType = "file";
    private const string ClassType = "class";
    private const string ObjectType = "object";

    private static readonly string[] SceneExtensions = [".scene.json", ".tmj", ".tmx"];

    // The members of the two class values Capsule converts, in the order the document's array writes them.
    private static readonly string[] VectorMembers = ["x", "y"];
    internal static readonly string[] RectMembers = ["left", "top", "right", "bottom"];

    internal string Owner => owner;

    internal string? String(string name) => Value(name, StringType) switch
    {
        null => null,
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        { } value => throw Invalid(name, value.ToString(), StringType),
    };

    // An absent flag is false.
    internal bool Bool(string name) => Value(name, BoolType) switch
    {
        null => false,
        { ValueKind: JsonValueKind.True or JsonValueKind.False } value => value.GetBoolean(),
        { } value => throw Invalid(name, value.ToString(), BoolType),
    };

    internal int? Int(string name) => Value(name, IntType) switch
    {
        null => null,
        { ValueKind: JsonValueKind.Number } value when value.TryGetInt32(out int number) => number,
        { } value => throw Invalid(name, value.ToString(), IntType),
    };

    internal ColorRgba? Color(string name) => Value(name, ColorType) switch
    {
        null => null,
        { ValueKind: JsonValueKind.String } value => OpaqueColor(name, value.GetString()!),
        { } value => throw Invalid(name, value.ToString(), ColorType),
    };

    // A scene's colours are opaque.
    internal ColorRgba OpaqueColor(string name, string text)
    {
        const string expected = "opaque colour";
        ColorRgba parsed = ParseColor(name, text, expected);

        return parsed.A == byte.MaxValue ? parsed : throw Invalid(name, text, expected);
    }

    internal bool Has(string name) =>
        (properties ?? []).Any(property => string.Equals(property.Name, name, StringComparison.Ordinal));

    // Every property but the excluded ones, in the scene document's value forms, as members of the
    // object the writer has open. The engine's build checks each against the authorable members of the
    // entity's, the scene's or the tile type's class. Tiled writes a file relative to the directory of
    // the map or tileset holding the property.
    internal void WriteValues(Utf8JsonWriter writer, ReadOnlySpan<string> excluded, string directory, string assetRoot)
    {
        foreach (TiledProperty property in properties ?? [])
        {
            string name = property.Name ?? string.Empty;
            if (excluded.Contains(name))
            {
                continue;
            }

            switch (property.Type ?? StringType)
            {
                case IntType when property.PropertyType is { } enumType:
                    throw new TiledImportException(
                        $"{owner} has '{name}' of enum '{enumType}' stored as a number; Capsule reads an enum by its member name. Set '{enumType}' to save its values as strings in Tiled's Custom Types Editor.");

                case StringType or IntType or FloatType or BoolType:
                    writer.WritePropertyName(name);
                    property.Value.WriteTo(writer);
                    break;

                // An unset file is an empty string, which the document leaves to the member's initializer.
                case FileType when property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { Length: > 0 } file:
                    writer.WriteString(name, AssetKeyOf(name, file, directory, assetRoot));
                    break;

                case FileType:
                    break;

                // An unset colour is an empty string, which the document leaves to the member's initializer.
                case ColorType when property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { Length: > 0 } text:
                    ColorRgba color = ParseColor(name, text, ColorType);
                    writer.WriteString(name, color.A == byte.MaxValue
                        ? $"#{color.R:x2}{color.G:x2}{color.B:x2}"
                        : $"#{color.R:x2}{color.G:x2}{color.B:x2}{color.A:x2}");
                    break;

                case ColorType:
                    break;

                case ClassType:
                    WriteClass(writer, name, property);
                    break;

                // A Tiled object id is the placement's id in the document. 0 references no object and
                // leaves the member to its initializer.
                case ObjectType when property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int id) && id >= 0:
                    if (id > 0)
                    {
                        writer.WriteNumber(name, id);
                    }

                    break;

                case ObjectType:
                    throw Invalid(name, property.Value.ToString(), "an object id");

                case { } other:
                    throw new TiledImportException(
                        $"{owner} has '{name}' of type {other}, which Capsule does not import. Use a string, int, float, bool, color, file, object, enum, x/y class or left/top/right/bottom class property.");
            }
        }
    }

    // The file's path under the asset root, which Capsule keys by its own spelling rules. A scene
    // document is keyed without its extension, and a map keys the same as the document it imports to.
    private string AssetKeyOf(string name, string file, string directory, string assetRoot)
    {
        string path = Path.GetFullPath(Path.Combine(directory, file));
        if (!TilesetImporter.IsWithin(path, assetRoot))
        {
            throw new TiledImportException(
                $"{owner} has '{name}' at '{file}', which resolves to '{path}'; a scene document names an asset by its path under '{assetRoot}', so move the file under that root.");
        }

        string key = Path.GetRelativePath(assetRoot, path).Replace('\\', '/');
        string? sceneExtension = Array.Find(SceneExtensions, extension => key.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        return sceneExtension is null ? key : key[..^sceneExtension.Length];
    }

    // A class value holding exactly the members x and y is a Vector2, and one holding exactly left, top, right
    // and bottom is a Rect. Tiled writes only the members a value sets, so a value missing one cannot know
    // the class's default for it.
    private void WriteClass(Utf8JsonWriter writer, string name, TiledProperty property)
    {
        string className = property.PropertyType ?? ClassType;
        JsonProperty[] members = property.Value.ValueKind == JsonValueKind.Object ? [.. property.Value.EnumerateObject()] : [];
        string[] shape = members.All(static member => member.Name is "x" or "y") ? VectorMembers : RectMembers;
        if (members.Any(member => !shape.Contains(member.Name) || member.Value.ValueKind != JsonValueKind.Number))
        {
            throw new TiledImportException(
                $"{owner} has '{name}' of class '{className}' with members {string.Join(", ", members.Select(static member => member.Name))}; Capsule converts only a class whose members are the numbers x and y, or the numbers left, top, right and bottom. Use one property per member instead.");
        }

        string every = shape == VectorMembers ? "both x and y" : "all of left, top, right and bottom";
        string[] repeated = [.. members.GroupBy(static member => member.Name).Where(static group => group.Count() > 1).Select(static group => group.Key)];
        if (repeated.Length > 0)
        {
            throw new TiledImportException(
                $"{owner} has '{name}' of class '{className}' setting {string.Join(", ", repeated)} more than once. Set {every} once each.");
        }

        if (members.Length != shape.Length)
        {
            string set = members.Length == 0 ? "no member" : "only " + string.Join(", ", members.Select(static member => member.Name));
            throw new TiledImportException(
                $"{owner} has '{name}' of class '{className}' setting {set}; Tiled saves only the members a value sets. Set {(members.Length == 0 ? "both x and y, or all of left, top, right and bottom," : every)} even where one is 0.");
        }

        writer.WriteStartArray(name);
        foreach (string member in shape)
        {
            property.Value.GetProperty(member).WriteTo(writer);
        }

        writer.WriteEndArray();
    }

    // Tiled writes "#rrggbb" for an opaque colour and "#aarrggbb" otherwise. ColorRgba.FromHex takes
    // the alpha last.
    private ColorRgba ParseColor(string name, string text, string expected)
    {
        try
        {
            return ColorRgba.FromHex(text.Length == 9 && text[0] == '#'
                ? string.Concat("#".AsSpan(), text.AsSpan(3), text.AsSpan(1, 2))
                : text);
        }
        catch (FormatException)
        {
            throw Invalid(name, text, expected);
        }
    }

    internal TiledImportException Invalid(string name, string value, string expected) =>
        Refusal(name, $"'{value}'", expected);

    private JsonElement? Value(string name, string type)
    {
        foreach (TiledProperty property in properties ?? [])
        {
            if (!string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            string declared = property.Type ?? StringType;
            return string.Equals(declared, type, StringComparison.Ordinal)
                ? property.Value
                : throw Refusal(name, $"type {declared}", type);
        }

        return null;
    }

    private TiledImportException Refusal(string name, string found, string expected) =>
        new($"{owner} has '{name}' of {found}; Capsule reads it as {expected}.");
}

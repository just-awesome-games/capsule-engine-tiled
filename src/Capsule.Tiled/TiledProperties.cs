using System.Text.Json;
using Capsule.Rendering;

namespace Capsule.Tiled;

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

    // Every property but the excluded one, in the scene document's value forms, as members of the
    // object the writer has open. The engine's build checks each against the entity class's
    // authorable members.
    internal void WriteEntityValues(Utf8JsonWriter writer, string excluded)
    {
        foreach (TiledProperty property in properties ?? [])
        {
            string name = property.Name ?? string.Empty;
            if (string.Equals(name, excluded, StringComparison.Ordinal))
            {
                continue;
            }

            switch (property.Type ?? StringType)
            {
                case IntType when property.PropertyType is { } enumType:
                    throw new TiledImportException(
                        $"{owner} has '{name}' of enum '{enumType}' stored as a number; Capsule reads an enum by its member name. Set '{enumType}' to save its values as strings in Tiled's Custom Types Editor.");

                case StringType or FileType or IntType or FloatType or BoolType:
                    writer.WritePropertyName(name);
                    property.Value.WriteTo(writer);
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
                    WriteVector(writer, name, property);
                    break;

                case ObjectType:
                    throw new TiledImportException(
                        $"{owner} has '{name}' referencing another object; Capsule imports no object references. Remove the property and find the other entity in code.");

                case { } other:
                    throw new TiledImportException(
                        $"{owner} has '{name}' of type {other}, which Capsule does not import. Use a string, int, float, bool, color, file, enum or x/y class property.");
            }
        }
    }

    // A class value holding exactly the members x and y is a Vector2. Tiled writes only the members
    // an object sets, so a value missing one cannot know the class's default for it.
    private void WriteVector(Utf8JsonWriter writer, string name, TiledProperty property)
    {
        string className = property.PropertyType ?? ClassType;
        JsonProperty[] members = property.Value.ValueKind == JsonValueKind.Object ? [.. property.Value.EnumerateObject()] : [];
        if (members.Any(static member => member.Name is not ("x" or "y") || member.Value.ValueKind != JsonValueKind.Number))
        {
            throw new TiledImportException(
                $"{owner} has '{name}' of class '{className}' with members {string.Join(", ", members.Select(static member => member.Name))}; Capsule converts only a class whose members are the numbers x and y. Give the entity one property per member instead.");
        }

        if (members.Length != 2)
        {
            throw new TiledImportException(
                $"{owner} has '{name}' of class '{className}' setting {(members.Length == 0 ? "neither x nor y" : "only " + members[0].Name)}; Tiled saves only the members an object sets. Set both x and y on the object, even where one is 0.");
        }

        writer.WriteStartArray(name);
        property.Value.GetProperty("x").WriteTo(writer);
        property.Value.GetProperty("y").WriteTo(writer);
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

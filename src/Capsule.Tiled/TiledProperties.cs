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

    // Tiled writes "#rrggbb" for an opaque colour and "#aarrggbb" otherwise. ColorRgba.FromHex takes
    // the alpha last. A scene's colours are opaque.
    internal ColorRgba OpaqueColor(string name, string text)
    {
        const string expected = "opaque colour";
        ColorRgba parsed;
        try
        {
            parsed = ColorRgba.FromHex(text.Length == 9 && text[0] == '#'
                ? string.Concat("#".AsSpan(), text.AsSpan(3), text.AsSpan(1, 2))
                : text);
        }
        catch (FormatException)
        {
            throw Invalid(name, text, expected);
        }

        return parsed.A == byte.MaxValue ? parsed : throw Invalid(name, text, expected);
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

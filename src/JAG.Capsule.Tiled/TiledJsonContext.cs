using System.Text.Json.Serialization;

namespace JAG.Capsule.Tiled;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TiledMap))]
[JsonSerializable(typeof(TiledTileset))]
internal sealed partial class TiledJsonContext : JsonSerializerContext;

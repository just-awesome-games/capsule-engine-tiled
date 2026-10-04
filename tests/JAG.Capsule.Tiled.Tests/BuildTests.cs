using System.IO.Compression;
using Capsule.Scenes.Documents;

namespace JAG.Capsule.Tiled.Tests;

// This project's build imports Assets/ through its build project's TiledImporter and ships the
// scenes beside the test binary.
public sealed class BuildTests
{
    [Fact]
    public void TheBuildShipsAMapAtItsKey()
    {
        using GZipStream shipped = new(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "assets", "scenes/upper-halls/room-02.scene.json.gz")),
            CompressionMode.Decompress);
        using StreamReader reader = new(shipped);

        Assert.Equal("tile-map", SceneDocument.Parse(reader.ReadToEnd()).Entries[0].Type);
    }
}

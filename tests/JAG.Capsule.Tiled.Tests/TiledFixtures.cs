using System.Reflection;
using System.Text.Json;
using Capsule.Scenes.Documents;

namespace JAG.Capsule.Tiled.Tests;

internal static class TiledFixtures
{
    // The repository root and engine clone, for specs that run the test project's asset build.
    internal static string Metadata(string key) =>
        typeof(TiledFixtures).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value ?? string.Empty;

    internal static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    internal static string Read(string name) => File.ReadAllText(Path(name));

    internal static Workspace CopyTiledSources(params string[] names)
    {
        Workspace workspace = new();
        foreach (string name in names)
        {
            string source = name + ".tmj";
            string directory = System.IO.Path.GetDirectoryName(source)!;
            if (directory.Length > 0)
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(Path("room.tmj"), source);

            string tileset = System.IO.Path.Combine(directory, "tiles.tsj");
            if (!File.Exists(tileset))
            {
                File.Copy(Path("tiles.tsj"), tileset);
            }
        }

        return workspace;
    }

    internal static string Mutate(string text, string from, string to)
    {
        Assert.Contains(from, text, StringComparison.Ordinal);
        return text.Replace(from, to, StringComparison.Ordinal);
    }

    // Imports the map beside the tileset in a fresh workspace and returns the refusal.
    internal static TiledImportException ImportFailure(string map, string tileset)
    {
        using Workspace workspace = new();
        workspace.Write("tiles.tsj", tileset);
        string mapPath = workspace.Write("room.tmj", map);

        return Assert.Throws<TiledImportException>(() => MapImporter.Import(mapPath, "."));
    }

    // The members of the document's tile map entry at index.
    internal static JsonElement TileMapOf(SceneDocument document, int index = 0)
    {
        Assert.Equal("tile-map", document.Entries[index].Type);

        return document.Entries[index].Properties!.Value;
    }

    internal static JsonElement[] Palette(SceneDocument document, int index = 0) =>
        [.. TileMapOf(document, index).GetProperty("tileTypes").EnumerateArray()];

    internal sealed class Workspace : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("capsule-tiled-");
        private readonly string _entryDirectory = Directory.GetCurrentDirectory();

        internal Workspace() => Directory.SetCurrentDirectory(_directory.FullName);

        internal string Write(string name, string text)
        {
            string path = System.IO.Path.Combine(_directory.FullName, name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);

            return name;
        }

        // Windows cannot remove a working directory. Leave the tree before deleting it.
        public void Dispose()
        {
            Directory.SetCurrentDirectory(_entryDirectory);
            _directory.Delete(recursive: true);
        }
    }
}

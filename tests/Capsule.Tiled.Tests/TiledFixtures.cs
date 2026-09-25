using System.Reflection;
using Capsule.Scenes.Documents;
using Capsule.Tiles;

namespace Capsule.Tiled.Tests;

internal static class TiledFixtures
{
    // The repository root and build mode, for specs that drive this repository's targets.
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

        return Assert.Throws<TiledImportException>(() => TiledImporter.Import(mapPath, "."));
    }

    internal static TileMapPlacement TileMapOf(SceneDocument document, int index = 0) =>
        document.Entries[index].TileMap!.Value;

    internal static ReadOnlySpan<TileDefinition> Palette(SceneDocument document) =>
        document.Entries[0].TileMap!.Value.Grid.TileTypes;

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

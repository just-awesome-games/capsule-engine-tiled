using System.Reflection;

namespace Capsule.Tiled.Tests;

internal static class SceneDocumentFixtures
{
    // The repository root and build mode, for specs that drive this repository's targets.
    internal static string Metadata(string key) =>
        typeof(SceneDocumentFixtures).Assembly
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

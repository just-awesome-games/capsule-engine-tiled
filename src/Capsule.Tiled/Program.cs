using System.Globalization;
using Capsule.Scenes.Documents;

namespace Capsule.Tiled;

internal static class Program
{
    // JAG.Capsule.Tiled.targets is the only caller. The arguments are positional in this order.
    private const string Usage = """
        Capsule.Tiled --out <dir> --asset-root <dir> [--tile-size <px>] --scenes-from <list.txt>

          Imports every map named in <list.txt> into <dir>/<key>.scene.json. Each line is
          'key|path', with the path relative to the working directory. Tilesets and their images
          lie under --asset-root, and a texture is named by its path there. --tile-size is the
          size the game declares. Exit 0 when all succeeded, 1 when any failed, 2 on a usage error.
        """;

    private const char KeySeparator = '|';

    private const string DocumentExtension = ".scene.json";

    private static int Main(string[] args)
    {
        if (args is not ["--out", string outputDirectory, "--asset-root", string assetRoot, .. string[] rest])
        {
            return UsageError();
        }

        int? tileSize = null;
        if (rest is ["--tile-size", string declared, .. string[] afterTileSize])
        {
            if (!int.TryParse(declared, NumberStyles.None, CultureInfo.InvariantCulture, out int size) || size <= 0)
            {
                return UsageError();
            }

            tileSize = size;
            rest = afterTileSize;
        }

        if (rest is not ["--scenes-from", string listPath])
        {
            return UsageError();
        }

        (string Key, string Path)[] maps;
        try
        {
            maps = ReadList(listPath);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            Console.Error.WriteLine($"{TiledImporter.ToolName}: cannot read the map list '{listPath}': {ex.Message}");
            return 1;
        }

        int failures = 0;
        foreach ((string key, string mapPath) in maps)
        {
            string documentPath = Path.Combine(outputDirectory, key + DocumentExtension);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(documentPath)!);
                SceneDocumentFile.Save(TiledImporter.Import(mapPath, assetRoot, tileSize), documentPath);
                Console.WriteLine($"{TiledImporter.ToolName}: {mapPath} -> {documentPath}");
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                Console.Error.WriteLine($"{mapPath}: {ex.Message}");
                failures++;
            }
        }

        if (failures > 0)
        {
            Console.Error.WriteLine($"{TiledImporter.ToolName}: {failures} of {maps.Length} map(s) failed");
            return 1;
        }

        return 0;
    }

    private static (string Key, string Path)[] ReadList(string listPath) =>
    [
        .. File.ReadAllLines(listPath).Select(static (line, index) => line.Split(KeySeparator) is [{ Length: > 0 } key, { Length: > 0 } path]
            ? (key, path)
            : throw new TiledImportException($"line {index + 1} is '{line}', not 'key|path'.")),
    ];

    private static bool IsReportable(Exception exception) =>
        exception is TiledImportException or SceneDocumentFormatException or IOException or UnauthorizedAccessException;

    private static int UsageError()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}

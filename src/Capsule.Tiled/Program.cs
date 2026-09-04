using System.Globalization;

namespace Capsule.Tiled;

internal static class Program
{
    // JAG.Capsule.Tiled.targets is the only caller; the arguments are positional in this order.
    private const string Usage = """
        Capsule.Tiled --out <dir> [--dependency-root <dir>] [--tile-size <px>] --scenes-from <list.txt>

          Translates every map named in <list.txt> (one 'key|path' per line, the path relative to
          the working directory) into <dir>/<key>.scene.json. --dependency-root confines external
          tilesets and their images to a tracked tree; --tile-size is the size the game declares.
          Exit 0 when all succeeded, 1 when any failed, 2 on a usage error.
        """;

    private static int Main(string[] args)
    {
        if (args is not ["--out", string outputDirectory, .. string[] rest])
        {
            return UsageError();
        }

        string? dependencyRoot = null;
        if (rest is ["--dependency-root", string root, .. string[] afterRoot])
        {
            dependencyRoot = root;
            rest = afterRoot;
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

        return rest is ["--scenes-from", string list]
            ? TiledSceneTool.ImportFromList(outputDirectory, list, tileSize, Console.Out, Console.Error, dependencyRoot)
            : UsageError();
    }

    private static int UsageError()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}

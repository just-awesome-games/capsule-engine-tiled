using System.Globalization;
using Capsule.Scenes.Documents;

namespace JAG.Capsule.Tiled;

internal static class Program
{
    // Capsule's derivation contract, the engine's docs/build-and-publish.md#build-derivations, is the
    // only caller.
    private const string Usage = """
        JAG.Capsule.Tiled <asset root> <out directory> <sources file> [CapsuleTileSize=<px>]

          Imports every map the sources file names, one per line relative to the working directory,
          into <out directory> at the map's path below <asset root> as a .scene.json. Tilesets and
          their images lie under <asset root>, and a texture is named by its path there.
          CapsuleTileSize is the size the game declares, and an empty value declares none. Exit 0
          when all succeeded, 1 when any failed, 2 on a usage error.
        """;

    private const string TileSizeProperty = "CapsuleTileSize=";

    private const string DocumentExtension = ".scene.json";

    private static int Main(string[] args)
    {
        if (args is not [string assetRoot, string outputDirectory, string listPath, .. string[] properties])
        {
            return UsageError();
        }

        int? tileSize = null;
        foreach (string property in properties)
        {
            if (!property.StartsWith(TileSizeProperty, StringComparison.Ordinal))
            {
                return UsageError();
            }

            string declared = property[TileSizeProperty.Length..];
            if (declared.Length == 0)
            {
                continue;
            }

            if (!int.TryParse(declared, NumberStyles.None, CultureInfo.InvariantCulture, out int size) || size <= 0)
            {
                return UsageError();
            }

            tileSize = size;
        }

        string[] maps;
        try
        {
            maps = [.. File.ReadAllLines(listPath).Where(static line => line.Length > 0)];
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            Console.Error.WriteLine($"{TiledImporter.ToolName}: cannot read the map list '{listPath}': {ex.Message}");
            return 1;
        }

        int failures = 0;
        foreach (string mapPath in maps)
        {
            string documentPath = Path.Combine(outputDirectory, Path.ChangeExtension(Path.GetRelativePath(assetRoot, mapPath), DocumentExtension));
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

    private static bool IsReportable(Exception exception) =>
        exception is TiledImportException or SceneDocumentFormatException or IOException or UnauthorizedAccessException;

    private static int UsageError()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}

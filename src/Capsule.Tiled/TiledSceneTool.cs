using System.Text;
using Capsule.Scenes.Documents;

namespace Capsule.Tiled;

internal static class TiledSceneTool
{
    private const string DocumentExtension = ".scene.json";

    internal static int ImportFromList(
        string outputDirectory,
        string listPath,
        int? tileSize,
        TextWriter output,
        TextWriter error,
        string? dependencyRoot = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        TiledSource[] sources;
        try
        {
            sources = TiledSource.Read(listPath);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{TiledImporter.ToolName}: cannot read the source list '{listPath}': {ex.Message}");
            return 1;
        }

        return Import(outputDirectory, sources, tileSize, output, error, dependencyRoot);
    }

    // Imports every source to <outputDirectory>/<key>.scene.json and returns 1 if any failed.
    internal static int Import(
        string outputDirectory,
        IReadOnlyList<TiledSource> sources,
        int? tileSize,
        TextWriter output,
        TextWriter error,
        string? dependencyRoot = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{TiledImporter.ToolName}: cannot create '{outputDirectory}': {ex.Message}");
            return 1;
        }

        Dictionary<string, string> claimedBy = new(StringComparer.OrdinalIgnoreCase);
        int failures = 0;
        foreach (TiledSource source in sources)
        {
            string documentPath = Path.Combine(outputDirectory, source.Key + DocumentExtension);

            if (!claimedBy.TryAdd(documentPath, source.Path))
            {
                error.WriteLine(
                    $"{source.Path}: would overwrite the scene document of '{claimedBy[documentPath]}'; a document is written at the key its map claims, so keys must be unique.");
                failures++;
                continue;
            }

            try
            {
                // A nested key's directory may not exist yet.
                Directory.CreateDirectory(Path.GetDirectoryName(documentPath)!);
                SceneDocumentFile.Save(TiledImporter.Import(source.Path, tileSize, dependencyRoot), documentPath);
                output.WriteLine($"{TiledImporter.ToolName}: {source.Path} -> {documentPath}");
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                error.WriteLine($"{source.Path}: {ex.Message}");
                failures++;
            }
        }

        if (failures > 0)
        {
            error.WriteLine($"{TiledImporter.ToolName}: {failures} of {sources.Count} source(s) failed");
            return 1;
        }

        return 0;
    }

    private static bool IsReportable(Exception exception) =>
        exception is SceneDocumentFormatException or TiledImportException or IOException
            or UnauthorizedAccessException or DecoderFallbackException;
}

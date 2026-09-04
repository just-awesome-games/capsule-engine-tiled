using System.Text;
using Capsule.Scenes.Documents;

namespace Capsule.Tiled;

public static class TiledSceneTool
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
            error.WriteLine($"{TiledImporter.ToolName}: cannot read the source list '{listPath}' — {ex.Message}");
            return 1;
        }

        return Import(outputDirectory, sources, tileSize, output, error, dependencyRoot);
    }

    /// <summary>
    /// Imports every source to <c>&lt;outputDirectory&gt;/&lt;key&gt;.scene.json</c>. Each source is
    /// attempted; a failure is written to <paramref name="error"/> anchored to the map that failed.
    /// </summary>
    /// <param name="outputDirectory">Where the derived documents are written.</param>
    /// <param name="sources">The maps to import, each with the scene key it claims.</param>
    /// <param name="tileSize">The tile size every map must be authored at, or null to impose none.</param>
    /// <param name="output">Progress, one line per source.</param>
    /// <param name="error">Failures.</param>
    /// <param name="dependencyRoot">The asset source root tilesets and their images are confined to.</param>
    /// <returns>0 when every map succeeded, 1 when any failed.</returns>
    public static int Import(
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
            error.WriteLine($"{TiledImporter.ToolName}: cannot create '{outputDirectory}' — {ex.Message}");
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
                // The key nests, so the directory the document lands in may not exist yet.
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

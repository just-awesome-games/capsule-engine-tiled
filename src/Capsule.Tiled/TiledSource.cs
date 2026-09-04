namespace Capsule.Tiled;

/// <summary>
/// One Tiled map and the scene key it claims: its path under the game's <c>scenes</c> root, forward
/// slashes and no extension — <c>highway/room-02</c>, or <c>room-02</c> for a map at the root.
/// </summary>
/// <param name="Key">The map's scenes-root-relative key.</param>
/// <param name="Path">Where the map is, relative to the working directory.</param>
public readonly record struct TiledSource(string Key, string Path)
{
    private const char Separator = '|';

    /// <summary>
    /// The sources a batch file names, one <c>key|path</c> per line. Blank lines are skipped; a
    /// line with no separator is the path alone, keyed by its file name without extension.
    /// </summary>
    internal static TiledSource[] Read(string listPath)
    {
        List<TiledSource> sources = [];

        foreach (string line in File.ReadAllLines(listPath))
        {
            string entry = line.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            int separator = entry.IndexOf(Separator);
            sources.Add(separator < 0
                ? new TiledSource(System.IO.Path.GetFileNameWithoutExtension(entry), entry)
                : new TiledSource(entry[..separator], entry[(separator + 1)..]));
        }

        return [.. sources];
    }
}

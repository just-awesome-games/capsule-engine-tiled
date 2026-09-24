namespace Capsule.Tiled;

// A map path, relative to the working directory, and the scene key it claims.
internal readonly record struct TiledSource(string Key, string Path)
{
    private const char Separator = '|';

    // One 'key|path' per line. A line with no separator is a path keyed by its file name.
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

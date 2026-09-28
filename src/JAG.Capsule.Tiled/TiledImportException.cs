namespace JAG.Capsule.Tiled;

// A FormatException is a source defect to the build, which reports it against the map and builds
// every other source.
internal sealed class TiledImportException : FormatException
{
    public TiledImportException(string message)
        : base(message)
    {
    }

    public TiledImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

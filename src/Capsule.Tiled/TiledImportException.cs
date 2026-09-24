namespace Capsule.Tiled;

internal sealed class TiledImportException : Exception
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

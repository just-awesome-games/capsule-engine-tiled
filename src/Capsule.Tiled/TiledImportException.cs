namespace Capsule.Tiled;

public sealed class TiledImportException : Exception
{
    public TiledImportException()
    {
    }

    public TiledImportException(string message)
        : base(message)
    {
    }

    public TiledImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

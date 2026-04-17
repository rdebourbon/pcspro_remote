namespace PcsRemote.Core;

/// <summary>
/// Represents an error that occurred during a YouTube live streaming operation.
/// </summary>
public class YouTubeStreamException : Exception
{
    /// <summary>
    /// Initialises a new instance with the specified error message.
    /// </summary>
    public YouTubeStreamException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance with the specified error message and inner exception.
    /// </summary>
    public YouTubeStreamException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

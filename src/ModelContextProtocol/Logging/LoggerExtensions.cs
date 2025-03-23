using Microsoft.Extensions.Logging;

namespace ModelContextProtocol.Logging;

/// <summary>
/// Extensions methods for ILogger instances used in MCP protocol.
/// </summary>
public static partial class LoggerExtensions
{
    /// <summary>
    /// Logs the byte representation of a message in UTF-8 encoding.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="endpointName">The name of the endpoint.</param>
    /// <param name="byteRepresentation">The byte representation as a hex string.</param>
    [LoggerMessage(EventId = 39000, Level = LogLevel.Trace, Message = "Transport {EndpointName}: Message bytes (UTF-8): {ByteRepresentation}")]
    public static partial void TransportMessageBytes(this ILogger logger, string endpointName, string byteRepresentation);

    /// <summary>
    /// Logs the byte representation of a message for diagnostic purposes.
    /// This is useful for diagnosing encoding issues with non-ASCII characters.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="endpointName">The name of the endpoint.</param>
    /// <param name="message">The message to log bytes for.</param>
    public static void TransportMessageBytesUtf8(this ILogger logger, string endpointName, string message)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(message);
            var byteRepresentation = string.Join(" ", bytes.Select(b => $"{b:X2}"));
            logger.TransportMessageBytes(endpointName, byteRepresentation);
        }
    }
}

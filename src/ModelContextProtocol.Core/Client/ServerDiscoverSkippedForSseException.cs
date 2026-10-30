namespace ModelContextProtocol.Client;

/// <summary>Signals that AutoDetect selected SSE and the client must initialize instead of discovering.</summary>
internal sealed class ServerDiscoverSkippedForSseException()
    : Exception("AutoDetect selected HTTP+SSE. Use initialize instead of server/discover.");

using System.Net;

namespace ModelContextProtocol.Server;

/// <summary>
/// Configuration options for the TcpServerTransport.
/// </summary>
public class McpServerTcpTransportOptions
{
    /// <summary>
    /// The TCP port to listen on.
    /// </summary>
    public required int Port { get; set; } = 60606;

    /// <summary>
    /// The TCP host to listen on. This is typically the IP address of the server. If not specified, the server will listen on all available network interfaces.
    /// </summary>
    public required IPAddress IpAddress { get; set; } = IPAddress.Any;
}

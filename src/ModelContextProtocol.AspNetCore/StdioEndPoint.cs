using System.Net;
using System.Net.Sockets;

namespace ModelContextProtocol.AspNetCore;

internal sealed class StdioEndPoint : EndPoint
{
    public static StdioEndPoint Instance { get; } = new();

    private StdioEndPoint()
    {
    }

    public override AddressFamily AddressFamily => AddressFamily.Unspecified;

    public override string ToString() => "stdio";
}

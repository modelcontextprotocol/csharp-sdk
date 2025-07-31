using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AspNetCoreMcpServer.Resources;

[McpServerResourceType]
public class SimpleResourceType
{
    [McpServerResource, Description("A direct text resource")]
    public static string DirectTextResource() => "This is a direct resource";
}

using System.Security.Claims;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides a context container that provides access to the server and resources for processing a JSON-RPC message.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="MessageContext"/> encapsulates contextual information for handling any JSON-RPC message,
/// including requests, responses, notifications, and errors. This is the base class for
/// <see cref="RequestContext{TParams}"/>, which adds request-specific properties.
/// </para>
/// <para>
/// This type is typically received as a parameter in message filter delegates registered via
/// <see cref="McpServerFilters.IncomingMessageFilters"/> or <see cref="McpServerFilters.OutgoingMessageFilters"/>.
/// </para>
/// </remarks>
public class MessageContext
{
    /// <summary>The server with which this instance is associated.</summary>
    private McpServer _server;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageContext"/> class with the specified server and JSON-RPC message.
    /// </summary>
    /// <param name="server">The server with which this instance is associated.</param>
    /// <param name="jsonRpcMessage">The JSON-RPC message associated with this context.</param>
    public MessageContext(McpServer server, JsonRpcMessage jsonRpcMessage)
    {
        Throw.IfNull(server);
        Throw.IfNull(jsonRpcMessage);

        _server = server;
        JsonRpcMessage = jsonRpcMessage;
        Services = server.Services;
        User = jsonRpcMessage.Context?.User;
    }

    /// <summary>Gets or sets the server with which this instance is associated.</summary>
    public McpServer Server
    {
        get => _server;
        set
        {
            Throw.IfNull(value);
            _server = value;
        }
    }

    /// <summary>
    /// Gets or sets a key/value collection that can be used to share data within the scope of this message.
    /// </summary>
    public IDictionary<string, object?> Items
    {
        get => field ??= new Dictionary<string, object?>();
        set => field = value;
    }

    /// <summary>Gets or sets the services associated with this message.</summary>
    /// <remarks>
    /// This provider might not be the same instance stored in <see cref="McpServer.Services"/>
    /// if <see cref="McpServerOptions.ScopeRequests"/> was true, in which case this
    /// might be a scoped <see cref="IServiceProvider"/> derived from the server's
    /// <see cref="McpServer.Services"/>.
    /// </remarks>
    public IServiceProvider? Services { get; set; }

    /// <summary>Gets or sets the user associated with this message.</summary>
    public ClaimsPrincipal? User { get; set; }

    /// <summary>
    /// Gets the JSON-RPC message associated with this context.
    /// </summary>
    /// <remarks>
    /// This property provides access to the complete JSON-RPC message,
    /// including the method name (for requests/notifications), request ID (for requests/responses),
    /// and associated transport and user information.
    /// </remarks>
    public JsonRpcMessage JsonRpcMessage { get; }
}

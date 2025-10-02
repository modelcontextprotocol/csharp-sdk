﻿using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides a container for handlers used in the creation of an MCP client.
/// </summary>
/// <remarks>
/// <para>
/// This class provides a centralized collection of delegates that implement various capabilities of the Model Context Protocol.
/// </para>
/// <para>
/// Each handler in this class corresponds to a specific client endpoint in the Model Context Protocol and
/// is responsible for processing a particular type of message. The handlers are used to customize
/// the behavior of the MCP server by providing implementations for the various protocol operations.
/// </para>
/// <para>
/// When a server sends a message to the client, the appropriate handler is invoked to process it
/// according to the protocol specification. Which handler is selected
/// is done based on an ordinal, case-sensitive string comparison.
/// </para>
/// </remarks>
public class McpClientHandlers
{
    /// <summary>Gets or sets notification handlers to register with the client.</summary>
    /// <remarks>
    /// <para>
    /// When constructed, the client will enumerate these handlers once, which may contain multiple handlers per notification method key.
    /// The client will not re-enumerate the sequence after initialization.
    /// </para>
    /// <para>
    /// Notification handlers allow the client to respond to server-sent notifications for specific methods.
    /// Each key in the collection is a notification method name, and each value is a callback that will be invoked
    /// when a notification with that method is received.
    /// </para>
    /// <para>
    /// Handlers provided via <see cref="NotificationHandlers"/> will be registered with the client for the lifetime of the client.
    /// For transient handlers, <see cref="IMcpEndpoint.RegisterNotificationHandler"/> may be used to register a handler that can
    /// then be unregistered by disposing of the <see cref="IAsyncDisposable"/> returned from the method.
    /// </para>
    /// </remarks>
    public IEnumerable<KeyValuePair<string, Func<JsonRpcNotification, CancellationToken, ValueTask>>>? NotificationHandlers { get; set; }

    /// <summary>
    /// Gets or sets the handler for <see cref="RequestMethods.RootsList"/> requests.
    /// </summary>
    /// <remarks>
    /// This handler is invoked when a client sends a <see cref="RequestMethods.RootsList"/> request to retrieve available roots.
    /// The handler receives request parameters and should return a <see cref="ListRootsResult"/> containing the collection of available roots.
    /// </remarks>
    public Func<ListRootsRequestParams?, CancellationToken, ValueTask<ListRootsResult>>? RootsHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for processing <see cref="RequestMethods.ElicitationCreate"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler function is called when an MCP server requests the client to provide additional
    /// information during interactions. The client must set this property for the elicitation capability to work.
    /// </para>
    /// <para>
    /// The handler receives message parameters and a cancellation token.
    /// It should return a <see cref="ElicitResult"/> containing the response to the elicitation request.
    /// </para>
    /// </remarks>
    public Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? ElicitationHandler { get; set; }

    /// <summary>
    /// Gets or sets the handler for processing <see cref="RequestMethods.SamplingCreateMessage"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler function is called when an MCP server requests the client to generate content
    /// using an AI model. The client must set this property for the sampling capability to work.
    /// </para>
    /// <para>
    /// The handler receives message parameters, a progress reporter for updates, and a 
    /// cancellation token. It should return a <see cref="CreateMessageResult"/> containing the 
    /// generated content.
    /// </para>
    /// <para>
    /// You can create a handler using the <see cref="McpClientExtensions.CreateSamplingHandler"/> extension
    /// method with any implementation of <see cref="IChatClient"/>.
    /// </para>
    /// </remarks>
    public Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>, CancellationToken, ValueTask<CreateMessageResult>>? SamplingHandler { get; set; }
}

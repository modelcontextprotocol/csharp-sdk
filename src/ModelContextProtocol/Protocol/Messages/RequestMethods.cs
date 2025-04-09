namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Provides constants with the names of common request methods used in the MCP protocol.
/// </summary>
public static class RequestMethods
{
    /// <summary>
    /// The name of the request method sent from the client to request a list of the server's tools.
    /// </summary>
    public const string ToolsList = "tools/list";

    /// <summary>
    /// The name of the request method sent from the client to request that the server invoke a specific tool.
    /// </summary>
    public const string ToolsCall = "tools/call";

    /// <summary>
    /// The name of the request method sent from the client to request a list of the server's prompts.
    /// </summary>
    public const string PromptsList = "prompts/list";

    /// <summary>
    /// The name of the request method sent by the client to get a prompt provided by the server.
    /// </summary>
    public const string PromptsGet = "prompts/get";

    /// <summary>
    /// The name of the request method sent from the client to request a list of the server's resources.
    /// </summary>
    public const string ResourcesList = "resources/list";

    /// <summary>
    /// The name of the request method sent from the client to read a specific server resource.
    /// </summary>
    public const string ResourcesRead = "resources/read";

    /// <summary>
    /// The name of the request method sent from the client to request a list of the server's resource templates.
    /// </summary>
    public const string ResourcesTemplatesList = "resources/templates/list";

    /// <summary>
    /// The name of the request method sent from the client to request <see cref="NotificationMethods.ResourceUpdatedNotification"/> 
    /// notifications from the server whenever a particular resource changes.
    /// </summary>
    public const string ResourcesSubscribe = "resources/subscribe";

    /// <summary>
    /// The name of the request method sent from the client to request unsubscribing from <see cref="NotificationMethods.ResourceUpdatedNotification"/> 
    /// notifications from the server.
    /// </summary>
    public const string ResourcesUnsubscribe = "resources/unsubscribe";

    /// <summary>
    /// The name of the request method sent from the server to request a list of the client's roots.
    /// </summary>
    public const string RootsList = "roots/list";

    /// <summary>
    /// The name of the request method sent by either endpoint to check that the connected endpoint is still alive.
    /// </summary>
    public const string Ping = "ping";

    /// <summary>
    /// The name of the request method sent from the client to the server to adjust the logging level.
    /// </summary>
    /// <remarks>
    /// This request allows clients to control which log messages they receive from the server
    /// by setting a minimum severity threshold. After processing this request, the server will
    /// send log messages with severity at or above the specified level to the client as
    /// <see cref="NotificationMethods.LoggingMessageNotification"/> notifications.
    /// </remarks>
    public const string LoggingSetLevel = "logging/setLevel";

    /// <summary>
    /// The name of the request method sent from the client to the server to ask for completion suggestions.
    /// </summary>
    /// <remarks>
    /// This is used to provide autocompletion-like functionality for arguments in a resource reference or a prompt template.
    /// The client provides a reference (resource or prompt), argument name, and partial value, and the server 
    /// responds with matching completion options.
    /// </remarks>
    public const string CompletionComplete = "completion/complete";

    /// <summary>
    /// The name of the request method sent from the server to sample an large language model (LLM) via the client.
    /// </summary>
    /// <remarks>
    /// This request allows servers to utilize an LLM available on the client side to generate text or image responses
    /// based on provided messages. It is part of the sampling capability in the Model Context Protocol and enables servers to access
    /// client-side AI models without needing direct API access to those models.
    /// </remarks>
    public const string SamplingCreateMessage = "sampling/createMessage";

    /// <summary>
    /// The name of the request method sent from the client to the server when it first connects, asking it initialize.
    /// </summary>
    /// <remarks>
    /// The initialize request is the first request sent by the client to the server. It provides client information
    /// and capabilities to the server during connection establishment. The server responds with its own capabilities
    /// and information, establishing the protocol version and available features for the session.
    /// </remarks>
    public const string Initialize = "initialize";
}
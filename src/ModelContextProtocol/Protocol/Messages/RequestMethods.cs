namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Provides names for request methods used in the Model Context Protocol (MCP).
/// </summary>
public static class RequestMethods
{
    /// <summary>
    /// Sent from the client to request a list of tools the server has.
    /// </summary>
    public const string ToolsList = "tools/list";

    /// <summary>
    /// Used by the client to invoke a tool provided by the server.
    /// </summary>
    public const string ToolsCall = "tools/call";

    /// <summary>
    /// Sent from the client to request a list of prompts and prompt templates the server has.
    /// </summary>
    public const string PromptsList = "prompts/list";

    /// <summary>
    /// Used by the client to get a prompt provided by the server.
    /// </summary>
    public const string PromptsGet = "prompts/get";

    /// <summary>
    /// Sent from the client to request a list of resources the server has.
    /// </summary>
    public const string ResourcesList = "resources/list";

    /// <summary>
    /// Sent from the client to the server, to read a specific resource URI.
    /// </summary>
    public const string ResourcesRead = "resources/read";

    /// <summary>
    /// Sent from the client to request a list of resource templates the server has.
    /// </summary>
    public const string ResourcesTemplatesList = "resources/templates/list";

    /// <summary>
    /// Sent from the client to request resources/updated notifications from the server whenever a particular resource changes.
    /// </summary>
    public const string ResourcesSubscribe = "resources/subscribe";

    /// <summary>
    /// Sent from the client to request cancellation of resources/updated notifications from the server.
    /// </summary>
    public const string ResourcesUnsubscribe = "resources/unsubscribe";

    /// <summary>
    /// Sent from the server to request a list of root URIs from the client.
    /// </summary>
    public const string RootsList = "roots/list";

    /// <summary>
    /// A ping, issued by either the server or the client, to check that the other party is still alive.
    /// </summary>
    public const string Ping = "ping";

    /// <summary>
    /// A request from the client to the server, to enable or adjust logging.
    /// </summary>
    public const string LoggingSetLevel = "logging/setLevel";

    /// <summary>
    /// A request from the client to the server, to ask for completion options.
    /// </summary>
    public const string CompletionComplete = "completion/complete";

    /// <summary>
    /// A request from the server to sample an LLM via the client.
    /// </summary>
    public const string SamplingCreateMessage = "sampling/createMessage";

    /// <summary>
    /// This request is sent from the client to the server when it first connects, asking it to begin initialization.
    /// </summary>
    public const string Initialize = "initialize";
}
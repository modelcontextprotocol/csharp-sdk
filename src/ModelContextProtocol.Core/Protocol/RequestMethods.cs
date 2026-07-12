namespace ModelContextProtocol.Protocol;

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
    [Obsolete(Obsoletions.DeprecatedRoots_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
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
    [Obsolete(Obsoletions.DeprecatedLogging_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
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
    /// The name of the request method sent from the server to sample a large language model (LLM) via the client.
    /// </summary>
    /// <remarks>
    /// This request allows servers to utilize an LLM available on the client side to generate text or image responses
    /// based on provided messages. It is part of the sampling capability in the Model Context Protocol and enables servers to access
    /// client-side AI models without needing direct API access to those models.
    /// </remarks>
    [Obsolete(Obsoletions.DeprecatedSampling_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public const string SamplingCreateMessage = "sampling/createMessage";

    /// <summary>
    /// The name of the request method sent from the server to elicit additional information from the user via the client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This request is used when the server needs more information from the client to proceed with a task or interaction.
    /// Servers can request structured data from users, with optional JSON schemas to validate responses (form mode),
    /// or request URL mode (out-of-band) user interaction via navigation for sensitive operations.
    /// </para>
    /// <para>
    /// Two modes are supported:
    /// <list type="bullet">
    ///   <item><description><b>form</b>: In-band elicitation where structured data is collected and returned to the server</description></item>
    ///   <item><description><b>url</b>: URL mode (out-of-band) elicitation for sensitive operations like OAuth or payments</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public const string ElicitationCreate = "elicitation/create";

    /// <summary>
    /// The name of the request method sent from the client to the server when it first connects, asking it to initialize.
    /// </summary>
    /// <remarks>
    /// The initialize request is the first request sent by the client to the server. It provides client information
    /// and capabilities to the server during connection establishment. The server responds with its own capabilities
    /// and information, establishing the protocol version and available features for the session.
    /// </remarks>
    public const string Initialize = "initialize";

    /// <summary>
    /// The name of the request method sent from the client to poll for task completion.
    /// </summary>
    /// <remarks>
    /// Part of the <c>io.modelcontextprotocol/tasks</c> extension.
    /// Clients poll for task status by sending this request with the task ID.
    /// </remarks>
    public const string TasksGet = "tasks/get";

    /// <summary>
    /// The name of the request method sent from the client to provide input responses to a task.
    /// </summary>
    /// <remarks>
    /// Part of the <c>io.modelcontextprotocol/tasks</c> extension.
    /// Used when a task has <c>input_required</c> status and the client needs to fulfill outstanding requests.
    /// </remarks>
    public const string TasksUpdate = "tasks/update";

    /// <summary>
    /// The name of the request method sent from the client to signal intent to cancel a task.
    /// </summary>
    /// <remarks>
    /// Part of the <c>io.modelcontextprotocol/tasks</c> extension.
    /// Cancellation is cooperative — the server decides whether and when to honor it.
    /// </remarks>
    public const string TasksCancel = "tasks/cancel";

    /// <summary>
    /// The name of the request method sent from the client to discover the server's protocol versions,
    /// capabilities, and metadata.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This RPC is introduced in the 2026-07-28 protocol revision (SEP-2575) as the canonical way for a client
    /// to learn what a server supports without performing the <c>initialize</c> handshake.
    /// </para>
    /// <para>
    /// The server's response includes its supported protocol versions, capabilities, implementation
    /// information, and optional usage instructions.
    /// </para>
    /// <para>
    /// Servers SHOULD implement this method. Initialize-handshake clients MAY ignore it. Clients on the
    /// 2026-07-28 revision typically call this once during connection establishment.
    /// </para>
    /// </remarks>
    public const string ServerDiscover = "server/discover";

    /// <summary>
    /// The name of the request method sent from the client to open a long-lived subscription for
    /// receiving server-to-client notifications outside of a specific request's response stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This RPC is introduced in the 2026-07-28 protocol revision (SEP-2575) and replaces the unsolicited
    /// HTTP GET endpoint and the initialize-handshake <see cref="ResourcesSubscribe"/> / <see cref="ResourcesUnsubscribe"/>
    /// request methods.
    /// </para>
    /// <para>
    /// The request opens a response stream on which the server first sends a
    /// <see cref="NotificationMethods.SubscriptionsAcknowledgedNotification"/> describing the granted
    /// notifications, and then streams matching notifications until the subscription is cancelled.
    /// </para>
    /// </remarks>
    public const string SubscriptionsListen = "subscriptions/listen";
}
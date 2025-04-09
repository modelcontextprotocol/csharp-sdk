using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides extension methods for interacting with an <see cref="IMcpClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class contains extension methods that simplify common operations with an MCP client,
/// such as pinging a server, listing and working with tools, prompts, and resources, and
/// managing subscriptions to resources.
/// </para>
/// <para>
/// These methods build on the core functionality provided by <see cref="IMcpClient"/> to offer
/// a more convenient API for client applications.
/// </para>
/// </remarks>
public static class McpClientExtensions
{
    /// <summary>
    /// Sends a ping request to verify server connectivity.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that completes when the ping is successful.</returns>
    /// <remarks>
    /// <para>
    /// This method is used to check if the MCP server is online and responding to requests.
    /// It can be useful for health checking, ensuring the connection is established, or verifying 
    /// that the client has proper authorization to communicate with the server.
    /// </para>
    /// <para>
    /// The ping operation is lightweight and does not require any parameters. A successful completion
    /// of the task indicates that the server is operational and accessible.
    /// </para>
    /// </remarks>
    /// <exception cref="McpException">Thrown when the server cannot be reached or returns an error response.</exception>
    /// <example>
    /// <code>
    /// // Check if the server is responsive
    /// try
    /// {
    ///     await mcpClient.PingAsync();
    ///     Console.WriteLine("Server is online and responding");
    /// }
    /// catch (McpException ex)
    /// {
    ///     Console.WriteLine($"Server is not responding: {ex.Message}");
    /// }
    /// 
    /// // With cancellation token
    /// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    /// await mcpClient.PingAsync(cts.Token);
    /// </code>
    /// </example>
    public static Task PingAsync(this IMcpClient client, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        return client.SendRequestAsync(
            RequestMethods.Ping,
            parameters: null,
            McpJsonUtilities.JsonContext.Default.Object!,
            McpJsonUtilities.JsonContext.Default.Object,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Retrieves a list of available tools from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="serializerOptions">The serializer options governing tool parameter serialization. If null, the default options will be used.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available tools as <see cref="McpClientTool"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method fetches all available tools from the MCP server and returns them as a complete list.
    /// It automatically handles pagination with cursors if the server has many tools.
    /// </para>
    /// <para>
    /// For servers with a large number of tools, consider using <see cref="EnumerateToolsAsync"/> instead,
    /// which streams tools as they arrive rather than loading them all into memory at once.
    /// </para>
    /// <para>
    /// The serializer options provided are flowed to each <see cref="McpClientTool"/> and will be used
    /// when invoking tools with parameters.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Get all tools available on the server
    /// var tools = await mcpClient.ListToolsAsync();
    /// 
    /// // Display information about each tool
    /// foreach (var tool in tools)
    /// {
    ///     Console.WriteLine($"Tool: {tool.Name}");
    ///     
    ///     // Use tools with an AI client
    ///     var chatOptions = new ChatOptions
    ///     {
    ///         Tools = [.. tools]
    ///     };
    ///     
    ///     await foreach (var response in chatClient.GetStreamingResponseAsync(userMessage, chatOptions))
    ///     {
    ///         Console.Write(response);
    ///     }
    /// }
    /// </code>
    /// </example>
    public static async Task<IList<McpClientTool>> ListToolsAsync(
        this IMcpClient client,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        serializerOptions ??= McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        List<McpClientTool>? tools = null;
        string? cursor = null;
        do
        {
            var toolResults = await client.SendRequestAsync(
                RequestMethods.ToolsList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListToolsRequestParams,
                McpJsonUtilities.JsonContext.Default.ListToolsResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            tools ??= new List<McpClientTool>(toolResults.Tools.Count);
            foreach (var tool in toolResults.Tools)
            {
                tools.Add(new McpClientTool(client, tool, serializerOptions));
            }

            cursor = toolResults.NextCursor;
        }
        while (cursor is not null);

        return tools;
    }

    /// <summary>
    /// Creates an enumerable for asynchronously enumerating all available tools from the server.
    /// This method provides a streaming approach to access tools, yielding each tool as it's retrieved.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="serializerOptions">The serializer options governing tool parameter serialization. If null, the default options will be used.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An asynchronous sequence of all available tools as <see cref="McpClientTool"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method uses async enumeration to retrieve tools from the server, which allows processing tools
    /// as they arrive rather than waiting for all tools to be retrieved. This can be more memory-efficient
    /// when dealing with servers that expose a large number of tools.
    /// </para>
    /// <para>
    /// The method automatically handles pagination with cursors if the server has many tools.
    /// </para>
    /// <para>
    /// Unlike <see cref="ListToolsAsync"/> which loads all tools into memory at once, this method yields
    /// tools one at a time as they are received, allowing for better memory efficiency.
    /// </para>
    /// <para>
    /// The serializer options provided are flowed to each <see cref="McpClientTool"/> and will be used
    /// when invoking tools with parameters.
    /// </para>
    /// <para>
    /// Every iteration through the returned <see cref="IAsyncEnumerable{McpClientTool}"/>
    /// will result in requerying the server and yielding the sequence of available tools.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Enumerate all tools available on the server
    /// await foreach (var tool in client.EnumerateToolsAsync(cancellationToken: CancellationToken.None))
    /// {
    ///     Console.WriteLine($"Tool: {tool.Name}");
    ///     
    ///     // Invoke a tool when you find the one you need
    ///     if (tool.Name == "Calculator")
    ///     {
    ///         var result = await tool.InvokeAsync(
    ///             new Dictionary&lt;string, object?&gt; { ["x"] = 10, ["y"] = 5 }, 
    ///             CancellationToken.None);
    ///         Console.WriteLine($"Result: {result}");
    ///     }
    /// }
    /// </code>
    /// </example>
    public static async IAsyncEnumerable<McpClientTool> EnumerateToolsAsync(
        this IMcpClient client,
        JsonSerializerOptions? serializerOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        serializerOptions ??= McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        string? cursor = null;
        do
        {
            var toolResults = await client.SendRequestAsync(
                RequestMethods.ToolsList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListToolsRequestParams,
                McpJsonUtilities.JsonContext.Default.ListToolsResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var tool in toolResults.Tools)
            {
                yield return new McpClientTool(client, tool, serializerOptions);
            }

            cursor = toolResults.NextCursor;
        }
        while (cursor is not null);
    }

    /// <summary>
    /// Retrieves a list of available prompts from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available prompts as <see cref="McpClientPrompt"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method fetches all available prompts from the MCP server and returns them as a complete list.
    /// It automatically handles pagination with cursors if the server has many prompts.
    /// </para>
    /// <para>
    /// For servers with a large number of prompts, consider using <see cref="EnumeratePromptsAsync"/> instead,
    /// which streams prompts as they arrive rather than loading them all into memory at once.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Get all prompts available on the server
    /// var prompts = await client.ListPromptsAsync(cancellationToken);
    /// 
    /// // Display information about each prompt
    /// foreach (var prompt in prompts)
    /// {
    ///     Console.WriteLine($"Prompt: {prompt.Name}");
    ///     
    ///     if (prompt.Arguments?.Count > 0)
    ///     {
    ///         Console.WriteLine("Arguments:");
    ///         foreach (var arg in prompt.Arguments)
    ///         {
    ///             Console.WriteLine($"  - {arg.Name}: {arg.Description} (Required: {arg.Required})");
    ///         }
    ///     }
    /// }
    /// </code>
    /// </example>
    public static async Task<IList<McpClientPrompt>> ListPromptsAsync(
        this IMcpClient client, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        List<McpClientPrompt>? prompts = null;
        string? cursor = null;
        do
        {
            var promptResults = await client.SendRequestAsync(
                RequestMethods.PromptsList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListPromptsRequestParams,
                McpJsonUtilities.JsonContext.Default.ListPromptsResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            prompts ??= new List<McpClientPrompt>(promptResults.Prompts.Count);
            foreach (var prompt in promptResults.Prompts)
            {
                prompts.Add(new McpClientPrompt(client, prompt));
            }

            cursor = promptResults.NextCursor;
        }
        while (cursor is not null);

        return prompts;
    }

    /// <summary>
    /// Creates an enumerable for asynchronously enumerating all available prompts from the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An asynchronous sequence of all available prompts.</returns>
    /// <remarks>
    /// <para>
    /// This method provides a streaming approach to access all available prompts from the server using 
    /// C#'s asynchronous enumeration pattern. It automatically handles pagination with cursors when the server 
    /// has large numbers of prompts.
    /// </para>
    /// <para>
    /// Unlike <see cref="ListPromptsAsync"/> which loads all prompts into memory at once, this method yields 
    /// prompts as they are received, allowing for better memory efficiency when working with large collections.
    /// </para>
    /// <para>
    /// Each <see cref="Prompt"/> in the returned sequence contains metadata such as the prompt's name, 
    /// description, and any arguments it accepts for template customization.
    /// </para>
    /// <para>
    /// Every iteration through the returned <see cref="IAsyncEnumerable{Prompt}"/>
    /// will result in requerying the server and yielding the sequence of available prompts.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Enumerate all prompts available on the server
    /// await foreach (var prompt in client.EnumeratePromptsAsync(cancellationToken))
    /// {
    ///     Console.WriteLine($"Prompt: {prompt.Name}");
    ///     
    ///     if (prompt.Arguments?.Count > 0)
    ///     {
    ///         Console.WriteLine("Arguments:");
    ///         foreach (var arg in prompt.Arguments)
    ///         {
    ///             Console.WriteLine($"  - {arg.Name}: {arg.Description} (Required: {arg.Required})");
    ///         }
    ///     }
    /// }
    /// </code>
    /// </example>
    public static async IAsyncEnumerable<Prompt> EnumeratePromptsAsync(
        this IMcpClient client, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        string? cursor = null;
        do
        {
            var promptResults = await client.SendRequestAsync(
                RequestMethods.PromptsList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListPromptsRequestParams,
                McpJsonUtilities.JsonContext.Default.ListPromptsResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var prompt in promptResults.Prompts)
            {
                yield return prompt;
            }

            cursor = promptResults.NextCursor;
        }
        while (cursor is not null);
    }

    /// <summary>
    /// Retrieves a specific prompt with optional arguments from the MCP server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="name">The name of the prompt to retrieve.</param>
    /// <param name="arguments">Optional arguments for the prompt. Keys are argument names, and values are the argument values.</param>
    /// <param name="serializerOptions">The serialization options governing argument serialization.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the prompt's result with content and messages.</returns>
    /// <remarks>
    /// <para>
    /// This method sends a request to the MCP server to execute the specified prompt with the provided arguments.
    /// The server will process the prompt and return a result containing messages or other content.
    /// </para>
    /// <para>
    /// Arguments are serialized into JSON and passed to the server, where they may be used to customize the 
    /// prompt's behavior or content. Each prompt may have different argument requirements.
    /// </para>
    /// <para>
    /// The returned <see cref="GetPromptResult"/> contains a collection of <see cref="PromptMessage"/> objects,
    /// which can be converted to <see cref="ChatMessage"/> objects using the <see cref="AIContentExtensions.ToChatMessages"/> method.
    /// </para>
    /// </remarks>
    /// <exception cref="McpException">Thrown when the prompt does not exist, when required arguments are missing, or when the server encounters an error processing the prompt.</exception>
    /// <example>
    /// <code>
    /// // Get a simple prompt with no arguments
    /// var result = await mcpClient.GetPromptAsync("simple_prompt");
    /// 
    /// // Get a prompt with arguments
    /// var arguments = new Dictionary&lt;string, object?&gt;
    /// {
    ///     ["temperature"] = "0.7",
    ///     ["style"] = "formal"
    /// };
    /// var result = await mcpClient.GetPromptAsync("complex_prompt", arguments);
    /// 
    /// // Access the prompt messages
    /// foreach (var message in result.Messages)
    /// {
    ///     Console.WriteLine($"{message.Role}: {message.Content.Text}");
    /// }
    /// 
    /// // Convert to ChatMessages for use with AI clients
    /// var chatMessages = result.ToChatMessages();
    /// </code>
    /// </example>
    public static Task<GetPromptResult> GetPromptAsync(
        this IMcpClient client,
        string name,
        IReadOnlyDictionary<string, object?>? arguments = null,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);
        Throw.IfNullOrWhiteSpace(name);
        serializerOptions ??= McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        return client.SendRequestAsync(
            RequestMethods.PromptsGet,
            new() { Name = name, Arguments = ToArgumentsDictionary(arguments, serializerOptions) },
            McpJsonUtilities.JsonContext.Default.GetPromptRequestParams,
            McpJsonUtilities.JsonContext.Default.GetPromptResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Retrieves a list of available resource templates from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available resource templates as <see cref="ResourceTemplate"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method fetches all available resource templates from the MCP server and returns them as a complete list.
    /// It automatically handles pagination with cursors if the server has many resource templates.
    /// </para>
    /// <para>
    /// Each <see cref="ResourceTemplate"/> contains metadata about available resource templates on the server,
    /// including template name, description, and schema information defining how resources of this template
    /// type should be structured.
    /// </para>
    /// <para>
    /// For servers with a large number of resource templates, consider using <see cref="EnumerateResourceTemplatesAsync"/> instead,
    /// which streams templates as they arrive rather than loading them all into memory at once.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Get all resource templates available on the server
    /// var resourceTemplates = await client.ListResourceTemplatesAsync(cancellationToken);
    /// 
    /// // Display information about each resource template
    /// foreach (var template in resourceTemplates)
    /// {
    ///     Console.WriteLine($"Template Name: {template.Name}");
    ///     
    ///     if (template.Description != null)
    ///     {
    ///         Console.WriteLine($"Description: {template.Description}");
    ///     }
    ///     
    ///     // Access template schema if available
    ///     if (template.Schema != null)
    ///     {
    ///         Console.WriteLine("Template Schema Available");
    ///     }
    /// }
    /// </code>
    /// </example>
    public static async Task<IList<ResourceTemplate>> ListResourceTemplatesAsync(
        this IMcpClient client, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        List<ResourceTemplate>? templates = null;

        string? cursor = null;
        do
        {
            var templateResults = await client.SendRequestAsync(
                RequestMethods.ResourcesTemplatesList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListResourceTemplatesRequestParams,
                McpJsonUtilities.JsonContext.Default.ListResourceTemplatesResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (templates is null)
            {
                templates = templateResults.ResourceTemplates;
            }
            else
            {
                templates.AddRange(templateResults.ResourceTemplates);
            }

            cursor = templateResults.NextCursor;
        }
        while (cursor is not null);

        return templates;
    }

    /// <summary>
    /// Creates an enumerable for asynchronously enumerating all available resource templates from the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An asynchronous sequence of all available resource templates.</returns>
    /// <remarks>
    /// <para>
    /// This method provides a streaming approach to access all available resource templates from the server using 
    /// C#'s asynchronous enumeration pattern. It automatically handles pagination with cursors when the server 
    /// has large numbers of resource templates.
    /// </para>
    /// <para>
    /// Unlike <see cref="ListResourceTemplatesAsync"/> which loads all resource templates into memory at once, this method yields 
    /// templates as they are received, allowing for better memory efficiency when working with large collections.
    /// </para>
    /// <para>
    /// Each <see cref="ResourceTemplate"/> in the returned sequence contains metadata about available resource templates
    /// on the server, including template name, description, and schema information defining how resources of this template type
    /// should be structured.
    /// </para>
    /// <para>
    /// Every iteration through the returned <see cref="IAsyncEnumerable{ResourceTemplate}"/>
    /// will result in requerying the server and yielding the sequence of available resource templates.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Enumerate all resource templates available on the server
    /// await foreach (var template in client.EnumerateResourceTemplatesAsync(cancellationToken))
    /// {
    ///     Console.WriteLine($"Template Name: {template.Name}");
    ///     
    ///     if (template.Description != null)
    ///     {
    ///         Console.WriteLine($"Description: {template.Description}");
    ///     }
    ///     
    ///     // Access template schema if available
    ///     if (template.Schema != null)
    ///     {
    ///         Console.WriteLine("Template Schema Available");
    ///     }
    /// }
    /// </code>
    /// </example>
    public static async IAsyncEnumerable<ResourceTemplate> EnumerateResourceTemplatesAsync(
        this IMcpClient client, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        string? cursor = null;
        do
        {
            var templateResults = await client.SendRequestAsync(
                RequestMethods.ResourcesTemplatesList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListResourceTemplatesRequestParams,
                McpJsonUtilities.JsonContext.Default.ListResourceTemplatesResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var template in templateResults.ResourceTemplates)
            {
                yield return template;
            }

            cursor = templateResults.NextCursor;
        }
        while (cursor is not null);
    }

    /// <summary>
    /// Retrieves a list of available resources from the server.
    /// </summary>
    /// <param name="client">The client instance used to communicate with the MCP server.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available resources as <see cref="Resource"/> instances.</returns>
    /// <remarks>
    /// <para>
    /// This method fetches all available resources from the MCP server and returns them as a complete list.
    /// It automatically handles pagination with cursors if the server has many resources.
    /// </para>
    /// <para>
    /// For servers with a large number of resources, consider using <see cref="EnumerateResourcesAsync"/> instead,
    /// which streams resources as they arrive rather than loading them all into memory at once.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Get all resources available on the server
    /// var resources = await client.ListResourcesAsync(cancellationToken);
    /// 
    /// // Display information about each resource
    /// foreach (var resource in resources)
    /// {
    ///     Console.WriteLine($"Resource URI: {resource.Uri}");
    ///     Console.WriteLine($"Resource Type: {resource.Type}");
    ///     
    ///     // Access resource properties if available
    ///     if (resource.Properties?.Count > 0)
    ///     {
    ///         Console.WriteLine("Resource Properties:");
    ///         foreach (var prop in resource.Properties)
    ///         {
    ///             Console.WriteLine($"  - {prop.Key}: {prop.Value}");
    ///         }
    ///     }
    /// }
    /// </code>
    /// </example>
    public static async Task<IList<Resource>> ListResourcesAsync(
        this IMcpClient client, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        List<Resource>? resources = null;

        string? cursor = null;
        do
        {
            var resourceResults = await client.SendRequestAsync(
                RequestMethods.ResourcesList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListResourcesRequestParams,
                McpJsonUtilities.JsonContext.Default.ListResourcesResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (resources is null)
            {
                resources = resourceResults.Resources;
            }
            else
            {
                resources.AddRange(resourceResults.Resources);
            }

            cursor = resourceResults.NextCursor;
        }
        while (cursor is not null);

        return resources;
    }

    /// <summary>
    /// Creates an enumerable for asynchronously enumerating all available resources from the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An asynchronous sequence of all available resources.</returns>
    /// <remarks>
    /// <para>
    /// This method provides a streaming approach to access all available resources from the server using 
    /// C#'s asynchronous enumeration pattern. It automatically handles pagination with cursors when the server 
    /// has large numbers of resources.
    /// </para>
    /// <para>
    /// Unlike <see cref="ListResourcesAsync"/> which loads all resources into memory at once, this method yields 
    /// resources as they are received, allowing for better memory efficiency when working with large collections.
    /// </para>
    /// <para>
    /// Each <see cref="Resource"/> in the returned sequence contains metadata such as the resource's URI, 
    /// type, and any associated data or properties defined by the server.
    /// </para>
    /// <para>
    /// Every iteration through the returned <see cref="IAsyncEnumerable{Resource}"/>
    /// will result in requerying the server and yielding the sequence of available resources.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Enumerate all resources available on the server
    /// await foreach (var resource in client.EnumerateResourcesAsync(cancellationToken))
    /// {
    ///     Console.WriteLine($"Resource URI: {resource.Uri}");
    ///     Console.WriteLine($"Resource Type: {resource.Type}");
    ///     
    ///     // Access resource properties if available
    ///     if (resource.Properties?.Count > 0)
    ///     {
    ///         Console.WriteLine("Resource Properties:");
    ///         foreach (var prop in resource.Properties)
    ///         {
    ///             Console.WriteLine($"  - {prop.Key}: {prop.Value}");
    ///         }
    ///     }
    /// }
    /// </code>
    /// </example>
    public static async IAsyncEnumerable<Resource> EnumerateResourcesAsync(
        this IMcpClient client, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        string? cursor = null;
        do
        {
            var resourceResults = await client.SendRequestAsync(
                RequestMethods.ResourcesList,
                new() { Cursor = cursor },
                McpJsonUtilities.JsonContext.Default.ListResourcesRequestParams,
                McpJsonUtilities.JsonContext.Default.ListResourcesResult,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var resource in resourceResults.Resources)
            {
                yield return resource;
            }

            cursor = resourceResults.NextCursor;
        }
        while (cursor is not null);
    }

    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="uri">The uri of the resource.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    public static Task<ReadResourceResult> ReadResourceAsync(
        this IMcpClient client, string uri, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);
        Throw.IfNullOrWhiteSpace(uri);

        return client.SendRequestAsync(
            RequestMethods.ResourcesRead,
            new() { Uri = uri },
            McpJsonUtilities.JsonContext.Default.ReadResourceRequestParams,
            McpJsonUtilities.JsonContext.Default.ReadResourceResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Requests completion suggestions for a prompt argument or resource reference.
    /// </summary>
    /// <param name="client">The client making the request.</param>
    /// <param name="reference">The reference object specifying the type (e.g., "ref/prompt" or "ref/resource") and optional URI or name.</param>
    /// <param name="argumentName">The name of the argument for which completions are requested.</param>
    /// <param name="argumentValue">The current value of the argument, used to filter relevant completions.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="CompleteResult"/> containing completion suggestions.</returns>
    /// <remarks>
    /// <para>
    /// This method allows clients to request auto-completion suggestions for arguments in a prompt template
    /// or for resource references.
    /// </para>
    /// <para>
    /// When working with prompt references, the server will return suggestions for the specified argument
    /// that match or begin with the current argument value. This is useful for implementing intelligent
    /// auto-completion in user interfaces.
    /// </para>
    /// <para>
    /// When working with resource references, the server will return suggestions relevant to the specified 
    /// resource URI.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> or <paramref name="reference"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reference"/> is invalid or when <paramref name="argumentName"/> is null or whitespace.</exception>
    /// <exception cref="McpException">Thrown when the server returns an error response.</exception>
    /// <example>
    /// <para>Request completions for a prompt argument:</para>
    /// <code>
    /// var result = await client.CompleteAsync(
    ///     new Reference { Type = "ref/prompt", Name = "my_template" },
    ///     argumentName: "style",
    ///     argumentValue: "f", 
    ///     cancellationToken: ct);
    /// 
    /// foreach (var value in result.Completion.Values)
    /// {
    ///     Console.WriteLine(value); // e.g. "formal", "friendly"
    /// }
    /// </code>
    /// 
    /// <para>Request completions for a resource reference:</para>
    /// <code>
    /// var result = await client.CompleteAsync(
    ///     new Reference { Type = "ref/resource", Uri = "test://static/resource/1" },
    ///     argumentName: "parameter",
    ///     argumentValue: "1",
    ///     cancellationToken: ct);
    /// 
    /// foreach (var value in result.Completion.Values)
    /// {
    ///     Console.WriteLine(value);
    /// }
    /// </code>
    /// </example>
    public static Task<CompleteResult> CompleteAsync(this IMcpClient client, Reference reference, string argumentName, string argumentValue, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);
        Throw.IfNull(reference);
        Throw.IfNullOrWhiteSpace(argumentName);

        if (!reference.Validate(out string? validationMessage))
        {
            throw new ArgumentException($"Invalid reference: {validationMessage}", nameof(reference));
        }

        return client.SendRequestAsync(
            RequestMethods.CompletionComplete,
            new()
            {
                Ref = reference,
                Argument = new Argument { Name = argumentName, Value = argumentValue }
            },
            McpJsonUtilities.JsonContext.Default.CompleteRequestParams,
            McpJsonUtilities.JsonContext.Default.CompleteResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Subscribes to a resource on the server to receive notifications when it changes.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="uri">The URI of the resource to subscribe to.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// This method allows the client to register interest in a specific resource identified by its URI.
    /// When the resource changes, the server will send notifications to the client, enabling real-time
    /// updates without polling.
    /// </para>
    /// <para>
    /// The subscription remains active until explicitly canceled using <see cref="UnsubscribeFromResourceAsync"/>
    /// or until the client disconnects from the server.
    /// </para>
    /// <para>
    /// To handle resource change notifications, register an event handler for the appropriate notification events
    /// on your client implementation.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Subscribe to a specific resource
    /// await client.SubscribeToResourceAsync("resource://documents/123", cancellationToken);
    /// 
    /// // Set up notification handler (implementation depends on your client)
    /// client.ResourceChanged += (sender, e) => 
    /// {
    ///     Console.WriteLine($"Resource {e.Uri} has changed");
    ///     // Update your application state with the new resource information
    /// };
    /// </code>
    /// </example>
    public static Task SubscribeToResourceAsync(this IMcpClient client, string uri, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);
        Throw.IfNullOrWhiteSpace(uri);

        return client.SendRequestAsync(
            RequestMethods.ResourcesSubscribe,
            new() { Uri = uri },
            McpJsonUtilities.JsonContext.Default.SubscribeRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unsubscribes from a resource on the server to stop receiving notifications about its changes.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="uri">The URI of the resource to unsubscribe from.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// This method cancels a previous subscription to a resource, stopping the client from receiving
    /// notifications when that resource changes. This is useful for conserving resources and network
    /// bandwidth when the client no longer needs to track changes to a particular resource.
    /// </para>
    /// <para>
    /// The unsubscribe operation is idempotent, meaning it can be called multiple times for the same
    /// resource without causing errors, even if there is no active subscription.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // First subscribe to a resource
    /// await client.SubscribeToResourceAsync("resource://documents/123", cancellationToken);
    /// 
    /// // Later, when updates are no longer needed
    /// await client.UnsubscribeFromResourceAsync("resource://documents/123", cancellationToken);
    /// </code>
    /// </example>
    /// <seealso cref="SubscribeToResourceAsync"/>
    public static Task UnsubscribeFromResourceAsync(this IMcpClient client, string uri, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);
        Throw.IfNullOrWhiteSpace(uri);

        return client.SendRequestAsync(
            RequestMethods.ResourcesUnsubscribe,
            new() { Uri = uri },
            McpJsonUtilities.JsonContext.Default.UnsubscribeRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Invokes a tool on the server with optional arguments. This method calls a named tool on the MCP server
    /// and passes arguments to it as a dictionary of name-value pairs. The server processes the request and
    /// returns a response that contains the tool's output data.
    /// </summary>
    /// <param name="client">The MCP client instance used to send the request.</param>
    /// <param name="toolName">The name of the tool to call on the server. The tool must be registered on the server-side.</param>
    /// <param name="arguments">Optional dictionary of arguments to pass to the tool. Each key represents a parameter name,
    /// and its associated value represents the parameter value. Pass null if the tool requires no arguments.</param>
    /// <param name="serializerOptions">The JSON serialization options governing argument serialization. If null,
    /// the default serialization options will be used.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.
    /// The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the <see cref="CallToolResponse"/> from the tool execution. The response includes
    /// the tool's output content, which may be structured data, text, or an error message.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> or <paramref name="toolName"/> is null.</exception>
    /// <exception cref="McpException">Thrown when the server cannot find the requested tool or when the server encounters an error while processing the request.</exception>
    /// <example>
    /// <code>
    /// // Call a simple echo tool with a string argument
    /// var result = await client.CallToolAsync(
    ///     "echo",
    ///     new Dictionary&lt;string, object?&gt;
    ///     {
    ///         ["message"] = "Hello MCP!"
    ///     });
    /// 
    /// // Call an LLM tool with multiple parameters
    /// var result = await client.CallToolAsync(
    ///     "sampleLLM", 
    ///     new Dictionary&lt;string, object?&gt;
    ///     {
    ///         ["prompt"] = "What is the capital of France?",
    ///         ["maxTokens"] = 100
    ///     });
    /// </code>
    /// </example>
    public static Task<CallToolResponse> CallToolAsync(
        this IMcpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);
        Throw.IfNull(toolName);
        serializerOptions ??= McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        return client.SendRequestAsync(
            RequestMethods.ToolsCall,
            new() { Name = toolName, Arguments = ToArgumentsDictionary(arguments, serializerOptions) },
            McpJsonUtilities.JsonContext.Default.CallToolRequestParams,
            McpJsonUtilities.JsonContext.Default.CallToolResponse,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Converts the contents of a <see cref="CreateMessageRequestParams"/> into a pair of
    /// <see cref="IEnumerable{ChatMessage}"/> and <see cref="ChatOptions"/> instances to use
    /// as inputs into a <see cref="IChatClient"/> operation.
    /// </summary>
    /// <param name="requestParams"></param>
    /// <returns>The created pair of messages and options.</returns>
    internal static (IList<ChatMessage> Messages, ChatOptions? Options) ToChatClientArguments(
        this CreateMessageRequestParams requestParams)
    {
        Throw.IfNull(requestParams);

        ChatOptions? options = null;

        if (requestParams.MaxTokens is int maxTokens)
        {
            (options ??= new()).MaxOutputTokens = maxTokens;
        }

        if (requestParams.Temperature is float temperature)
        {
            (options ??= new()).Temperature = temperature;
        }

        if (requestParams.StopSequences is { } stopSequences)
        {
            (options ??= new()).StopSequences = stopSequences.ToArray();
        }

        List<ChatMessage> messages = [];
        foreach (SamplingMessage sm in requestParams.Messages)
        {
            ChatMessage message = new()
            {
                Role = sm.Role == Role.User ? ChatRole.User : ChatRole.Assistant,
            };

            if (sm.Content is { Type: "text" })
            {
                message.Contents.Add(new TextContent(sm.Content.Text));
            }
            else if (sm.Content is { Type: "image" or "audio", MimeType: not null, Data: not null })
            {
                message.Contents.Add(new DataContent(Convert.FromBase64String(sm.Content.Data), sm.Content.MimeType));
            }
            else if (sm.Content is { Type: "resource", Resource: not null })
            {
                message.Contents.Add(sm.Content.Resource.ToAIContent());
            }

            messages.Add(message);
        }

        return (messages, options);
    }

    /// <summary>Converts the contents of a <see cref="ChatResponse"/> into a <see cref="CreateMessageResult"/>.</summary>
    /// <param name="chatResponse">The <see cref="ChatResponse"/> whose contents should be extracted.</param>
    /// <returns>The created <see cref="CreateMessageResult"/>.</returns>
    internal static CreateMessageResult ToCreateMessageResult(this ChatResponse chatResponse)
    {
        Throw.IfNull(chatResponse);

        // The ChatResponse can include multiple messages, of varying modalities, but CreateMessageResult supports
        // only either a single blob of text or a single image. Heuristically, we'll use an image if there is one
        // in any of the response messages, or we'll use all the text from them concatenated, otherwise.

        ChatMessage? lastMessage = chatResponse.Messages.LastOrDefault();

        Content? content = null;
        if (lastMessage is not null)
        {
            foreach (var lmc in lastMessage.Contents)
            {
                if (lmc is DataContent dc && (dc.HasTopLevelMediaType("image") || dc.HasTopLevelMediaType("audio")))
                {
                    content = new()
                    {
                        Type = dc.HasTopLevelMediaType("image") ? "image" : "audio",
                        MimeType = dc.MediaType,
                        Data = dc.GetBase64Data(),
                    };
                }
            }
        }

        content ??= new()
        {
            Text = lastMessage?.Text ?? string.Empty,
            Type = "text",
        };

        return new()
        {
            Content = content,
            Model = chatResponse.ModelId ?? "unknown",
            Role = lastMessage?.Role == ChatRole.User ? "user" : "assistant",
            StopReason = chatResponse.FinishReason == ChatFinishReason.Length ? "maxTokens" : "endTurn",
        };
    }

    /// <summary>
    /// Creates a sampling handler for use with <see cref="SamplingCapability.SamplingHandler"/> that will
    /// satisfy sampling requests using the specified <see cref="IChatClient"/>.
    /// </summary>
    /// <param name="chatClient">The <see cref="IChatClient"/> with which to satisfy sampling requests.</param>
    /// <returns>The created handler delegate that can be assigned to <see cref="SamplingCapability.SamplingHandler"/>.</returns>
    /// <remarks>
    /// <para>
    /// This method creates a function that converts MCP message requests into chat client calls, enabling
    /// an MCP client to generate text or other content using an actual AI model via the provided chat client.
    /// </para>
    /// <para>
    /// The returned function handles streaming responses from the chat client, converting them to the
    /// proper MCP protocol format and supporting progress reporting during generation.
    /// </para>
    /// <para>
    /// The handler can process text messages, image messages, and resource messages as defined in the
    /// Model Context Protocol.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create an IChatClient using OpenAI or other provider
    /// using IChatClient chatClient = new OpenAIClient(apiKey).AsChatClient("gpt-4");
    /// 
    /// // Create a sampling handler and assign it to the client options
    /// var clientOptions = new McpClientOptions
    /// {
    ///     Capabilities = new ClientCapabilities
    ///     {
    ///         Sampling = new SamplingCapability
    ///         {
    ///             SamplingHandler = chatClient.CreateSamplingHandler()
    ///         }
    ///     }
    /// };
    /// 
    /// // Use the options when creating the MCP client
    /// var mcpClient = await McpClientFactory.CreateAsync(serverConfig, clientOptions);
    /// </code>
    /// </example>
    public static Func<CreateMessageRequestParams?, IProgress<ProgressNotificationValue>, CancellationToken, Task<CreateMessageResult>> CreateSamplingHandler(
        this IChatClient chatClient)
    {
        Throw.IfNull(chatClient);

        return async (requestParams, progress, cancellationToken) =>
        {
            Throw.IfNull(requestParams);

            var (messages, options) = requestParams.ToChatClientArguments();
            var progressToken = requestParams.Meta?.ProgressToken;

            List<ChatResponseUpdate> updates = [];
            await foreach (var update in chatClient.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                updates.Add(update);

                if (progressToken is not null)
                {
                    progress.Report(new()
                    {
                        Progress = updates.Count,
                    });
                }
            }

            return updates.ToChatResponse().ToCreateMessageResult();
        };
    }

    /// <summary>
    /// Sets the logging level for the server to control which log messages are sent to the client.
    /// </summary>
    /// <param name="client">The MCP client.</param>
    /// <param name="level">The minimum severity level of log messages to receive from the server.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// After this request is processed, the server will send log messages at or above the specified
    /// logging level as notifications to the client. For example, if <see cref="LoggingLevel.Warning"/> is set,
    /// the client will receive Warning, Error, Critical, Alert, and Emergency level messages.
    /// </para>
    /// <para>
    /// Log levels follow standard severity ordering where higher levels are more severe (fewer messages)
    /// and lower levels are less severe (more messages):
    /// Debug &lt; Info &lt; Notice &lt; Warning &lt; Error &lt; Critical &lt; Alert &lt; Emergency
    /// </para>
    /// <para>
    /// To receive all log messages, set the level to <see cref="LoggingLevel.Debug"/>.
    /// </para>
    /// <para>
    /// The server must have the Logging capability enabled for this request to succeed. If the server
    /// does not support logging, a <see cref="McpException"/> will be thrown.
    /// </para>
    /// <para>
    /// Log messages are delivered as notifications to the client and can be captured by registering
    /// appropriate event handlers with the client implementation.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Set the logging level to receive only warning and higher severity messages
    /// await client.SetLoggingLevel(LoggingLevel.Warning);
    /// 
    /// // Later, to receive more detailed logs:
    /// await client.SetLoggingLevel(LoggingLevel.Debug);
    /// 
    /// // Turn off logging completely
    /// await client.SetLoggingLevel(LoggingLevel.Off);
    /// </code>
    /// </example>
    /// <seealso cref="LoggingLevel"/>
    /// <seealso cref="LoggingCapability"/>
    public static Task SetLoggingLevel(this IMcpClient client, LoggingLevel level, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(client);

        return client.SendRequestAsync(
            RequestMethods.LoggingSetLevel,
            new() { Level = level },
            McpJsonUtilities.JsonContext.Default.SetLevelRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Configures the minimum logging level for the server.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="level">The minimum log level of messages to be generated.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    public static Task SetLoggingLevel(this IMcpClient client, LogLevel level, CancellationToken cancellationToken = default) =>
        SetLoggingLevel(client, McpServer.ToLoggingLevel(level), cancellationToken);

    /// <summary>Convers a dictionary with <see cref="object"/> values to a dictionary with <see cref="JsonElement"/> values.</summary>
    private static IReadOnlyDictionary<string, JsonElement>? ToArgumentsDictionary(
        IReadOnlyDictionary<string, object?>? arguments, JsonSerializerOptions options)
    {
        var typeInfo = options.GetTypeInfo<object?>();

        Dictionary<string, JsonElement>? result = null;
        if (arguments is not null)
        {
            result = new(arguments.Count);
            foreach (var kvp in arguments)
            {
                result.Add(kvp.Key, kvp.Value is JsonElement je ? je : JsonSerializer.SerializeToElement(kvp.Value, typeInfo));
            }
        }

        return result;
    }
}
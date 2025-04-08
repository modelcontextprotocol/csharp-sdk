namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's response to a prompts/get request from the client.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GetPromptResult"/> is a key class in the Model Context Protocol system that represents
/// the result of retrieving a prompt from an MCP server. It contains the prompt messages and optional 
/// descriptive information that clients can use to understand and utilize the prompt.
/// </para>
/// <para>
/// When working with MCP clients, this class is typically returned from the <c>GetPromptAsync</c> method
/// after requesting a specific prompt by name, optionally with arguments to customize the prompt.
/// </para>
/// <para>
/// The <see cref="Messages"/> property contains the actual prompt content as a list of <see cref="PromptMessage"/> 
/// objects, which can include both user and assistant roles to form a conversation template.
/// </para>
/// <para>
/// For integration with AI client libraries, <see cref="GetPromptResult"/> can be converted to
/// a collection of <see cref="Microsoft.Extensions.AI.ChatMessage"/> objects using the 
/// <see cref="AIContentExtensions.ToChatMessages"/> extension method.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Get a prompt from an MCP client
/// var result = await mcpClient.GetPromptAsync("weather_prompt", 
///     new Dictionary&lt;string, object?&gt; { ["location"] = "Seattle" });
/// 
/// // Access the prompt description
/// Console.WriteLine($"Prompt description: {result.Description}");
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
public class GetPromptResult
{
    /// <summary>
    /// An optional description for the prompt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This description provides contextual information about the prompt's purpose and use cases.
    /// It helps developers understand what the prompt is designed for and how it should be used.
    /// </para>
    /// <para>
    /// When returned from a server in response to a GetPrompt request, this description can be used
    /// by client applications to provide context about the prompt or to display in user interfaces.
    /// </para>
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// The prompt or prompt template that the server offers.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("messages")]
    public List<PromptMessage> Messages { get; set; } = [];
}

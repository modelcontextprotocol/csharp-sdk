namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the parameters for a request to list available resource templates from the server.
/// Resource templates define the structure and URI templates for resources that can be created within the system.
/// </summary>
/// <remarks>
/// <para>
/// This class inherits from <see cref="PaginatedRequestParams"/>, providing pagination capabilities
/// to allow clients to retrieve resource templates in manageable chunks.
/// </para>
/// <para>
/// Resource templates typically contain information such as name, description, and URI patterns that
/// clients can use to discover and interact with available resources.
/// </para>
/// <para>
/// See the official MCP specification for detailed schema information:
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">MCP Schema</see>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Client request example:
/// var params = new ListResourceTemplatesRequestParams
/// {
///     Count = 10,    // Request up to 10 resource templates
///     Cursor = "0"   // Start from the beginning
/// };
/// 
/// // Server handler example:
/// Func&lt;RequestContext&lt;ListResourceTemplatesRequestParams&gt;, CancellationToken, Task&lt;ListResourceTemplatesResult&gt;&gt; handler = 
///     (context, token) => 
///     {
///         return Task.FromResult(new ListResourceTemplatesResult
///         {
///             ResourceTemplates = 
///             [
///                 new ResourceTemplate 
///                 { 
///                     Name = "Document", 
///                     Description = "A document resource", 
///                     UriTemplate = "files://document/{id}" 
///                 }
///             ]
///         });
///     };
/// </code>
/// </example>
public class ListResourceTemplatesRequestParams : PaginatedRequestParams;
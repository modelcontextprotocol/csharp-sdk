using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System;
using System.Collections.Concurrent;

namespace ModelContextProtocol.Logging
{
    /// <summary>
    /// Provides logging over MCP's notifications to send log messages to the client
    /// </summary>
    /// <param name="mcpServer">MCP Server.</param>
    public class McpLoggerProvider(IMcpServer mcpServer) : ILoggerProvider
    {
        /// <summary>
        /// Creates a new instance of an MCP logger
        /// </summary>
        /// <param name="categoryName">Logger Category Name</param>
        /// <returns>New Logger instance</returns>
        public ILogger CreateLogger(string categoryName)
            => new McpLogger(categoryName, mcpServer);

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
        }
    }
}
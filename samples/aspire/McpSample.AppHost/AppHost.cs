var builder = DistributedApplication.CreateBuilder(args);

var mcpServer = builder.AddProject<Projects.McpSample_AspNetServer>("mcp-server");

builder.AddProject<Projects.McpSample_Client>("client")
    .WithReference(mcpServer)
    .WaitFor(mcpServer);

builder.Build().Run();

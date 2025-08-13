# Dynamic Tool Filtering MCP Server - Docker Configuration
# Multi-stage build for optimized production image

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
# This layer is cached unless project file changes
COPY DynamicToolFiltering.csproj .
RUN dotnet restore

# Copy source code and build
COPY . .
RUN dotnet build -c Release -o /app/build --no-restore

# Publish stage
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish --no-restore --no-build

# Runtime stage - minimal ASP.NET Core runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Install dependencies for health checks and debugging
RUN apt-get update && apt-get install -y \
    curl \
    jq \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user for security
RUN groupadd -r appgroup && useradd -r -g appgroup appuser

# Set working directory
WORKDIR /app

# Copy published application
COPY --from=publish /app/publish .

# Create necessary directories and set permissions
RUN mkdir -p logs data && \
    chown -R appuser:appgroup /app

# Switch to non-root user
USER appuser

# Expose port
EXPOSE 8080

# Add metadata labels
LABEL org.opencontainers.image.title="Dynamic Tool Filtering MCP Server" \
      org.opencontainers.image.description="Advanced MCP server demonstrating tool filtering and authorization" \
      org.opencontainers.image.source="https://github.com/microsoft/mcp-csharp-sdk" \
      org.opencontainers.image.documentation="https://github.com/microsoft/mcp-csharp-sdk/tree/main/samples/DynamicToolFiltering"

# Health check - verify the application is responding
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# Set environment variables for container
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true

# Start application
ENTRYPOINT ["dotnet", "DynamicToolFiltering.dll"]
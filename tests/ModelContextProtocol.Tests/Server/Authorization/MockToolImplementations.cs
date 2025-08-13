using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Server.Authorization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ModelContextProtocol.Tests.Server.Authorization;

/// <summary>
/// Mock tool implementations for comprehensive authorization testing.
/// </summary>
public static class MockToolImplementations
{
    /// <summary>
    /// Creates a collection of mock tools for testing various authorization scenarios.
    /// </summary>
    public static IEnumerable<McpServerTool> CreateTestToolSuite()
    {
        yield return new ReadOnlyTool();
        yield return new WriteDataTool();
        yield return new AdminDeleteTool();
        yield return new AdminCreateTool();
        yield return new PublicInfoTool();
        yield return new PrivateDataTool();
        yield return new BetaFeatureTool();
        yield return new HighRateOperationTool();
        yield return new SecureApiTool();
        yield return new UserProfileTool();
        yield return new SystemStatusTool();
        yield return new ComplexAnalysisTool();
        yield return new TimeSensitiveTool();
        yield return new QuotaConsumingTool();
        yield return new AuditableTool();
    }

    /// <summary>
    /// Creates a collection of test tools with specific categories for testing.
    /// </summary>
    public static Dictionary<string, IEnumerable<McpServerTool>> CreateCategorizedTools()
    {
        return new Dictionary<string, IEnumerable<McpServerTool>>
        {
            ["read_only"] = new McpServerTool[] { new ReadOnlyTool(), new PublicInfoTool(), new SystemStatusTool() },
            ["write_operations"] = new McpServerTool[] { new WriteDataTool(), new UserProfileTool() },
            ["admin_tools"] = new McpServerTool[] { new AdminDeleteTool(), new AdminCreateTool() },
            ["beta_features"] = new McpServerTool[] { new BetaFeatureTool() },
            ["high_privilege"] = new McpServerTool[] { new PrivateDataTool(), new SecureApiTool() },
            ["resource_intensive"] = new McpServerTool[] { new ComplexAnalysisTool(), new HighRateOperationTool() },
            ["time_sensitive"] = new McpServerTool[] { new TimeSensitiveTool() },
            ["quota_limited"] = new McpServerTool[] { new QuotaConsumingTool() },
            ["auditable"] = new McpServerTool[] { new AuditableTool() }
        };
    }
}

/// <summary>
/// Mock tool for read-only operations - typically allowed by most filters.
/// </summary>
public class ReadOnlyTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "read_only_data",
        Description = "Reads data without making any changes",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["resource_id"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The ID of the resource to read"
                }
            },
            ["required"] = new JsonArray { "resource_id" }
        }
    };

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var resourceId = request.Params.Arguments?.GetValueOrDefault("resource_id")?.ToString() ?? "unknown";
        
        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextResourceContents 
            { 
                Text = $"Successfully read resource: {resourceId}. Data: {{\"id\":\"{resourceId}\",\"status\":\"active\",\"created\":\"2024-01-01T00:00:00Z\"}}" 
            }]
        });
    }
}

/// <summary>
/// Mock tool for write operations - may require elevated permissions.
/// </summary>
public class WriteDataTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "write_data",
        Description = "Writes data to the system",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["resource_id"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The ID of the resource to write"
                },
                ["data"] = new JsonObject
                {
                    ["type"] = "object",
                    ["description"] = "The data to write"
                }
            },
            ["required"] = new JsonArray { "resource_id", "data" }
        }
    };

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var resourceId = request.Params.Arguments?.GetValueOrDefault("resource_id")?.ToString() ?? "unknown";
        var data = request.Params.Arguments?.GetValueOrDefault("data");
        
        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextResourceContents 
            { 
                Text = $"Successfully wrote data to resource: {resourceId}. Data written: {JsonSerializer.Serialize(data)}" 
            }]
        });
    }
}

/// <summary>
/// Mock tool for admin delete operations - requires admin privileges.
/// </summary>
public class AdminDeleteTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "admin_delete_resource",
        Description = "Permanently deletes a resource (admin only)",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["resource_id"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The ID of the resource to delete"
                },
                ["confirm"] = new JsonObject
                {
                    ["type"] = "boolean",
                    ["description"] = "Confirmation flag to prevent accidental deletions"
                }
            },
            ["required"] = new JsonArray { "resource_id", "confirm" }
        }
    };

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var resourceId = request.Params.Arguments?.GetValueOrDefault("resource_id")?.ToString() ?? "unknown";
        var confirm = request.Params.Arguments?.GetValueOrDefault("confirm");
        
        if (confirm is JsonElement element && element.GetBoolean())
        {
            return ValueTask.FromResult(new CallToolResult
            {
                Content = [new TextResourceContents 
                { 
                    Text = $"Resource {resourceId} has been permanently deleted. This action cannot be undone." 
                }]
            });
        }
        
        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextResourceContents 
            { 
                Text = "Delete operation cancelled - confirmation required." 
            }]
        });
    }
}

/// <summary>
/// Mock tool for admin create operations - requires admin privileges.
/// </summary>
public class AdminCreateTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "admin_create_resource",
        Description = "Creates a new system resource (admin only)",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["resource_type"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "user", "organization", "project", "system" },
                    ["description"] = "The type of resource to create"
                },
                ["config"] = new JsonObject
                {
                    ["type"] = "object",
                    ["description"] = "Configuration for the new resource"
                }
            },
            ["required"] = new JsonArray { "resource_type", "config" }
        }
    };

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var resourceType = request.Params.Arguments?.GetValueOrDefault("resource_type")?.ToString() ?? "unknown";
        var config = request.Params.Arguments?.GetValueOrDefault("config");
        var newId = Guid.NewGuid().ToString();
        
        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextResourceContents 
            { 
                Text = $"Created new {resourceType} resource with ID: {newId}. Configuration: {JsonSerializer.Serialize(config)}" 
            }]
        });
    }
}

/// <summary>
/// Mock tool for public information - typically unrestricted.
/// </summary>
public class PublicInfoTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "public_info",
        Description = "Retrieves publicly available information",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["info_type"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "status", "version", "features", "documentation" },
                    ["description"] = "The type of public information to retrieve"
                }
            },
            ["required"] = new JsonArray { "info_type" }
        }
    };

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var infoType = request.Params.Arguments?.GetValueOrDefault("info_type")?.ToString() ?? "status";
        
        var info = infoType switch
        {
            "status" => "System is operational",
            "version" => "v1.2.3",
            "features" => "Authentication, Authorization, Audit Logging",
            "documentation" => "https://docs.example.com",
            _ => "Unknown information type"
        };
        
        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextResourceContents { Text = $"Public {infoType}: {info}" }]
        });
    }
}

/// <summary>
/// Mock tool for private data access - requires authentication and authorization.
/// </summary>
public class PrivateDataTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "private_data_access",
        Description = "Accesses private data (requires proper authorization)",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["user_id"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The user ID to access private data for"
                },
                ["data_type"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "profile", "settings", "history", "analytics" },
                    ["description"] = "The type of private data to access"
                }
            },
            ["required"] = new JsonArray { "user_id", "data_type" }
        }
    };

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var userId = request.Params.Arguments?.GetValueOrDefault("user_id")?.ToString() ?? "unknown";
        var dataType = request.Params.Arguments?.GetValueOrDefault("data_type")?.ToString() ?? "profile";
        
        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextResourceContents 
            { 
                Text = $"Private {dataType} data for user {userId}: {{\"sensitive\":\"data\",\"access_time\":\"{DateTime.UtcNow:O}\"}}" 
            }]
        });
    }
}

/// <summary>
/// Mock tool for beta features - may be restricted based on feature flags.
/// </summary>
public class BetaFeatureTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "beta_advanced_analytics",
        Description = "Advanced analytics feature (beta)",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["dataset_id"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The dataset to analyze"
                },
                ["analysis_type"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "trend", "correlation", "prediction", "anomaly" },
                    ["description"] = "Type of analysis to perform"
                }
            },
            ["required"] = new JsonArray { "dataset_id", "analysis_type" }
        }
    };

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var datasetId = request.Params.Arguments?.GetValueOrDefault("dataset_id")?.ToString() ?? "unknown";
        var analysisType = request.Params.Arguments?.GetValueOrDefault("analysis_type")?.ToString() ?? "trend";
        
        // Simulate beta feature processing
        await Task.Delay(100, cancellationToken);
        
        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextResourceContents 
            { 
                Text = $"Beta {analysisType} analysis completed for dataset {datasetId}. Results: {{\"confidence\":0.85,\"insights\":[\"pattern_detected\",\"seasonal_trend\"]}}" 
            }]
        });
    }
}

/// <summary>
/// Mock tool for high-rate operations - may be limited by rate limiting filters.
/// </summary>
public class HighRateOperationTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "high_rate_batch_process",
        Description = "Processes data in high-frequency batches",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["batch_size"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["minimum"] = 1,
                    ["maximum"] = 10000,
                    ["description"] = "Number of items to process in the batch"
                },
                ["processing_mode"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "fast", "thorough", "balanced" },
                    ["description"] = "Processing mode"
                }
            },
            ["required"] = new JsonArray { "batch_size", "processing_mode" }
        }
    };

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var batchSize = request.Params.Arguments?.GetValueOrDefault("batch_size");
        var processingMode = request.Params.Arguments?.GetValueOrDefault("processing_mode")?.ToString() ?? "balanced";
        
        var size = batchSize is JsonElement element ? element.GetInt32() : 100;
        
        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextResourceContents 
            { 
                Text = $"Batch processing completed: {size} items processed in {processingMode} mode. Rate: {size * 10} items/minute" 
            }]
        });
    }
}

/// <summary>
/// Mock tool for secure API operations - requires strong authentication.
/// </summary>
public class SecureApiTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "secure_api_operation",
        Description = "Secure API operation requiring strong authentication",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["operation"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "encrypt", "decrypt", "sign", "verify" },
                    ["description"] = "Cryptographic operation to perform"
                },
                ["payload"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Data to process"
                }
            },
            ["required"] = new JsonArray { "operation", "payload" }
        }
    };

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var operation = request.Params.Arguments?.GetValueOrDefault("operation")?.ToString() ?? "encrypt";
        var payload = request.Params.Arguments?.GetValueOrDefault("payload")?.ToString() ?? "";
        
        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextResourceContents 
            { 
                Text = $"Secure {operation} operation completed. Processed {payload.Length} bytes. Result hash: {payload.GetHashCode():X8}" 
            }]
        });
    }
}

/// <summary>
/// Mock tool for user profile operations - context-dependent access.
/// </summary>
public class UserProfileTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "user_profile_update",
        Description = "Updates user profile information",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["user_id"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "User ID to update"
                },
                ["updates"] = new JsonObject
                {
                    ["type"] = "object",
                    ["description"] = "Profile updates to apply"
                }
            },
            ["required"] = new JsonArray { "user_id", "updates" }
        }
    };

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var userId = request.Params.Arguments?.GetValueOrDefault("user_id")?.ToString() ?? "unknown";
        var updates = request.Params.Arguments?.GetValueOrDefault("updates");
        
        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextResourceContents 
            { 
                Text = $"Profile updated for user {userId}. Changes: {JsonSerializer.Serialize(updates)}" 
            }]
        });
    }
}

/// <summary>
/// Mock tool for system status - usually publicly accessible.
/// </summary>
public class SystemStatusTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "system_status_check",
        Description = "Checks system health and status",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["component"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "all", "database", "cache", "queue", "storage" },
                    ["description"] = "System component to check"
                }
            }
        }
    };

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var component = request.Params.Arguments?.GetValueOrDefault("component")?.ToString() ?? "all";
        
        var status = component switch
        {
            "database" => "Connected, 50ms latency",
            "cache" => "Active, 95% hit rate",
            "queue" => "Processing, 123 items pending",
            "storage" => "Available, 78% capacity",
            "all" => "All systems operational",
            _ => "Component status unknown"
        };
        
        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextResourceContents 
            { 
                Text = $"Status for {component}: {status}" 
            }]
        });
    }
}

/// <summary>
/// Mock tool for complex analysis - may require premium access.
/// </summary>
public class ComplexAnalysisTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "complex_data_analysis",
        Description = "Performs complex data analysis (resource intensive)",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["dataset"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "object" },
                    ["description"] = "Dataset to analyze"
                },
                ["algorithms"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject 
                    { 
                        ["type"] = "string",
                        ["enum"] = new JsonArray { "regression", "clustering", "classification", "neural_network" }
                    },
                    ["description"] = "Analysis algorithms to apply"
                }
            },
            ["required"] = new JsonArray { "dataset", "algorithms" }
        }
    };

    public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var dataset = request.Params.Arguments?.GetValueOrDefault("dataset");
        var algorithms = request.Params.Arguments?.GetValueOrDefault("algorithms");
        
        // Simulate complex processing
        await Task.Delay(500, cancellationToken);
        
        return new CallToolResult
        {
            Content = [new TextResourceContents 
            { 
                Text = $"Complex analysis completed using algorithms: {JsonSerializer.Serialize(algorithms)}. Dataset size: {(dataset as JsonArray)?.Count ?? 0} records. Processing time: 500ms" 
            }]
        };
    }
}

/// <summary>
/// Mock tool for time-sensitive operations - may be restricted by time-based filters.
/// </summary>
public class TimeSensitiveTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "time_sensitive_trading",
        Description = "Time-sensitive trading operation (business hours only)",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["symbol"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Trading symbol"
                },
                ["action"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "buy", "sell", "quote" },
                    ["description"] = "Trading action"
                },
                ["quantity"] = new JsonObject
                {
                    ["type"] = "number",
                    ["minimum"] = 0,
                    ["description"] = "Quantity to trade"
                }
            },
            ["required"] = new JsonArray { "symbol", "action" }
        }
    };

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var symbol = request.Params.Arguments?.GetValueOrDefault("symbol")?.ToString() ?? "UNKNOWN";
        var action = request.Params.Arguments?.GetValueOrDefault("action")?.ToString() ?? "quote";
        var quantity = request.Params.Arguments?.GetValueOrDefault("quantity");
        
        var currentTime = DateTime.Now;
        
        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextResourceContents 
            { 
                Text = $"Trading {action} for {symbol} executed at {currentTime:HH:mm:ss}. Quantity: {quantity ?? "N/A"}. Market status: {(currentTime.Hour >= 9 && currentTime.Hour <= 17 ? "Open" : "Closed")}" 
            }]
        });
    }
}

/// <summary>
/// Mock tool for quota-consuming operations - limited by usage quotas.
/// </summary>
public class QuotaConsumingTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "quota_consuming_operation",
        Description = "Operation that consumes user quota",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["operation_size"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "small", "medium", "large", "xlarge" },
                    ["description"] = "Size of operation (affects quota consumption)"
                },
                ["priority"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "low", "normal", "high", "urgent" },
                    ["description"] = "Operation priority"
                }
            },
            ["required"] = new JsonArray { "operation_size" }
        }
    };

    public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var operationSize = request.Params.Arguments?.GetValueOrDefault("operation_size")?.ToString() ?? "small";
        var priority = request.Params.Arguments?.GetValueOrDefault("priority")?.ToString() ?? "normal";
        
        var quotaCost = operationSize switch
        {
            "small" => 1,
            "medium" => 5,
            "large" => 20,
            "xlarge" => 100,
            _ => 1
        };
        
        // Simulate processing time based on size
        var processingTime = quotaCost * 10;
        await Task.Delay(processingTime, cancellationToken);
        
        return new CallToolResult
        {
            Content = [new TextResourceContents 
            { 
                Text = $"Operation completed. Size: {operationSize}, Priority: {priority}, Quota consumed: {quotaCost} units, Processing time: {processingTime}ms" 
            }]
        };
    }
}

/// <summary>
/// Mock tool for auditable operations - logs all access attempts.
/// </summary>
public class AuditableTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "auditable_financial_operation",
        Description = "Financial operation that requires full audit trail",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["transaction_type"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "transfer", "deposit", "withdrawal", "balance_check" },
                    ["description"] = "Type of financial transaction"
                },
                ["amount"] = new JsonObject
                {
                    ["type"] = "number",
                    ["minimum"] = 0,
                    ["description"] = "Transaction amount"
                },
                ["currency"] = new JsonObject
                {
                    ["type"] = "string",
                    ["pattern"] = "^[A-Z]{3}$",
                    ["description"] = "Currency code (ISO 4217)"
                },
                ["reference"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Transaction reference"
                }
            },
            ["required"] = new JsonArray { "transaction_type" }
        }
    };

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var transactionType = request.Params.Arguments?.GetValueOrDefault("transaction_type")?.ToString() ?? "balance_check";
        var amount = request.Params.Arguments?.GetValueOrDefault("amount");
        var currency = request.Params.Arguments?.GetValueOrDefault("currency")?.ToString() ?? "USD";
        var reference = request.Params.Arguments?.GetValueOrDefault("reference")?.ToString() ?? Guid.NewGuid().ToString();
        
        var auditTrail = new
        {
            TransactionId = Guid.NewGuid().ToString(),
            Type = transactionType,
            Amount = amount,
            Currency = currency,
            Reference = reference,
            Timestamp = DateTime.UtcNow,
            Status = "Completed",
            AuditLevel = "Full"
        };
        
        return ValueTask.FromResult(new CallToolResult
        {
            Content = [new TextResourceContents 
            { 
                Text = $"Financial operation completed with full audit trail: {JsonSerializer.Serialize(auditTrail)}" 
            }]
        });
    }
}

/// <summary>
/// Test utilities for working with mock tools.
/// </summary>
public static class MockToolTestUtilities
{
    /// <summary>
    /// Creates a test authorization context with specified properties.
    /// </summary>
    public static ToolAuthorizationContext CreateTestContext(
        string? sessionId = null,
        string? userId = null,
        IEnumerable<string>? roles = null,
        IEnumerable<string>? permissions = null,
        Dictionary<string, object>? properties = null)
    {
        var context = ToolAuthorizationContext.ForSession(sessionId ?? "test-session-" + Guid.NewGuid().ToString("N")[..8]);
        
        if (userId != null)
        {
            context.UserId = userId;
        }
        
        if (roles != null)
        {
            foreach (var role in roles)
            {
                context.UserRoles.Add(role);
            }
        }
        
        if (permissions != null)
        {
            foreach (var permission in permissions)
            {
                context.UserPermissions.Add(permission);
            }
        }
        
        if (properties != null)
        {
            foreach (var kvp in properties)
            {
                context.Properties[kvp.Key] = kvp.Value;
            }
        }
        
        return context;
    }

    /// <summary>
    /// Validates that a tool result contains expected content.
    /// </summary>
    public static void AssertToolResultContains(CallToolResult result, string expectedContent)
    {
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);
        
        var textContent = result.Content.OfType<TextResourceContents>().FirstOrDefault();
        Assert.NotNull(textContent);
        Assert.Contains(expectedContent, textContent.Text);
    }

    /// <summary>
    /// Creates a basic tool filter for testing that allows/denies based on tool name patterns.
    /// </summary>
    public static IToolFilter CreatePatternFilter(string[] patterns, bool allowMatching, int priority = 100)
    {
        return new ToolNamePatternFilter(patterns, allowMatching, priority);
    }

    /// <summary>
    /// Creates a role-based filter for testing.
    /// </summary>
    public static IToolFilter CreateRoleFilter(string requiredRole, string[] toolPatterns, int priority = 100)
    {
        return new TestRoleFilter(requiredRole, toolPatterns, priority);
    }

    private class TestRoleFilter : IToolFilter
    {
        private readonly string _requiredRole;
        private readonly string[] _toolPatterns;

        public TestRoleFilter(string requiredRole, string[] toolPatterns, int priority)
        {
            _requiredRole = requiredRole;
            _toolPatterns = toolPatterns;
            Priority = priority;
        }

        public int Priority { get; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            if (MatchesPattern(tool.Name))
            {
                return Task.FromResult(context.UserRoles.Contains(_requiredRole));
            }
            return Task.FromResult(true);
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            if (MatchesPattern(toolName) && !context.UserRoles.Contains(_requiredRole))
            {
                return Task.FromResult(AuthorizationResult.Deny($"Required role: {_requiredRole}"));
            }
            return Task.FromResult(AuthorizationResult.Allow());
        }

        private bool MatchesPattern(string toolName)
        {
            return _toolPatterns.Any(pattern => 
                pattern.EndsWith("*") ? toolName.StartsWith(pattern[..^1]) : 
                pattern.StartsWith("*") ? toolName.EndsWith(pattern[1..]) :
                toolName.Equals(pattern));
        }
    }
}
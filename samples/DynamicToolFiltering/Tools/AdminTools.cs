using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DynamicToolFiltering.Tools;

/// <summary>
/// Administrative tools that require elevated permissions.
/// These tools are only available to users with admin roles.
/// </summary>
public class AdminTools
{
    /// <summary>
    /// Get detailed system diagnostics and performance metrics.
    /// </summary>
    [McpServerTool(Name = "admin_get_system_diagnostics", Description = "Get detailed system diagnostics and performance metrics")]
    public static async Task<CallToolResult> GetSystemDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(200, cancellationToken);
        
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var gc = GC.GetTotalMemory(false);
        
        var diagnostics = new
        {
            ProcessInfo = new
            {
                ProcessId = process.Id,
                StartTime = process.StartTime.ToString("O"),
                TotalProcessorTime = process.TotalProcessorTime.ToString(),
                WorkingSet = process.WorkingSet64,
                PrivateMemorySize = process.PrivateMemorySize64,
                VirtualMemorySize = process.VirtualMemorySize64
            },
            MemoryInfo = new
            {
                GCTotalMemory = gc,
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2)
            },
            EnvironmentInfo = new
            {
                OSVersion = Environment.OSVersion.ToString(),
                CLRVersion = Environment.Version.ToString(),
                MachineName = Environment.MachineName,
                UserName = Environment.UserName,
                ProcessorCount = Environment.ProcessorCount,
                SystemDirectory = Environment.SystemDirectory,
                TickCount = Environment.TickCount64
            },
            Timestamp = DateTime.UtcNow.ToString("O")
        };

        return CallToolResult.FromContent(
            TextContent.Create($"System Diagnostics: {System.Text.Json.JsonSerializer.Serialize(diagnostics, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}"));
    }

    /// <summary>
    /// Force garbage collection (admin operation).
    /// </summary>
    [McpServerTool(Name = "admin_force_gc", Description = "Force garbage collection (administrative operation)")]
    public static async Task<CallToolResult> ForceGarbageCollectionAsync(
        [Description("GC generation (0, 1, 2, or -1 for all)")] int generation = -1,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken);
        
        var beforeMemory = GC.GetTotalMemory(false);
        var beforeCollections = new
        {
            Gen0 = GC.CollectionCount(0),
            Gen1 = GC.CollectionCount(1),
            Gen2 = GC.CollectionCount(2)
        };

        if (generation == -1)
        {
            GC.Collect();
        }
        else if (generation >= 0 && generation <= 2)
        {
            GC.Collect(generation);
        }
        else
        {
            return CallToolResult.FromError("Invalid generation. Must be 0, 1, 2, or -1 for all generations");
        }

        GC.WaitForPendingFinalizers();
        
        var afterMemory = GC.GetTotalMemory(false);
        var afterCollections = new
        {
            Gen0 = GC.CollectionCount(0),
            Gen1 = GC.CollectionCount(1),
            Gen2 = GC.CollectionCount(2)
        };

        var result = new
        {
            Generation = generation,
            MemoryBefore = beforeMemory,
            MemoryAfter = afterMemory,
            MemoryReclaimed = beforeMemory - afterMemory,
            CollectionsBefore = beforeCollections,
            CollectionsAfter = afterCollections,
            ExecutedAt = DateTime.UtcNow.ToString("O")
        };

        return CallToolResult.FromContent(
            TextContent.Create($"Garbage Collection Result: {System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}"));
    }

    /// <summary>
    /// Get list of all running processes (admin operation).
    /// </summary>
    [McpServerTool(Name = "admin_list_processes", Description = "Get list of all running processes")]
    public static async Task<CallToolResult> ListProcessesAsync(
        [Description("Maximum number of processes to return")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(300, cancellationToken);
        
        if (limit < 1 || limit > 100)
        {
            return CallToolResult.FromError("Limit must be between 1 and 100");
        }

        var processes = System.Diagnostics.Process.GetProcesses()
            .Take(limit)
            .Select(p => new
            {
                ProcessId = p.Id,
                ProcessName = p.ProcessName,
                StartTime = TryGetStartTime(p),
                WorkingSet = TryGetWorkingSet(p),
                HasExited = TryGetHasExited(p)
            })
            .ToArray();

        var result = new
        {
            ProcessCount = processes.Length,
            Processes = processes,
            RetrievedAt = DateTime.UtcNow.ToString("O")
        };

        return CallToolResult.FromContent(
            TextContent.Create($"Process List: {System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}"));
    }

    /// <summary>
    /// Simulate configuration reload (admin operation).
    /// </summary>
    [McpServerTool(Name = "admin_reload_config", Description = "Simulate configuration reload")]
    public static async Task<CallToolResult> ReloadConfigurationAsync(
        [Description("Configuration section to reload")] string section = "all",
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(500, cancellationToken); // Simulate configuration reload time
        
        var result = new
        {
            Section = section,
            Status = "success",
            ReloadedAt = DateTime.UtcNow.ToString("O"),
            Message = $"Configuration section '{section}' has been reloaded successfully",
            Version = Guid.NewGuid().ToString("N")[..8] // Simulate new config version
        };

        return CallToolResult.FromContent(
            TextContent.Create($"Configuration Reload: {System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}"));
    }

    private static string TryGetStartTime(System.Diagnostics.Process process)
    {
        try
        {
            return process.StartTime.ToString("O");
        }
        catch
        {
            return "N/A";
        }
    }

    private static long TryGetWorkingSet(System.Diagnostics.Process process)
    {
        try
        {
            return process.WorkingSet64;
        }
        catch
        {
            return -1;
        }
    }

    private static bool TryGetHasExited(System.Diagnostics.Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }
}
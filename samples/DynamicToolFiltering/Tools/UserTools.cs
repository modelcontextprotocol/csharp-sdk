using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DynamicToolFiltering.Tools;

/// <summary>
/// User-level tools that require basic authentication.
/// These tools are available to authenticated users with basic permissions.
/// </summary>
public class UserTools
{
    /// <summary>
    /// Get current user profile information.
    /// </summary>
    [McpServerTool(Name = "get_user_profile", Description = "Get current user's profile information")]
    public static async Task<CallToolResult> GetUserProfileAsync(
        RequestContext context,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken);
        
        // Extract user information from the request context
        var userId = context.Session.ClientInfo?.Name ?? "anonymous";
        
        var userProfile = new
        {
            UserId = userId,
            AuthenticatedAt = DateTime.UtcNow.ToString("O"),
            SessionId = context.Session.ToString(),
            ClientInfo = context.Session.ClientInfo?.Name ?? "Unknown Client",
            Permissions = new[] { "user:read", "user:profile" }
        };

        return CallToolResult.FromContent(
            TextContent.Create($"User Profile: {System.Text.Json.JsonSerializer.Serialize(userProfile, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}"));
    }

    /// <summary>
    /// Calculate hash of provided text.
    /// </summary>
    [McpServerTool(Name = "calculate_hash", Description = "Calculate SHA256 hash of provided text")]
    public static async Task<CallToolResult> CalculateHashAsync(
        [Description("Text to hash")] string text,
        [Description("Hash algorithm (sha256, sha1, md5)")] string algorithm = "sha256",
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken);
        
        using var hashAlgorithm = algorithm.ToLowerInvariant() switch
        {
            "sha256" => System.Security.Cryptography.SHA256.Create(),
            "sha1" => System.Security.Cryptography.SHA1.Create(),
            "md5" => System.Security.Cryptography.MD5.Create(),
            _ => System.Security.Cryptography.SHA256.Create()
        };

        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var hashBytes = hashAlgorithm.ComputeHash(bytes);
        var hashString = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var result = new
        {
            Algorithm = algorithm,
            Input = text,
            Hash = hashString,
            ComputedAt = DateTime.UtcNow.ToString("O")
        };

        return CallToolResult.FromContent(
            TextContent.Create($"Hash Result: {System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}"));
    }

    /// <summary>
    /// Generate a random UUID.
    /// </summary>
    [McpServerTool(Name = "generate_uuid", Description = "Generate a random UUID")]
    public static async Task<CallToolResult> GenerateUuidAsync(
        [Description("Number of UUIDs to generate")] int count = 1,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(20, cancellationToken);
        
        if (count < 1 || count > 10)
        {
            return CallToolResult.FromError("Count must be between 1 and 10");
        }

        var uuids = Enumerable.Range(0, count)
            .Select(_ => Guid.NewGuid().ToString())
            .ToArray();

        var result = new
        {
            Count = count,
            UUIDs = uuids,
            GeneratedAt = DateTime.UtcNow.ToString("O")
        };

        return CallToolResult.FromContent(
            TextContent.Create($"Generated UUIDs: {System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}"));
    }

    /// <summary>
    /// Validate an email address format.
    /// </summary>
    [McpServerTool(Name = "validate_email", Description = "Validate email address format")]
    public static async Task<CallToolResult> ValidateEmailAsync(
        [Description("Email address to validate")] string email,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(30, cancellationToken);
        
        bool isValid;
        string reason = "Valid email format";
        
        try
        {
            var mail = new System.Net.Mail.MailAddress(email);
            isValid = mail.Address == email;
        }
        catch
        {
            isValid = false;
            reason = "Invalid email format";
        }

        var result = new
        {
            Email = email,
            IsValid = isValid,
            Reason = reason,
            ValidatedAt = DateTime.UtcNow.ToString("O")
        };

        return CallToolResult.FromContent(
            TextContent.Create($"Email Validation: {System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}"));
    }
}
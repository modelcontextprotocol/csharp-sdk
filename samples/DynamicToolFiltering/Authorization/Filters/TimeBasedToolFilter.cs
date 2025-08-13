using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server.Authorization;
using DynamicToolFiltering.Configuration;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DynamicToolFiltering.Authorization.Filters;

/// <summary>
/// Time-based tool filter that restricts access based on business hours and maintenance windows.
/// </summary>
public class TimeBasedToolFilter : IToolFilter
{
    private readonly TimeBasedFilteringOptions _options;
    private readonly ILogger<TimeBasedToolFilter> _logger;
    private readonly TimeZoneInfo _timeZone;

    public TimeBasedToolFilter(IOptions<FilteringOptions> options, ILogger<TimeBasedToolFilter> logger)
    {
        _options = options.Value.TimeBased;
        _logger = logger;
        
        try
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            _logger.LogWarning("Time zone '{TimeZone}' not found, falling back to UTC", _options.TimeZone);
            _timeZone = TimeZoneInfo.Utc;
        }
    }

    public int Priority => _options.Priority;

    public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(true);
        }

        var canAccess = CanAccessTool(tool.Name);
        
        _logger.LogDebug("Tool inclusion check for {ToolName}: CanAccess: {CanAccess}", tool.Name, canAccess);
        
        return Task.FromResult(canAccess);
    }

    public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(AuthorizationResult.Allow("Time-based filtering disabled"));
        }

        var currentTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
        
        // Check maintenance windows first (highest priority)
        foreach (var maintenanceWindow in _options.MaintenanceWindows)
        {
            if (maintenanceWindow.IsActive && IsInMaintenanceWindow(currentTime, maintenanceWindow))
            {
                if (IsToolBlocked(toolName, maintenanceWindow.BlockedTools))
                {
                    var reason = $"Tool '{toolName}' is blocked during maintenance window: {maintenanceWindow.Description}";
                    
                    _logger.LogWarning("Tool execution denied during maintenance: {ToolName}, Window: {Description}", 
                        toolName, maintenanceWindow.Description);
                    
                    var challenge = AuthorizationChallenge.CreateCustomChallenge(
                        "Maintenance",
                        ("realm", "mcp-api"),
                        ("maintenance_start", maintenanceWindow.StartTime.ToString("O")),
                        ("maintenance_end", maintenanceWindow.EndTime.ToString("O")),
                        ("description", maintenanceWindow.Description));

                    return Task.FromResult(AuthorizationResult.DenyWithChallenge(reason, challenge));
                }
            }
        }

        // Check business hours restrictions
        if (_options.BusinessHours.Enabled && IsToolRestrictedToBusinessHours(toolName))
        {
            if (!IsWithinBusinessHours(currentTime))
            {
                var reason = $"Tool '{toolName}' is only available during business hours: {_options.BusinessHours.StartTime}-{_options.BusinessHours.EndTime} on {string.Join(", ", _options.BusinessHours.BusinessDays)}";
                
                _logger.LogWarning("Tool execution denied outside business hours: {ToolName}", toolName);
                
                var challenge = AuthorizationChallenge.CreateCustomChallenge(
                    "BusinessHours",
                    ("realm", "mcp-api"),
                    ("business_start", _options.BusinessHours.StartTime),
                    ("business_end", _options.BusinessHours.EndTime),
                    ("business_days", string.Join(",", _options.BusinessHours.BusinessDays)),
                    ("current_time", currentTime.ToString("O")),
                    ("timezone", _timeZone.Id));

                return Task.FromResult(AuthorizationResult.DenyWithChallenge(reason, challenge));
            }
        }

        _logger.LogDebug("Tool execution authorized by time-based filter: {ToolName}", toolName);
        return Task.FromResult(AuthorizationResult.Allow($"Tool '{toolName}' is available at current time"));
    }

    private bool CanAccessTool(string toolName)
    {
        var currentTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
        
        // Check maintenance windows
        foreach (var maintenanceWindow in _options.MaintenanceWindows)
        {
            if (maintenanceWindow.IsActive && 
                IsInMaintenanceWindow(currentTime, maintenanceWindow) && 
                IsToolBlocked(toolName, maintenanceWindow.BlockedTools))
            {
                return false;
            }
        }

        // Check business hours
        if (_options.BusinessHours.Enabled && 
            IsToolRestrictedToBusinessHours(toolName) && 
            !IsWithinBusinessHours(currentTime))
        {
            return false;
        }

        return true;
    }

    private bool IsInMaintenanceWindow(DateTime currentTime, MaintenanceWindowOptions maintenanceWindow)
    {
        var windowStart = TimeZoneInfo.ConvertTimeFromUtc(maintenanceWindow.StartTime, _timeZone);
        var windowEnd = TimeZoneInfo.ConvertTimeFromUtc(maintenanceWindow.EndTime, _timeZone);
        
        return currentTime >= windowStart && currentTime <= windowEnd;
    }

    private bool IsToolBlocked(string toolName, string[] blockedPatterns)
    {
        return blockedPatterns.Any(pattern => IsPatternMatch(pattern, toolName));
    }

    private bool IsToolRestrictedToBusinessHours(string toolName)
    {
        return _options.BusinessHours.RestrictedTools.Any(pattern => IsPatternMatch(pattern, toolName));
    }

    private bool IsWithinBusinessHours(DateTime currentTime)
    {
        // Check if current day is a business day
        var currentDayName = currentTime.DayOfWeek.ToString();
        if (!_options.BusinessHours.BusinessDays.Contains(currentDayName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // Parse business hours
        if (!TimeOnly.TryParse(_options.BusinessHours.StartTime, CultureInfo.InvariantCulture, out var startTime) ||
            !TimeOnly.TryParse(_options.BusinessHours.EndTime, CultureInfo.InvariantCulture, out var endTime))
        {
            _logger.LogError("Invalid business hours format. Start: {StartTime}, End: {EndTime}", 
                _options.BusinessHours.StartTime, _options.BusinessHours.EndTime);
            return false;
        }

        var currentTimeOnly = TimeOnly.FromDateTime(currentTime);

        // Handle cases where end time is before start time (spans midnight)
        if (endTime < startTime)
        {
            return currentTimeOnly >= startTime || currentTimeOnly <= endTime;
        }
        else
        {
            return currentTimeOnly >= startTime && currentTimeOnly <= endTime;
        }
    }

    private static bool IsPatternMatch(string pattern, string toolName)
    {
        if (pattern == "*")
        {
            return true;
        }
        
        // Convert glob pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(toolName, regexPattern, RegexOptions.IgnoreCase);
    }
}
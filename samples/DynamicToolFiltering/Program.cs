using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

using DynamicToolFiltering.Authorization.Filters;
using DynamicToolFiltering.Configuration;
using DynamicToolFiltering.Services;
using DynamicToolFiltering.Tools;
using ModelContextProtocol.Server.Authorization;

// Configure Serilog for comprehensive logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/dynamic-tool-filtering-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Replace default logging with Serilog
builder.Host.UseSerilog();

// Configure filtering options from configuration
builder.Services.Configure<FilteringOptions>(builder.Configuration.GetSection(FilteringOptions.SectionName));

// Register core services
builder.Services.AddSingleton<IRateLimitingService, InMemoryRateLimitingService>();
builder.Services.AddSingleton<IFeatureFlagService, InMemoryFeatureFlagService>();
builder.Services.AddSingleton<IQuotaService, InMemoryQuotaService>();

// Configure multiple authentication schemes
ConfigureAuthentication(builder);

// Configure authorization and filtering
ConfigureFiltering(builder);

// Configure MCP server with tools
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<PublicTools>()
    .WithTools<UserTools>()
    .WithTools<AdminTools>()
    .WithTools<PremiumTools>();

// Add telemetry for monitoring
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b
        .AddSource("*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(b => b
        .AddMeter("*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithLogging()
    .UseOtlpExporter();

// Add CORS for web clients
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("WWW-Authenticate");
    });
});

var app = builder.Build();

// Configure request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseSerilogRequestLogging();
app.UseCors();

// Authentication must come before authorization
app.UseAuthentication();
app.UseAuthorization();

// Map MCP endpoints
app.MapMcp();

// Add health check endpoint
app.MapGet("/health", () => new { 
    Status = "healthy", 
    Timestamp = DateTime.UtcNow,
    Environment = app.Environment.EnvironmentName,
    Version = "1.0.0"
});

// Add filter management endpoints (for demo purposes)
app.MapGet("/admin/filters/status", async (IServiceProvider services) =>
{
    var toolAuthService = services.GetRequiredService<IToolAuthorizationService>();
    
    return new
    {
        Message = "Filter management endpoints would be implemented here",
        Timestamp = DateTime.UtcNow,
        FiltersRegistered = "Multiple filters active (see configuration)"
    };
}).RequireAuthorization("AdminPolicy");

// Add feature flag management endpoints
app.MapGet("/admin/feature-flags", async (IFeatureFlagService featureFlagService) =>
{
    var flags = await featureFlagService.GetAllFlagsAsync("admin");
    return new { FeatureFlags = flags, Timestamp = DateTime.UtcNow };
}).RequireAuthorization("AdminPolicy");

app.MapPost("/admin/feature-flags/{flagName}", async (
    string flagName, 
    bool enabled, 
    IFeatureFlagService featureFlagService) =>
{
    await featureFlagService.SetFlagAsync(flagName, enabled);
    return new { FlagName = flagName, Enabled = enabled, UpdatedAt = DateTime.UtcNow };
}).RequireAuthorization("AdminPolicy");

Log.Information("Starting Dynamic Tool Filtering MCP Server on {Environment}", app.Environment.EnvironmentName);

app.Run();

static void ConfigureAuthentication(WebApplicationBuilder builder)
{
    var authBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    });

    // JWT Bearer authentication
    authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        var jwtSettings = builder.Configuration.GetSection("Jwt");
        var secretKey = jwtSettings["SecretKey"] ?? "your-256-bit-secret-key-here-make-it-secure";
        var issuer = jwtSettings["Issuer"] ?? "dynamic-tool-filtering";
        var audience = jwtSettings["Audience"] ?? "mcp-api";

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Log.Warning("JWT authentication failed: {Error}", context.Exception.Message);
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                Log.Debug("JWT token validated for user: {UserId}", userId);
                return Task.CompletedTask;
            }
        };
    });

    // API Key authentication (custom scheme)
    authBuilder.AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        "ApiKey", options =>
        {
            options.HeaderName = "X-API-Key";
            options.QueryStringKey = "apikey";
        });

    // Configure authorization policies
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("AdminPolicy", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(ClaimTypes.Role, "admin", "super_admin");
        });

        options.AddPolicy("PremiumPolicy", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(ClaimTypes.Role, "premium", "admin", "super_admin");
        });

        options.AddPolicy("UserPolicy", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(ClaimTypes.Role, "user", "premium", "admin", "super_admin");
        });
    });
}

static void ConfigureFiltering(WebApplicationBuilder builder)
{
    // Register the tool authorization service
    builder.Services.AddSingleton<IToolAuthorizationService, ToolAuthorizationService>();

    // Register all filter implementations with proper ordering (priority-based)
    builder.Services.AddSingleton<IToolFilter, RateLimitingToolFilter>(); // Priority 50 - highest
    builder.Services.AddSingleton<IToolFilter, TenantIsolationFilter>(); // Priority 75
    builder.Services.AddSingleton<IToolFilter, RoleBasedToolFilter>(); // Priority 100
    builder.Services.AddSingleton<IToolFilter, ScopeBasedToolFilter>(); // Priority 150
    builder.Services.AddSingleton<IToolFilter, TimeBasedToolFilter>(); // Priority 200
    builder.Services.AddSingleton<IToolFilter, BusinessLogicFilter>(); // Priority 300 - lowest

    // Configure the tool authorization service with all filters
    builder.Services.AddSingleton<IToolAuthorizationService>(serviceProvider =>
    {
        var authService = new ToolAuthorizationService();
        var filters = serviceProvider.GetServices<IToolFilter>();
        
        // Register filters in priority order
        foreach (var filter in filters.OrderBy(f => f.Priority))
        {
            authService.RegisterFilter(filter);
            Log.Information("Registered tool filter: {FilterType} with priority {Priority}", 
                filter.GetType().Name, filter.Priority);
        }
        
        return authService;
    });
}

// Custom API Key authentication handler
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>
{
    private readonly ILogger<ApiKeyAuthenticationHandler> _logger;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder)
        : base(options, loggerFactory, encoder)
    {
        _logger = loggerFactory.CreateLogger<ApiKeyAuthenticationHandler>();
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Try to get API key from header
        var apiKey = Request.Headers[Options.HeaderName].FirstOrDefault();
        
        // If not in header, try query string
        if (string.IsNullOrEmpty(apiKey))
        {
            apiKey = Request.Query[Options.QueryStringKey].FirstOrDefault();
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Validate API key (in production, use secure storage and proper validation)
        var validApiKeys = new Dictionary<string, (string UserId, string Role, string[] Scopes)>
        {
            { "demo-guest-key", ("guest-user", "guest", new[] { "basic:tools" }) },
            { "demo-user-key", ("demo-user", "user", new[] { "user:tools", "read:tools", "basic:tools" }) },
            { "demo-premium-key", ("premium-user", "premium", new[] { "premium:tools", "user:tools", "read:tools", "basic:tools" }) },
            { "demo-admin-key", ("admin-user", "admin", new[] { "admin:tools", "premium:tools", "user:tools", "read:tools", "basic:tools" }) }
        };

        if (!validApiKeys.TryGetValue(apiKey, out var keyInfo))
        {
            _logger.LogWarning("Invalid API key attempted: {ApiKey}", apiKey[..Math.Min(8, apiKey.Length)] + "...");
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        // Create claims for the authenticated user
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, keyInfo.UserId),
            new(ClaimTypes.Name, keyInfo.UserId),
            new(ClaimTypes.Role, keyInfo.Role),
            new(ClaimTypes.AuthenticationMethod, "ApiKey")
        };

        // Add scope claims
        foreach (var scope in keyInfo.Scopes)
        {
            claims.Add(new Claim("scope", scope));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        _logger.LogDebug("API key authentication successful for user: {UserId}, Role: {Role}", keyInfo.UserId, keyInfo.Role);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.Add("WWW-Authenticate", $"ApiKey realm=\"mcp-api\", parameter=\"{Options.HeaderName}\"");
        return base.HandleChallengeAsync(properties);
    }
}

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-API-Key";
    public string QueryStringKey { get; set; } = "apikey";
}
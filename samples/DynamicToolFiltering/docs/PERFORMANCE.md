# Performance Testing and Monitoring Guide

This guide provides comprehensive information on performance testing, monitoring, and optimization for the Dynamic Tool Filtering MCP server.

## Table of Contents

1. [Performance Testing](#performance-testing)
2. [Monitoring Setup](#monitoring-setup)
3. [Benchmarking](#benchmarking)
4. [Optimization Guidelines](#optimization-guidelines)
5. [Production Monitoring](#production-monitoring)
6. [Troubleshooting Performance Issues](#troubleshooting-performance-issues)

## Performance Testing

### Load Testing Tools

#### 1. Apache Bench (ab)

Basic load testing for HTTP endpoints:

```bash
# Install Apache Bench
# Ubuntu/Debian: sudo apt-get install apache2-utils
# macOS: brew install httpd

# Test health endpoint - 1000 requests, 10 concurrent
ab -n 1000 -c 10 http://localhost:8080/health

# Test tool listing with API key
ab -n 500 -c 5 -H "X-API-Key: demo-user-key" http://localhost:8080/mcp/v1/tools

# Test tool execution with POST data
ab -n 100 -c 2 -p echo_test.json -T application/json -H "X-API-Key: demo-user-key" http://localhost:8080/mcp/v1/tools/call
```

Create `echo_test.json` for POST testing:
```json
{
  "name": "echo",
  "arguments": {
    "message": "Performance test"
  }
}
```

#### 2. wrk (Recommended for Advanced Testing)

```bash
# Install wrk
# Ubuntu: sudo apt install wrk
# macOS: brew install wrk

# Basic load test
wrk -t4 -c50 -d30s http://localhost:8080/health

# Test with custom script for authenticated requests
wrk -t4 -c20 -d30s -s auth_test.lua http://localhost:8080/mcp/v1/tools
```

Create `auth_test.lua`:
```lua
wrk.method = "GET"
wrk.headers["X-API-Key"] = "demo-user-key"
wrk.headers["Content-Type"] = "application/json"
```

#### 3. Artillery.js (Advanced Scenarios)

Install and configure Artillery for complex test scenarios:

```bash
npm install -g artillery
```

Create `performance-test.yml`:
```yaml
config:
  target: 'http://localhost:8080'
  phases:
    - duration: 60
      arrivalRate: 10
      name: "Warm up"
    - duration: 120
      arrivalRate: 20
      name: "Sustained load"
    - duration: 60
      arrivalRate: 50
      name: "Peak load"
  defaults:
    headers:
      X-API-Key: "demo-user-key"

scenarios:
  - name: "Mixed workload"
    weight: 100
    flow:
      - get:
          url: "/health"
          capture:
            - json: "$.Status"
              as: "health_status"
      - get:
          url: "/mcp/v1/tools"
      - post:
          url: "/mcp/v1/tools/call"
          json:
            name: "echo"
            arguments:
              message: "Load test {{ $randomString() }}"
      - think: 1
```

Run Artillery test:
```bash
artillery run performance-test.yml
```

### Custom Performance Test Script

Create a comprehensive test script:

```bash
#!/bin/bash
# scripts/performance-test.sh

BASE_URL="http://localhost:8080"
USER_KEY="demo-user-key"
ADMIN_KEY="demo-admin-key"

echo "=== Performance Test Suite ==="

# Test 1: Health endpoint baseline
echo "Testing health endpoint..."
ab -n 1000 -c 10 -q $BASE_URL/health | grep "Requests per second"

# Test 2: Tool listing performance
echo "Testing tool listing..."
ab -n 500 -c 5 -H "X-API-Key: $USER_KEY" -q $BASE_URL/mcp/v1/tools | grep "Requests per second"

# Test 3: Tool execution performance
echo "Testing tool execution..."
echo '{"name": "echo", "arguments": {"message": "perf test"}}' > /tmp/echo_test.json
ab -n 200 -c 4 -p /tmp/echo_test.json -T application/json -H "X-API-Key: $USER_KEY" -q $BASE_URL/mcp/v1/tools/call | grep "Requests per second"

# Test 4: Rate limiting behavior
echo "Testing rate limiting..."
for i in {1..25}; do
  curl -s -w "%{http_code}\n" -o /dev/null \
    -H "X-API-Key: demo-guest-key" \
    -X POST \
    -H "Content-Type: application/json" \
    -d '{"name": "echo", "arguments": {"message": "rate limit test"}}' \
    $BASE_URL/mcp/v1/tools/call
done | sort | uniq -c

# Test 5: Concurrent filter execution
echo "Testing concurrent requests..."
(
for i in {1..5}; do
  (ab -n 50 -c 2 -H "X-API-Key: $USER_KEY" -q $BASE_URL/mcp/v1/tools &)
done
wait
) 2>/dev/null

echo "Performance tests complete"
```

## Monitoring Setup

### Application Performance Monitoring (APM)

#### 1. OpenTelemetry Configuration

The sample already includes OpenTelemetry. Enhance it for production:

```csharp
// Program.cs - Enhanced telemetry configuration
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b
        .SetSampler(new TraceIdRatioBasedSampler(0.1)) // 10% sampling
        .AddSource("DynamicToolFiltering")
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
            options.EnrichWithHttpRequest = (activity, request) =>
            {
                activity.SetTag("user.role", request.Headers["X-API-Key"]);
            };
        })
        .AddHttpClientInstrumentation()
        .AddSqlClientInstrumentation()
        .UseOtlpExporter())
    .WithMetrics(b => b
        .AddMeter("DynamicToolFiltering")
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .UseOtlpExporter());
```

#### 2. Custom Metrics

Add custom metrics for filter performance:

```csharp
// Services/MetricsService.cs
public class MetricsService
{
    private readonly Meter _meter;
    private readonly Counter<int> _filterExecutions;
    private readonly Histogram<double> _filterDuration;
    private readonly Counter<int> _authorizationFailures;

    public MetricsService()
    {
        _meter = new Meter("DynamicToolFiltering");
        _filterExecutions = _meter.CreateCounter<int>("filter_executions_total");
        _filterDuration = _meter.CreateHistogram<double>("filter_duration_seconds");
        _authorizationFailures = _meter.CreateCounter<int>("authorization_failures_total");
    }

    public void RecordFilterExecution(string filterName, string result)
    {
        _filterExecutions.Add(1, new("filter", filterName), new("result", result));
    }

    public void RecordFilterDuration(string filterName, double durationSeconds)
    {
        _filterDuration.Record(durationSeconds, new("filter", filterName));
    }

    public void RecordAuthorizationFailure(string reason, string toolName)
    {
        _authorizationFailures.Add(1, new("reason", reason), new("tool", toolName));
    }
}
```

### Prometheus and Grafana Setup

#### 1. Prometheus Configuration

Create `monitoring/prometheus.yml`:

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

rule_files:
  - "alert_rules.yml"

scrape_configs:
  - job_name: 'dynamic-tool-filtering'
    static_configs:
      - targets: ['dynamic-tool-filtering:8080']
    metrics_path: '/metrics'
    scrape_interval: 10s

  - job_name: 'redis'
    static_configs:
      - targets: ['redis:6379']

  - job_name: 'postgres'
    static_configs:
      - targets: ['postgres:5432']

alerting:
  alertmanagers:
    - static_configs:
        - targets:
          - alertmanager:9093
```

#### 2. Grafana Dashboard

Create `monitoring/grafana/dashboards/mcp-server.json`:

```json
{
  "dashboard": {
    "id": null,
    "title": "Dynamic Tool Filtering MCP Server",
    "tags": ["mcp", "performance"],
    "timezone": "browser",
    "panels": [
      {
        "title": "Request Rate",
        "type": "graph",
        "targets": [
          {
            "expr": "rate(http_requests_total[5m])",
            "legendFormat": "Requests/sec"
          }
        ]
      },
      {
        "title": "Response Times",
        "type": "graph",
        "targets": [
          {
            "expr": "histogram_quantile(0.95, rate(http_request_duration_seconds_bucket[5m]))",
            "legendFormat": "95th percentile"
          },
          {
            "expr": "histogram_quantile(0.50, rate(http_request_duration_seconds_bucket[5m]))",
            "legendFormat": "50th percentile"
          }
        ]
      },
      {
        "title": "Filter Performance",
        "type": "graph",
        "targets": [
          {
            "expr": "rate(filter_executions_total[5m])",
            "legendFormat": "{{filter}} - {{result}}"
          }
        ]
      },
      {
        "title": "Authorization Failures",
        "type": "graph",
        "targets": [
          {
            "expr": "rate(authorization_failures_total[5m])",
            "legendFormat": "{{reason}}"
          }
        ]
      }
    ],
    "time": {
      "from": "now-1h",
      "to": "now"
    },
    "refresh": "10s"
  }
}
```

### Performance Benchmarks

#### Expected Performance Baselines

| Endpoint | Concurrent Users | Expected RPS | 95th Percentile |
|----------|-----------------|--------------|-----------------|
| `/health` | 50 | 1000+ | < 50ms |
| `/mcp/v1/tools` (auth) | 20 | 500+ | < 100ms |
| `/mcp/v1/tools/call` (simple) | 10 | 200+ | < 200ms |
| `/mcp/v1/tools/call` (complex) | 5 | 50+ | < 500ms |

#### Resource Usage Targets

- **Memory**: < 100MB under normal load
- **CPU**: < 50% on single core under load
- **Response Time**: 95th percentile < 200ms
- **Error Rate**: < 0.1% under normal conditions

## Optimization Guidelines

### 1. Filter Performance

```csharp
// Optimize filter execution with caching
public class CachedRoleBasedFilter : IToolFilter
{
    private readonly IMemoryCache _cache;
    private readonly RoleBasedToolFilter _innerFilter;

    public async Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken)
    {
        var cacheKey = $"role_filter_{context.User?.Identity?.Name}_{tool.Name}";
        
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(5);
            return await _innerFilter.ShouldIncludeToolAsync(tool, context, cancellationToken);
        });
    }
}
```

### 2. Rate Limiting Optimization

```csharp
// Use Redis for distributed rate limiting
public class RedisRateLimitingService : IRateLimitingService
{
    private readonly IDatabase _database;
    
    public async Task<bool> IsAllowedAsync(string userId, string toolName)
    {
        var key = $"rate_limit:{userId}:{toolName}";
        var current = await _database.StringIncrementAsync(key);
        
        if (current == 1)
        {
            await _database.KeyExpireAsync(key, TimeSpan.FromHours(1));
        }
        
        return current <= GetLimitForUser(userId);
    }
}
```

### 3. Database Query Optimization

```csharp
// Optimize quota queries with indexing
public class OptimizedQuotaService : IQuotaService
{
    public async Task<bool> HasAvailableQuotaAsync(string userId, string toolName)
    {
        // Use efficient query with proper indexing
        var result = await _context.Database.ExecuteSqlRawAsync(
            "SELECT usage_count FROM user_quotas WHERE user_id = {0} AND tool_name = {1}",
            userId, toolName);
        
        return result < GetQuotaLimit(userId, toolName);
    }
}
```

## Production Monitoring

### Key Metrics to Monitor

#### 1. Application Metrics
- Request rate and response times
- Filter execution times
- Authorization success/failure rates
- Rate limiting violations
- Feature flag evaluation times

#### 2. Infrastructure Metrics
- CPU and memory usage
- Disk I/O and network latency
- Database connection pool usage
- Redis hit/miss ratios

#### 3. Business Metrics
- Tool usage patterns by role
- Peak usage times
- Most popular tools
- Error patterns and trends

### Alerting Rules

Create `monitoring/alert_rules.yml`:

```yaml
groups:
  - name: mcp-server-alerts
    rules:
      - alert: HighResponseTime
        expr: histogram_quantile(0.95, rate(http_request_duration_seconds_bucket[5m])) > 0.5
        for: 2m
        annotations:
          summary: "High response time detected"
          
      - alert: HighErrorRate
        expr: rate(http_requests_total{status=~"5.."}[5m]) > 0.01
        for: 1m
        annotations:
          summary: "High error rate detected"
          
      - alert: MemoryUsageHigh
        expr: process_resident_memory_bytes / 1024 / 1024 > 500
        for: 5m
        annotations:
          summary: "High memory usage"
```

## Troubleshooting Performance Issues

### Common Performance Problems

#### 1. Slow Filter Execution
```bash
# Check filter execution times in logs
grep "Filter execution" logs/dynamic-tool-filtering-*.log | \
  awk '{print $NF}' | sort -n | tail -10
```

#### 2. Memory Leaks
```bash
# Monitor memory usage over time
docker stats dynamic-tool-filtering --format "table {{.MemUsage}}\t{{.CPUPerc}}"
```

#### 3. Database Performance
```sql
-- Check slow queries (PostgreSQL)
SELECT query, calls, total_time, mean_time 
FROM pg_stat_statements 
ORDER BY mean_time DESC 
LIMIT 10;
```

### Performance Profiling

#### Using dotnet-trace
```bash
# Install profiling tools
dotnet tool install -g dotnet-trace
dotnet tool install -g dotnet-counters

# Collect performance trace
dotnet-trace collect --process-id <PID> --providers Microsoft-AspNetCore-Server-Kestrel

# Monitor real-time counters
dotnet-counters monitor --process-id <PID> --counters System.Runtime,Microsoft.AspNetCore.Hosting
```

#### Using Application Insights
```csharp
// Add detailed telemetry
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.EnableAdaptiveSampling = true;
    options.EnableQuickPulseMetricStream = true;
    options.EnablePerformanceCounterCollectionModule = true;
});
```

### Load Testing in CI/CD

Create `.github/workflows/performance-test.yml`:

```yaml
name: Performance Tests

on:
  pull_request:
    branches: [ main ]
  schedule:
    - cron: '0 2 * * *'  # Daily at 2 AM

jobs:
  performance-test:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '9.0.x'
    
    - name: Build application
      run: dotnet build -c Release
    
    - name: Start application
      run: |
        dotnet run --launch-profile DevelopmentMode &
        sleep 30  # Wait for startup
    
    - name: Install testing tools
      run: |
        sudo apt-get update
        sudo apt-get install -y apache2-utils
    
    - name: Run performance tests
      run: |
        # Health endpoint test
        ab -n 1000 -c 10 http://localhost:8080/health
        
        # API performance test
        ab -n 500 -c 5 -H "X-API-Key: demo-user-key" \
           http://localhost:8080/mcp/v1/tools
    
    - name: Verify performance benchmarks
      run: |
        # Add custom verification logic
        ./scripts/verify-performance.sh
```

This comprehensive performance guide provides the foundation for monitoring, testing, and optimizing the Dynamic Tool Filtering MCP server in production environments.
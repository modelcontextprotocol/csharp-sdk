#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Dynamic Tool Filtering - Comprehensive Test Suite (PowerShell)

.DESCRIPTION
    This script tests all aspects of the MCP server including authentication,
    authorization, rate limiting, feature flags, and error handling.

.PARAMETER Category
    Test category to run. Options: health, authentication, authorization, visibility, 
    rate-limiting, feature-flags, error-handling, performance, all

.PARAMETER BaseUrl
    Base URL of the server to test (default: http://localhost:8080)

.EXAMPLE
    .\test-all.ps1
    Runs all test categories

.EXAMPLE
    .\test-all.ps1 -Category authentication
    Runs only authentication tests

.EXAMPLE
    .\test-all.ps1 -BaseUrl "http://localhost:9000"
    Tests a server on a different port
#>

param(
    [Parameter(Position=0)]
    [ValidateSet("health", "authentication", "authorization", "visibility", "rate-limiting", "feature-flags", "error-handling", "performance", "all")]
    [string]$Category = "all",
    
    [Parameter()]
    [string]$BaseUrl = "http://localhost:8080"
)

# Configuration
$script:BaseUrl = $BaseUrl
$script:GuestKey = "demo-guest-key"
$script:UserKey = "demo-user-key"
$script:PremiumKey = "demo-premium-key"
$script:AdminKey = "demo-admin-key"
$script:InvalidKey = "invalid-test-key"

# Test counters
$script:TotalTests = 0
$script:PassedTests = 0
$script:FailedTests = 0

# Helper functions
function Write-InfoLog {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Blue
}

function Write-SuccessLog {
    param([string]$Message)
    Write-Host "[PASS] $Message" -ForegroundColor Green
    $script:PassedTests++
}

function Write-ErrorLog {
    param([string]$Message)
    Write-Host "[FAIL] $Message" -ForegroundColor Red
    $script:FailedTests++
}

function Write-WarningLog {
    param([string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Invoke-TestRequest {
    param(
        [string]$TestName,
        [int]$ExpectedStatus,
        [hashtable]$Headers = @{},
        [string]$Method = "GET",
        [string]$Uri,
        [string]$Body = $null,
        [string]$ContentType = "application/json"
    )
    
    $script:TotalTests++
    Write-InfoLog "Running test: $TestName"
    
    try {
        $requestParams = @{
            Uri = $Uri
            Method = $Method
            Headers = $Headers
            UseBasicParsing = $true
            ErrorAction = 'Stop'
        }
        
        if ($Body) {
            $requestParams.Body = $Body
            $requestParams.Headers['Content-Type'] = $ContentType
        }
        
        $response = Invoke-WebRequest @requestParams
        $actualStatus = $response.StatusCode
        
        if ($actualStatus -eq $ExpectedStatus) {
            Write-SuccessLog "$TestName (HTTP $actualStatus)"
            if ($response.Content) {
                $content = $response.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
                if ($content) {
                    Write-Host "    Response: $($content | ConvertTo-Json -Compress | Select-Object -First 1)" -ForegroundColor DarkGray
                } else {
                    Write-Host "    Response: $($response.Content.Substring(0, [Math]::Min(100, $response.Content.Length)))" -ForegroundColor DarkGray
                }
            }
        } else {
            Write-ErrorLog "$TestName (Expected HTTP $ExpectedStatus, got HTTP $actualStatus)"
            if ($response.Content) {
                Write-Host "    Response: $($response.Content.Substring(0, [Math]::Min(200, $response.Content.Length)))" -ForegroundColor DarkGray
            }
        }
    }
    catch {
        $actualStatus = 0
        if ($_.Exception.Response) {
            $actualStatus = [int]$_.Exception.Response.StatusCode
        }
        
        if ($actualStatus -eq $ExpectedStatus) {
            Write-SuccessLog "$TestName (HTTP $actualStatus)"
            if ($_.Exception.Response) {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $responseBody = $reader.ReadToEnd()
                Write-Host "    Response: $($responseBody.Substring(0, [Math]::Min(200, $responseBody.Length)))" -ForegroundColor DarkGray
            }
        } else {
            Write-ErrorLog "$TestName (Expected HTTP $ExpectedStatus, got HTTP $actualStatus)"
            Write-Host "    Error: $($_.Exception.Message)" -ForegroundColor DarkGray
        }
    }
    
    Write-Host ""
}

function Wait-ForServer {
    Write-InfoLog "Waiting for server to be ready at $script:BaseUrl..."
    $retries = 30
    $count = 0
    
    while ($count -lt $retries) {
        try {
            $response = Invoke-WebRequest -Uri "$script:BaseUrl/health" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
            if ($response.StatusCode -eq 200) {
                Write-SuccessLog "Server is ready!"
                return $true
            }
        }
        catch {
            # Server not ready yet
        }
        
        $count++
        if ($count -eq $retries) {
            Write-ErrorLog "Server did not start within expected time"
            return $false
        }
        
        Start-Sleep -Seconds 2
    }
    
    return $false
}

function Test-HealthCheck {
    Write-InfoLog "=== Health Check Tests ==="
    Invoke-TestRequest -TestName "Health endpoint availability" -ExpectedStatus 200 -Uri "$script:BaseUrl/health"
}

function Test-Authentication {
    Write-InfoLog "=== Authentication Tests ==="
    
    # Test valid API keys
    Invoke-TestRequest -TestName "Guest API key authentication" -ExpectedStatus 200 `
        -Headers @{"X-API-Key" = $script:GuestKey} -Uri "$script:BaseUrl/mcp/v1/tools"
    
    Invoke-TestRequest -TestName "User API key authentication" -ExpectedStatus 200 `
        -Headers @{"X-API-Key" = $script:UserKey} -Uri "$script:BaseUrl/mcp/v1/tools"
    
    Invoke-TestRequest -TestName "Premium API key authentication" -ExpectedStatus 200 `
        -Headers @{"X-API-Key" = $script:PremiumKey} -Uri "$script:BaseUrl/mcp/v1/tools"
    
    Invoke-TestRequest -TestName "Admin API key authentication" -ExpectedStatus 200 `
        -Headers @{"X-API-Key" = $script:AdminKey} -Uri "$script:BaseUrl/mcp/v1/tools"
    
    # Test invalid API key
    Invoke-TestRequest -TestName "Invalid API key rejection" -ExpectedStatus 401 `
        -Headers @{"X-API-Key" = $script:InvalidKey} -Uri "$script:BaseUrl/mcp/v1/tools"
    
    # Test missing API key for protected endpoint
    Invoke-TestRequest -TestName "Missing API key for admin endpoint" -ExpectedStatus 401 `
        -Uri "$script:BaseUrl/admin/filters/status"
}

function Test-Authorization {
    Write-InfoLog "=== Authorization Tests ==="
    
    # Test tool execution with proper authorization
    Invoke-TestRequest -TestName "Public tool execution (no auth)" -ExpectedStatus 200 `
        -Method POST -Uri "$script:BaseUrl/mcp/v1/tools/call" `
        -Body '{"name": "echo", "arguments": {"message": "test"}}'
    
    Invoke-TestRequest -TestName "User tool execution (user key)" -ExpectedStatus 200 `
        -Method POST -Headers @{"X-API-Key" = $script:UserKey} `
        -Uri "$script:BaseUrl/mcp/v1/tools/call" `
        -Body '{"name": "get_user_profile", "arguments": {}}'
    
    Invoke-TestRequest -TestName "Premium tool execution (premium key)" -ExpectedStatus 200 `
        -Method POST -Headers @{"X-API-Key" = $script:PremiumKey} `
        -Uri "$script:BaseUrl/mcp/v1/tools/call" `
        -Body '{"name": "premium_generate_secure_random", "arguments": {"byteCount": 16}}'
    
    Invoke-TestRequest -TestName "Admin tool execution (admin key)" -ExpectedStatus 200 `
        -Method POST -Headers @{"X-API-Key" = $script:AdminKey} `
        -Uri "$script:BaseUrl/mcp/v1/tools/call" `
        -Body '{"name": "admin_get_system_diagnostics", "arguments": {}}'
    
    # Test authorization failures
    Invoke-TestRequest -TestName "User trying admin tool (should fail)" -ExpectedStatus 401 `
        -Method POST -Headers @{"X-API-Key" = $script:UserKey} `
        -Uri "$script:BaseUrl/mcp/v1/tools/call" `
        -Body '{"name": "admin_get_system_diagnostics", "arguments": {}}'
    
    Invoke-TestRequest -TestName "Guest trying premium tool (should fail)" -ExpectedStatus 401 `
        -Method POST -Headers @{"X-API-Key" = $script:GuestKey} `
        -Uri "$script:BaseUrl/mcp/v1/tools/call" `
        -Body '{"name": "premium_generate_secure_random", "arguments": {"byteCount": 16}}'
    
    Invoke-TestRequest -TestName "Guest trying user tool (should fail)" -ExpectedStatus 401 `
        -Method POST -Headers @{"X-API-Key" = $script:GuestKey} `
        -Uri "$script:BaseUrl/mcp/v1/tools/call" `
        -Body '{"name": "get_user_profile", "arguments": {}}'
}

function Test-ToolVisibility {
    Write-InfoLog "=== Tool Visibility Tests ==="
    
    try {
        # Get tool lists for different roles and count them
        $guestResponse = Invoke-WebRequest -Uri "$script:BaseUrl/mcp/v1/tools" -Headers @{"X-API-Key" = $script:GuestKey} -UseBasicParsing
        $userResponse = Invoke-WebRequest -Uri "$script:BaseUrl/mcp/v1/tools" -Headers @{"X-API-Key" = $script:UserKey} -UseBasicParsing
        $premiumResponse = Invoke-WebRequest -Uri "$script:BaseUrl/mcp/v1/tools" -Headers @{"X-API-Key" = $script:PremiumKey} -UseBasicParsing
        $adminResponse = Invoke-WebRequest -Uri "$script:BaseUrl/mcp/v1/tools" -Headers @{"X-API-Key" = $script:AdminKey} -UseBasicParsing
        
        $guestTools = ($guestResponse.Content | ConvertFrom-Json).result.tools.Count
        $userTools = ($userResponse.Content | ConvertFrom-Json).result.tools.Count
        $premiumTools = ($premiumResponse.Content | ConvertFrom-Json).result.tools.Count
        $adminTools = ($adminResponse.Content | ConvertFrom-Json).result.tools.Count
        
        $script:TotalTests += 4
        
        # Verify hierarchical access (each higher role should see more or equal tools)
        if ($guestTools -gt 0) {
            Write-SuccessLog "Guest can see tools ($guestTools tools visible)"
        } else {
            Write-ErrorLog "Guest cannot see any tools"
        }
        
        if ($userTools -ge $guestTools) {
            Write-SuccessLog "User sees >= guest tools ($userTools >= $guestTools)"
        } else {
            Write-ErrorLog "User sees fewer tools than guest ($userTools < $guestTools)"
        }
        
        if ($premiumTools -ge $userTools) {
            Write-SuccessLog "Premium sees >= user tools ($premiumTools >= $userTools)"
        } else {
            Write-ErrorLog "Premium sees fewer tools than user ($premiumTools < $userTools)"
        }
        
        if ($adminTools -ge $premiumTools) {
            Write-SuccessLog "Admin sees >= premium tools ($adminTools >= $premiumTools)"
        } else {
            Write-ErrorLog "Admin sees fewer tools than premium ($adminTools < $premiumTools)"
        }
    }
    catch {
        Write-ErrorLog "Failed to test tool visibility: $($_.Exception.Message)"
        $script:TotalTests += 4
        $script:FailedTests += 4
    }
}

function Test-RateLimiting {
    Write-InfoLog "=== Rate Limiting Tests ==="
    
    # Test rapid requests to trigger rate limiting
    $successCount = 0
    $rateLimitedCount = 0
    
    Write-InfoLog "Making 10 rapid requests with guest key to test rate limiting..."
    
    for ($i = 1; $i -le 10; $i++) {
        try {
            $response = Invoke-WebRequest -Uri "$script:BaseUrl/mcp/v1/tools/call" `
                -Method POST -Headers @{"X-API-Key" = $script:GuestKey} `
                -Body '{"name": "echo", "arguments": {"message": "rate limit test"}}' `
                -ContentType "application/json" -UseBasicParsing -ErrorAction Stop
            
            if ($response.StatusCode -eq 200) {
                $successCount++
            }
        }
        catch {
            if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 429) {
                $rateLimitedCount++
            }
        }
        
        Start-Sleep -Milliseconds 100
    }
    
    $script:TotalTests++
    if ($successCount -gt 0) {
        Write-SuccessLog "Rate limiting test: $successCount successful, $rateLimitedCount rate-limited"
    } else {
        Write-ErrorLog "Rate limiting test: All requests failed"
    }
}

function Test-FeatureFlags {
    Write-InfoLog "=== Feature Flag Tests ==="
    
    # Test feature flag endpoint (admin only)
    Invoke-TestRequest -TestName "Get feature flags (admin)" -ExpectedStatus 200 `
        -Headers @{"X-API-Key" = $script:AdminKey} -Uri "$script:BaseUrl/admin/feature-flags"
    
    Invoke-TestRequest -TestName "Get feature flags (non-admin should fail)" -ExpectedStatus 401 `
        -Headers @{"X-API-Key" = $script:UserKey} -Uri "$script:BaseUrl/admin/feature-flags"
    
    # Test setting feature flags
    Invoke-TestRequest -TestName "Set feature flag (admin)" -ExpectedStatus 200 `
        -Method POST -Headers @{"X-API-Key" = $script:AdminKey} `
        -Uri "$script:BaseUrl/admin/feature-flags/premium_features?enabled=true"
}

function Test-ErrorHandling {
    Write-InfoLog "=== Error Handling Tests ==="
    
    # Test malformed requests
    Invoke-TestRequest -TestName "Malformed JSON request" -ExpectedStatus 400 `
        -Method POST -Headers @{"X-API-Key" = $script:UserKey} `
        -Uri "$script:BaseUrl/mcp/v1/tools/call" `
        -Body '{"invalid": json}'
    
    # Test nonexistent tool
    Invoke-TestRequest -TestName "Nonexistent tool execution" -ExpectedStatus 400 `
        -Method POST -Headers @{"X-API-Key" = $script:UserKey} `
        -Uri "$script:BaseUrl/mcp/v1/tools/call" `
        -Body '{"name": "nonexistent_tool", "arguments": {}}'
    
    # Test invalid arguments
    Invoke-TestRequest -TestName "Invalid tool arguments" -ExpectedStatus 400 `
        -Method POST -Headers @{"X-API-Key" = $script:UserKey} `
        -Uri "$script:BaseUrl/mcp/v1/tools/call" `
        -Body '{"name": "calculate_hash", "arguments": {"text": "test", "algorithm": "invalid"}}'
    
    # Test nonexistent endpoint
    Invoke-TestRequest -TestName "Nonexistent endpoint" -ExpectedStatus 404 `
        -Uri "$script:BaseUrl/nonexistent/endpoint"
}

function Test-Performance {
    Write-InfoLog "=== Performance Tests ==="
    
    # Test concurrent requests
    Write-InfoLog "Testing concurrent requests..."
    $startTime = Get-Date
    
    # Launch 5 concurrent requests
    $jobs = @()
    for ($i = 1; $i -le 5; $i++) {
        $job = Start-Job -ScriptBlock {
            param($baseUrl, $userKey)
            try {
                Invoke-WebRequest -Uri "$baseUrl/mcp/v1/tools/call" `
                    -Method POST -Headers @{"X-API-Key" = $userKey} `
                    -Body '{"name": "echo", "arguments": {"message": "concurrent test"}}' `
                    -ContentType "application/json" -UseBasicParsing -ErrorAction Stop
                return $true
            }
            catch {
                return $false
            }
        } -ArgumentList $script:BaseUrl, $script:UserKey
        
        $jobs += $job
    }
    
    # Wait for all requests to complete
    $jobs | Wait-Job | Out-Null
    $endTime = Get-Date
    $duration = ($endTime - $startTime).TotalSeconds
    
    # Clean up jobs
    $jobs | Remove-Job
    
    $script:TotalTests++
    if ($duration -le 10) {
        Write-SuccessLog "Concurrent requests completed in $([Math]::Round($duration, 2))s (acceptable)"
    } else {
        Write-ErrorLog "Concurrent requests took $([Math]::Round($duration, 2))s (too slow)"
    }
}

function Invoke-SpecificCategory {
    param([string]$TestCategory)
    
    switch ($TestCategory) {
        "health" { Test-HealthCheck }
        "authentication" { Test-Authentication }
        "authorization" { Test-Authorization }
        "visibility" { Test-ToolVisibility }
        "rate-limiting" { Test-RateLimiting }
        "feature-flags" { Test-FeatureFlags }
        "error-handling" { Test-ErrorHandling }
        "performance" { Test-Performance }
        "all" {
            Test-HealthCheck
            Test-Authentication
            Test-Authorization
            Test-ToolVisibility
            Test-RateLimiting
            Test-FeatureFlags
            Test-ErrorHandling
            Test-Performance
        }
        default {
            Write-ErrorLog "Unknown test category: $TestCategory"
            Write-Host "Available categories: health, authentication, authorization, visibility, rate-limiting, feature-flags, error-handling, performance, all"
            exit 1
        }
    }
}

# Main execution
function Main {
    Write-Host "==================================" -ForegroundColor Cyan
    Write-Host "Dynamic Tool Filtering Test Suite" -ForegroundColor Cyan
    Write-Host "==================================" -ForegroundColor Cyan
    Write-Host "Server: $script:BaseUrl"
    Write-Host "Category: $Category"
    Write-Host "Time: $(Get-Date)"
    Write-Host ""
    
    # Wait for server to be ready
    if (-not (Wait-ForServer)) {
        exit 1
    }
    
    # Run tests
    Invoke-SpecificCategory -TestCategory $Category
    
    # Results summary
    Write-Host ""
    Write-Host "==================================" -ForegroundColor Cyan
    Write-Host "Test Results Summary" -ForegroundColor Cyan
    Write-Host "==================================" -ForegroundColor Cyan
    Write-Host "Total Tests: $script:TotalTests"
    Write-Host "Passed: $script:PassedTests" -ForegroundColor Green
    Write-Host "Failed: $script:FailedTests" -ForegroundColor Red
    
    if ($script:FailedTests -eq 0) {
        Write-Host "`n✅ All tests passed!" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "`n❌ Some tests failed!" -ForegroundColor Red
        exit 1
    }
}

# Execute main function
Main
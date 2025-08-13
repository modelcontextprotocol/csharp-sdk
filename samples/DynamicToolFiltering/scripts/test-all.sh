#!/bin/bash

# Dynamic Tool Filtering - Comprehensive Test Suite
# This script tests all aspects of the MCP server including authentication,
# authorization, rate limiting, feature flags, and error handling.

set -e

# Configuration
BASE_URL="${BASE_URL:-http://localhost:8080}"
GUEST_KEY="demo-guest-key"
USER_KEY="demo-user-key"
PREMIUM_KEY="demo-premium-key"
ADMIN_KEY="demo-admin-key"
INVALID_KEY="invalid-test-key"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Test counters
TOTAL_TESTS=0
PASSED_TESTS=0
FAILED_TESTS=0

# Test categories
CATEGORY="${1:-all}"

# Helper functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[PASS]${NC} $1"
    ((PASSED_TESTS++))
}

log_error() {
    echo -e "${RED}[FAIL]${NC} $1"
    ((FAILED_TESTS++))
}

log_warning() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

run_test() {
    local test_name="$1"
    local expected_status="$2"
    local curl_args="${@:3}"
    
    ((TOTAL_TESTS++))
    log_info "Running test: $test_name"
    
    # Execute curl command and capture both status and response
    local response
    local status
    response=$(curl -s -w "HTTPSTATUS:%{http_code}" $curl_args 2>/dev/null || echo "HTTPSTATUS:000")
    status=$(echo "$response" | grep -o "HTTPSTATUS:[0-9]*" | cut -d: -f2)
    local body=$(echo "$response" | sed -E 's/HTTPSTATUS:[0-9]*$//')
    
    if [[ "$status" == "$expected_status" ]]; then
        log_success "$test_name (HTTP $status)"
        if [[ -n "$body" && "$body" != "null" ]]; then
            echo "    Response: $(echo "$body" | jq -r . 2>/dev/null || echo "$body" | head -c 100)..."
        fi
    else
        log_error "$test_name (Expected HTTP $expected_status, got HTTP $status)"
        if [[ -n "$body" ]]; then
            echo "    Response: $(echo "$body" | jq -r . 2>/dev/null || echo "$body" | head -c 200)..."
        fi
    fi
    
    echo ""
}

wait_for_server() {
    log_info "Waiting for server to be ready at $BASE_URL..."
    local retries=30
    local count=0
    
    while [ $count -lt $retries ]; do
        if curl -s "$BASE_URL/health" > /dev/null 2>&1; then
            log_success "Server is ready!"
            return 0
        fi
        
        ((count++))
        if [ $count -eq $retries ]; then
            log_error "Server did not start within expected time"
            return 1
        fi
        
        sleep 2
    done
}

test_health_check() {
    log_info "=== Health Check Tests ==="
    run_test "Health endpoint availability" "200" "$BASE_URL/health"
}

test_authentication() {
    log_info "=== Authentication Tests ==="
    
    # Test valid API keys
    run_test "Guest API key authentication" "200" \
        -H "X-API-Key: $GUEST_KEY" "$BASE_URL/mcp/v1/tools"
    
    run_test "User API key authentication" "200" \
        -H "X-API-Key: $USER_KEY" "$BASE_URL/mcp/v1/tools"
    
    run_test "Premium API key authentication" "200" \
        -H "X-API-Key: $PREMIUM_KEY" "$BASE_URL/mcp/v1/tools"
    
    run_test "Admin API key authentication" "200" \
        -H "X-API-Key: $ADMIN_KEY" "$BASE_URL/mcp/v1/tools"
    
    # Test invalid API key
    run_test "Invalid API key rejection" "401" \
        -H "X-API-Key: $INVALID_KEY" "$BASE_URL/mcp/v1/tools"
    
    # Test missing API key for protected endpoint
    run_test "Missing API key for admin endpoint" "401" \
        "$BASE_URL/admin/filters/status"
}

test_authorization() {
    log_info "=== Authorization Tests ==="
    
    # Test tool execution with proper authorization
    run_test "Public tool execution (no auth)" "200" \
        -X POST \
        -H "Content-Type: application/json" \
        -d '{"name": "echo", "arguments": {"message": "test"}}' \
        "$BASE_URL/mcp/v1/tools/call"
    
    run_test "User tool execution (user key)" "200" \
        -X POST \
        -H "X-API-Key: $USER_KEY" \
        -H "Content-Type: application/json" \
        -d '{"name": "get_user_profile", "arguments": {}}' \
        "$BASE_URL/mcp/v1/tools/call"
    
    run_test "Premium tool execution (premium key)" "200" \
        -X POST \
        -H "X-API-Key: $PREMIUM_KEY" \
        -H "Content-Type: application/json" \
        -d '{"name": "premium_generate_secure_random", "arguments": {"byteCount": 16}}' \
        "$BASE_URL/mcp/v1/tools/call"
    
    run_test "Admin tool execution (admin key)" "200" \
        -X POST \
        -H "X-API-Key: $ADMIN_KEY" \
        -H "Content-Type: application/json" \
        -d '{"name": "admin_get_system_diagnostics", "arguments": {}}' \
        "$BASE_URL/mcp/v1/tools/call"
    
    # Test authorization failures
    run_test "User trying admin tool (should fail)" "401" \
        -X POST \
        -H "X-API-Key: $USER_KEY" \
        -H "Content-Type: application/json" \
        -d '{"name": "admin_get_system_diagnostics", "arguments": {}}' \
        "$BASE_URL/mcp/v1/tools/call"
    
    run_test "Guest trying premium tool (should fail)" "401" \
        -X POST \
        -H "X-API-Key: $GUEST_KEY" \
        -H "Content-Type: application/json" \
        -d '{"name": "premium_generate_secure_random", "arguments": {"byteCount": 16}}' \
        "$BASE_URL/mcp/v1/tools/call"
    
    run_test "Guest trying user tool (should fail)" "401" \
        -X POST \
        -H "X-API-Key: $GUEST_KEY" \
        -H "Content-Type: application/json" \
        -d '{"name": "get_user_profile", "arguments": {}}' \
        "$BASE_URL/mcp/v1/tools/call"
}

test_tool_visibility() {
    log_info "=== Tool Visibility Tests ==="
    
    # Get tool lists for different roles and count them
    local guest_tools=$(curl -s -H "X-API-Key: $GUEST_KEY" "$BASE_URL/mcp/v1/tools" | jq -r '.result.tools | length' 2>/dev/null || echo "0")
    local user_tools=$(curl -s -H "X-API-Key: $USER_KEY" "$BASE_URL/mcp/v1/tools" | jq -r '.result.tools | length' 2>/dev/null || echo "0")
    local premium_tools=$(curl -s -H "X-API-Key: $PREMIUM_KEY" "$BASE_URL/mcp/v1/tools" | jq -r '.result.tools | length' 2>/dev/null || echo "0")
    local admin_tools=$(curl -s -H "X-API-Key: $ADMIN_KEY" "$BASE_URL/mcp/v1/tools" | jq -r '.result.tools | length' 2>/dev/null || echo "0")
    
    ((TOTAL_TESTS += 4))
    
    # Verify hierarchical access (each higher role should see more or equal tools)
    if [[ "$guest_tools" -gt 0 ]]; then
        log_success "Guest can see tools ($guest_tools tools visible)"
        ((PASSED_TESTS++))
    else
        log_error "Guest cannot see any tools"
        ((FAILED_TESTS++))
    fi
    
    if [[ "$user_tools" -ge "$guest_tools" ]]; then
        log_success "User sees >= guest tools ($user_tools >= $guest_tools)"
        ((PASSED_TESTS++))
    else
        log_error "User sees fewer tools than guest ($user_tools < $guest_tools)"
        ((FAILED_TESTS++))
    fi
    
    if [[ "$premium_tools" -ge "$user_tools" ]]; then
        log_success "Premium sees >= user tools ($premium_tools >= $user_tools)"
        ((PASSED_TESTS++))
    else
        log_error "Premium sees fewer tools than user ($premium_tools < $user_tools)"
        ((FAILED_TESTS++))
    fi
    
    if [[ "$admin_tools" -ge "$premium_tools" ]]; then
        log_success "Admin sees >= premium tools ($admin_tools >= $premium_tools)"
        ((PASSED_TESTS++))
    else
        log_error "Admin sees fewer tools than premium ($admin_tools < $premium_tools)"
        ((FAILED_TESTS++))
    fi
}

test_rate_limiting() {
    log_info "=== Rate Limiting Tests ==="
    
    # Test rapid requests to trigger rate limiting
    local success_count=0
    local rate_limited_count=0
    
    log_info "Making 10 rapid requests with guest key to test rate limiting..."
    
    for i in {1..10}; do
        local status=$(curl -s -w "%{http_code}" -o /dev/null \
            -H "X-API-Key: $GUEST_KEY" \
            -X POST \
            -H "Content-Type: application/json" \
            -d '{"name": "echo", "arguments": {"message": "rate limit test"}}' \
            "$BASE_URL/mcp/v1/tools/call")
        
        if [[ "$status" == "200" ]]; then
            ((success_count++))
        elif [[ "$status" == "429" ]]; then
            ((rate_limited_count++))
        fi
        
        sleep 0.1
    done
    
    ((TOTAL_TESTS++))
    if [[ "$success_count" -gt 0 ]]; then
        log_success "Rate limiting test: $success_count successful, $rate_limited_count rate-limited"
        ((PASSED_TESTS++))
    else
        log_error "Rate limiting test: All requests failed"
        ((FAILED_TESTS++))
    fi
}

test_feature_flags() {
    log_info "=== Feature Flag Tests ==="
    
    # Test feature flag endpoint (admin only)
    run_test "Get feature flags (admin)" "200" \
        -H "X-API-Key: $ADMIN_KEY" \
        "$BASE_URL/admin/feature-flags"
    
    run_test "Get feature flags (non-admin should fail)" "401" \
        -H "X-API-Key: $USER_KEY" \
        "$BASE_URL/admin/feature-flags"
    
    # Test setting feature flags
    run_test "Set feature flag (admin)" "200" \
        -X POST \
        -H "X-API-Key: $ADMIN_KEY" \
        "$BASE_URL/admin/feature-flags/premium_features?enabled=true"
}

test_error_handling() {
    log_info "=== Error Handling Tests ==="
    
    # Test malformed requests
    run_test "Malformed JSON request" "400" \
        -X POST \
        -H "X-API-Key: $USER_KEY" \
        -H "Content-Type: application/json" \
        -d '{"invalid": json}' \
        "$BASE_URL/mcp/v1/tools/call"
    
    # Test nonexistent tool
    run_test "Nonexistent tool execution" "400" \
        -X POST \
        -H "X-API-Key: $USER_KEY" \
        -H "Content-Type: application/json" \
        -d '{"name": "nonexistent_tool", "arguments": {}}' \
        "$BASE_URL/mcp/v1/tools/call"
    
    # Test invalid arguments
    run_test "Invalid tool arguments" "400" \
        -X POST \
        -H "X-API-Key: $USER_KEY" \
        -H "Content-Type: application/json" \
        -d '{"name": "calculate_hash", "arguments": {"text": "test", "algorithm": "invalid"}}' \
        "$BASE_URL/mcp/v1/tools/call"
    
    # Test nonexistent endpoint
    run_test "Nonexistent endpoint" "404" \
        "$BASE_URL/nonexistent/endpoint"
}

test_performance() {
    log_info "=== Performance Tests ==="
    
    # Test concurrent requests
    log_info "Testing concurrent requests..."
    local start_time=$(date +%s)
    local pids=()
    
    # Launch 5 concurrent requests
    for i in {1..5}; do
        (curl -s -o /dev/null \
            -H "X-API-Key: $USER_KEY" \
            -X POST \
            -H "Content-Type: application/json" \
            -d '{"name": "echo", "arguments": {"message": "concurrent test"}}' \
            "$BASE_URL/mcp/v1/tools/call") &
        pids+=($!)
    done
    
    # Wait for all requests to complete
    for pid in "${pids[@]}"; do
        wait $pid
    done
    
    local end_time=$(date +%s)
    local duration=$((end_time - start_time))
    
    ((TOTAL_TESTS++))
    if [[ $duration -le 10 ]]; then
        log_success "Concurrent requests completed in ${duration}s (acceptable)"
        ((PASSED_TESTS++))
    else
        log_error "Concurrent requests took ${duration}s (too slow)"
        ((FAILED_TESTS++))
    fi
}

run_specific_category() {
    case $CATEGORY in
        "health")
            test_health_check
            ;;
        "authentication")
            test_authentication
            ;;
        "authorization")
            test_authorization
            ;;
        "visibility")
            test_tool_visibility
            ;;
        "rate-limiting")
            test_rate_limiting
            ;;
        "feature-flags")
            test_feature_flags
            ;;
        "error-handling")
            test_error_handling
            ;;
        "performance")
            test_performance
            ;;
        "all")
            test_health_check
            test_authentication
            test_authorization
            test_tool_visibility
            test_rate_limiting
            test_feature_flags
            test_error_handling
            test_performance
            ;;
        *)
            log_error "Unknown test category: $CATEGORY"
            echo "Available categories: health, authentication, authorization, visibility, rate-limiting, feature-flags, error-handling, performance, all"
            exit 1
            ;;
    esac
}

show_help() {
    echo "Dynamic Tool Filtering - Test Suite"
    echo ""
    echo "Usage: $0 [CATEGORY]"
    echo ""
    echo "Categories:"
    echo "  health         - Test health endpoint"
    echo "  authentication - Test API key authentication"
    echo "  authorization  - Test role-based authorization"
    echo "  visibility     - Test tool visibility by role"
    echo "  rate-limiting  - Test rate limiting functionality"
    echo "  feature-flags  - Test feature flag management"
    echo "  error-handling - Test error responses"
    echo "  performance    - Test performance and concurrency"
    echo "  all            - Run all tests (default)"
    echo ""
    echo "Environment Variables:"
    echo "  BASE_URL       - Server URL (default: http://localhost:8080)"
    echo ""
    echo "Examples:"
    echo "  $0                           # Run all tests"
    echo "  $0 authentication            # Run only authentication tests"
    echo "  BASE_URL=http://localhost:9000 $0  # Test different server"
}

# Main execution
main() {
    if [[ "$1" == "--help" || "$1" == "-h" ]]; then
        show_help
        exit 0
    fi
    
    echo "=================================="
    echo "Dynamic Tool Filtering Test Suite"
    echo "=================================="
    echo "Server: $BASE_URL"
    echo "Category: $CATEGORY"
    echo "Time: $(date)"
    echo ""
    
    # Check dependencies
    if ! command -v curl &> /dev/null; then
        log_error "curl is required but not installed"
        exit 1
    fi
    
    if ! command -v jq &> /dev/null; then
        log_warning "jq is not installed - JSON parsing will be limited"
    fi
    
    # Wait for server to be ready
    wait_for_server || exit 1
    
    # Run tests
    run_specific_category
    
    # Results summary
    echo ""
    echo "=================================="
    echo "Test Results Summary"
    echo "=================================="
    echo "Total Tests: $TOTAL_TESTS"
    echo -e "Passed: ${GREEN}$PASSED_TESTS${NC}"
    echo -e "Failed: ${RED}$FAILED_TESTS${NC}"
    
    if [[ $FAILED_TESTS -eq 0 ]]; then
        echo -e "\n${GREEN}✅ All tests passed!${NC}"
        exit 0
    else
        echo -e "\n${RED}❌ Some tests failed!${NC}"
        exit 1
    fi
}

# Run main function with all arguments
main "$@"
#!/bin/bash

# Demo script to showcase per-user tool filtering in ASP.NET Core MCP Server
# Usage: ./demo.sh (make sure the server is running on localhost:3001)

echo "==============================================="
echo "ASP.NET Core MCP Server Per-User Tool Filter Demo"
echo "==============================================="
echo ""

BASE_URL="http://localhost:3001"
HEADERS_JSON=(-H "Content-Type: application/json" -H "Accept: application/json, text/event-stream")
LIST_TOOLS='{"jsonrpc":"2.0","id":1,"method":"tools/list"}'

echo "1. Testing ANONYMOUS user (no authentication headers):"
echo "   Expected tools: echo, get_time (2 total)"
echo "   ---"
response=$(curl -s -X POST "$BASE_URL/" "${HEADERS_JSON[@]}" -d "$LIST_TOOLS")
tool_count=$(echo "$response" | grep -o '"name":"[^"]*"' | wc -l)
echo "   Tools available: $tool_count"
echo "$response" | grep -o '"name":"[^"]*"' | sed 's/"name":"/ - /' | sed 's/"//'
echo ""

echo "2. Testing REGULAR USER (user role):"
echo "   Expected tools: echo, get_time, calculate, get_user_info (4 total)"
echo "   ---"
USER_HEADERS=(-H "X-User-Role: user" -H "X-User-Id: user-alice")
response=$(curl -s -X POST "$BASE_URL/" "${HEADERS_JSON[@]}" "${USER_HEADERS[@]}" -d "$LIST_TOOLS")
tool_count=$(echo "$response" | grep -o '"name":"[^"]*"' | wc -l)
echo "   Tools available: $tool_count"
echo "$response" | grep -o '"name":"[^"]*"' | sed 's/"name":"/ - /' | sed 's/"//'
echo ""

echo "3. Testing ADMIN USER (admin role):"
echo "   Expected tools: all 7 tools including admin-only ones"
echo "   ---"
ADMIN_HEADERS=(-H "X-User-Role: admin" -H "X-User-Id: admin-john")
response=$(curl -s -X POST "$BASE_URL/" "${HEADERS_JSON[@]}" "${ADMIN_HEADERS[@]}" -d "$LIST_TOOLS")
tool_count=$(echo "$response" | grep -o '"name":"[^"]*"' | wc -l)
echo "   Tools available: $tool_count"
echo "$response" | grep -o '"name":"[^"]*"' | sed 's/"name":"/ - /' | sed 's/"//'
echo ""

echo "4. Testing tool execution - Admin calling system status:"
echo "   ---"
CALL_ADMIN_TOOL='{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"get_system_status","arguments":{}}}'
response=$(curl -s -X POST "$BASE_URL/" "${HEADERS_JSON[@]}" "${ADMIN_HEADERS[@]}" -d "$CALL_ADMIN_TOOL")
echo "$response" | grep -o '"text":"[^"]*"' | sed 's/"text":"/' | sed 's/"//' | head -1
echo ""

echo "5. Testing tool execution - User calling calculator:"
echo "   ---" 
CALL_USER_TOOL='{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"calculate","arguments":{"expression":"10 + 15"}}}'
response=$(curl -s -X POST "$BASE_URL/" "${HEADERS_JSON[@]}" "${USER_HEADERS[@]}" -d "$CALL_USER_TOOL")
echo "$response" | grep -o '"text":"[^"]*"' | sed 's/"text":"/' | sed 's/"//' | head -1
echo ""

echo "==============================================="
echo "Demo completed! Per-user tool filtering working correctly."
echo "==============================================="
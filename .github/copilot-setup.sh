#!/bin/bash
set -e

echo "=== Copilot Setup: .NET SDK Installation ==="

# Parse the SDK version from global.json
GLOBAL_JSON_PATH="${GITHUB_WORKSPACE:-$(pwd)}/global.json"

if [ ! -f "$GLOBAL_JSON_PATH" ]; then
    echo "Error: global.json not found at $GLOBAL_JSON_PATH"
    exit 1
fi

echo "Reading SDK version from global.json..."
SDK_VERSION=$(grep -A 3 '"sdk"' "$GLOBAL_JSON_PATH" | grep '"version"' | sed 's/.*"version"[^"]*"\([^"]*\)".*/\1/')

if [ -z "$SDK_VERSION" ]; then
    echo "Error: Could not parse SDK version from global.json"
    exit 1
fi

echo "Required SDK version: $SDK_VERSION"

# Check if the SDK is already installed
if dotnet --list-sdks | grep -q "$SDK_VERSION"; then
    echo "✓ .NET SDK $SDK_VERSION is already installed"
else
    echo "Installing .NET SDK $SDK_VERSION..."
    
    # Download and install the .NET SDK
    # Using the dotnet-install script from Microsoft
    curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --version "$SDK_VERSION" --install-dir /usr/share/dotnet
    
    echo "✓ .NET SDK $SDK_VERSION installed successfully"
fi

# Display installed SDK information
echo ""
echo "=== Installed .NET SDKs ==="
dotnet --list-sdks

echo ""
echo "=== Current .NET SDK Version ==="
dotnet --version

echo ""
echo "=== .NET Info ==="
dotnet --info

echo ""
echo "=== Copilot Setup Complete ==="

#!/bin/bash

# Dynamic Tool Filtering - Development Environment Setup Script
# This script sets up a complete development environment with VS Code configuration,
# Docker setup, and necessary tools for MCP development.

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Helper functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

check_dependency() {
    local cmd="$1"
    local name="$2"
    local install_hint="$3"
    
    if command -v "$cmd" &> /dev/null; then
        log_success "$name is installed"
        return 0
    else
        log_warning "$name is not installed. $install_hint"
        return 1
    fi
}

create_vscode_config() {
    log_info "Creating VS Code configuration..."
    
    mkdir -p .vscode
    
    # Create launch.json for debugging
    cat > .vscode/launch.json << 'EOF'
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Launch (Development)",
            "type": "coreclr",
            "request": "launch",
            "preLaunchTask": "build",
            "program": "${workspaceFolder}/bin/Debug/net9.0/DynamicToolFiltering.dll",
            "args": [],
            "cwd": "${workspaceFolder}",
            "console": "integratedTerminal",
            "stopAtEntry": false,
            "env": {
                "ASPNETCORE_ENVIRONMENT": "Development",
                "ASPNETCORE_URLS": "http://localhost:8080",
                "Filtering__Enabled": "true",
                "Filtering__RoleBased__Enabled": "true",
                "Filtering__TimeBased__Enabled": "false",
                "Filtering__ScopeBased__Enabled": "true",
                "Filtering__RateLimiting__Enabled": "true",
                "Filtering__TenantIsolation__Enabled": "false",
                "Filtering__BusinessLogic__Enabled": "true"
            },
            "serverReadyAction": {
                "action": "openExternally",
                "pattern": "\\bNow listening on:\\s+(https?://\\S+)"
            }
        },
        {
            "name": "Launch (No Filtering)",
            "type": "coreclr",
            "request": "launch",
            "preLaunchTask": "build",
            "program": "${workspaceFolder}/bin/Debug/net9.0/DynamicToolFiltering.dll",
            "args": [],
            "cwd": "${workspaceFolder}",
            "console": "integratedTerminal",
            "stopAtEntry": false,
            "env": {
                "ASPNETCORE_ENVIRONMENT": "Development",
                "ASPNETCORE_URLS": "http://localhost:8080",
                "Filtering__Enabled": "false"
            }
        },
        {
            "name": "Launch (Production Mode)",
            "type": "coreclr",
            "request": "launch",
            "preLaunchTask": "build",
            "program": "${workspaceFolder}/bin/Debug/net9.0/DynamicToolFiltering.dll",
            "args": [],
            "cwd": "${workspaceFolder}",
            "console": "integratedTerminal",
            "stopAtEntry": false,
            "env": {
                "ASPNETCORE_ENVIRONMENT": "Production",
                "ASPNETCORE_URLS": "http://localhost:8080",
                "Filtering__Enabled": "true",
                "Filtering__RoleBased__Enabled": "true",
                "Filtering__TimeBased__Enabled": "true",
                "Filtering__ScopeBased__Enabled": "true",
                "Filtering__RateLimiting__Enabled": "true",
                "Filtering__TenantIsolation__Enabled": "true",
                "Filtering__BusinessLogic__Enabled": "true"
            }
        },
        {
            "name": "Attach to Process",
            "type": "coreclr",
            "request": "attach",
            "processId": "${command:pickProcess}"
        }
    ]
}
EOF
    
    # Create tasks.json for build tasks
    cat > .vscode/tasks.json << 'EOF'
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "build",
            "command": "dotnet",
            "type": "process",
            "args": [
                "build",
                "${workspaceFolder}/DynamicToolFiltering.csproj",
                "/property:GenerateFullPaths=true",
                "/consoleloggerparameters:NoSummary"
            ],
            "problemMatcher": "$msCompile",
            "group": {
                "kind": "build",
                "isDefault": true
            }
        },
        {
            "label": "publish",
            "command": "dotnet",
            "type": "process",
            "args": [
                "publish",
                "${workspaceFolder}/DynamicToolFiltering.csproj",
                "/property:GenerateFullPaths=true",
                "/consoleloggerparameters:NoSummary"
            ],
            "problemMatcher": "$msCompile"
        },
        {
            "label": "watch",
            "command": "dotnet",
            "type": "process",
            "args": [
                "watch",
                "run",
                "${workspaceFolder}/DynamicToolFiltering.csproj"
            ],
            "problemMatcher": "$msCompile"
        },
        {
            "label": "test",
            "command": "./scripts/test-all.sh",
            "type": "shell",
            "group": "test",
            "presentation": {
                "echo": true,
                "reveal": "always",
                "focus": false,
                "panel": "shared",
                "showReuseMessage": true,
                "clear": false
            },
            "problemMatcher": []
        },
        {
            "label": "test-authentication",
            "command": "./scripts/test-all.sh",
            "type": "shell",
            "args": ["authentication"],
            "group": "test",
            "presentation": {
                "echo": true,
                "reveal": "always",
                "focus": false,
                "panel": "shared"
            }
        },
        {
            "label": "test-authorization",
            "command": "./scripts/test-all.sh",
            "type": "shell",
            "args": ["authorization"],
            "group": "test",
            "presentation": {
                "echo": true,
                "reveal": "always",
                "focus": false,
                "panel": "shared"
            }
        },
        {
            "label": "docker-build",
            "command": "docker",
            "type": "shell",
            "args": [
                "build",
                "-t",
                "dynamic-tool-filtering",
                "."
            ],
            "group": "build",
            "presentation": {
                "echo": true,
                "reveal": "always",
                "focus": false,
                "panel": "shared"
            }
        },
        {
            "label": "docker-run",
            "command": "docker",
            "type": "shell",
            "args": [
                "run",
                "-p",
                "8080:8080",
                "--rm",
                "dynamic-tool-filtering"
            ],
            "group": "build",
            "presentation": {
                "echo": true,
                "reveal": "always",
                "focus": false,
                "panel": "shared"
            },
            "dependsOn": "docker-build"
        }
    ]
}
EOF
    
    # Create settings.json for VS Code settings
    cat > .vscode/settings.json << 'EOF'
{
    "dotnet.defaultSolution": "DynamicToolFiltering.csproj",
    "omnisharp.enableEditorConfigSupport": true,
    "omnisharp.enableImportCompletion": true,
    "omnisharp.enableRoslynAnalyzers": true,
    "files.exclude": {
        "**/bin": true,
        "**/obj": true,
        "**/.vs": true
    },
    "files.watcherExclude": {
        "**/bin/**": true,
        "**/obj/**": true,
        "**/.vs/**": true
    },
    "csharp.semanticHighlighting.enabled": true,
    "editor.formatOnSave": true,
    "editor.codeActionsOnSave": {
        "source.fixAll": "explicit"
    },
    "json.schemas": [
        {
            "fileMatch": ["appsettings*.json"],
            "schema": {
                "type": "object",
                "properties": {
                    "Filtering": {
                        "type": "object",
                        "description": "Tool filtering configuration",
                        "properties": {
                            "Enabled": {"type": "boolean"},
                            "RoleBased": {"type": "object"},
                            "TimeBased": {"type": "object"},
                            "ScopeBased": {"type": "object"},
                            "RateLimiting": {"type": "object"},
                            "TenantIsolation": {"type": "object"},
                            "BusinessLogic": {"type": "object"}
                        }
                    }
                }
            }
        }
    ],
    "rest-client.environmentVariables": {
        "local": {
            "baseUrl": "http://localhost:8080",
            "guestKey": "demo-guest-key",
            "userKey": "demo-user-key",
            "premiumKey": "demo-premium-key",
            "adminKey": "demo-admin-key"
        }
    }
}
EOF

    # Create extensions.json for recommended VS Code extensions
    cat > .vscode/extensions.json << 'EOF'
{
    "recommendations": [
        "ms-dotnettools.csharp",
        "ms-dotnettools.vscode-dotnet-runtime",
        "humao.rest-client",
        "ms-vscode.vscode-json",
        "redhat.vscode-yaml",
        "ms-azuretools.vscode-docker",
        "github.copilot",
        "streetsidesoftware.code-spell-checker",
        "esbenp.prettier-vscode",
        "bradlc.vscode-tailwindcss"
    ]
}
EOF
    
    log_success "VS Code configuration created"
}

create_docker_config() {
    log_info "Creating Docker configuration..."
    
    # Create Dockerfile
    cat > Dockerfile << 'EOF'
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY DynamicToolFiltering.csproj .
RUN dotnet restore

# Copy source code and build
COPY . .
RUN dotnet build -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=publish /app/publish .

# Create logs directory
RUN mkdir -p logs

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# Start application
ENTRYPOINT ["dotnet", "DynamicToolFiltering.dll"]
EOF
    
    # Create docker-compose.yml
    cat > docker-compose.yml << 'EOF'
version: '3.8'

services:
  dynamic-tool-filtering:
    build: .
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:8080
      - Filtering__Enabled=true
      - Filtering__RoleBased__Enabled=true
      - Filtering__TimeBased__Enabled=false
      - Filtering__ScopeBased__Enabled=true
      - Filtering__RateLimiting__Enabled=true
      - Filtering__TenantIsolation__Enabled=false
      - Filtering__BusinessLogic__Enabled=true
    volumes:
      - ./logs:/app/logs
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 10s

  # Optional: Redis for production-ready rate limiting
  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    restart: unless-stopped
    profiles:
      - production
    
  # Optional: PostgreSQL for production-ready quota management
  postgres:
    image: postgres:15-alpine
    ports:
      - "5432:5432"
    environment:
      - POSTGRES_DB=dynamic_tool_filtering
      - POSTGRES_USER=mcpuser
      - POSTGRES_PASSWORD=mcppassword
    volumes:
      - postgres_data:/var/lib/postgresql/data
    restart: unless-stopped
    profiles:
      - production

volumes:
  postgres_data:
EOF
    
    # Create .dockerignore
    cat > .dockerignore << 'EOF'
# Build artifacts
bin/
obj/
*.dll
*.exe
*.pdb

# Development files
.vs/
.vscode/
*.user
*.suo

# Logs
logs/
*.log

# OS files
.DS_Store
Thumbs.db

# Git
.git/
.gitignore

# Documentation (include in image if needed)
# *.md

# Test results
TestResults/
coverage/

# Node modules (if any)
node_modules/

# Temporary files
*.tmp
*.temp
EOF
    
    log_success "Docker configuration created"
}

create_rest_client_config() {
    log_info "Creating REST Client configuration for testing..."
    
    mkdir -p tests
    
    cat > tests/api-tests.http << 'EOF'
### Dynamic Tool Filtering API Tests
### Use with VS Code REST Client extension

@baseUrl = http://localhost:8080
@guestKey = demo-guest-key
@userKey = demo-user-key
@premiumKey = demo-premium-key
@adminKey = demo-admin-key

### Health Check
GET {{baseUrl}}/health

### List Tools - Guest User
GET {{baseUrl}}/mcp/v1/tools
X-API-Key: {{guestKey}}

### List Tools - User
GET {{baseUrl}}/mcp/v1/tools
X-API-Key: {{userKey}}

### List Tools - Premium User
GET {{baseUrl}}/mcp/v1/tools
X-API-Key: {{premiumKey}}

### List Tools - Admin User
GET {{baseUrl}}/mcp/v1/tools
X-API-Key: {{adminKey}}

### Execute Public Tool (Echo)
POST {{baseUrl}}/mcp/v1/tools/call
Content-Type: application/json

{
  "name": "echo",
  "arguments": {
    "message": "Hello from REST Client!"
  }
}

### Execute User Tool - Get Profile
POST {{baseUrl}}/mcp/v1/tools/call
Content-Type: application/json
X-API-Key: {{userKey}}

{
  "name": "get_user_profile",
  "arguments": {}
}

### Execute Premium Tool - Generate Secure Random
POST {{baseUrl}}/mcp/v1/tools/call
Content-Type: application/json
X-API-Key: {{premiumKey}}

{
  "name": "premium_generate_secure_random",
  "arguments": {
    "byteCount": 32,
    "format": "hex"
  }
}

### Execute Admin Tool - System Diagnostics
POST {{baseUrl}}/mcp/v1/tools/call
Content-Type: application/json
X-API-Key: {{adminKey}}

{
  "name": "admin_get_system_diagnostics",
  "arguments": {}
}

### Test Authorization Failure - User trying Admin Tool
POST {{baseUrl}}/mcp/v1/tools/call
Content-Type: application/json
X-API-Key: {{userKey}}

{
  "name": "admin_get_system_diagnostics",
  "arguments": {}
}

### Test Invalid API Key
GET {{baseUrl}}/mcp/v1/tools
X-API-Key: invalid-key-123

### Get Feature Flags (Admin only)
GET {{baseUrl}}/admin/feature-flags
X-API-Key: {{adminKey}}

### Set Feature Flag (Admin only)
POST {{baseUrl}}/admin/feature-flags/premium_features?enabled=true
X-API-Key: {{adminKey}}

### Test Rate Limiting (rapid requests)
POST {{baseUrl}}/mcp/v1/tools/call
Content-Type: application/json
X-API-Key: {{guestKey}}

{
  "name": "echo",
  "arguments": {
    "message": "Rate limit test 1"
  }
}

### (Copy the above request multiple times to test rate limiting)
EOF
    
    log_success "REST Client configuration created"
}

create_git_config() {
    log_info "Creating Git configuration..."
    
    # Create .gitignore if it doesn't exist
    if [ ! -f .gitignore ]; then
        cat > .gitignore << 'EOF'
# Build results
[Dd]ebug/
[Dd]ebugPublic/
[Rr]elease/
[Rr]eleases/
x64/
x86/
build/
bld/
[Bb]in/
[Oo]bj/
[Oo]ut/
msbuild.log
msbuild.err
msbuild.wrn

# Visual Studio
.vs/
*.user
*.suo
*.userosscache
*.sln.docstates
*.vspx
*.sap

# Logs
logs/
*.log

# Runtime data
pids
*.pid
*.seed
*.pid.lock

# Coverage directory used by tools like istanbul
coverage

# nyc test coverage
.nyc_output

# Dependency directories
node_modules/

# Optional npm cache directory
.npm

# Optional eslint cache
.eslintcache

# Microbundle cache
.rpt2_cache/
.rts2_cache_cjs/
.rts2_cache_es/
.rts2_cache_umd/

# Optional REPL history
.node_repl_history

# Output of 'npm pack'
*.tgz

# Yarn Integrity file
.yarn-integrity

# dotenv environment variables file
.env
.env.test
.env.local

# parcel-bundler cache (https://parceljs.org/)
.cache
.parcel-cache

# next.js build output
.next

# nuxt.js build output
.nuxt

# gatsby files
.cache/
public

# vuepress build output
.vuepress/dist

# Serverless directories
.serverless/

# FuseBox cache
.fusebox/

# DynamoDB Local files
.dynamodb/

# TernJS port file
.tern-port

# IDE files
.idea/
*.swp
*.swo
*~

# OS generated files
.DS_Store
.DS_Store?
._*
.Spotlight-V100
.Trashes
ehthumbs.db
Thumbs.db
EOF
        log_success "Created .gitignore file"
    else
        log_info ".gitignore already exists"
    fi
}

install_dev_tools() {
    log_info "Installing development tools..."
    
    # Install .NET tools if not already installed
    if check_dependency "dotnet" ".NET SDK"; then
        log_info "Installing useful .NET tools..."
        
        # Install dotnet-format for code formatting
        dotnet tool install -g dotnet-format 2>/dev/null || log_info "dotnet-format already installed"
        
        # Install dotnet-outdated for checking outdated packages
        dotnet tool install -g dotnet-outdated-tool 2>/dev/null || log_info "dotnet-outdated already installed"
        
        # Install dotnet-ef for Entity Framework migrations (if needed)
        dotnet tool install -g dotnet-ef 2>/dev/null || log_info "dotnet-ef already installed"
        
        log_success "Development tools installed"
    fi
}

create_documentation() {
    log_info "Creating additional documentation..."
    
    mkdir -p docs
    
    # Create DEVELOPMENT.md
    cat > docs/DEVELOPMENT.md << 'EOF'
# Development Guide

## Quick Start

1. **Setup Development Environment**
   ```bash
   ./scripts/setup-dev.sh
   ```

2. **Open in VS Code**
   ```bash
   code .
   ```

3. **Start Debugging**
   - Press F5 or use "Launch (Development)" configuration
   - The server will start with development settings

## Available Configurations

### VS Code Launch Profiles
- **Launch (Development)**: Standard development mode with basic filtering
- **Launch (No Filtering)**: All filtering disabled for testing
- **Launch (Production Mode)**: All filters enabled with strict settings
- **Attach to Process**: Attach debugger to running process

### VS Code Tasks
- **build**: Build the project
- **test**: Run all tests
- **test-authentication**: Run authentication tests only
- **test-authorization**: Run authorization tests only
- **docker-build**: Build Docker image
- **docker-run**: Run in Docker container

## Testing

### Manual Testing with REST Client
Use the `tests/api-tests.http` file with VS Code REST Client extension for interactive testing.

### Automated Testing
```bash
# Run all tests
./scripts/test-all.sh

# Run specific test category
./scripts/test-all.sh authentication
./scripts/test-all.sh authorization
./scripts/test-all.sh rate-limiting
```

### PowerShell Testing
```powershell
# Run all tests
.\scripts\test-all.ps1

# Run specific category
.\scripts\test-all.ps1 -Category authentication
```

## Docker Development

### Build and Run
```bash
# Build image
docker build -t dynamic-tool-filtering .

# Run container
docker run -p 8080:8080 dynamic-tool-filtering

# Use docker-compose
docker-compose up --build
```

### Production-like Environment
```bash
# Start with Redis and PostgreSQL
docker-compose --profile production up
```

## Debugging Tips

1. **Filter Execution**: Set breakpoints in filter classes to see execution flow
2. **Authentication**: Check claims in the `ToolAuthorizationContext`
3. **Rate Limiting**: Monitor the `IRateLimitingService` implementation
4. **Feature Flags**: Debug the `IFeatureFlagService` to see flag evaluations

## Code Formatting

```bash
# Format code
dotnet format

# Check for outdated packages
dotnet outdated
```

## Adding New Filters

1. Create a new class implementing `IToolFilter`
2. Set appropriate priority value
3. Register in `Program.cs` 
4. Add configuration options to `FilteringOptions.cs`
5. Write tests for the new filter

## Performance Profiling

Use dotnet-trace for performance analysis:
```bash
dotnet-trace collect --process-id <PID> --providers Microsoft-AspNetCore-Server-Kestrel
```
EOF
    
    log_success "Development documentation created"
}

main() {
    echo "========================================"
    echo "Dynamic Tool Filtering - Dev Setup"
    echo "========================================"
    echo "Setting up development environment..."
    echo ""
    
    # Check if we're in the right directory
    if [ ! -f "Program.cs" ]; then
        log_error "This script must be run from the DynamicToolFiltering project directory"
        log_info "Expected to find Program.cs in current directory"
        exit 1
    fi
    
    # Check dependencies
    log_info "Checking dependencies..."
    check_dependency "dotnet" ".NET SDK" "Install from https://dotnet.microsoft.com/"
    check_dependency "git" "Git" "Install from https://git-scm.com/"
    check_dependency "curl" "curl" "Install with package manager"
    check_dependency "docker" "Docker" "Install from https://www.docker.com/" || log_warning "Docker is optional but recommended"
    check_dependency "code" "VS Code" "Install from https://code.visualstudio.com/" || log_warning "VS Code is optional but recommended"
    
    echo ""
    
    # Create configurations
    create_vscode_config
    create_docker_config
    create_rest_client_config
    create_git_config
    install_dev_tools
    create_documentation
    
    # Make scripts executable
    chmod +x scripts/*.sh 2>/dev/null || true
    
    echo ""
    echo "========================================"
    echo "Setup Complete!"
    echo "========================================"
    echo ""
    echo "Next steps:"
    echo "1. Open VS Code: code ."
    echo "2. Install recommended extensions (VS Code will prompt)"
    echo "3. Press F5 to start debugging"
    echo "4. Run tests: ./scripts/test-all.sh"
    echo "5. Try API testing with tests/api-tests.http"
    echo ""
    echo "Documentation:"
    echo "- Development Guide: docs/DEVELOPMENT.md"
    echo "- VS Code Tasks: Ctrl+Shift+P -> 'Tasks: Run Task'"
    echo "- REST Client: Open tests/api-tests.http in VS Code"
    echo ""
    log_success "Development environment ready!"
}

# Execute main function
main "$@"
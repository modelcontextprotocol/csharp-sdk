# MCP Conformance Test Framework

A framework for testing MCP (Model Context Protocol) client and server implementations against the specification.

> [!WARNING]
> This repository is a work in progress and is unstable. Join the conversation in the #conformance-testing-wg in the MCP Contributors discord.

**For SDK maintainers:** See [SDK Integration Guide](./SDK_INTEGRATION.md) for a streamlined guide on integrating conformance tests into your SDK repository.

## Quick Start

### Testing Clients

```bash
# Using the everything-client (recommended)
npx @modelcontextprotocol/conformance client --command "tsx examples/clients/typescript/everything-client.ts" --scenario initialize

# Run an entire suite of tests
npx @modelcontextprotocol/conformance client --command "tsx examples/clients/typescript/everything-client.ts" --suite auth
```

### Testing Servers

```bash
# Run all server scenarios (default)
npx @modelcontextprotocol/conformance server --url http://localhost:3000/mcp

# Run a single scenario
npx @modelcontextprotocol/conformance server --url http://localhost:3000/mcp --scenario server-initialize
```

### List Available Scenarios

```bash
npx @modelcontextprotocol/conformance list
```

## Overview

The conformance test framework validates MCP implementations by:

**For Clients:**

1. Starting a test server for the specified scenario
2. Running the client implementation with the test server URL
3. Capturing MCP protocol interactions
4. Running conformance checks against the specification
5. Generating detailed test results

**For Servers:**

1. Connecting to the running server as an MCP client
2. Sending test requests and capturing responses
3. Running conformance checks against server behavior
4. Generating detailed test results

## Usage

### Client Testing

```bash
npx @modelcontextprotocol/conformance client --command "<client-command>" --scenario <scenario-name> [options]
```

**Options:**

- `--command` - The command to run your MCP client (can include flags)
- `--scenario` - The test scenario to run (e.g., "initialize")
- `--suite` - Run a suite of tests in parallel (e.g., "auth")
- `--expected-failures <path>` - Path to YAML baseline file of known failures (see [Expected Failures](#expected-failures))
- `--timeout` - Timeout in milliseconds (default: 30000)
- `--verbose` - Show verbose output

The framework appends `<server-url>` as an argument to your command and sets the `MCP_CONFORMANCE_SCENARIO` environment variable to the scenario name. For scenarios that require additional context (e.g., client credentials), the `MCP_CONFORMANCE_CONTEXT` environment variable contains a JSON object with scenario-specific data.

### Server Testing

```bash
npx @modelcontextprotocol/conformance server --url <url> [--scenario <scenario>]
```

**Options:**

- `--url` - URL of the server to test
- `--scenario <scenario>` - Test scenario to run (e.g., "server-initialize"). Runs all available scenarios by default
- `--suite <suite>` - Suite to run: "active" (default), "all", or "pending"
- `--expected-failures <path>` - Path to YAML baseline file of known failures (see [Expected Failures](#expected-failures))
- `--verbose` - Show verbose output

## Test Results

**Client Testing** - Results are saved to `results/<scenario>-<timestamp>/`:

- `checks.json` - Array of conformance check results with pass/fail status
- `stdout.txt` - Client stdout output
- `stderr.txt` - Client stderr output

**Server Testing** - Results are saved to `results/server-<scenario>-<timestamp>/`:

- `checks.json` - Array of conformance check results with pass/fail status

## Expected Failures

SDKs that don't yet pass all conformance tests can specify a baseline of known failures. This allows running conformance tests in CI without failing, while still catching regressions.

Create a YAML file listing expected failures by mode:

```yaml
# conformance-baseline.yml
server:
  - tools-call-with-progress
  - resources-subscribe
client:
  - sse-retry
```

Then pass it to the CLI:

```bash
npx @modelcontextprotocol/conformance server --url http://localhost:3000/mcp --expected-failures ./conformance-baseline.yml
```

**Exit code behavior:**

| Scenario Result | In Baseline? | Outcome                                   |
| --------------- | ------------ | ----------------------------------------- |
| Fails           | Yes          | Exit 0 — expected failure                 |
| Fails           | No           | Exit 1 — unexpected regression            |
| Passes          | Yes          | Exit 1 — stale baseline, remove the entry |
| Passes          | No           | Exit 0 — normal pass                      |

This ensures:

- CI passes when only known failures occur
- CI fails on new regressions (unexpected failures)
- CI fails when a fix lands but the baseline isn't updated (stale entries)

## GitHub Action

This repo provides a composite GitHub Action so SDK repos don't need to write their own conformance scripts.

### Server Testing

```yaml
steps:
  - uses: actions/checkout@v4

  # Start your server (SDK-specific)
  - run: |
      my-server --port 3001 &
      timeout 15 bash -c 'until curl -s http://localhost:3001/mcp; do sleep 0.5; done'

  - uses: modelcontextprotocol/conformance@v0.1.11
    with:
      mode: server
      url: http://localhost:3001/mcp
      expected-failures: ./conformance-baseline.yml # optional
```

### Client Testing

```yaml
steps:
  - uses: actions/checkout@v4

  - uses: modelcontextprotocol/conformance@v0.1.11
    with:
      mode: client
      command: 'python tests/conformance/client.py'
      expected-failures: ./conformance-baseline.yml # optional
```

### Action Inputs

| Input               | Required    | Description                                     |
| ------------------- | ----------- | ----------------------------------------------- |
| `mode`              | Yes         | `server` or `client`                            |
| `url`               | Server mode | URL of the server to test                       |
| `command`           | Client mode | Command to run the client under test            |
| `expected-failures` | No          | Path to YAML baseline file                      |
| `suite`             | No          | Test suite to run                               |
| `scenario`          | No          | Run a single scenario by name                   |
| `timeout`           | No          | Timeout in ms for client tests (default: 30000) |
| `verbose`           | No          | Show verbose output (default: false)            |
| `node-version`      | No          | Node.js version (default: 20)                   |

## Example Clients

- `examples/clients/typescript/everything-client.ts` - Single client that handles all scenarios based on scenario name (recommended)
- `examples/clients/typescript/test1.ts` - Simple MCP client (for reference)
- `examples/clients/typescript/auth-test.ts` - Well-behaved OAuth client (for reference)

## Available Scenarios

### Client Scenarios

- **initialize** - Tests MCP client initialization handshake
  - Validates protocol version
  - Validates clientInfo (name and version)
  - Validates server response handling
- **tools-call** - Tests tool invocation
- **auth/basic-dcr** - Tests OAuth Dynamic Client Registration flow
- **auth/basic-metadata-var1** - Tests OAuth with authorization metadata

### Server Scenarios

Run `npx @modelcontextprotocol/conformance list --server` to see all available server scenarios, including:

- **server-initialize** - Tests server initialization and capabilities
- **tools-list** - Tests tool listing endpoint
- **tools-call-\*** - Various tool invocation scenarios
- **resources-\*** - Resource management scenarios
- **prompts-\*** - Prompt management scenarios

## Architecture

See `src/runner/DESIGN.md` for detailed architecture documentation.

### Key Components

- **Runner** (`src/runner/`) - Orchestrates test execution and result generation
  - `client.ts` - Client testing implementation
  - `server.ts` - Server testing implementation
  - `utils.ts` - Shared utilities
  - `index.ts` - Public API exports
- **CLI** (`src/index.ts`) - Command-line interface using Commander.js
- **Scenarios** (`src/scenarios/`) - Test scenarios with expected behaviors
- **Checks** (`src/checks/`) - Conformance validation functions
- **Types** (`src/types.ts`) - Shared type definitions

## Adding New Scenarios

1. Create a new directory in `src/scenarios/<scenario-name>/`
2. Implement the `Scenario` interface with `start()`, `stop()`, and `getChecks()`
3. Register the scenario in `src/scenarios/index.ts`

See `src/scenarios/initialize/` for a reference implementation.

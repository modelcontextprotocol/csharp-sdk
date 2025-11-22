# MCP Conformance Tests

This project contains integration tests that run the official Model Context Protocol (MCP) conformance test suite against the C# SDK's ConformanceServer implementation.

## Overview

The conformance tests verify that the C# MCP server implementation adheres to the MCP specification by running the official Node.js-based conformance test suite.

## Prerequisites

- .NET 10.0 SDK or later
- Node.js and npm (required to run the `@modelcontextprotocol/conformance` package)

## Running the Tests

These tests will run as part of the standard `dotnet test` command if Node.js is installed
but will be skipped if Node.js is not detected.

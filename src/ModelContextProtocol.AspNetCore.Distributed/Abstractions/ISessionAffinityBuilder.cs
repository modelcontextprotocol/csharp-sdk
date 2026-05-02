// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.AspNetCore.Distributed.Abstractions;

/// <summary>
/// A builder for configuring MCP session affinity.
/// </summary>
public interface ISessionAffinityBuilder
{
    /// <summary>
    /// Gets the host application builder.
    /// </summary>
    IServiceCollection Services { get; }
}

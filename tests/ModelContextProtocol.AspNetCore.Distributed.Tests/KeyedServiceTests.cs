// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Distributed.Abstractions;
using Xunit;

namespace ModelContextProtocol.AspNetCore.Distributed.Tests;

public class KeyedServiceTests
{
    [Fact]
    public void AddMcpHttpSessionAffinity_WithHybridCacheServiceKey_ConfiguresOptionsCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache(); // Default cache

        // Act - Use non-keyed registration but specify key in options
        services.AddMcpHttpSessionAffinity(options =>
        {
            options.HybridCacheServiceKey = "my-cache";
        });

        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<SessionAffinityOptions>>().Value;
        Assert.Equal("my-cache", options.HybridCacheServiceKey);
    }

    [Fact]
    public void AddMcpHttpSessionAffinity_WithoutHybridCacheServiceKey_UsesDefaultCache()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache(); // Default cache

        // Act
        services.AddMcpHttpSessionAffinity(); // Should use default

        var provider = services.BuildServiceProvider();

        // Assert
        var sessionStore = provider.GetService<ISessionStore>();
        Assert.NotNull(sessionStore);
        Assert.IsType<HybridCacheSessionStore>(sessionStore);
    }

    [Fact]
    public async Task SessionStore_StoreAndRetrieve_WorksCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        services.AddMcpHttpSessionAffinity();

        var provider = services.BuildServiceProvider();
        var sessionStore = provider.GetRequiredService<ISessionStore>();

        var sessionId = "test-session-123";
        var ownerInfo = new SessionOwnerInfo
        {
            OwnerId = "server-1",
            Address = "http://server-1:5000",
            ClaimedAt = DateTimeOffset.UtcNow,
        };

        // Act - Claim ownership using the factory pattern
        var setResult = await sessionStore.GetOrClaimOwnershipAsync(
            sessionId,
            async ct =>
            {
                await Task.Yield(); // Simulate async work
                return ownerInfo;
            },
            TestContext.Current.CancellationToken
        );

        var getResult = await sessionStore.GetOrClaimOwnershipAsync(
            sessionId,
            async ct =>
            {
                await Task.Yield();
                // This factory shouldn't be called since the session already exists
                throw new InvalidOperationException(
                    "Factory should not be called for existing session"
                );
            },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.NotNull(setResult);
        Assert.NotNull(getResult);
        Assert.Equal(ownerInfo.OwnerId, setResult.OwnerId);
        Assert.Equal(ownerInfo.OwnerId, getResult.OwnerId);
        Assert.Equal(ownerInfo.Address, getResult.Address);
    }

    [Fact]
    public void AddMcpHttpSessionAffinity_RegistersReverseProxyServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();

        // Act
        services.AddMcpHttpSessionAffinity();

        // Assert - Verify reverse proxy services are registered
        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType.FullName?.Contains("ReverseProxy", StringComparison.Ordinal) == true
        );

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
    }
}

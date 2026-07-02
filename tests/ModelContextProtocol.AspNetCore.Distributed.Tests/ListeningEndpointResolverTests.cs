// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using ModelContextProtocol.AspNetCore.Distributed;
using ModelContextProtocol.AspNetCore.Distributed.Abstractions;
using Moq;
using Xunit;

namespace ModelContextProtocol.AspNetCore.Distributed.Tests;

public sealed class ListeningEndpointResolverTests
{
    private readonly ListeningEndpointResolver _resolver;

    public ListeningEndpointResolverTests()
    {
        _resolver = new ListeningEndpointResolver();
    }

    #region Explicit Configuration Tests

    [Fact]
    public void ResolveListeningEndpoint_WithValidExplicitAddress_ReturnsNormalizedAddress()
    {
        // Arrange
        var server = CreateServer();
        var options = new SessionAffinityOptions
        {
            LocalServerAddress = "http://pod-1.service.cluster.local:8080",
        };

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://pod-1.service.cluster.local:8080", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithExplicitHttpsAddress_ReturnsNormalizedAddress()
    {
        // Arrange
        var server = CreateServer();
        var options = new SessionAffinityOptions
        {
            LocalServerAddress = "https://secure.example.com:443",
        };

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("https://secure.example.com:443", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithExplicitAddressWithPath_RemovesPath()
    {
        // Arrange
        var server = CreateServer();
        var options = new SessionAffinityOptions
        {
            LocalServerAddress = "http://example.com:5000/api/mcp",
        };

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://example.com:5000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithExplicitAddressWithQueryString_RemovesQuery()
    {
        // Arrange
        var server = CreateServer();
        var options = new SessionAffinityOptions
        {
            LocalServerAddress = "http://example.com:5000?param=value",
        };

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://example.com:5000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithExplicitAddressWithFragment_RemovesFragment()
    {
        // Arrange
        var server = CreateServer();
        var options = new SessionAffinityOptions
        {
            LocalServerAddress = "http://example.com:5000#section",
        };

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://example.com:5000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithInvalidUri_ThrowsArgumentException()
    {
        // Arrange
        var server = CreateServer();
        var options = new SessionAffinityOptions { LocalServerAddress = "not a valid uri" };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveListeningEndpoint(server, options)
        );
    }

    [Fact]
    public void ResolveListeningEndpoint_WithRelativeUri_ThrowsArgumentException()
    {
        // Arrange
        var server = CreateServer();
        var options = new SessionAffinityOptions { LocalServerAddress = "/api/mcp" };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveListeningEndpoint(server, options)
        );
    }

    [Fact]
    public void ResolveListeningEndpoint_WithInvalidScheme_ThrowsArgumentException()
    {
        // Arrange
        var server = CreateServer();
        var options = new SessionAffinityOptions { LocalServerAddress = "ftp://example.com:21" };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveListeningEndpoint(server, options)
        );
    }

    [Fact]
    public void ResolveListeningEndpoint_WithIPv4Address_ReturnsNormalizedAddress()
    {
        // Arrange
        var server = CreateServer();
        var options = new SessionAffinityOptions
        {
            LocalServerAddress = "http://192.168.1.100:5000",
        };

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://192.168.1.100:5000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithIPv6Address_ReturnsNormalizedAddress()
    {
        // Arrange
        var server = CreateServer();
        var options = new SessionAffinityOptions
        {
            LocalServerAddress = "http://[2001:db8::1]:8080",
        };

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://[2001:db8::1]:8080", result);
    }

    #endregion

    #region Server Binding Resolution Tests

    [Fact]
    public void ResolveListeningEndpoint_WithHttpBinding_ReturnsHttpAddress()
    {
        // Arrange
        var server = CreateServer("http://0.0.0.0:5000");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://0.0.0.0:5000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithHttpsBinding_ReturnsHttpsAddress()
    {
        // Arrange
        var server = CreateServer("https://0.0.0.0:5001");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("https://0.0.0.0:5001", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithMultipleBindings_PrefersHttpOverHttps()
    {
        // Arrange - HTTP should be preferred for service mesh scenarios
        var server = CreateServer("https://0.0.0.0:5001", "http://0.0.0.0:5000");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://0.0.0.0:5000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithHttpAndHttpsBindings_PrefersHttp()
    {
        // Arrange
        var server = CreateServer("http://10.0.1.5:5000", "https://10.0.1.5:5001");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://10.0.1.5:5000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_PrefersExternalOverLocalhost()
    {
        // Arrange - External interfaces should be preferred over localhost
        var server = CreateServer("http://localhost:5000", "http://192.168.1.100:5000");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://192.168.1.100:5000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithOnlyLocalhostBinding_ReturnsLocalhostAddress()
    {
        // Arrange
        var server = CreateServer("http://localhost:5000");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://localhost:5000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithOnlyHttpsAndLocalhostBindings_PrefersLocalhostHttps()
    {
        // Arrange - Only localhost HTTPS available
        var server = CreateServer("https://localhost:5001");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("https://localhost:5001", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithComplexBindings_FollowsPriorityOrder()
    {
        // Arrange - Priority: external HTTP > external HTTPS > localhost HTTP > localhost HTTPS
        var server = CreateServer(
            "https://localhost:5443",
            "http://localhost:5000",
            "https://10.0.1.5:5001",
            "http://10.0.1.5:5000"
        );
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://10.0.1.5:5000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithNoAddresses_ReturnsFallbackAddress()
    {
        // Arrange
        var server = CreateServer(); // No addresses
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://localhost:80", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithNullServerAddressesFeature_ReturnsFallbackAddress()
    {
        // Arrange
        var server = CreateServerWithoutAddressesFeature();
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://localhost:80", result);
    }

    #endregion

    #region Localhost Detection Tests

    [Fact]
    public void ResolveListeningEndpoint_WithLocalhostVariants_DetectsAllAsLocalhost()
    {
        // Test various localhost representations
        var testCases = new[]
        {
            "http://localhost:5000",
            "http://LOCALHOST:5000",
            "http://127.0.0.1:5000",
            "http://[::1]:5000",
            "http://subdomain.localhost:5000",
        };

        foreach (var localhostAddress in testCases)
        {
            // Arrange - Add an external address to verify localhost is NOT preferred
            var server = CreateServer(localhostAddress, "http://10.0.1.5:5000");
            var options = new SessionAffinityOptions();

            // Act
            var result = _resolver.ResolveListeningEndpoint(server, options);

            // Assert - Should prefer external address over localhost variants
            Assert.Equal(
                "http://10.0.1.5:5000",
                result);
        }
    }

    [Fact]
    public void ResolveListeningEndpoint_WithIPv4Loopback_TreatedAsLocalhost()
    {
        // Arrange
        var server = CreateServer("http://127.0.0.1:5000", "http://192.168.1.100:5000");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert - External address should be preferred
        Assert.Equal("http://192.168.1.100:5000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithIPv6Loopback_TreatedAsLocalhost()
    {
        // Arrange
        var server = CreateServer("http://[::1]:5000", "http://[2001:db8::1]:5000");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert - External address should be preferred
        Assert.Equal("http://[2001:db8::1]:5000", result);
    }

    #endregion

    #region Null/Empty Validation Tests

    [Fact]
    public void ResolveListeningEndpoint_WithNullServer_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new SessionAffinityOptions();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _resolver.ResolveListeningEndpoint(null!, options)
        );
    }

    [Fact]
    public void ResolveListeningEndpoint_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var server = CreateServer();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _resolver.ResolveListeningEndpoint(server, null!)
        );
    }

    [Fact]
    public void ResolveListeningEndpoint_WithEmptyStringAddress_IgnoresAndResolvesFromServer()
    {
        // Arrange
        var server = CreateServer("http://10.0.1.5:5000");
        var options = new SessionAffinityOptions { LocalServerAddress = "" };

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://10.0.1.5:5000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithWhitespaceAddress_IgnoresAndResolvesFromServer()
    {
        // Arrange
        var server = CreateServer("http://10.0.1.5:5000");
        var options = new SessionAffinityOptions { LocalServerAddress = "   " };

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://10.0.1.5:5000", result);
    }

    #endregion

    #region Priority and Selection Tests

    [Fact]
    public void ResolveListeningEndpoint_WithOnlyHttpsExternal_ReturnsHttpsAddress()
    {
        // Arrange - No HTTP available, should return HTTPS
        var server = CreateServer("https://10.0.1.5:5001");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("https://10.0.1.5:5001", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithMultipleHttpBindings_ReturnsFirstHttpBinding()
    {
        // Arrange
        var server = CreateServer("http://10.0.1.5:5000", "http://10.0.1.6:5000");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert - First HTTP binding should be selected
        Assert.Equal("http://10.0.1.5:5000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithInvalidBindings_SkipsInvalidAndUsesValid()
    {
        // Arrange
        var mockFeature = new Mock<IServerAddressesFeature>();
        mockFeature.Setup(x => x.Addresses).Returns(
            new List<string>
            {
                "not-a-valid-uri",
                "http://valid.example.com:5000",
                "also-invalid",
            }
        );

        var mockServer = new Mock<IServer>();
        var features = new FeatureCollection();
        features.Set(mockFeature.Object);
        mockServer.Setup(x => x.Features).Returns(features);

        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(mockServer.Object, options);

        // Assert
        Assert.Equal("http://valid.example.com:5000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithAllInvalidBindings_ReturnsFallback()
    {
        // Arrange
        var mockFeature = new Mock<IServerAddressesFeature>();
        mockFeature.Setup(x => x.Addresses).Returns(new List<string> { "not-a-valid-uri", "also-invalid", "still-not-valid" });

        var mockServer = new Mock<IServer>();
        var features = new FeatureCollection();
        features.Set(mockFeature.Object);
        mockServer.Setup(x => x.Features).Returns(features);

        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(mockServer.Object, options);

        // Assert
        Assert.Equal("http://localhost:80", result);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void ResolveListeningEndpoint_WithNonStandardPorts_PreservesPort()
    {
        // Arrange
        var server = CreateServer("http://example.com:8888");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://example.com:8888", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_WithDefaultHttpPort_IncludesPort80Explicitly()
    {
        // Arrange - Port 80 should be explicitly included in the result
        var server = CreateServer("http://example.com:80");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert - Verify port 80 is explicitly included
        Assert.Equal("http://example.com:80", result);
        Assert.True(
            result.EndsWith(":80", StringComparison.Ordinal),
            "Port 80 should be explicitly included"
        );
    }

    [Fact]
    public void ResolveListeningEndpoint_WithDefaultHttpsPort_IncludesPort443Explicitly()
    {
        // Arrange - Port 443 should be explicitly included in the result
        var server = CreateServer("https://example.com:443");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert - Verify port 443 is explicitly included
        Assert.Equal("https://example.com:443", result);
        Assert.True(
            result.EndsWith(":443", StringComparison.Ordinal),
            "Port 443 should be explicitly included"
        );
    }

    [Fact]
    public void ResolveListeningEndpoint_WithWildcardAddress_ReturnsWildcardAddress()
    {
        // Arrange - Wildcard addresses (0.0.0.0, [::]) are valid and should be preserved
        var server = CreateServer("http://0.0.0.0:5000");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert - IPv4 wildcard should be preserved
        Assert.Equal("http://0.0.0.0:5000", result);
        Assert.True(
            result.Contains("0.0.0.0", StringComparison.Ordinal),
            "Should preserve IPv4 wildcard address"
        );
    }

    [Fact]
    public void ResolveListeningEndpoint_WithIPv6WildcardAddress_ReturnsWildcardAddress()
    {
        // Arrange
        var server = CreateServer("http://[::]:5000");
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert - IPv6 wildcard should be preserved
        Assert.Equal("http://[::]:5000", result);
        Assert.True(
            result.Contains("[::]", StringComparison.Ordinal),
            "Should preserve IPv6 wildcard address"
        );
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void ResolveListeningEndpoint_ExplicitConfigTakesPrecedenceOverServerBindings()
    {
        // Arrange - Even with server bindings, explicit config should win
        var server = CreateServer("http://10.0.1.5:5000", "https://10.0.1.5:5001");
        var options = new SessionAffinityOptions
        {
            LocalServerAddress = "http://custom.example.com:9000",
        };

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://custom.example.com:9000", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_ServiceMeshScenario_PrefersHttpForInternalRouting()
    {
        // Arrange - Typical service mesh: HTTPS external, HTTP internal
        var server = CreateServer(
            "https://external.service.mesh:443",
            "http://internal.service.mesh:8080"
        );
        var options = new SessionAffinityOptions();

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert - HTTP should be preferred for internal service mesh routing
        Assert.Equal("http://internal.service.mesh:8080", result);
    }

    [Fact]
    public void ResolveListeningEndpoint_KubernetesScenario_UsesExplicitPodAddress()
    {
        // Arrange - Kubernetes pod with explicit service address
        var server = CreateServer("http://0.0.0.0:8080");
        var options = new SessionAffinityOptions
        {
            LocalServerAddress = "http://pod-1.mcp-service.default.svc.cluster.local:8080",
        };

        // Act
        var result = _resolver.ResolveListeningEndpoint(server, options);

        // Assert
        Assert.Equal("http://pod-1.mcp-service.default.svc.cluster.local:8080", result);
    }

    #endregion

    #region Helper Methods

    private static IServer CreateServer(params string[] addresses)
    {
        var mockFeature = new Mock<IServerAddressesFeature>();
        mockFeature.Setup(x => x.Addresses).Returns(new List<string>(addresses));

        var mockServer = new Mock<IServer>();
        var features = new FeatureCollection();
        features.Set(mockFeature.Object);
        mockServer.Setup(x => x.Features).Returns(features);

        return mockServer.Object;
    }

    private static IServer CreateServerWithoutAddressesFeature()
    {
        var mockServer = new Mock<IServer>();
        var features = new FeatureCollection();
        // Deliberately not setting IServerAddressesFeature
        mockServer.Setup(x => x.Features).Returns(features);

        return mockServer.Object;
    }

    #endregion
}

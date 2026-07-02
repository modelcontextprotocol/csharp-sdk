// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Distributed.Abstractions;
using Xunit;

namespace ModelContextProtocol.AspNetCore.Distributed.Tests;

public class SessionAffinityOptionsValidationTests
{
    [Fact]
    public void Validate_WithValidHttpUri_Succeeds()
    {
        // Arrange
        SessionAffinityOptions options = new() { LocalServerAddress = "http://localhost:5000" };
        SessionAffinityOptionsValidator validator = new();

        // Act
        ValidateOptionsResult result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithValidHttpsUri_Succeeds()
    {
        // Arrange
        SessionAffinityOptions options = new()
        {
            LocalServerAddress = "https://server1.internal:443",
        };
        SessionAffinityOptionsValidator validator = new();

        // Act
        ValidateOptionsResult result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithNullLocalServerAddress_Succeeds()
    {
        // Arrange
        SessionAffinityOptions options = new() { LocalServerAddress = null };
        SessionAffinityOptionsValidator validator = new();

        // Act
        ValidateOptionsResult result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithEmptyLocalServerAddress_Succeeds()
    {
        // Arrange
        SessionAffinityOptions options = new() { LocalServerAddress = string.Empty };
        SessionAffinityOptionsValidator validator = new();

        // Act
        ValidateOptionsResult result = validator.Validate(null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WithInvalidUri_Fails()
    {
        // Arrange
        SessionAffinityOptions options = new() { LocalServerAddress = "not a valid uri" };
        SessionAffinityOptionsValidator validator = new();

        // Act
        ValidateOptionsResult result = validator.Validate(null, options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.True(
            result.FailureMessage?.Contains("not a valid absolute URI", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Validate_WithRelativeUri_Fails()
    {
        // Arrange
        SessionAffinityOptions options = new() { LocalServerAddress = "/relative/path" };
        SessionAffinityOptionsValidator validator = new();

        // Act
        ValidateOptionsResult result = validator.Validate(null, options);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Validate_WithFtpScheme_Fails()
    {
        // Arrange
        SessionAffinityOptions options = new() { LocalServerAddress = "ftp://server:21" };
        SessionAffinityOptionsValidator validator = new();

        // Act
        ValidateOptionsResult result = validator.Validate(null, options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.True(result.FailureMessage?.Contains("HTTP or HTTPS", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithWsScheme_Fails()
    {
        // Arrange
        SessionAffinityOptions options = new() { LocalServerAddress = "ws://server:8080" };
        SessionAffinityOptionsValidator validator = new();

        // Act
        ValidateOptionsResult result = validator.Validate(null, options);

        // Assert
        Assert.False(result.Succeeded);
        Assert.True(result.FailureMessage?.Contains("HTTP or HTTPS", StringComparison.Ordinal));
    }

    [Fact]
    public void AddMcpHttpSessionAffinity_RegistersValidator()
    {
        // Arrange
        ServiceCollection services = [];
        services.AddLogging();
        services.AddHybridCache();

        // Act
        services.AddMcpHttpSessionAffinity();
        ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        var validators = provider.GetServices<IValidateOptions<SessionAffinityOptions>>();
        Assert.True(
            validators.Any(v => v is SessionAffinityOptionsValidator),
            "SessionAffinityOptionsValidator should be registered"
        );
    }

    [Fact]
    public void ValidationAttribute_WithValidHttpUri_Succeeds()
    {
        // Arrange
        var attribute = new HttpOrHttpsUriAttribute();
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(new object());

        // Act
        var result = attribute.GetValidationResult("http://localhost:5000", context);

        // Assert
        Assert.Equal(System.ComponentModel.DataAnnotations.ValidationResult.Success, result);
    }

    [Fact]
    public void ValidationAttribute_WithInvalidScheme_Fails()
    {
        // Arrange
        var attribute = new HttpOrHttpsUriAttribute();
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(new object());

        // Act
        var result = attribute.GetValidationResult("ftp://server:21", context);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ErrorMessage?.Contains("HTTP or HTTPS", StringComparison.Ordinal));
    }
}

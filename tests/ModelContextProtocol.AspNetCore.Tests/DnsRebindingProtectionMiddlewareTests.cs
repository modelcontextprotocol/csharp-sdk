using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using System.Net;

namespace ModelContextProtocol.AspNetCore.Tests;

public class DnsRebindingProtectionMiddlewareTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    [Theory]
    [InlineData("localhost", HttpStatusCode.OK)]
    [InlineData("localhost:5000", HttpStatusCode.OK)]
    [InlineData("127.0.0.1", HttpStatusCode.OK)]
    [InlineData("127.0.0.1:5000", HttpStatusCode.OK)]
    [InlineData("[::1]", HttpStatusCode.OK)]
    [InlineData("[::1]:5000", HttpStatusCode.OK)]
    [InlineData("evil.com", HttpStatusCode.Forbidden)]
    [InlineData("evil.localhost", HttpStatusCode.Forbidden)]
    [InlineData("localhost.evil.com", HttpStatusCode.Forbidden)]
    public async Task ValidatesHostHeader(string hostHeader, HttpStatusCode expectedStatusCode)
    {
        Builder.Services.AddMcpServer().WithHttpTransport();
        await using var app = Builder.Build();

        app.UseMcpDnsRebindingProtection();
        app.MapGet("/test", () => "OK");

        await app.StartAsync(TestContext.Current.CancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Host = hostHeader;

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatusCode, response.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost", HttpStatusCode.OK)]
    [InlineData("http://localhost:5000", HttpStatusCode.OK)]
    [InlineData("http://127.0.0.1", HttpStatusCode.OK)]
    [InlineData("http://127.0.0.1:5000", HttpStatusCode.OK)]
    [InlineData("http://[::1]", HttpStatusCode.OK)]
    [InlineData("http://[::1]:5000", HttpStatusCode.OK)]
    [InlineData("http://evil.com", HttpStatusCode.Forbidden)]
    [InlineData("http://evil.localhost", HttpStatusCode.Forbidden)]
    [InlineData("https://malicious.site", HttpStatusCode.Forbidden)]
    public async Task ValidatesOriginHeader(string originHeader, HttpStatusCode expectedStatusCode)
    {
        Builder.Services.AddMcpServer().WithHttpTransport();
        await using var app = Builder.Build();

        app.UseMcpDnsRebindingProtection();
        app.MapGet("/test", () => "OK");

        await app.StartAsync(TestContext.Current.CancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Host = "localhost"; // Valid host
        request.Headers.Add("Origin", originHeader);

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatusCode, response.StatusCode);
    }

    [Fact]
    public async Task AllowsRequestsWithNoOriginHeader()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();
        await using var app = Builder.Build();

        app.UseMcpDnsRebindingProtection();
        app.MapGet("/test", () => "OK");

        await app.StartAsync(TestContext.Current.CancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Host = "localhost";
        // No Origin header

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsForbiddenWithJsonRpcErrorFormat()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();
        await using var app = Builder.Build();

        app.UseMcpDnsRebindingProtection();
        app.MapGet("/test", () => "OK");

        await app.StartAsync(TestContext.Current.CancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Host = "localhost";
        request.Headers.Add("Origin", "http://evil.com");

        var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("jsonrpc", content);
        Assert.Contains("error", content);
        Assert.Contains("-32000", content);
    }
}

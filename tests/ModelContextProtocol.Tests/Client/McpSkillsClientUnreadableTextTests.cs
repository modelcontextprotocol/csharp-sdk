using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// A JSON string can hold an escaped unpaired surrogate such as <c>"\uD800"</c>, which is not text. A server built on
/// System.Text.Json cannot write one, so these tests answer the client with raw response bodies and check that the
/// Skills client fails such an entry like any other malformed entry, without an <see cref="InvalidOperationException"/>.
/// </summary>
public class McpSkillsClientUnreadableTextTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    private const string ValidFrontmatter = """{"name":"good","description":"d"}""";
    private const string UnpairedHighSurrogate = """{"name":"bad","description":"\uD800"}""";

    [Theory]
    [InlineData(UnpairedHighSurrogate)]
    [InlineData("""{"name":"bad","description":"\uDC00 after an unpaired low surrogate"}""")]
    [InlineData("""{"name":"bad","description":"d","\uD800":"in a key"}""")]
    public async Task ListSkillsWithDiagnosticsAsync_RejectsAnEntryWithUnreadableText_AndKeepsTheValidEntry(string frontmatter)
    {
        await using var transport = CreateTransport(listResult: Page(Entry("good", ValidFrontmatter), Entry("bad", frontmatter)));
        await using var client = await ConnectAsync(transport);

        var listing = await client.ListSkillsWithDiagnosticsAsync(new ListSkillsRequestParams(), TestContext.Current.CancellationToken);

        Assert.Equal("good", Assert.Single(listing.Page.Skills).Name);
        var rejection = Assert.Single(listing.RejectedEntries);
        Assert.Equal(1, rejection.Index);
        AssertUnreadableText(Assert.IsType<JsonException>(rejection.Error));
    }

    [Fact]
    public async Task ListSkillsWithDiagnosticsAsync_ThrowsJsonException_WhenThePageHasUnreadableText()
    {
        string page = """{"skills":[],"nextCursor":"\uD800"}""";
        await using var transport = CreateTransport(listResult: page);
        await using var client = await ConnectAsync(transport);

        var error = await Assert.ThrowsAsync<JsonException>(
            async () => await client.ListSkillsWithDiagnosticsAsync(new ListSkillsRequestParams(), TestContext.Current.CancellationToken));

        AssertUnreadableText(error);
    }

    [Fact]
    public async Task ListSkillsAsync_ThrowsJsonException_ForAnEntryWithUnreadableText()
    {
        await using var transport = CreateTransport(listResult: Page(Entry("good", ValidFrontmatter), Entry("bad", UnpairedHighSurrogate)));
        await using var client = await ConnectAsync(transport);

        var error = await Assert.ThrowsAsync<JsonException>(async () => await client.ListSkillsAsync(TestContext.Current.CancellationToken));

        AssertUnreadableText(error);
    }

    [Fact]
    public async Task GetSkillAsync_ThrowsJsonException_ForAnEntryWithUnreadableText()
    {
        await using var transport = CreateTransport(listResult: Page(), getResult: "{\"skill\":" + Entry("bad", UnpairedHighSurrogate) + "}");
        await using var client = await ConnectAsync(transport);

        var error = await Assert.ThrowsAsync<JsonException>(
            async () => await client.GetSkillAsync("skill://bad/SKILL.md", TestContext.Current.CancellationToken));

        AssertUnreadableText(error);
    }

    private void AssertUnreadableText(JsonException error)
    {
        Assert.Contains("not valid Unicode text", error.Message);
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    private string Page(params string[] entries) => "{\"skills\":[" + string.Join(",", entries) + "]}";

    private string Entry(string name, string frontmatter) =>
        "{\"uri\":\"skill://" + name + "/SKILL.md\",\"frontmatter\":" + frontmatter +
        ",\"resources\":[{\"uri\":\"skill://" + name + "/SKILL.md\",\"digest\":\"sha256:" + new string('a', 64) + "\",\"size\":1}]}";

    /// <summary>Creates a transport to a raw Streamable HTTP server that answers <c>skills/list</c> and <c>skills/get</c> with the given result bodies.</summary>
    private HttpClientTransport CreateTransport(string listResult, string? getResult = null)
    {
        var handler = new MockHttpHandler { RequestHandler = request => RespondAsync(request, listResult, getResult) };
        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost"),
            TransportMode = HttpTransportMode.StreamableHttp,
            Name = "Raw skills server test client",
        }, new HttpClient(handler), LoggerFactory, ownsHttpClient: true);
    }

    private Task<McpClient> ConnectAsync(HttpClientTransport transport) => McpClient.CreateAsync(
        transport,
        new McpClientOptions { ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion },
        LoggerFactory,
        TestContext.Current.CancellationToken);

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, string listResult, string? getResult)
    {
        // The server offers no standalone SSE stream, which the spec permits.
        // net472 does not populate a default Content, so every response sets one explicitly.
        if (request.Method != HttpMethod.Post)
        {
            var status = request.Method == HttpMethod.Get ? HttpStatusCode.MethodNotAllowed : HttpStatusCode.OK;
            return new HttpResponseMessage(status) { Content = new StringContent(string.Empty) };
        }

        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        if (!body.RootElement.TryGetProperty("id", out var id))
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent(string.Empty) };
        }

        string method = body.RootElement.GetProperty("method").GetString()!;
        string result = method switch
        {
            RequestMethods.Initialize =>
                "{\"protocolVersion\":\"" + McpProtocolVersions.November2025ProtocolVersion + "\"," +
                "\"capabilities\":{\"extensions\":{\"" + SkillsProtocol.ExtensionId + "\":{}}}," +
                "\"serverInfo\":{\"name\":\"raw\",\"version\":\"1.0\"}}",
            SkillsProtocol.MethodSkillsList => listResult,
            SkillsProtocol.MethodSkillsGet when getResult is not null => getResult,
            _ => throw new InvalidOperationException($"The raw test server does not answer '{method}'."),
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":" + result + "}", Encoding.UTF8, "application/json"),
        };
        response.Headers.Add("mcp-session-id", "raw-session");
        return response;
    }
}

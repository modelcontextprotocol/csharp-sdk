using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Configuration;

/// <summary>
/// Test suite for UriTemplate.CreateParser method.
/// Tests are based on RFC 6570 (URI Template) specification.
/// Since UriTemplate is internal, we test it indirectly through the MCP server resource routing mechanism.
/// </summary>
public sealed class McpServerResourceRoutingTests(ITestOutputHelper testOutputHelper) : ClientServerTestBase(testOutputHelper, startServer: false)
{
    /// <summary>
    /// Starts the server with the specified resources and creates a client.
    /// </summary>
    private async Task<McpClient> CreateClientWithResourcesAsync(params McpServerResource[] resources)
    {
        McpServerBuilder.WithResources(resources);
        StartServer();
        return await CreateMcpClientForServer();
    }

    /// <summary>
    /// Asserts that the given URI matches the template and produces the expected text result.
    /// </summary>
    private async Task AssertMatchAsync(
        string uriTemplate,
        Delegate method,
        string uri,
        string expectedResult)
    {
        var resource = McpServerResource.Create(options: new() { UriTemplate = uriTemplate }, method: method);
        var client = await CreateClientWithResourcesAsync(resource);

        var result = await client.ReadResourceAsync(uri, null, TestContext.Current.CancellationToken);
        var text = ((TextResourceContents)result.Contents[0]).Text;
        Assert.Equal(expectedResult, text);
    }

    /// <summary>
    /// Asserts that the given URI does NOT match the template.
    /// </summary>
    private async Task AssertNoMatchAsync(
        string uriTemplate,
        Delegate method,
        string uri)
    {
        var resource = McpServerResource.Create(options: new() { UriTemplate = uriTemplate }, method: method);
        var client = await CreateClientWithResourcesAsync(resource);

        var ex = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.ReadResourceAsync(uri, null, TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.ResourceNotFound, ex.ErrorCode);
    }

    /// <summary>
    /// Verify that when multiple templated resources exist, the correct one is matched based on the URI pattern.
    /// Regression test for https://github.com/modelcontextprotocol/csharp-sdk/issues/821.
    /// </summary>
    [Fact]
    public async Task MultipleTemplatedResources_MatchesCorrectResource()
    {
        // Register templates from most specific to least specific
        var client = await CreateClientWithResourcesAsync(
            McpServerResource.Create(options: new() { UriTemplate = "test://resource/non-templated" }, method: () => "static"),
            McpServerResource.Create(options: new() { UriTemplate = "test://resource/{id}" }, method: (string id) => $"template: {id}"),
            McpServerResource.Create(options: new() { UriTemplate = "test://params{?a1,a2,a3}" }, method: (string a1, string a2, string a3) => $"params: {a1}, {a2}, {a3}"),
            McpServerResource.Create(options: new() { UriTemplate = "file://{prefix}/{+path}" }, method: (string prefix, string path) => $"prefix: {prefix}, path: {path}"));

        // Non-templated URI - exact match
        var nonTemplatedResult = await client.ReadResourceAsync("test://resource/non-templated", null, TestContext.Current.CancellationToken);
        Assert.Equal("static", ((TextResourceContents)nonTemplatedResult.Contents[0]).Text);

        // Templated URI
        var templatedResult = await client.ReadResourceAsync("test://resource/12345", null, TestContext.Current.CancellationToken);
        Assert.Equal("template: 12345", ((TextResourceContents)templatedResult.Contents[0]).Text);

        // Exact match for templated URI
        var exactTemplatedResult = await client.ReadResourceAsync("test://resource/{id}", null, TestContext.Current.CancellationToken);
        Assert.Equal("template: {id}", ((TextResourceContents)exactTemplatedResult.Contents[0]).Text);

        // Templated URI with query params
        var paramsResult = await client.ReadResourceAsync("test://params?a1=a&a2=b&a3=c", null, TestContext.Current.CancellationToken);
        Assert.Equal("params: a, b, c", ((TextResourceContents)paramsResult.Contents[0]).Text);

        // Reserved expansion path - matches the generic {prefix}/{+path} template
        var pathResult = await client.ReadResourceAsync("file://foo/examples/example.cs", null, TestContext.Current.CancellationToken);
        Assert.Equal("prefix: foo, path: examples/example.cs", ((TextResourceContents)pathResult.Contents[0]).Text);

        // Literal template braces in URI should not match (template literal is not a valid URI)
        var mcpEx = await Assert.ThrowsAsync<McpProtocolException>(async () => await client.ReadResourceAsync("test://params{?a1,a2,a3}", null, TestContext.Current.CancellationToken));
        Assert.Equal(McpErrorCode.ResourceNotFound, mcpEx.ErrorCode);
        Assert.Equal("Request failed (remote): Unknown resource URI: 'test://params{?a1,a2,a3}'", mcpEx.Message);
    }

    #region Level 1: Simple String Expansion {var}

    [Fact]
    public async Task SimpleExpansion_MatchesSingleVariable()
    {
        await AssertMatchAsync(
            uriTemplate: "test://example.com/{var}",
            method: (string var) => $"var:{var}",
            uri: "test://example.com/value",
            expectedResult: "var:value");
    }

    [Fact]
    public async Task SimpleExpansion_DoesNotMatchSlash()
    {
        // Simple expansion should NOT match slashes
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/{var}",
            method: (string var) => $"var:{var}",
            uri: "test://example.com/foo/bar");
    }

    [Fact]
    public async Task SimpleExpansion_DoesNotMatchQuestionMark()
    {
        // Simple expansion should NOT match query string characters
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/{var}",
            method: (string var) => $"var:{var}",
            uri: "test://example.com/foo?query");
    }

    [Fact]
    public async Task SimpleExpansion_DoesNotMatchFragment()
    {
        // Simple expansion should NOT match fragment delimiter
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/{var}",
            method: (string var) => $"var:{var}",
            uri: "test://example.com/foo#section");
    }

    [Fact]
    public async Task SimpleExpansion_DoesNotMatchMissingSegment()
    {
        // Simple expansion is not optional when it's the only content of a segment
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/{var}",
            method: (string var) => $"var:{var}",
            uri: "test://example.com");
    }

    [Fact]
    public async Task SimpleExpansion_DoesNotMatchExtraPath()
    {
        // Template requires exact match, extra segments should not match
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/{var}",
            method: (string var) => $"var:{var}",
            uri: "test://example.com/value/extra");
    }

    [Fact]
    public async Task SimpleExpansion_MultipleVariables()
    {
        await AssertMatchAsync(
            uriTemplate: "test://example.com/{x}/{y}",
            method: (string x, string y) => $"x:{x},y:{y}",
            uri: "test://example.com/1024/768",
            expectedResult: "x:1024,y:768");
    }

    #endregion

    #region Level 2: Reserved Expansion {+var} - REGRESSION TESTS FOR BUG FIX

    /// <summary>
    /// FIXED BUG: Reserved expansion {+var} should match slashes.
    /// This was the bug that caused samples://{dependency}/{+path} to fail.
    /// Per RFC 6570 Section 3.2.3, the + operator allows reserved characters including "/" to pass through.
    /// </summary>
    [Fact]
    public async Task ReservedExpansion_MatchesSlashes()
    {
        // FIXED: {+path} should match paths containing slashes
        await AssertMatchAsync(
            uriTemplate: "test://{dependency}/{+path}",
            method: (string dependency, string path) => $"dependency:{dependency},path:{path}",
            uri: "test://foo/README.md",
            expectedResult: "dependency:foo,path:README.md");
    }

    /// <summary>
    /// FIXED BUG: Reserved expansion with nested path containing slashes.
    /// This is the exact failing case from the issue.
    /// </summary>
    [Fact]
    public async Task ReservedExpansion_MatchesNestedPath()
    {
        // FIXED: {+path} should match paths with multiple segments
        await AssertMatchAsync(
            uriTemplate: "test://{dependency}/{+path}",
            method: (string dependency, string path) => $"dependency:{dependency},path:{path}",
            uri: "test://foo/examples/example.rs",
            expectedResult: "dependency:foo,path:examples/example.rs");
    }

    /// <summary>
    /// FIXED BUG: Reserved expansion with deep nested path.
    /// </summary>
    [Fact]
    public async Task ReservedExpansion_MatchesDeeplyNestedPath()
    {
        // FIXED: {+path} should match deeply nested paths
        await AssertMatchAsync(
            uriTemplate: "test://{dependency}/{+path}",
            method: (string dependency, string path) => $"dependency:{dependency},path:{path}",
            uri: "test://mylib/src/components/utils/helper.ts",
            expectedResult: "dependency:mylib,path:src/components/utils/helper.ts");
    }

    [Fact]
    public async Task ReservedExpansion_SimpleValue()
    {
        // Reserved expansion should still work for simple values without slashes
        await AssertMatchAsync(
            uriTemplate: "test://{+var}",
            method: (string var) => $"var:{var}",
            uri: "test://value",
            expectedResult: "var:value");
    }

    [Fact]
    public async Task ReservedExpansion_WithPathStartingWithSlash()
    {
        // Reserved expansion allows reserved URI characters like /
        await AssertMatchAsync(
            uriTemplate: "test://{+path}",
            method: (string path) => $"path:{path}",
            uri: "test:///foo/bar",
            expectedResult: "path:/foo/bar");
    }

    [Fact]
    public async Task ReservedExpansion_StopsAtQueryString()
    {
        // Reserved expansion should stop at ? (query string delimiter)
        // The template doesn't match because it expects the URI to end after {+path}
        // but there's a query string. We should verify it doesn't capture the query.
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/{+path}",
            method: (string path) => $"path:{path}",
            uri: "test://example.com/foo/bar?query=test");
    }

    [Fact]
    public async Task ReservedExpansion_StopsAtFragment()
    {
        // Reserved expansion should stop at # (fragment delimiter)
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/{+path}",
            method: (string path) => $"path:{path}",
            uri: "test://example.com/foo/bar#section");
    }

    [Fact]
    public async Task ReservedExpansion_DoesNotMatchWrongScheme()
    {
        // Scheme must match exactly
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/{+path}",
            method: (string path) => $"path:{path}",
            uri: "wrongscheme://example.com/foo");
    }

    /// <summary>
    /// RFC 6570 specifies that empty values should expand to empty strings.
    /// See https://datatracker.ietf.org/doc/html/rfc6570#page-22 test cases: O{+empty}X matches OX.
    /// </summary>
    [Fact]
    public async Task ReservedExpansion_MatchesEmptyValue()
    {
        // Per RFC 6570: O{+empty}X should match OX when empty is ""
        await AssertMatchAsync(
            uriTemplate: "test://O{+empty}X",
            method: (string empty) => $"empty:[{empty}]",
            uri: "test://OX",
            expectedResult: "empty:[]");
    }

    /// <summary>
    /// RFC 6570 empty expansion test - reserved expansion at end of template.
    /// </summary>
    [Fact]
    public async Task ReservedExpansion_MatchesEmptyValueAtEnd()
    {
        // {+var} at the end should match empty string
        await AssertMatchAsync(
            uriTemplate: "test://prefix{+suffix}",
            method: (string suffix) => $"suffix:[{suffix}]",
            uri: "test://prefix",
            expectedResult: "suffix:[]");
    }

    /// <summary>
    /// RFC 6570 empty expansion test - reserved expansion at start of template.
    /// </summary>
    [Fact]
    public async Task ReservedExpansion_MatchesEmptyValueAtStart()
    {
        // {+var} at the start should match empty string
        await AssertMatchAsync(
            uriTemplate: "test://{+prefix}suffix",
            method: (string prefix) => $"prefix:[{prefix}]",
            uri: "test://suffix",
            expectedResult: "prefix:[]");
    }

    #endregion

    #region Level 2: Fragment Expansion {#var}

    [Fact]
    public async Task FragmentExpansion_MatchesWithHashPrefix()
    {
        await AssertMatchAsync(
            uriTemplate: "test://example.com/page{#section}",
            method: (string section) => $"section:{section}",
            uri: "test://example.com/page#intro",
            expectedResult: "section:intro");
    }

    [Fact]
    public async Task FragmentExpansion_MatchesSlashes()
    {
        // Fragment expansion allows reserved characters including /
        await AssertMatchAsync(
            uriTemplate: "test://{#path}",
            method: (string path) => $"path:{path}",
            uri: "test://#/foo/bar",
            expectedResult: "path:/foo/bar");
    }

    [Fact]
    public async Task FragmentExpansion_MatchesWithoutHash()
    {
        // Fragment expansion prefix is optional - matches with captured value even without #
        await AssertMatchAsync(
            uriTemplate: "test://{#section}",
            method: (string section) => $"section:{section}",
            uri: "test://intro",
            expectedResult: "section:intro");
    }

    [Fact]
    public async Task FragmentExpansion_DoesNotMatchWrongPath()
    {
        // The path must match exactly
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/page{#section}",
            method: (string section) => $"section:{section}",
            uri: "test://example.com/other#intro");
    }

    #endregion

    #region Level 3: Label Expansion with Dot-Prefix {.var} - BUG FIX

    /// <summary>
    /// FIXED BUG: Label expansion {.var} should match dot-prefixed values.
    /// The . operator was falling through to the default case which didn't handle the dot prefix.
    /// </summary>
    [Fact]
    public async Task LabelExpansion_MatchesDotPrefixedSingleValue()
    {
        // FIXED: {.var} should match .value
        await AssertMatchAsync(
            uriTemplate: "test://X{.var}",
            method: (string var) => $"var:{var}",
            uri: "test://X.value",
            expectedResult: "var:value");
    }

    /// <summary>
    /// FIXED BUG: Label expansion with multiple variables should use dot as separator.
    /// </summary>
    [Fact]
    public async Task LabelExpansion_GreedilyMatchesMultipleValues()
    {
        // FIXED: {.x,y} should match .1024.768 (dot separated)
        await AssertMatchAsync(
            uriTemplate: "test://www{.x,y}",
            method: (string x, string y) => $"x:{x},y:{y}",
            uri: "test://www.example.com",
            expectedResult: "x:example.com,y:");
    }

    [Fact]
    public async Task LabelExpansion_DomainStyle()
    {
        // Common use case: domain name labels
        await AssertMatchAsync(
            uriTemplate: "test://www{.dom}",
            method: (string dom) => $"dom:{dom}",
            uri: "test://www.example",
            expectedResult: "dom:example");
    }

    [Fact]
    public async Task LabelExpansion_MatchesWithoutDot()
    {
        // Label expansion prefix is optional - matches with captured value even without .
        await AssertMatchAsync(
            uriTemplate: "test://www{.dom}",
            method: (string dom) => $"dom:{dom}",
            uri: "test://wwwexample",
            expectedResult: "dom:example");
    }

    [Fact]
    public async Task LabelExpansion_DoesNotMatchSlash()
    {
        // Label expansion should not match slashes
        await AssertNoMatchAsync(
            uriTemplate: "test://www{.dom}",
            method: (string dom) => $"dom:{dom}",
            uri: "test://www.foo/bar");
    }

    #endregion

    #region Level 3: Path-Style Parameter Expansion {;var} - BUG FIX

    /// <summary>
    /// FIXED BUG: Path-style parameter expansion {;var} should match semicolon-prefixed name=value pairs.
    /// The ; operator was falling through to the default case which didn't handle the semicolon prefix or name=value format.
    /// </summary>
    [Fact]
    public async Task PathParameterExpansion_MatchesSingleParameter()
    {
        // FIXED: {;x} should match ;x=1024
        await AssertMatchAsync(
            uriTemplate: "test:///path{;x}",
            method: (string x) => $"x:{x}",
            uri: "test:///path;x=1024",
            expectedResult: "x:1024");
    }

    /// <summary>
    /// FIXED BUG: Path-style parameter expansion with multiple parameters.
    /// </summary>
    [Fact]
    public async Task PathParameterExpansion_MatchesMultipleParameters()
    {
        // FIXED: {;x,y} should match ;x=1024;y=768
        await AssertMatchAsync(
            uriTemplate: "test:///path{;x,y}",
            method: (string x, string y) => $"x:{x},y:{y}",
            uri: "test:///path;x=1024;y=768",
            expectedResult: "x:1024,y:768");
    }

    [Fact]
    public async Task PathParameterExpansion_DoesNotMatchMissingSemicolon()
    {
        // Path parameter expansion requires the ; prefix
        await AssertNoMatchAsync(
            uriTemplate: "test:///path{;x}",
            method: (string x) => $"x:{x}",
            uri: "test:///pathx=1024");
    }

    [Fact]
    public async Task PathParameterExpansion_DoesNotMatchWrongParamName()
    {
        // Parameter name must match
        await AssertNoMatchAsync(
            uriTemplate: "test:///path{;x}",
            method: (string x) => $"x:{x}",
            uri: "test:///path;y=1024");
    }

    [Fact]
    public async Task PathParameterExpansion_DoesNotMatchSlashInValue()
    {
        // Path parameter values should not contain slashes
        await AssertNoMatchAsync(
            uriTemplate: "test:///path{;x}",
            method: (string x) => $"x:{x}",
            uri: "test:///path;x=foo/bar");
    }

    #endregion

    #region Level 3: Path Segment Expansion {/var}

    [Fact]
    public async Task PathSegmentExpansion_MatchesSingleSegment()
    {
        await AssertMatchAsync(
            uriTemplate: "test://{/var}",
            method: (string var) => $"var:{var}",
            uri: "test:///value",
            expectedResult: "var:value");
    }

    [Fact]
    public async Task PathSegmentExpansion_MultipleSegments()
    {
        // Multiple comma-separated variables in path expansion with / operator
        // The template {/x,y} expands to paths like "/value1/value2"
        await AssertMatchAsync(
            uriTemplate: "test://{/x,y}",
            method: (string x, string y) => $"x:{x},y:{y}",
            uri: "test:///1024/768",
            expectedResult: "x:1024,y:768");
    }

    [Fact]
    public async Task PathSegmentExpansion_ThreeSegments()
    {
        // Multiple comma-separated variables in path expansion with / operator
        // The template {/x,y,z} expands to paths like "/value1/value2/value3"
        await AssertMatchAsync(
            uriTemplate: "test://{/x,y,z}",
            method: (string x, string y, string z) => $"x:{x},y:{y},z:{z}",
            uri: "test:///a/b/c",
            expectedResult: "x:a,y:b,z:c");
    }

    [Fact]
    public async Task PathSegmentExpansion_DoesNotMatchSlashInValue()
    {
        // Path segment expansion should NOT match slashes within a single variable's value
        // Each variable should match one segment only, so /foo/bar doesn't fully match {/var}
        await AssertNoMatchAsync(
            uriTemplate: "test://{/var}",
            method: (string var) => $"var:{var}",
            uri: "test:///foo/bar");
    }

    [Fact]
    public async Task PathSegmentExpansion_CombinedWithLiterals()
    {
        await AssertMatchAsync(
            uriTemplate: "test:///users{/id}",
            method: (string id) => $"id:{id}",
            uri: "test:///users/123",
            expectedResult: "id:123");
    }

    [Fact]
    public async Task PathSegmentExpansion_MatchesWithoutSlash()
    {
        // Path segment expansion prefix is optional - matches with captured value even without /
        await AssertMatchAsync(
            uriTemplate: "test://{/var}",
            method: (string var) => $"var:{var}",
            uri: "test://value",
            expectedResult: "var:value");
    }

    [Fact]
    public async Task PathSegmentExpansion_DoesNotMatchFragment()
    {
        // Path segment expansion should not match fragment
        await AssertNoMatchAsync(
            uriTemplate: "test://{/var}",
            method: (string var) => $"var:{var}",
            uri: "test:///value#section");
    }

    [Fact]
    public async Task PathSegmentExpansion_DoesNotMatchQuery()
    {
        // Path segment expansion should not match query
        await AssertNoMatchAsync(
            uriTemplate: "test://{/var}",
            method: (string var) => $"var:{var}",
            uri: "test:///value?query");
    }

    #endregion

    #region Level 3: Form-Style Query Expansion {?var}

    [Fact]
    public async Task QueryExpansion_MatchesSingleParameter()
    {
        await AssertMatchAsync(
            uriTemplate: "test://example.com/search{?q}",
            method: (string q) => $"q:{q}",
            uri: "test://example.com/search?q=test",
            expectedResult: "q:test");
    }

    [Fact]
    public async Task QueryExpansion_MatchesMultipleParameters()
    {
        await AssertMatchAsync(
            uriTemplate: "test://example.com/search{?q,lang}",
            method: (string q, string lang) => $"q:{q},lang:{lang}",
            uri: "test://example.com/search?q=cat&lang=en",
            expectedResult: "q:cat,lang:en");
    }

    [Fact]
    public async Task QueryExpansion_ThreeParameters()
    {
        await AssertMatchAsync(
            uriTemplate: "test://params{?a1,a2,a3}",
            method: (string a1, string a2, string a3) => $"a1:{a1},a2:{a2},a3:{a3}",
            uri: "test://params?a1=a&a2=b&a3=c",
            expectedResult: "a1:a,a2:b,a3:c");
    }

    [Fact]
    public async Task QueryExpansion_DoesNotMatchWrongPath()
    {
        // The path must match exactly
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/search{?q}",
            method: (string q) => $"q:{q}",
            uri: "test://example.com/find?q=test");
    }

    [Fact]
    public async Task QueryExpansion_DoesNotMatchMissingQuestionMark()
    {
        // Query expansion requires the ? prefix when parameters are present
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/search{?q}",
            method: (string q) => $"q:{q}",
            uri: "test://example.com/searchq=test");
    }

    [Fact]
    public async Task QueryExpansion_DoesNotMatchSlashInValue()
    {
        // Query parameter values should not contain slashes
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/search{?q}",
            method: (string q) => $"q:{q}",
            uri: "test://example.com/search?q=foo/bar");
    }

    #endregion

    #region Level 3: Form-Style Query Continuation {&var}

    [Fact]
    public async Task QueryContinuation_MatchesWithExistingQuery()
    {
        await AssertMatchAsync(
            uriTemplate: "test://example.com/search?fixed=yes{&x}",
            method: (string x) => $"x:{x}",
            uri: "test://example.com/search?fixed=yes&x=1024",
            expectedResult: "x:1024");
    }

    [Fact]
    public async Task QueryContinuation_MultipleParameters()
    {
        await AssertMatchAsync(
            uriTemplate: "test://example.com/search?start=0{&x,y}",
            method: (string x, string y) => $"x:{x},y:{y}",
            uri: "test://example.com/search?start=0&x=1024&y=768",
            expectedResult: "x:1024,y:768");
    }

    [Fact]
    public async Task QueryContinuation_DoesNotMatchMissingAmpersand()
    {
        // Query continuation requires & prefix
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/search?start=0{&x}",
            method: (string x) => $"x:{x}",
            uri: "test://example.com/search?start=0x=1024");
    }

    [Fact]
    public async Task QueryContinuation_DoesNotMatchMissingFixedQuery()
    {
        // The fixed query part must be present
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/search?start=0{&x}",
            method: (string x) => $"x:{x}",
            uri: "test://example.com/search&x=1024");
    }

    #endregion

    #region Edge Cases and Special Characters

    [Fact]
    public async Task PctEncodedInValue_MatchesEncodedCharacters()
    {
        // MCP server automatically decodes percent-encoded characters
        await AssertMatchAsync(
            uriTemplate: "test://{var}",
            method: (string var) => $"var:{var}",
            uri: "test://Hello%20World",
            expectedResult: "var:Hello World");  // MCP decodes the %20 to a space
    }

    [Fact]
    public async Task EmptyTemplate_MatchesEmpty()
    {
        await AssertMatchAsync(
            uriTemplate: "test://",
            method: () => "matched",
            uri: "test://",
            expectedResult: "matched");
    }

    [Fact]
    public async Task LiteralOnlyTemplate_MatchesExactly()
    {
        await AssertMatchAsync(
            uriTemplate: "test://example.com/static",
            method: () => "matched",
            uri: "test://example.com/static",
            expectedResult: "matched");
    }

    [Fact]
    public async Task LiteralOnlyTemplate_DoesNotMatchDifferentUri()
    {
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/static",
            method: () => "matched",
            uri: "test://example.com/dynamic");
    }

    [Fact]
    public async Task CaseInsensitiveMatching()
    {
        // URI matching should be case-insensitive for the host portion
        await AssertMatchAsync(
            uriTemplate: "test://EXAMPLE.COM/{var}",
            method: (string var) => $"var:{var}",
            uri: "test://example.com/value",
            expectedResult: "var:value");
    }

    [Fact]
    public async Task EmptyTemplate_DoesNotMatchNonEmpty()
    {
        // Empty template should only match empty string
        await AssertNoMatchAsync(
            uriTemplate: "test://",
            method: () => "matched",
            uri: "test://example.com");
    }

    [Fact]
    public async Task LiteralOnlyTemplate_DoesNotMatchPartial()
    {
        // Literal template must match completely
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/static",
            method: () => "matched",
            uri: "test://example.com/static/extra");
    }

    [Fact]
    public async Task LiteralOnlyTemplate_DoesNotMatchPrefix()
    {
        // Literal template must match completely
        await AssertNoMatchAsync(
            uriTemplate: "test://example.com/static",
            method: () => "matched",
            uri: "test://example.com/stat");
    }

    #endregion

    #region Complex Real-World Templates

    [Fact]
    public async Task RealWorld_GitHubApiStyle()
    {
        await AssertMatchAsync(
            uriTemplate: "test://api.github.com/repos/{owner}/{repo}/contents/{+path}",
            method: (string owner, string repo, string path) => $"owner:{owner},repo:{repo},path:{path}",
            uri: "test://api.github.com/repos/microsoft/vscode/contents/src/vs/editor/editor.main.ts",
            expectedResult: "owner:microsoft,repo:vscode,path:src/vs/editor/editor.main.ts");
    }

    [Fact]
    public async Task RealWorld_FileSystemPath()
    {
        await AssertMatchAsync(
            uriTemplate: "test:///{+path}",
            method: (string path) => $"path:{path}",
            uri: "test:///home/user/documents/file.txt",
            expectedResult: "path:home/user/documents/file.txt");
    }

    [Fact]
    public async Task RealWorld_ResourceWithQuery()
    {
        await AssertMatchAsync(
            uriTemplate: "test://resource/{id}{?format,version}",
            method: (string id, string format, string version) => $"id:{id},format:{format},version:{version}",
            uri: "test://resource/12345?format=json&version=2",
            expectedResult: "id:12345,format:json,version:2");
    }

    [Fact]
    public async Task RealWorld_NonTemplatedUri()
    {
        // Non-templated URIs should match exactly with no captures
        await AssertMatchAsync(
            uriTemplate: "test://resource/non-templated",
            method: () => "matched",
            uri: "test://resource/non-templated",
            expectedResult: "matched");
    }

    [Fact]
    public async Task RealWorld_MixedTemplateAndLiteral()
    {
        await AssertMatchAsync(
            uriTemplate: "test://example.com/users/{userId}/posts/{postId}",
            method: (string userId, string postId) => $"userId:{userId},postId:{postId}",
            uri: "test://example.com/users/42/posts/100",
            expectedResult: "userId:42,postId:100");
    }

    /// <summary>
    /// FIXED BUG: The exact case from the bug report - samples scheme with dependency and path.
    /// </summary>
    [Fact]
    public async Task RealWorld_SamplesSchemeWithDependency()
    {
        await AssertMatchAsync(
            uriTemplate: "test://{dependency}/{+path}",
            method: (string dependency, string path) => $"dependency:{dependency},path:{path}",
            uri: "test://csharp-sdk/README.md",
            expectedResult: "dependency:csharp-sdk,path:README.md");
    }

    #endregion

    #region Operator Combinations

    [Fact]
    public async Task CombinedOperators_PathAndQuery()
    {
        await AssertMatchAsync(
            uriTemplate: "test:///api{/version}/resource{?page,limit}",
            method: (string version, string page, string limit) => $"version:{version},page:{page},limit:{limit}",
            uri: "test:///api/v2/resource?page=1&limit=10",
            expectedResult: "version:v2,page:1,limit:10");
    }

    [Fact]
    public async Task CombinedOperators_ReservedAndFragment()
    {
        // Reserved expansion should stop at # (fragment delimiter) so both parts are captured correctly
        await AssertMatchAsync(
            uriTemplate: "test://{+base}{#section}",
            method: (string @base, string section) => $"base:{@base},section:{section}",
            uri: "test://example.com/#intro",
            expectedResult: "base:example.com/,section:intro");
    }

    #endregion

    #region Variable Modifiers (prefix `:n`)

    [Fact]
    public async Task PrefixModifier_InTemplate()
    {
        // Templates with prefix modifiers should still parse and match
        // The regex captures whatever matches (the parser doesn't enforce prefix length)
        await AssertMatchAsync(
            uriTemplate: "test://{var:3}",
            method: (string var) => $"var:{var}",
            uri: "test://val",
            expectedResult: "var:val");
    }

    #endregion

    #region Explode Modifier

    [Fact]
    public async Task ExplodeModifier_InTemplate()
    {
        // Templates with explode modifiers should still parse and match single values
        await AssertMatchAsync(
            uriTemplate: "test://{/list*}",
            method: (string list) => $"list:{list}",
            uri: "test:///item",
            expectedResult: "list:item");
    }

    #endregion
}

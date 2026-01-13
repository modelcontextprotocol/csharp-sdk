using System.Text.RegularExpressions;

namespace ModelContextProtocol.Tests;

/// <summary>
/// Comprehensive test suite for UriTemplate.CreateParser method.
/// Tests are based on RFC 6570 (URI Template) specification.
/// </summary>
public sealed class UriTemplateCreateParserTests
{
    private static Regex CreateParser(string template) => UriTemplate.CreateParser(template);

    private static Match MatchUri(string template, string uri)
    {
        var regex = CreateParser(template);
        return regex.Match(uri);
    }

    private static void AssertMatch(string template, string uri, params (string name, string value)[] expectedGroups)
    {
        var match = MatchUri(template, uri);
        Assert.True(match.Success, $"Template '{template}' should match URI '{uri}'");

        foreach (var (name, value) in expectedGroups)
        {
            Assert.True(match.Groups[name].Success, $"Group '{name}' should be captured");
            Assert.Equal(value, match.Groups[name].Value);
        }
    }

    private static void AssertMatchWithGroupCount(string template, string uri, int expectedCapturedGroupCount, params (string name, string value)[] expectedGroups)
    {
        var match = MatchUri(template, uri);
        Assert.True(match.Success, $"Template '{template}' should match URI '{uri}'");

        // Count groups that actually captured (excluding the default group 0 which is the full match)
        int capturedCount = 0;
        for (int i = 1; i < match.Groups.Count; i++)
        {
            if (match.Groups[i].Success && !string.IsNullOrEmpty(match.Groups[i].Value))
            {
                capturedCount++;
            }
        }
        Assert.Equal(expectedCapturedGroupCount, capturedCount);

        foreach (var (name, value) in expectedGroups)
        {
            Assert.True(match.Groups[name].Success, $"Group '{name}' should be captured");
            Assert.Equal(value, match.Groups[name].Value);
        }
    }

    private static void AssertNoMatch(string template, string uri)
    {
        var match = MatchUri(template, uri);
        Assert.False(match.Success, $"Template '{template}' should NOT match URI '{uri}'");
    }

    private static void AssertMatchWithNoCaptures(string template, string uri)
    {
        var match = MatchUri(template, uri);
        Assert.True(match.Success, $"Template '{template}' should match URI '{uri}'");

        // Verify that no named groups captured anything (excluding group 0 which is the full match)
        for (int i = 1; i < match.Groups.Count; i++)
        {
            Assert.True(
                !match.Groups[i].Success || string.IsNullOrEmpty(match.Groups[i].Value),
                $"Group {i} should not capture any value, but captured '{match.Groups[i].Value}'");
        }
    }

    #region Level 1: Simple String Expansion {var}

    [Fact]
    public void SimpleExpansion_MatchesSingleVariable()
    {
        AssertMatch("http://example.com/{var}", "http://example.com/value", ("var", "value"));
    }

    [Fact]
    public void SimpleExpansion_DoesNotMatchSlash()
    {
        // Simple expansion should NOT match slashes
        AssertNoMatch("http://example.com/{var}", "http://example.com/foo/bar");
    }

    [Fact]
    public void SimpleExpansion_DoesNotMatchQuestionMark()
    {
        // Simple expansion should NOT match query string characters
        AssertNoMatch("http://example.com/{var}", "http://example.com/foo?query");
    }

    [Fact]
    public void SimpleExpansion_DoesNotMatchFragment()
    {
        // Simple expansion should NOT match fragment delimiter
        AssertNoMatch("http://example.com/{var}", "http://example.com/foo#section");
    }

    [Fact]
    public void SimpleExpansion_MatchesWithEmptyValue()
    {
        // Simple expansion variables are optional - empty value matches but captures nothing
        AssertMatchWithGroupCount("http://example.com/{var}", "http://example.com/", 0);
    }

    [Fact]
    public void SimpleExpansion_DoesNotMatchMissingSegment()
    {
        // Simple expansion is not optional when it's the only content of a segment
        AssertNoMatch("http://example.com/{var}", "http://example.com");
    }

    [Fact]
    public void SimpleExpansion_DoesNotMatchExtraPath()
    {
        // Template requires exact match, extra segments should not match
        AssertNoMatch("http://example.com/{var}", "http://example.com/value/extra");
    }

    [Fact]
    public void SimpleExpansion_MultipleVariables()
    {
        AssertMatch(
            "http://example.com/{x}/{y}",
            "http://example.com/1024/768",
            ("x", "1024"),
            ("y", "768"));
    }

    [Fact]
    public void SimpleExpansion_MultipleVariables_MatchesWithMissingSecond()
    {
        // Second variable is optional - matches with only first captured
        AssertMatchWithGroupCount(
            "http://example.com/{x}/{y}",
            "http://example.com/1024/",
            1,
            ("x", "1024"));
    }

    [Fact]
    public void SimpleExpansion_MultipleVariables_MatchesWithMissingFirst()
    {
        // First variable is optional - matches with only second captured
        AssertMatchWithGroupCount(
            "http://example.com/{x}/{y}",
            "http://example.com//768",
            1,
            ("y", "768"));
    }

    #endregion

    #region Level 2: Reserved Expansion {+var} - REGRESSION TESTS FOR BUG FIX

    /// <summary>
    /// FIXED BUG: Reserved expansion {+var} should match slashes.
    /// This was the bug that caused samples://{dependency}/{+path} to fail.
    /// Per RFC 6570 Section 3.2.3, the + operator allows reserved characters including "/" to pass through.
    /// </summary>
    [Fact]
    public void ReservedExpansion_MatchesSlashes()
    {
        // FIXED: {+path} should match paths containing slashes
        AssertMatch(
            "samples://{dependency}/{+path}",
            "samples://foo/README.md",
            ("dependency", "foo"),
            ("path", "README.md"));
    }

    /// <summary>
    /// FIXED BUG: Reserved expansion with nested path containing slashes.
    /// This is the exact failing case from the issue.
    /// </summary>
    [Fact]
    public void ReservedExpansion_MatchesNestedPath()
    {
        // FIXED: {+path} should match paths with multiple segments
        AssertMatch(
            "samples://{dependency}/{+path}",
            "samples://foo/examples/example.rs",
            ("dependency", "foo"),
            ("path", "examples/example.rs"));
    }

    /// <summary>
    /// FIXED BUG: Reserved expansion with deep nested path.
    /// </summary>
    [Fact]
    public void ReservedExpansion_MatchesDeeplyNestedPath()
    {
        // FIXED: {+path} should match deeply nested paths
        AssertMatch(
            "samples://{dependency}/{+path}",
            "samples://mylib/src/components/utils/helper.ts",
            ("dependency", "mylib"),
            ("path", "src/components/utils/helper.ts"));
    }

    [Fact]
    public void ReservedExpansion_SimpleValue()
    {
        // Reserved expansion should still work for simple values without slashes
        AssertMatch("{+var}", "value", ("var", "value"));
    }

    [Fact]
    public void ReservedExpansion_WithPathStartingWithSlash()
    {
        // Reserved expansion allows reserved URI characters like /
        AssertMatch("{+path}", "/foo/bar", ("path", "/foo/bar"));
    }

    [Fact]
    public void ReservedExpansion_StopsAtQueryString()
    {
        // Reserved expansion should stop at ? (query string delimiter)
        // The template doesn't match because it expects the URI to end after {+path}
        // but there's a query string. We should verify it doesn't capture the query.
        AssertNoMatch("http://example.com/{+path}", "http://example.com/foo/bar?query=test");
    }

    [Fact]
    public void ReservedExpansion_StopsAtFragment()
    {
        // Reserved expansion should stop at # (fragment delimiter)
        AssertNoMatch("http://example.com/{+path}", "http://example.com/foo/bar#section");
    }

    [Fact]
    public void ReservedExpansion_MatchesEmpty()
    {
        // Reserved expansion variables are optional - empty matches with 0 captures
        AssertMatchWithGroupCount("{+var}", "", 0);
    }

    [Fact]
    public void ReservedExpansion_DoesNotMatchWrongScheme()
    {
        // Scheme must match exactly
        AssertNoMatch("http://example.com/{+path}", "https://example.com/foo");
    }

    #endregion

    #region Level 2: Fragment Expansion {#var}

    [Fact]
    public void FragmentExpansion_MatchesWithHashPrefix()
    {
        AssertMatch("http://example.com/page{#section}", "http://example.com/page#intro", ("section", "intro"));
    }

    [Fact]
    public void FragmentExpansion_MatchesSlashes()
    {
        // Fragment expansion allows reserved characters including /
        AssertMatch("{#path}", "#/foo/bar", ("path", "/foo/bar"));
    }

    [Fact]
    public void FragmentExpansion_OptionalFragment()
    {
        // Fragment should be optional - match succeeds but section is not captured
        AssertMatchWithGroupCount("http://example.com/page{#section}", "http://example.com/page", 0);
    }

    [Fact]
    public void FragmentExpansion_MatchesWithoutHash()
    {
        // Fragment expansion prefix is optional - matches with captured value even without #
        AssertMatchWithGroupCount("{#section}", "intro", 1, ("section", "intro"));
    }

    [Fact]
    public void FragmentExpansion_DoesNotMatchWrongPath()
    {
        // The path must match exactly
        AssertNoMatch("http://example.com/page{#section}", "http://example.com/other#intro");
    }

    #endregion

    #region Level 3: Label Expansion with Dot-Prefix {.var} - BUG FIX

    /// <summary>
    /// FIXED BUG: Label expansion {.var} should match dot-prefixed values.
    /// The . operator was falling through to the default case which didn't handle the dot prefix.
    /// </summary>
    [Fact]
    public void LabelExpansion_MatchesDotPrefixedSingleValue()
    {
        // FIXED: {.var} should match .value
        AssertMatch("X{.var}", "X.value", ("var", "value"));
    }

    /// <summary>
    /// FIXED BUG: Label expansion with multiple variables should use dot as separator.
    /// </summary>
    [Fact]
    public void LabelExpansion_MatchesMultipleValues()
    {
        // FIXED: {.x,y} should match .1024.768 (dot separated)
        AssertMatch("www{.x,y}", "www.example.com", ("x", "example"), ("y", "com"));
    }

    [Fact]
    public void LabelExpansion_DomainStyle()
    {
        // Common use case: domain name labels
        AssertMatch("www{.dom}", "www.example", ("dom", "example"));
    }

    [Fact]
    public void LabelExpansion_MatchesWithoutDot()
    {
        // Label expansion prefix is optional - matches with captured value even without .
        AssertMatchWithGroupCount("www{.dom}", "wwwexample", 1, ("dom", "example"));
    }

    [Fact]
    public void LabelExpansion_DoesNotMatchSlash()
    {
        // Label expansion should not match slashes
        AssertNoMatch("www{.dom}", "www.foo/bar");
    }

    [Fact]
    public void LabelExpansion_MatchesEmptyAfterDot()
    {
        // Label expansion variables are optional - dot with no value matches with 0 captures
        AssertMatchWithGroupCount("www{.dom}", "www.", 0);
    }

    #endregion

    #region Level 3: Path-Style Parameter Expansion {;var} - BUG FIX

    /// <summary>
    /// FIXED BUG: Path-style parameter expansion {;var} should match semicolon-prefixed name=value pairs.
    /// The ; operator was falling through to the default case which didn't handle the semicolon prefix or name=value format.
    /// </summary>
    [Fact]
    public void PathParameterExpansion_MatchesSingleParameter()
    {
        // FIXED: {;x} should match ;x=1024
        AssertMatch("/path{;x}", "/path;x=1024", ("x", "1024"));
    }

    /// <summary>
    /// FIXED BUG: Path-style parameter expansion with multiple parameters.
    /// </summary>
    [Fact]
    public void PathParameterExpansion_MatchesMultipleParameters()
    {
        // FIXED: {;x,y} should match ;x=1024;y=768
        AssertMatch("/path{;x,y}", "/path;x=1024;y=768", ("x", "1024"), ("y", "768"));
    }

    [Fact]
    public void PathParameterExpansion_MatchesNameOnly()
    {
        // Path parameters can be just ;name (without =value) when the value is empty
        // The name is present but no value is captured
        AssertMatchWithGroupCount("/path{;empty}", "/path;empty", 0);
    }

    [Fact]
    public void PathParameterExpansion_DoesNotMatchMissingSemicolon()
    {
        // Path parameter expansion requires the ; prefix
        AssertNoMatch("/path{;x}", "/pathx=1024");
    }

    [Fact]
    public void PathParameterExpansion_DoesNotMatchWrongParamName()
    {
        // Parameter name must match
        AssertNoMatch("/path{;x}", "/path;y=1024");
    }

    [Fact]
    public void PathParameterExpansion_DoesNotMatchSlashInValue()
    {
        // Path parameter values should not contain slashes
        AssertNoMatch("/path{;x}", "/path;x=foo/bar");
    }

    #endregion

    #region Level 3: Path Segment Expansion {/var}

    [Fact]
    public void PathSegmentExpansion_MatchesSingleSegment()
    {
        AssertMatch("{/var}", "/value", ("var", "value"));
    }

    [Fact]
    public void PathSegmentExpansion_MultipleSegments()
    {
        // Multiple comma-separated variables in path expansion with / operator
        // The template {/x,y} expands to paths like "/value1/value2"
        AssertMatchWithGroupCount(
            "{/x,y}",
            "/1024/768",
            2,
            ("x", "1024"),
            ("y", "768"));
    }

    [Fact]
    public void PathSegmentExpansion_ThreeSegments()
    {
        // Multiple comma-separated variables in path expansion with / operator
        // The template {/x,y,z} expands to paths like "/value1/value2/value3"
        AssertMatchWithGroupCount(
            "{/x,y,z}",
            "/a/b/c",
            3,
            ("x", "a"),
            ("y", "b"),
            ("z", "c"));
    }

    [Fact]
    public void PathSegmentExpansion_DoesNotMatchSlashInValue()
    {
        // Path segment expansion should NOT match slashes within a single variable's value
        // Each variable should match one segment only, so /foo/bar doesn't fully match {/var}
        AssertNoMatch("{/var}", "/foo/bar");
    }

    [Fact]
    public void PathSegmentExpansion_CombinedWithLiterals()
    {
        AssertMatch("/users{/id}", "/users/123", ("id", "123"));
    }

    [Fact]
    public void PathSegmentExpansion_MatchesWithoutSlash()
    {
        // Path segment expansion prefix is optional - matches with captured value even without /
        AssertMatchWithGroupCount("{/var}", "value", 1, ("var", "value"));
    }

    [Fact]
    public void PathSegmentExpansion_MatchesEmptyAfterSlash()
    {
        // Path segment expansion variables are optional - slash with no value matches with 0 captures
        AssertMatchWithGroupCount("{/var}", "/", 0);
    }

    [Fact]
    public void PathSegmentExpansion_DoesNotMatchFragment()
    {
        // Path segment expansion should not match fragment
        AssertNoMatch("{/var}", "/value#section");
    }

    [Fact]
    public void PathSegmentExpansion_DoesNotMatchQuery()
    {
        // Path segment expansion should not match query
        AssertNoMatch("{/var}", "/value?query");
    }

    #endregion

    #region Level 3: Form-Style Query Expansion {?var}

    [Fact]
    public void QueryExpansion_MatchesSingleParameter()
    {
        AssertMatch("http://example.com/search{?q}", "http://example.com/search?q=test", ("q", "test"));
    }

    [Fact]
    public void QueryExpansion_MatchesMultipleParameters()
    {
        AssertMatch(
            "http://example.com/search{?q,lang}",
            "http://example.com/search?q=cat&lang=en",
            ("q", "cat"),
            ("lang", "en"));
    }

    [Fact]
    public void QueryExpansion_OptionalParameters()
    {
        // All query parameters should be optional - no parameters means no captures
        AssertMatchWithGroupCount("http://example.com/search{?q,lang}", "http://example.com/search", 0);
    }

    [Fact]
    public void QueryExpansion_PartialParameters()
    {
        // Should match even if not all parameters are present - only q is captured
        AssertMatchWithGroupCount(
            "http://example.com/search{?q,lang}",
            "http://example.com/search?q=test",
            1,
            ("q", "test"));
    }

    [Fact]
    public void QueryExpansion_ThreeParameters()
    {
        AssertMatch(
            "test://params{?a1,a2,a3}",
            "test://params?a1=a&a2=b&a3=c",
            ("a1", "a"),
            ("a2", "b"),
            ("a3", "c"));
    }

    [Fact]
    public void QueryExpansion_DoesNotMatchWrongPath()
    {
        // The path must match exactly
        AssertNoMatch("http://example.com/search{?q}", "http://example.com/find?q=test");
    }

    [Fact]
    public void QueryExpansion_DoesNotMatchMissingQuestionMark()
    {
        // Query expansion requires the ? prefix when parameters are present
        AssertNoMatch("http://example.com/search{?q}", "http://example.com/searchq=test");
    }

    [Fact]
    public void QueryExpansion_DoesNotMatchSlashInValue()
    {
        // Query parameter values should not contain slashes
        AssertNoMatch("http://example.com/search{?q}", "http://example.com/search?q=foo/bar");
    }

    #endregion

    #region Level 3: Form-Style Query Continuation {&var}

    [Fact]
    public void QueryContinuation_MatchesWithExistingQuery()
    {
        AssertMatch(
            "http://example.com/search?fixed=yes{&x}",
            "http://example.com/search?fixed=yes&x=1024",
            ("x", "1024"));
    }

    [Fact]
    public void QueryContinuation_MultipleParameters()
    {
        AssertMatch(
            "http://example.com/search?start=0{&x,y}",
            "http://example.com/search?start=0&x=1024&y=768",
            ("x", "1024"),
            ("y", "768"));
    }

    [Fact]
    public void QueryContinuation_DoesNotMatchMissingAmpersand()
    {
        // Query continuation requires & prefix
        AssertNoMatch("http://example.com/search?start=0{&x}", "http://example.com/search?start=0x=1024");
    }

    [Fact]
    public void QueryContinuation_DoesNotMatchMissingFixedQuery()
    {
        // The fixed query part must be present
        AssertNoMatch("http://example.com/search?start=0{&x}", "http://example.com/search&x=1024");
    }

    #endregion

    #region Edge Cases and Special Characters

    [Fact]
    public void PctEncodedInValue_MatchesEncodedCharacters()
    {
        // Values containing percent-encoded characters
        AssertMatch("{var}", "Hello%20World", ("var", "Hello%20World"));
    }

    [Fact]
    public void EmptyTemplate_MatchesEmpty()
    {
        AssertMatchWithNoCaptures("", "");
    }

    [Fact]
    public void LiteralOnlyTemplate_MatchesExactly()
    {
        AssertMatchWithNoCaptures("http://example.com/static", "http://example.com/static");
    }

    [Fact]
    public void LiteralOnlyTemplate_DoesNotMatchDifferentUri()
    {
        AssertNoMatch("http://example.com/static", "http://example.com/dynamic");
    }

    [Fact]
    public void CaseInsensitiveMatching()
    {
        // URI matching should be case-insensitive for the host portion
        AssertMatchWithGroupCount(
            "http://EXAMPLE.COM/{var}",
            "http://example.com/value",
            1,
            ("var", "value"));
    }

    [Fact]
    public void EmptyTemplate_DoesNotMatchNonEmpty()
    {
        // Empty template should only match empty string
        AssertNoMatch("", "http://example.com");
    }

    [Fact]
    public void LiteralOnlyTemplate_DoesNotMatchPartial()
    {
        // Literal template must match completely
        AssertNoMatch("http://example.com/static", "http://example.com/static/extra");
    }

    [Fact]
    public void LiteralOnlyTemplate_DoesNotMatchPrefix()
    {
        // Literal template must match completely
        AssertNoMatch("http://example.com/static", "http://example.com/stat");
    }

    #endregion

    #region Complex Real-World Templates

    [Fact]
    public void RealWorld_GitHubApiStyle()
    {
        AssertMatch(
            "https://api.github.com/repos/{owner}/{repo}/contents/{+path}",
            "https://api.github.com/repos/microsoft/vscode/contents/src/vs/editor/editor.main.ts",
            ("owner", "microsoft"),
            ("repo", "vscode"),
            ("path", "src/vs/editor/editor.main.ts"));
    }

    [Fact]
    public void RealWorld_FileSystemPath()
    {
        AssertMatch(
            "file:///{+path}",
            "file:///home/user/documents/file.txt",
            ("path", "home/user/documents/file.txt"));
    }

    [Fact]
    public void RealWorld_ResourceWithOptionalQuery()
    {
        AssertMatch(
            "test://resource/{id}{?format,version}",
            "test://resource/12345?format=json&version=2",
            ("id", "12345"),
            ("format", "json"),
            ("version", "2"));
    }

    [Fact]
    public void RealWorld_NonTemplatedUri()
    {
        // Non-templated URIs should match exactly with no captures
        AssertMatchWithNoCaptures("test://resource/non-templated", "test://resource/non-templated");
    }

    [Fact]
    public void RealWorld_MixedTemplateAndLiteral()
    {
        AssertMatch(
            "http://example.com/users/{userId}/posts/{postId}",
            "http://example.com/users/42/posts/100",
            ("userId", "42"),
            ("postId", "100"));
    }

    /// <summary>
    /// FIXED BUG: The exact case from the bug report - samples scheme with dependency and path.
    /// </summary>
    [Fact]
    public void RealWorld_SamplesSchemeWithDependency()
    {
        AssertMatch(
            "samples://{dependency}/{+path}",
            "samples://csharp-sdk/README.md",
            ("dependency", "csharp-sdk"),
            ("path", "README.md"));
    }

    #endregion

    #region Operator Combinations

    [Fact]
    public void CombinedOperators_PathAndQuery()
    {
        AssertMatch(
            "/api{/version}/resource{?page,limit}",
            "/api/v2/resource?page=1&limit=10",
            ("version", "v2"),
            ("page", "1"),
            ("limit", "10"));
    }

    [Fact]
    public void CombinedOperators_ReservedAndFragment()
    {
        // Reserved expansion should stop at # (fragment delimiter) so both parts are captured correctly
        AssertMatchWithGroupCount(
            "{+base}{#section}",
            "http://example.com/#intro",
            2,
            ("base", "http://example.com/"),
            ("section", "intro"));
    }

    #endregion

    #region Variable Modifiers (prefix `:n`)

    [Fact]
    public void PrefixModifier_InTemplate()
    {
        // Templates with prefix modifiers should still parse and match
        // The regex captures whatever matches (the parser doesn't enforce prefix length)
        AssertMatchWithGroupCount("{var:3}", "val", 1, ("var", "val"));
    }

    #endregion

    #region Explode Modifier (`*`)

    [Fact]
    public void ExplodeModifier_InTemplate()
    {
        // Templates with explode modifiers should still parse and match single values
        AssertMatchWithGroupCount("{/list*}", "/item", 1, ("list", "item"));
    }

    #endregion
}

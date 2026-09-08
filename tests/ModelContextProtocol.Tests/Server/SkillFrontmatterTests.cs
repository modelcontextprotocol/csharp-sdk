using ModelContextProtocol.Extensions.Skills;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for <see cref="SkillFrontmatter"/>: the YAML subset it accepts, YAML 1.2 core-schema scalar resolution,
/// and the constructs it rejects.
/// </summary>
public class SkillFrontmatterTests
{
    private static JsonObject Parse(string yamlBody, string suffix = "\n# Body\n") =>
        SkillFrontmatter.Parse("---\n" + yamlBody + "\n---" + suffix);

    private static void AssertJson(string expectedJson, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectedJson), actual), $"Expected {expectedJson} but got {actual?.ToJsonString() ?? "null"}.");

    [Fact]
    public void Parses_TheAgentSkillsReferenceShape()
    {
        var frontmatter = Parse("""
            name: pdf-processing
            description: Extract, fill, and assemble PDF documents
            license: Apache-2.0
            compatibility: Designed for Claude Code
            metadata:
              author: example-org
              version: "1.0"
            allowed-tools: Bash(git:*) Read
            """);

        AssertJson("""
            {
              "name": "pdf-processing",
              "description": "Extract, fill, and assemble PDF documents",
              "license": "Apache-2.0",
              "compatibility": "Designed for Claude Code",
              "metadata": { "author": "example-org", "version": "1.0" },
              "allowed-tools": "Bash(git:*) Read"
            }
            """, frontmatter);
    }

    [Fact]
    public void PreservesKeyOrder()
    {
        var frontmatter = Parse("zeta: 1\nalpha: 2\nmid: 3");

        Assert.Equal(["zeta", "alpha", "mid"], frontmatter.Select(p => p.Key));
    }

    [Theory]
    [InlineData("null", "null")]
    [InlineData("~", "null")]
    [InlineData("", "null")]
    [InlineData("Null", "null")]
    [InlineData("true", "true")]
    [InlineData("False", "false")]
    [InlineData("42", "42")]
    [InlineData("-7", "-7")]
    [InlineData("007", "7")]
    [InlineData("0x1F", "31")]
    [InlineData("0o17", "15")]
    [InlineData("1.5", "1.5")]
    [InlineData(".5", "0.5")]
    [InlineData("1e3", "1000")]
    [InlineData("-2.5E-1", "-0.25")]
    [InlineData("123456789012345678901234567890", "123456789012345678901234567890")]
    [InlineData("yes", "\"yes\"")]
    [InlineData("on", "\"on\"")]
    [InlineData("1.0.0", "\"1.0.0\"")]
    [InlineData("2026-09-09", "\"2026-09-09\"")]
    [InlineData("1_000", "\"1_000\"")]
    [InlineData("+", "\"+\"")]
    [InlineData("Bash(git:*)", "\"Bash(git:*)\"")]
    [InlineData("https://example.com/a:b", "\"https://example.com/a:b\"")]
    public void ResolvesPlainScalarsPerCoreSchema(string yaml, string expectedJson)
    {
        var frontmatter = Parse("value: " + yaml);

        AssertJson("{ \"value\": " + expectedJson + " }", frontmatter);
    }

    [Theory]
    [InlineData("'single quoted'", "single quoted")]
    [InlineData("'it''s'", "it's")]
    [InlineData("\"double quoted\"", "double quoted")]
    [InlineData("\"tab\\there\"", "tab\there")]
    [InlineData("\"new\\nline\"", "new\nline")]
    [InlineData("\"quote \\\" inside\"", "quote \" inside")]
    [InlineData("\"\\u00e9\\x41\"", "éA")]
    [InlineData("\"1.0\"", "1.0")]
    [InlineData("'true'", "true")]
    [InlineData("\"a # not a comment\"", "a # not a comment")]
    [InlineData("'key: value'", "key: value")]
    public void ParsesQuotedScalars(string yaml, string expected)
    {
        var frontmatter = Parse("value: " + yaml);

        Assert.Equal(expected, frontmatter["value"]?.GetValue<string>());
    }

    [Fact]
    public void StripsCommentsOutsideQuotes()
    {
        var frontmatter = Parse("""
            # leading comment
            name: demo # trailing comment
            description: has#hash inside # but this is a comment
              # an indented comment line
            license: 'MIT' # after quotes
            """);

        Assert.Equal("demo", frontmatter["name"]?.GetValue<string>());
        Assert.Equal("has#hash inside", frontmatter["description"]?.GetValue<string>());
        Assert.Equal("MIT", frontmatter["license"]?.GetValue<string>());
    }

    [Fact]
    public void FoldsPlainMultiLineScalars()
    {
        var frontmatter = Parse("""
            description: This description
              continues on the next line
              and the one after.
            name: demo
            """);

        Assert.Equal("This description continues on the next line and the one after.", frontmatter["description"]?.GetValue<string>());
        Assert.Equal("demo", frontmatter["name"]?.GetValue<string>());
    }

    [Fact]
    public void ParsesLiteralBlockScalar()
    {
        var frontmatter = Parse("""
            description: |
              Line one.
              Line two.

                Indented line.
            name: demo
            """);

        Assert.Equal("Line one.\nLine two.\n\n  Indented line.\n", frontmatter["description"]?.GetValue<string>());
        Assert.Equal("demo", frontmatter["name"]?.GetValue<string>());
    }

    [Fact]
    public void ParsesFoldedBlockScalar()
    {
        var frontmatter = Parse("""
            description: >
              Folded text
              on two lines.

              New paragraph.
            """);

        Assert.Equal("Folded text on two lines.\nNew paragraph.\n", frontmatter["description"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("|-", "a\nb")]
    [InlineData("|", "a\nb\n")]
    [InlineData("|+", "a\nb\n\n")]
    [InlineData(">-", "a b")]
    public void HonorsChompingIndicators(string header, string expected)
    {
        var frontmatter = Parse($"value: {header}\n  a\n  b\n\nnext: 1");

        Assert.Equal(expected, frontmatter["value"]?.GetValue<string>());
        Assert.Equal(1, frontmatter["next"]?.GetValue<int>());
    }

    [Fact]
    public void ParsesBlockSequences()
    {
        var frontmatter = Parse("""
            tags:
              - one
              - "two"
              - 3
            same-indent:
            - a
            - b
            objects:
              - name: x
                value: 1
              - name: y
            """);

        AssertJson("""
            {
              "tags": ["one", "two", 3],
              "same-indent": ["a", "b"],
              "objects": [{ "name": "x", "value": 1 }, { "name": "y" }]
            }
            """, frontmatter);
    }

    [Fact]
    public void ParsesFlowCollections()
    {
        var frontmatter = Parse("""
            tags: [a, "b c", 'd', 4, true]
            empty: []
            map: { k: v, n: 2 }
            emptymap: {}
            """);

        AssertJson("""{ "tags": ["a", "b c", "d", 4, true], "empty": [], "map": { "k": "v", "n": 2 }, "emptymap": {} }""", frontmatter);
    }

    [Fact]
    public void ParsesNestedMappingsToAnyDepth()
    {
        var frontmatter = Parse("""
            a:
              b:
                c:
                  d: deep
              e: 1
            f: 2
            """);

        AssertJson("""{ "a": { "b": { "c": { "d": "deep" } }, "e": 1 }, "f": 2 }""", frontmatter);
    }

    [Fact]
    public void HandlesCrlfAndBom()
    {
        var frontmatter = SkillFrontmatter.Parse("\uFEFF---\r\nname: demo\r\ndescription: d\r\n---\r\n\r\n# Body\r\n");

        AssertJson("""{ "name": "demo", "description": "d" }""", frontmatter);
    }

    [Fact]
    public void AcceptsDocumentEndMarkerAsCloser()
    {
        var frontmatter = SkillFrontmatter.Parse("---\nname: demo\n...\nbody");

        Assert.Equal("demo", frontmatter["name"]?.GetValue<string>());
    }

    [Fact]
    public void EmptyFrontmatterIsAnEmptyObject()
    {
        Assert.Empty(SkillFrontmatter.Parse("---\n---\n# Body"));
    }

    [Theory]
    [InlineData("# no frontmatter\nname: x", "must begin")]
    [InlineData("---\nname: x\n", "not closed")]
    [InlineData("---\nname: x\nname: y\n---", "duplicate key")]
    [InlineData("---\n\tname: x\n---", "tabs")]
    [InlineData("---\nname: &anchor x\n---", "anchors")]
    [InlineData("---\nname: *alias\n---", "anchors")]
    [InlineData("---\nname: !!str x\n---", "anchors")]
    [InlineData("---\n? complex\n: key\n---", "complex")]
    [InlineData("---\nname: \"unterminated\n---", "unterminated")]
    [InlineData("---\nname: [a, [b]]\n---", "nested flow")]
    [InlineData("---\nname: [a,\n  b]\n---", "unterminated flow")]
    [InlineData("---\nvalue: .inf\n---", "cannot be represented")]
    [InlineData("---\nvalue: \"\\q\"\n---", "unsupported escape")]
    [InlineData("---\njust a scalar\n---", "key: value")]
    [InlineData("---\n- item\n---", "must be a YAML mapping")]
    [InlineData("---\nname: a: b\n---", "cannot contain ': '")]
    [InlineData("---\ndescription: text\n  other: mis-indented key\n---", "cannot contain ': '")]
    [InlineData("---\nname: x\n  extra: indented\n---", "cannot contain ': '")]
    public void RejectsUnsupportedOrMalformedInput(string markdown, string messageFragment)
    {
        var exception = Assert.Throws<FormatException>(() => SkillFrontmatter.Parse(markdown));

        Assert.Contains(messageFragment, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoundTripsThroughSkillEntryValidation()
    {
        const string Markdown = "---\nname: git-workflow\ndescription: Git conventions\n---\n\n# Git workflow\n";

        var skill = McpServerSkill.Create([McpServerSkillFile.FromText("SKILL.md", Markdown)]);

        Assert.Equal("skill://git-workflow/SKILL.md", skill.ProtocolSkill.Uri);
        Assert.Equal("git-workflow", skill.ProtocolSkill.Name);
        Assert.Equal("Git conventions", skill.ProtocolSkill.Description);
    }
}

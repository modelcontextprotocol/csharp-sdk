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
    [InlineData("0xffffffffffffffff", "18446744073709551615")]
    [InlineData("0o777777777777777777777777", "4722366482869645213695")]
    [InlineData("+0x10", "\"+0x10\"")]
    [InlineData("-0o10", "\"-0o10\"")]
    [InlineData("0x", "\"0x\"")]
    [InlineData("0xG", "\"0xG\"")]
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
    [InlineData("\"\\U0001F600\"", "😀")]
    [InlineData("\"\\uD83D\\uDE00\"", "😀")]
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
            apostrophe: Follow the team's workflow # author note
            quote: She said "go" # and left
            tags: ["a # b", 'c # d', e] # trailing
            map: { k: "v # w" } # trailing
            block: | # header comment
              text # kept
            tab: x	#tab before hash
            dash: -a
            """);

        Assert.Equal("demo", frontmatter["name"]?.GetValue<string>());
        Assert.Equal("has#hash inside", frontmatter["description"]?.GetValue<string>());
        Assert.Equal("MIT", frontmatter["license"]?.GetValue<string>());
        Assert.Equal("Follow the team's workflow", frontmatter["apostrophe"]?.GetValue<string>());
        Assert.Equal("She said \"go\"", frontmatter["quote"]?.GetValue<string>());
        AssertJson("""["a # b", "c # d", "e"]""", frontmatter["tags"]);
        Assert.Equal("v # w", frontmatter["map"]?["k"]?.GetValue<string>());
        Assert.Equal("text # kept\n", frontmatter["block"]?.GetValue<string>());
        Assert.Equal("x", frontmatter["tab"]?.GetValue<string>());
        Assert.Equal("-a", frontmatter["dash"]?.GetValue<string>());
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
    [InlineData(">", "\nText\n")]
    [InlineData(">-", "\nText")]
    [InlineData("|", "\nText\n")]
    public void PreservesLeadingEmptyLinesInBlockScalars(string header, string expected)
    {
        var frontmatter = Parse($"value: {header}\n\n  Text\nnext: 1");

        Assert.Equal(expected, frontmatter["value"]?.GetValue<string>());
        Assert.Equal(1, frontmatter["next"]?.GetValue<int>());
    }

    [Theory]
    [InlineData("|-", "a\nb")]
    [InlineData("|", "a\nb\n")]
    [InlineData("|+", "a\nb\n\n")]
    [InlineData(">-", "a b")]
    [InlineData("|- # comment", "a\nb")]
    [InlineData("|2-", "a\nb")]
    [InlineData("|-2", "a\nb")]
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
    public void ResolvesPlainMappingKeysLikeReferenceParsers()
    {
        var frontmatter = Parse("TRUE: a\n0x10: b\n~: c\n1.5: d\nplain: e");

        Assert.Equal(["true", "16", "", "1.5", "plain"], frontmatter.Select(p => p.Key));
    }

    [Fact]
    public void PreservesNonBreakingSpacesInScalars()
    {
        var frontmatter = Parse("value: \u00A0padded\u00A0\nother: a\u00A0b");

        Assert.Equal("\u00A0padded\u00A0", frontmatter["value"]?.GetValue<string>());
        Assert.Equal("a\u00A0b", frontmatter["other"]?.GetValue<string>());
    }

    [Fact]
    public void FlowMappingHonorsSeparatorRules()
    {
        var frontmatter = Parse("""
            a: {version:1}
            b: {version: 1}
            c: {"version":1}
            d: {k, m: 2}
            e: {TRUE: x}
            """);

        AssertJson("""{ "a": { "version:1": null }, "b": { "version": 1 }, "c": { "version": 1 }, "d": { "k": null, "m": 2 }, "e": { "true": "x" } }""", frontmatter);
    }

    [Fact]
    public void RejectsNestingBeyondTheDepthLimit()
    {
        string deep = string.Join("\n", Enumerable.Range(0, 40).Select(i => new string(' ', i) + $"k{i}:")) + " v";

        var exception = Assert.Throws<FormatException>(() => Parse(deep));
        Assert.Contains("nested more than", exception.Message);

        string shallow = string.Join("\n", Enumerable.Range(0, 20).Select(i => new string(' ', i) + $"k{i}:")) + " v";
        Assert.NotNull(Parse(shallow));
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
    [InlineData("-0", "0")]
    [InlineData("-00", "0")]
    [InlineData("-0.0", "0")]
    [InlineData("+0", "0")]
    public void NegativeZeroIsZero(string yaml, string expectedJson)
    {
        // Reference parsers render -0 as 0 (JavaScript has no distinct -0 in JSON; Python's int has none at all).
        Assert.Equal("{\"value\":" + expectedJson + "}", Parse("value: " + yaml).ToJsonString());
        Assert.Equal("{\"" + expectedJson + "\":1}", Parse(yaml + ": 1").ToJsonString());
    }

    [Fact]
    public void TabsSeparateValuesButDoNotIndent()
    {
        var frontmatter = Parse("a:\tone\nb:\n\t\nc: {d:\t2, \"e\"\t: 3}\nitems:\n  -\tx\nblock: |\n  \tindented by a tab\n  \t\ntail: 'a\tb'");

        AssertJson("""{ "a": "one", "b": null, "c": { "d": 2, "e": 3 }, "items": ["x"], "block": "\tindented by a tab\n\t\n", "tail": "a\tb" }""", frontmatter);
    }

    [Fact]
    public void BlockScalarsKeepWhiteSpaceOnLinesWiderThanTheirIndentation()
    {
        var frontmatter = Parse("lit: |\n  a\n    \n  b\n     \nfold: >\n  a\n    \n  b\n");

        AssertJson("""{ "lit": "a\n  \nb\n   \n", "fold": "a\n  \nb\n" }""", frontmatter);
    }

    [Fact]
    public void SequenceItemsMayBeMappingsWithQuotedKeys()
    {
        var frontmatter = Parse("items:\n  - \"k\": v\n    w: 1\n  - 'q': r\n  - \"plain\" # not a key\n");

        AssertJson("""{ "items": [ { "k": "v", "w": 1 }, { "q": "r" }, "plain" ] }""", frontmatter);
    }

    [Fact]
    public void KeysMayStartWithIndicatorsNotFollowedByWhiteSpace()
    {
        var frontmatter = Parse("?a: 1\n-a: 2\n-: 3\n:a: 4");

        AssertJson("""{ "?a": 1, "-a": 2, "-": 3, ":a": 4 }""", frontmatter);
    }

    [Fact]
    public void ACommentEndsAPlainScalarOnAnyOfItsLines()
    {
        var frontmatter = Parse("a: one\n  two # note\nb: 2");

        AssertJson("""{ "a": "one two", "b": 2 }""", frontmatter);
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
    [InlineData("---\nname: [a,\n  b]\n---", "multi-line flow collections")]
    [InlineData("---\nname: {a: 1,\n  b: 2}\n---", "multi-line flow collections")]
    [InlineData("---\nname: [a, b\nother: c\n---", "unterminated flow sequence")]
    [InlineData("---\nname: {a: b\nother: c\n---", "unterminated flow mapping")]
    [InlineData("---\nname: x\n\tother: y\n---", "tabs")]
    [InlineData("---\nitems:\n  -\tk: v\n---", "tabs")]
    [InlineData("---\nvalue: |\n  a\n\t\n  b\n---", "tabs")]
    [InlineData("---\nvalue: a\n  b # comment\n  c\n---", "a comment ended that value")]
    [InlineData("---\nvalue: ]x\n---", "flow indicator")]
    [InlineData("---\nvalue: }x\n---", "flow indicator")]
    [InlineData("---\nvalue: ,x\n---", "flow indicator")]
    [InlineData("---\nvalue: ?\n---", "block indicator")]
    [InlineData("---\nvalue: ? a\n---", "block indicator")]
    [InlineData("---\n&a: x\n---", "anchors")]
    [InlineData("---\n*a: x\n---", "anchors")]
    [InlineData("---\n!t: x\n---", "anchors")]
    [InlineData("---\n@a: x\n---", "reserves")]
    [InlineData("---\n`a: x\n---", "reserves")]
    [InlineData("---\n%a: x\n---", "reserves")]
    [InlineData("---\n]a: x\n---", "flow indicator")]
    [InlineData("---\n,a: x\n---", "flow indicator")]
    [InlineData("---\n|: x\n---", "block scalar header")]
    [InlineData("---\n[a]: x\n---", "flow collections are not supported as mapping keys")]
    [InlineData("---\n{a: 1}: x\n---", "flow collections are not supported as mapping keys")]
    [InlineData("---\nvalue: [,]\n---", "unexpected ','")]
    [InlineData("---\nvalue: [a,,b]\n---", "unexpected ','")]
    [InlineData("---\nvalue: [,a]\n---", "unexpected ','")]
    [InlineData("---\nvalue: {,}\n---", "unexpected ','")]
    [InlineData("---\nvalue: {a: 1,,b: 2}\n---", "unexpected ','")]
    [InlineData("---\nvalue: [- b]\n---", "block indicator")]
    [InlineData("---\nvalue: [}]\n---", "flow indicator")]
    [InlineData("---\nvalue: {]: 1}\n---", "flow indicator")]
    [InlineData("---\nvalue: [\"a\": b]\n---", "compact mappings inside flow sequences")]
    [InlineData("---\nvalue: |\n    \n  a\n---", "leading empty line")]
    [InlineData("---\nvalue: .inf\n---", "cannot be represented")]
    [InlineData("---\nvalue: @handle\n---", "reserves")]
    [InlineData("---\nvalue: `tick\n---", "reserves")]
    [InlineData("---\nvalue: %pct\n---", "reserves")]
    [InlineData("---\nvalue: [@a]\n---", "reserves")]
    [InlineData("---\nvalue: - a\n---", "same line as its key")]
    [InlineData("---\nvalue: |--\n  a\n---", "repeated chomping")]
    [InlineData("---\nvalue: |2-2\n  a\n---", "repeated indentation")]
    [InlineData("---\nvalue: | garbage\n  a\n---", "only a comment may follow")]
    [InlineData("---\nvalue: |x\n  a\n---", "invalid block scalar header")]
    [InlineData("---\nvalue: [a # comment, b]\n---", "comment inside a flow collection")]
    [InlineData("---\nvalue: { a: b # c }\n---", "comment inside a flow collection")]
    [InlineData("---\nvalue: \"\\U0000D800\"\n---", "not a valid Unicode scalar")]
    [InlineData("---\nvalue: \"\\U00110000\"\n---", "not a valid Unicode scalar")]
    [InlineData("---\nvalue: \"\\UFFFFFFFF\"\n---", "not a valid Unicode scalar")]
    [InlineData("---\nvalue: \"\\uD800\"\n---", "unpaired surrogate")]
    [InlineData("---\nvalue: \"\\uDE00x\"\n---", "unpaired surrogate")]
    [InlineData("---\nvalue: [a: b]\n---", "compact mappings inside flow sequences")]
    [InlineData("---\ndescription: first # note\n  second\n---", "a comment ended that value")]
    [InlineData("---\nitems:\n  - first # note\n      second\n---", "a comment ended that value")]
    [InlineData("---\nname: \"never closed\ndescription: d\n---", "unterminated quoted scalar")]
    [InlineData("---\nvalue: {a: b # c}\n---", "comment inside a flow collection")]
    [InlineData("---\nvalue: [a, b: c]\n---", "compact mappings inside flow sequences")]
    [InlineData("---\nvalue: -\n---", "same line as its key")]
    [InlineData("---\nvalue: \"\\q\"\n---", "unsupported escape")]
    [InlineData("---\njust a scalar\n---", "key: value")]
    [InlineData("---\n- item\n---", "must be a YAML mapping")]
    [InlineData("---\nname: a: b\n---", "cannot contain ': '")]
    [InlineData("---\ndescription: text\n  other: mis-indented key\n---", "cannot contain ': '")]
    [InlineData("---\nname: x\n  extra: indented\n---", "cannot contain ': '")]
    public void RejectsUnsupportedOrMalformedInput(string markdown, string messageFragment)
    {
        // Unsupported-but-valid YAML throws a FormatException subclass, so match by assignability.
        var exception = Assert.ThrowsAny<FormatException>(() => SkillFrontmatter.Parse(markdown));

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

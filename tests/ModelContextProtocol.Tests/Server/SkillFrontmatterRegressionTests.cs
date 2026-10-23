using ModelContextProtocol.Extensions.Skills;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

public class SkillFrontmatterRegressionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("{items:[one]}", """{"items":["one"]}""")]
    [InlineData("{items :[one]}", """{"items":["one"]}""")]
    [InlineData("{items:{value: one}}", """{"items":{"value":"one"}}""")]
    [InlineData("{items:[one], other: two}", """{"items":["one"],"other":"two"}""")]
    [InlineData("[key:{value}]", """[{"key":{"value":null}}]""")]
    [InlineData("[key:[one]]", """[{"key":["one"]}]""")]
    [InlineData("[key: {value}]", """[{"key":{"value":null}}]""")]
    [InlineData("[key :[one], other]", """[{"key":["one"]},"other"]""")]
    public void NestedFlowValuesAreRejectedInsteadOfPublishedAsPlainKeys(string yaml, string expectedJson)
    {
        var markdown = $"---\nname: alpha\ndescription: d\nextra: {yaml}\n---\n";
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };
        var expected = new JsonObject
        {
            ["name"] = "alpha",
            ["description"] = "d",
            ["extra"] = JsonNode.Parse(expectedJson),
        };

        var error = Record.Exception(() => SkillFrontmatter.Parse(markdown));
        Assert.Equal("UnsupportedYamlException", error?.GetType().Name);
        Assert.Contains("not supported", error!.Message);
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(files));
        var supplied = McpServerSkill.Create("skill://alpha/SKILL.md", expected, files);
        Assert.True(JsonNode.DeepEquals(expected, supplied.ProtocolSkill.Frontmatter));
    }

    [Theory]
    [InlineData("[word{value}]")]
    [InlineData("[word}value]")]
    [InlineData("{word[value]: one}")]
    public void FlowIndicatorsCannotBecomeUnquotedScalarContent(string yaml)
    {
        var markdown = $"---\nname: alpha\ndescription: d\nextra: {yaml}\n---\n";
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };
        var supplied = new JsonObject { ["name"] = "alpha", ["description"] = "d", ["extra"] = yaml };

        Assert.Throws<FormatException>(() => SkillFrontmatter.Parse(markdown));
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(files));
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create("skill://alpha/SKILL.md", supplied, files));
    }

    [Theory]
    [InlineData("[https://example.test/path, key:value]", """["https://example.test/path","key:value"]""")]
    [InlineData("[\"word{value}\", 'key:[one]']", """["word{value}","key:[one]"]""")]
    [InlineData("{\"word[value]\": one}", """{"word[value]":"one"}""")]
    [InlineData("{version:1}", """{"version:1":null}""")]
    public void PlainColonsAndQuotedFlowIndicatorsRemainSupported(string yaml, string expectedJson)
    {
        var expected = new JsonObject
        {
            ["name"] = "alpha",
            ["description"] = "d",
            ["extra"] = JsonNode.Parse(expectedJson),
        };
        AssertParsedAndPublished($"---\nname: alpha\ndescription: d\nextra: {yaml}\n---\n", expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(21)]
    public void OctalConversionIsExactWithin64Bits(int digits)
    {
        var parsed = SkillFrontmatter.Parse($"---\nvalue: 0o{new string('7', digits)}\n---\n");

        var expected = (BigInteger.One << (3 * digits)) - BigInteger.One;
        Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), parsed["value"]!.ToJsonString());
    }

    [Theory]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(40_000)]
    [InlineData(80_000)]
    [InlineData(160_000)]
    public void OctalIntegersWiderThan64Bits_AreUnsupported(int digits)
    {
        var markdown = $"---\nvalue: 0o{new string('7', digits)}\n---\n";
        var stopwatch = Stopwatch.StartNew();
        var error = Record.Exception(() => SkillFrontmatter.Parse(markdown));
        stopwatch.Stop();

        Assert.Equal("UnsupportedYamlException", error?.GetType().Name);
        Assert.Contains("wider than 64 bits", error!.Message);
        output.WriteLine($"OCTAL digits={digits}; elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F1}");
    }

    [Fact]
    public void OctalConversionPreservesMixedDigitsAndLeadingZeros()
    {
        var random = new Random(2640);
        for (int length = 1; length <= 128; length++)
        {
            var digits = new char[length + 1];
            digits[0] = '0';
            var expected = BigInteger.Zero;
            for (int index = 1; index < digits.Length; index++)
            {
                int digit = random.Next(8);
                digits[index] = (char)('0' + digit);
                expected = (expected * 8) + digit;
            }

            var markdown = $"---\nvalue: 0o{new string(digits)}\n---\n";
            if (expected <= ulong.MaxValue)
            {
                var parsed = SkillFrontmatter.Parse(markdown);
                Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), parsed["value"]!.ToJsonString());
            }
            else
            {
                var error = Record.Exception(() => SkillFrontmatter.Parse(markdown));
                Assert.Equal("UnsupportedYamlException", error?.GetType().Name);
            }
        }
    }

    [Theory]
    [InlineData("0xffffffffffffffff")]
    [InlineData("0xFFFFFFFFFFFFFFFF")]
    [InlineData("0o1777777777777777777777")]
    public void HexAndOctalIntegersAtTheUnsigned64BitMaximum_KeepTheirExactValue(string literal)
    {
        var expected = new JsonObject
        {
            ["name"] = "alpha",
            ["description"] = "d",
            ["value"] = JsonNode.Parse("18446744073709551615"),
        };

        AssertParsedAndPublished($"---\nname: alpha\ndescription: d\nvalue: {literal}\n---\n", expected);
        Assert.Equal("18446744073709551615", ParseValue(literal));
    }

    [Fact]
    public void LeadingZeros_DoNotCountTowardThe64BitLimit()
    {
        var zeros = new string('0', 10_000);

        Assert.Equal("1", ParseValue("0x" + zeros + "1"));
        Assert.Equal("7", ParseValue("0o" + zeros + "7"));
        Assert.Equal("18446744073709551615", ParseValue("0x" + zeros + "ffffffffffffffff"));
        Assert.Equal("18446744073709551615", ParseValue("0o" + zeros + "1777777777777777777777"));
    }

    [Theory]
    [InlineData("0x10000000000000000")]
    [InlineData("0x1ffffffffffffffff")]
    [InlineData("0o2000000000000000000000")]
    [InlineData("0o777777777777777777777777")]
    public void HexAndOctalIntegersWiderThan64Bits_AreUnsupportedForEveryAuthoringPath(string literal)
    {
        AssertWideIntegerIsUnsupported(literal);
    }

    [Theory]
    [InlineData("0x", 'f')]
    [InlineData("0o", '7')]
    public void HexAndOctalIntegersWith200000Digits_AreUnsupportedWithoutEchoingTheLiteral(string prefix, char digit)
    {
        var literal = prefix + new string(digit, 200_000);
        var stopwatch = Stopwatch.StartNew();
        var error = AssertWideIntegerIsUnsupported(literal);
        stopwatch.Stop();

        Assert.DoesNotContain(literal, error.Message);
        Assert.True(error.Message.Length < 400, $"The message has {error.Message.Length} characters.");
        output.WriteLine($"{prefix} digits=200000; elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F1}");
    }

    [Theory]
    [InlineData("0x10000000000000000")]
    [InlineData("0o2000000000000000000000")]
    public void WideIntegerKeys_AreUnsupportedLikeValues(string literal)
    {
        var blockKey = Record.Exception(() => SkillFrontmatter.Parse($"---\n{literal}: a\n---\n"));
        var flowKey = Record.Exception(() => SkillFrontmatter.Parse($"---\nvalue: {{{literal}: a}}\n---\n"));

        Assert.Equal("UnsupportedYamlException", blockKey?.GetType().Name);
        Assert.Equal("UnsupportedYamlException", flowKey?.GetType().Name);
        Assert.Equal(["18446744073709551615"], SkillFrontmatter.Parse("---\n0xffffffffffffffff: a\n---\n").Select(p => p.Key));
    }

    [Theory]
    [InlineData("description:\n  Builds reusable components with isolated tables.\n  Use for new components.",
        "Builds reusable components with isolated tables. Use for new components.")]
    [InlineData("description:\n  One line on the next line.", "One line on the next line.")]
    [InlineData("description:\n  \"Quoted on the next line\"", "Quoted on the next line")]
    [InlineData("description:\n  'Quoted on the next line'", "Quoted on the next line")]
    [InlineData("description: # note\n  Text on the next line", "Text on the next line")]
    [InlineData("description:\n  # note\n\n  Text on the next line", "Text on the next line")]
    [InlineData("description:\n    First line\n  continued with less indentation", "First line continued with less indentation")]
    public void NextLineScalar_IsParsedAndPublishedWithoutChangingItsValue(string body, string expectedDescription)
    {
        var markdown = "---\nname: alpha\n" + body + "\n---\n";
        var expected = new JsonObject { ["name"] = "alpha", ["description"] = expectedDescription };

        AssertParsedAndPublished(markdown, expected);
    }

    [Theory]
    [InlineData("tags:\n  -\n    text on next line", """{"tags":["text on next line"]}""")]
    [InlineData("tags:\n  - # note\n    \"text on next line\"", """{"tags":["text on next line"]}""")]
    [InlineData("metadata:\n  note:\n    first\n    second", """{"metadata":{"note":"first second"}}""")]
    public void NestedNextLineScalar_IsParsedAndPublished(string body, string expectedFields)
    {
        var markdown = "---\nname: alpha\ndescription: d\n" + body + "\n---\n\n# Body\n";
        var expected = JsonNode.Parse(expectedFields)!.AsObject();
        expected["name"] = "alpha";
        expected["description"] = "d";

        AssertParsedAndPublished(markdown, expected);
    }

    [Theory]
    [InlineData(">", "  First\n  \tCode\n  Last\n", "First\n\tCode\nLast\n")]
    [InlineData(">-", "  First\n  \tCode\n  Last\n", "First\n\tCode\nLast")]
    [InlineData(">+", "  First\n  \tCode\n  Last\n\n", "First\n\tCode\nLast\n\n")]
    [InlineData(">", "  First\n\n  \tCode\n\n  Last\n", "First\n\n\tCode\n\nLast\n")]
    [InlineData(">2", "  First\n  \tCode\n  Last\n", "First\n\tCode\nLast\n")]
    public void FoldedScalar_PreservesLineBreaksAroundTabLedContent(string header, string content, string expectedDescription)
    {
        var markdown = "---\nname: comparison\ndescription: " + header + "\n" + content + "---\n";
        var expected = new JsonObject { ["name"] = "comparison", ["description"] = expectedDescription };

        AssertParsedAndPublished(markdown, expected);
    }

    [Theory]
    [InlineData("|+", "Kept\n\n")]
    [InlineData("|", "Kept\n")]
    [InlineData("|-", "Kept")]
    public void BlockScalarAtClosingFence_PreservesAuthoredLineBreaks(string header, string expectedDescription)
    {
        var markdown = "---\nname: alpha\ndescription: " + header + "\n  Kept\n\n---\n";
        var expected = new JsonObject { ["name"] = "alpha", ["description"] = expectedDescription };

        AssertParsedAndPublished(markdown, expected);
    }

    [Theory]
    [InlineData("\"Useful\"#comment", "{version: '1'}", "[read]")]
    [InlineData("'Useful'#comment", "{version: '1'}", "[read]")]
    [InlineData("Useful", "{version: '1'}#comment", "[read]")]
    [InlineData("Useful", "{version: '1'}", "[read]#comment")]
    public void UnseparatedTrailingComment_IsRejectedByParserAndBothAuthoringOverloads(
        string description, string metadata, string tags)
    {
        var markdown = $"---\nname: comparison\ndescription: {description}\nmetadata: {metadata}\ntags: {tags}\n---\n";
        var expected = new JsonObject
        {
            ["name"] = "comparison",
            ["description"] = "Useful",
            ["metadata"] = new JsonObject { ["version"] = "1" },
            ["tags"] = new JsonArray("read"),
        };
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };

        var parseException = Record.Exception(() => SkillFrontmatter.Parse(markdown));
        var automaticException = Record.Exception(() => McpServerSkill.Create(files));
        var explicitException = Record.Exception(() => McpServerSkill.Create("skill://comparison/SKILL.md", expected, files));

        Assert.IsType<FormatException>(parseException);
        Assert.IsType<ArgumentException>(automaticException);
        Assert.IsType<ArgumentException>(explicitException);
    }

    [Theory]
    [InlineData("Useful#hash", "{version: '1'}", "[read]", "Useful#hash")]
    [InlineData("\"Useful\" #comment", "{version: '1'}", "[read]", "Useful")]
    [InlineData("'Useful'\t#comment", "{version: '1'}", "[read]", "Useful")]
    [InlineData("Useful", "{version: '1'} #comment", "[read]", "Useful")]
    [InlineData("Useful", "{version: '1'}\t#comment", "[read]", "Useful")]
    [InlineData("Useful", "{version: '1'}", "[read] #comment", "Useful")]
    [InlineData("Useful", "{version: '1'}", "[read]\t#comment", "Useful")]
    public void SeparatedCommentsAndPlainHashes_RemainValid(
        string description, string metadata, string tags, string expectedDescription)
    {
        var markdown = $"---\nname: comparison\ndescription: {description}\nmetadata: {metadata}\ntags: {tags}\n---\n";
        var expected = new JsonObject
        {
            ["name"] = "comparison",
            ["description"] = expectedDescription,
            ["metadata"] = new JsonObject { ["version"] = "1" },
            ["tags"] = new JsonArray("read"),
        };

        AssertParsedAndPublished(markdown, expected);
    }

    [Theory]
    [InlineData(".inf")]
    [InlineData("-.Inf")]
    [InlineData(".nan")]
    public void NonFiniteScalar_CannotBeReplacedWithJsonNullThroughExplicitFrontmatter(string value)
    {
        var markdown = $"---\nname: alpha\ndescription: d\nextra: {value}\n---\n";
        var supplied = new JsonObject { ["name"] = "alpha", ["description"] = "d", ["extra"] = null };
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };

        var exception = Assert.Throws<FormatException>(() => SkillFrontmatter.Parse(markdown));
        Assert.Contains("cannot be represented in JSON", exception.Message);
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create(files));
        Assert.Throws<ArgumentException>(() => McpServerSkill.Create("skill://alpha/SKILL.md", supplied, files));
    }

    [Theory]
    [InlineData("description: x{0}y")]
    [InlineData("description: \"x{0}y\"")]
    [InlineData("description: 'x{0}y'")]
    [InlineData("description: |\n  x{0}y")]
    [InlineData("description: d\n# note{0}")]
    [InlineData("description: d # note{0}")]
    [InlineData("description: d\nex{0}tra: e")]
    public void LiteralNul_IsRejectedByTheReaderAndBothAuthoringOverloads(string entry)
    {
        AssertRawCharacterIsRejected(string.Format(entry, '\0'), 0x0000);
    }

    [Theory]
    [InlineData(0x0001)]
    [InlineData(0x0008)]
    [InlineData(0x000B)]
    [InlineData(0x000C)]
    [InlineData(0x001B)]
    [InlineData(0x001F)]
    [InlineData(0x007F)]
    [InlineData(0x0080)]
    [InlineData(0x009F)]
    [InlineData(0xFFFE)]
    [InlineData(0xFFFF)]
    public void RawCharacterOutsideYamlsPrintableSet_IsRejected(int codePoint)
    {
        AssertRawCharacterIsRejected("description: x" + char.ConvertFromUtf32(codePoint) + "y", codePoint);
    }

    [Theory]
    [InlineData("\"", 0x007F)]
    [InlineData("'", 0x0080)]
    [InlineData("\"", 0xFFFE)]
    public void RawCharacterOutsideYamlsPrintableSet_IsRejectedInsideQuotesToo(string quote, int codePoint)
    {
        AssertRawCharacterIsRejected("description: " + quote + "x" + char.ConvertFromUtf32(codePoint) + "y" + quote, codePoint);
    }

    [Theory]
    [InlineData("description: d # note{0}allowed-tools: Bash", 0x000D)]
    [InlineData("description: d\n# note{0}allowed-tools: Bash", 0x2028)]
    [InlineData("description: d # note{0}allowed-tools: Bash", 0x0085)]
    [InlineData("description: d # note{0}allowed-tools: Bash", 0x2029)]
    [InlineData("description: x{0}y", 0x0085)]
    [InlineData("description: d{0}{0}", 0x000D)]
    public void RawCharacterSomeParsersReadAsALineBreak_IsRejected(string entry, int codePoint)
    {
        AssertRawCharacterIsRejected(string.Format(entry, (char)codePoint), codePoint);
    }

    [Fact]
    public void RawCharacter_IsRejectedEvenAfterAnUnsupportedConstruct()
    {
        var markdown = "---\nname: alpha\nx: &a 1\ndescription: x\0y\n---\n";
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };
        var supplied = new JsonObject { ["name"] = "alpha", ["x"] = 1, ["description"] = "x\0y" };

        var exception = Assert.Throws<ArgumentException>(() => McpServerSkill.Create("skill://alpha/SKILL.md", supplied, files));

        Assert.Contains("Line 4:", exception.Message);
        Assert.Contains("U+0000", exception.Message);
    }

    [Theory]
    [InlineData(new[] { 0xD800 }, false, 0xD800)]
    [InlineData(new[] { 0xD800 }, true, 0xD800)]
    [InlineData(new[] { 0xDC00 }, false, 0xDC00)]
    [InlineData(new[] { 0xDC00, 0xD800 }, false, 0xDC00)]
    public void UnpairedSurrogate_IsRejectedByTheReader(int[] codeUnits, bool atLineEnd, int expectedCodeUnit)
    {
        var markdown = "---\nname: alpha\ndescription: x" + Text(codeUnits) + (atLineEnd ? "" : "y") + "\n---\n";

        var exception = Assert.Throws<FormatException>(() => SkillFrontmatter.Parse(markdown));

        Assert.Contains($"U+{expectedCodeUnit:X4}", exception.Message);
    }

    [Theory]
    [InlineData(0x0009)]
    [InlineData(0x00A0)]
    [InlineData(0xE000)]
    [InlineData(0xFFFD)]
    [InlineData(0x1F916)]
    public void RawCharacterInYamlsPrintableSet_IsKept(int codePoint)
    {
        string character = char.ConvertFromUtf32(codePoint);
        var expected = new JsonObject { ["name"] = "alpha", ["description"] = "x" + character + "y" };

        AssertParsedAndPublished("---\nname: alpha\ndescription: x" + character + "y\n---\n", expected);
    }

    [Theory]
    [InlineData("\\0", 0x0000)]
    [InlineData("\\x1B", 0x001B)]
    [InlineData("\\x7F", 0x007F)]
    [InlineData("\\uFFFE", 0xFFFE)]
    public void EscapedCharacterOutsideYamlsPrintableSet_IsKept(string escape, int codePoint)
    {
        var expected = new JsonObject { ["name"] = "alpha", ["description"] = "x" + char.ConvertFromUtf32(codePoint) + "y" };

        AssertParsedAndPublished("---\nname: alpha\ndescription: \"x" + escape + "y\"\n---\n", expected);
    }

    [Theory]
    [InlineData("\\r", 0x000D)]
    [InlineData("\\N", 0x0085)]
    [InlineData("\\L", 0x2028)]
    [InlineData("\\P", 0x2029)]
    public void EscapedCharacterSomeParsersReadAsALineBreak_IsKept(string escape, int codePoint)
    {
        var expected = new JsonObject { ["name"] = "alpha", ["description"] = "x" + char.ConvertFromUtf32(codePoint) + "y" };

        AssertParsedAndPublished("---\nname: alpha\ndescription: \"x" + escape + "y\"\n---\n", expected);
    }

    private void AssertParsedAndPublished(string markdown, JsonObject expected)
    {
        var parsed = SkillFrontmatter.Parse(markdown);
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };
        var automatic = McpServerSkill.Create(files);
        var explicitMetadata = McpServerSkill.Create(automatic.ProtocolSkill.Uri, expected, files);

        Assert.True(JsonNode.DeepEquals(expected, parsed), $"Expected {expected}, parsed {parsed}.");
        Assert.True(JsonNode.DeepEquals(expected, automatic.ProtocolSkill.Frontmatter));
        Assert.True(JsonNode.DeepEquals(expected, explicitMetadata.ProtocolSkill.Frontmatter));
    }

    private string ParseValue(string literal) =>
        SkillFrontmatter.Parse($"---\nvalue: {literal}\n---\n")["value"]!.ToJsonString();

    /// <summary>
    /// Asserts that the reader treats a raw character it does not allow as malformed, not unsupported, so automatic
    /// authoring and explicit frontmatter both reject the file, each naming the character.
    /// </summary>
    private void AssertRawCharacterIsRejected(string entry, int codePoint)
    {
        var markdown = $"---\nname: alpha\n{entry}\n---\n";
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };
        var supplied = new JsonObject { ["name"] = "alpha", ["description"] = "d" };

        var parseException = Record.Exception(() => SkillFrontmatter.Parse(markdown));
        var automaticException = Record.Exception(() => McpServerSkill.Create(files));
        var explicitException = Record.Exception(() => McpServerSkill.Create("skill://alpha/SKILL.md", supplied, files));

        string character = $"U+{codePoint:X4}";
        Assert.Contains(character, Assert.IsType<FormatException>(parseException).Message);
        Assert.Contains(character, Assert.IsType<ArgumentException>(automaticException).Message);
        Assert.Contains(character, Assert.IsType<ArgumentException>(explicitException).Message);
    }

    private string Text(int[] codeUnits) => new(codeUnits.Select(codeUnit => (char)codeUnit).ToArray());

    /// <summary>
    /// Asserts the unsupported classification for each authoring path: the reader throws its FormatException-derived
    /// unsupported exception, automatic authoring rejects the file, and explicit frontmatter is accepted.
    /// </summary>
    private Exception AssertWideIntegerIsUnsupported(string literal)
    {
        var markdown = $"---\nname: alpha\ndescription: d\nvalue: {literal}\n---\n";
        var files = new[] { McpServerSkillFile.FromText("SKILL.md", markdown) };
        var supplied = new JsonObject { ["name"] = "alpha", ["description"] = "d", ["value"] = literal };

        var parseException = Record.Exception(() => SkillFrontmatter.Parse(markdown));
        var automaticException = Assert.Throws<ArgumentException>(() => McpServerSkill.Create(files));
        var skill = McpServerSkill.Create("skill://alpha/SKILL.md", supplied, files);

        Assert.Equal("UnsupportedYamlException", parseException?.GetType().Name);
        Assert.IsAssignableFrom<FormatException>(parseException);
        Assert.Contains("Line 4:", parseException!.Message);
        Assert.Contains("wider than 64 bits", parseException.Message);
        Assert.Contains("full YAML parser", parseException.Message);
        Assert.Contains("does not support", automaticException.Message);
        Assert.Contains("wider than 64 bits", automaticException.Message);
        Assert.True(JsonNode.DeepEquals(supplied, skill.ProtocolSkill.Frontmatter));
        return parseException;
    }
}

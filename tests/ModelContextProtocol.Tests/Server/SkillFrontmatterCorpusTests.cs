using ModelContextProtocol.Extensions.Skills;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Differential corpus for <see cref="SkillFrontmatter"/>. Each expected value was produced by the <c>yaml</c> npm
/// package (2.8.x) parsing the same text with its core schema, which is the resolution the YAML libraries used by
/// other MCP SDKs and hosts apply. A host verifies a skill by parsing the fetched <c>SKILL.md</c> with such a
/// library and comparing it field by field against the published entry, so agreement on these cases is the
/// property that makes entries produced by this reader verifiable.
/// </summary>
public class SkillFrontmatterCorpusTests
{
    [Theory]
        [InlineData("name: pdf-processing\ndescription: Extract, fill, and assemble PDF documents\nlicense: Apache-2.0\nmetadata:\n  author: example-org\n  version: \"1.0\"\nallowed-tools: Bash(git:*) Read", "{\"name\": \"pdf-processing\", \"description\": \"Extract, fill, and assemble PDF documents\", \"license\": \"Apache-2.0\", \"metadata\": {\"author\": \"example-org\", \"version\": \"1.0\"}, \"allowed-tools\": \"Bash(git:*) Read\"}")]
        [InlineData("description: Follow the team's workflow # author note", "{\"description\": \"Follow the team's workflow\"}")]
        [InlineData("description: She said \"go\" # and left", "{\"description\": \"She said \\\"go\\\"\"}")]
        [InlineData("description: has#hash inside # but this is a comment", "{\"description\": \"has#hash inside\"}")]
        [InlineData("tags: [\"a # b\", 'c # d', e] # trailing", "{\"tags\": [\"a # b\", \"c # d\", \"e\"]}")]
        [InlineData("map: { k: \"v # w\", n: 2 } # trailing", "{\"map\": {\"k\": \"v # w\", \"n\": 2}}")]
        [InlineData("value: >\n\n  Text", "{\"value\": \"\\nText\\n\"}")]
        [InlineData("value: |\n\n  Text", "{\"value\": \"\\nText\\n\"}")]
        [InlineData("value: >\n  Folded text\n  on two lines.\n\n  New paragraph.", "{\"value\": \"Folded text on two lines.\\nNew paragraph.\\n\"}")]
        [InlineData("value: |\n  Line one.\n  Line two.\n\n    Indented line.\nnext: 1", "{\"value\": \"Line one.\\nLine two.\\n\\n  Indented line.\\n\", \"next\": 1}")]
        [InlineData("value: >\n  para one\n    more indented\n  back", "{\"value\": \"para one\\n  more indented\\nback\\n\"}")]
        [InlineData("value: |-\n  a\n  b\n\nnext: 1", "{\"value\": \"a\\nb\", \"next\": 1}")]
        [InlineData("value: |+\n  a\n  b\n\nnext: 1", "{\"value\": \"a\\nb\\n\\n\", \"next\": 1}")]
        [InlineData("value: >-\n  a\n  b", "{\"value\": \"a b\"}")]
        [InlineData("value: |2\n    two extra\n   one extra", "{\"value\": \"  two extra\\n one extra\\n\"}")]
        [InlineData("description: This description\n  continues on the next line\n  and the one after.\nname: demo", "{\"description\": \"This description continues on the next line and the one after.\", \"name\": \"demo\"}")]
        [InlineData("description: first line\n\n  after blank\nname: demo", "{\"description\": \"first line\\nafter blank\", \"name\": \"demo\"}")]
        [InlineData("a: 0x1F\nb: 0o17\nc: 0xffffffffffffffff\nd: +0x10\ne: -0o10\nf: 007\ng: +42\nh: -7\ni: 1.5\nj: .5\nk: 1e3\nl: -2.5E-1\nm: 1.0.0\nn: 2026-09-09\no: 1_000\np: yes\nq: on\nr: ~\ns: null\nt: Null\nu: true\nv: FALSE\nw:\nx: \"\"\ny: ''\nz: 12345678901234567890123", "{\"a\": 31, \"b\": 15, \"c\": 18446744073709551615, \"d\": \"+0x10\", \"e\": \"-0o10\", \"f\": 7, \"g\": 42, \"h\": -7, \"i\": 1.5, \"j\": 0.5, \"k\": 1000, \"l\": -0.25, \"m\": \"1.0.0\", \"n\": \"2026-09-09\", \"o\": \"1_000\", \"p\": \"yes\", \"q\": \"on\", \"r\": null, \"s\": null, \"t\": null, \"u\": true, \"v\": false, \"w\": null, \"x\": \"\", \"y\": \"\", \"z\": 12345678901234567890123}")]
        [InlineData("a: 1.\nb: +.5\nc: 1e\nd: .\nf: 0b101\ng: 1e+3\nh: 5.e2", "{\"a\": 1, \"b\": 0.5, \"c\": \"1e\", \"d\": \".\", \"f\": \"0b101\", \"g\": 1000, \"h\": 500}")]
        [InlineData("e: -a", "{\"e\": \"-a\"}")]
        [InlineData("name: 'it''s'\nq: \"tab\\there\"\nn: \"new\\nline\"\ne: \"é\\x41\"\ns: 'key: value'\nu: \"\\/slash\"", "{\"name\": \"it's\", \"q\": \"tab\\there\", \"n\": \"new\\nline\", \"e\": \"éA\", \"s\": \"key: value\", \"u\": \"/slash\"}")]
        [InlineData("tags:\n  - one\n  - \"two\"\n  - 3\nsame-indent:\n- a\n- b\nobjects:\n  - name: x\n    value: 1\n  - name: y\nnested:\n  - - a\n    - b\n  - - c", "{\"tags\": [\"one\", \"two\", 3], \"same-indent\": [\"a\", \"b\"], \"objects\": [{\"name\": \"x\", \"value\": 1}, {\"name\": \"y\"}], \"nested\": [[\"a\", \"b\"], [\"c\"]]}")]
        [InlineData("a:\n  b:\n    c:\n      d: deep\n  e: 1\nf: 2", "{\"a\": {\"b\": {\"c\": {\"d\": \"deep\"}}, \"e\": 1}, \"f\": 2}")]
        [InlineData("empty: []\nemptymap: {}\nflow: [a, b c, 'd', 4, true, null]\nfm: { k: v, n: 2, q: 'x y' }", "{\"empty\": [], \"emptymap\": {}, \"flow\": [\"a\", \"b c\", \"d\", 4, true, null], \"fm\": {\"k\": \"v\", \"n\": 2, \"q\": \"x y\"}}")]
        [InlineData("\"quoted key\": 1\n'single key': 2\nkey with spaces: 3", "{\"quoted key\": 1, \"single key\": 2, \"key with spaces\": 3}")]
        [InlineData("list:\n- a\n\n- b", "{\"list\": [\"a\", \"b\"]}")]
        [InlineData("key:\n  # comment only\n  sub: 1", "{\"key\": {\"sub\": 1}}")]
        [InlineData("key: value\n  # indented comment\nother: 2", "{\"key\": \"value\", \"other\": 2}")]
        [InlineData("seq:\n  - a\n  -\n  - c", "{\"seq\": [\"a\", null, \"c\"]}")]
        [InlineData("seq:\n  -   spaced: 1\n      other: 2\n  - plain", "{\"seq\": [{\"spaced\": 1, \"other\": 2}, \"plain\"]}")]
        [InlineData("key:    value with leading spaces   ", "{\"key\": \"value with leading spaces\"}")]
        [InlineData("description: >\n  Line with trailing spaces   \n  next", "{\"description\": \"Line with trailing spaces    next\\n\"}")]
        [InlineData("metadata:\n  version: 2.1\n  major: 2\n  flag: true\n  ratio: 0.75\n", "{\"metadata\": {\"version\": 2.1, \"major\": 2, \"flag\": true, \"ratio\": 0.75}}")]
        [InlineData("value: |+\n  a\n\n\nnext: 1", "{\"value\": \"a\\n\\n\\n\", \"next\": 1}")]
        [InlineData("value: >\n  normal\n    indented one\n    indented two\n  normal again", "{\"value\": \"normal\\n  indented one\\n  indented two\\nnormal again\\n\"}")]
        [InlineData("value: |2\n\n  leading blank then text", "{\"value\": \"\\nleading blank then text\\n\"}")]
        [InlineData("value: |\nnext: 1", "{\"value\": \"\", \"next\": 1}")]
        [InlineData("value: >-\nnext: 1", "{\"value\": \"\", \"next\": 1}")]
        [InlineData("\"key: colon\": 1", "{\"key: colon\": 1}")]
        [InlineData("value: a,b", "{\"value\": \"a,b\"}")]
        [InlineData("value: [a, b, ]", "{\"value\": [\"a\", \"b\"]}")]
        [InlineData("value: { a: 1, }", "{\"value\": {\"a\": 1}}")]
        [InlineData("seq:\n- a\n- # comment item\n- c", "{\"seq\": [\"a\", null, \"c\"]}")]
        [InlineData("seq:\n  - key: v\n    # comment\n    other: w", "{\"seq\": [{\"key\": \"v\", \"other\": \"w\"}]}")]
        [InlineData("value:     \nnext: 1", "{\"value\": null, \"next\": 1}")]
        [InlineData("value: \"smart “quotes” inside\"", "{\"value\": \"smart “quotes” inside\"}")]
        [InlineData("value: -1", "{\"value\": -1}")]
        [InlineData("value: 1 2", "{\"value\": \"1 2\"}")]
        [InlineData("value: 1,000", "{\"value\": \"1,000\"}")]
        [InlineData("value: 0.", "{\"value\": 0}")]
        [InlineData("value: 00", "{\"value\": 0}")]
        [InlineData("value: 08", "{\"value\": 8}")]
        [InlineData("value: -0", "{\"value\": 0}")]
        [InlineData("value: 3.14e-2", "{\"value\": 0.0314}")]
        [InlineData("value: 1E5", "{\"value\": 100000}")]
        [InlineData("value: TRUE", "{\"value\": true}")]
        [InlineData("value: NULL", "{\"value\": null}")]
        [InlineData("value: True dat", "{\"value\": \"True dat\"}")]
        [InlineData("value: null value", "{\"value\": \"null value\"}")]
        [InlineData("value:   spaced   ", "{\"value\": \"spaced\"}")]
        [InlineData("value: a:b", "{\"value\": \"a:b\"}")]
        [InlineData("value: \"esc \\\\ back\"", "{\"value\": \"esc \\\\ back\"}")]
        [InlineData("value: '#not a comment'", "{\"value\": \"#not a comment\"}")]
        [InlineData("value: x#y", "{\"value\": \"x#y\"}")]
        [InlineData("value: x #y", "{\"value\": \"x\"}")]
        [InlineData("value: x\t#tab\n", "{\"value\": \"x\"}")]
    public void MatchesReferenceParser(string frontmatterBody, string expectedJson)
    {
        var actual = SkillFrontmatter.Parse("---\n" + frontmatterBody + "\n---\n");

        var expected = JsonNode.Parse(expectedJson);
        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"Expected {expected?.ToJsonString()} but got {actual.ToJsonString()}.");
    }
}

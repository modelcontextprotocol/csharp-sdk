using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

/// <summary>
/// Reads the YAML frontmatter of a fetched <c>SKILL.md</c> into JSON with YamlDotNet, a full YAML parser, so that the
/// host can compare it with the frontmatter of the entry it holds.
/// </summary>
/// <remarks>
/// <para>
/// Plain scalars are typed per the YAML 1.2 core schema (<c>null</c>, booleans, integers, finite floats, otherwise
/// strings), the typing the server used for the entry. Frontmatter that JSON cannot represent, such as <c>.inf</c> or
/// a key that is a collection, is rejected, and so is a tag this sample does not convert, on a scalar or a collection.
/// </para>
/// <para>
/// The same server supplies the file and its manifest, so the content is untrusted. Before parsing, the reader rejects
/// the raw characters the server's reader rejects: one outside YAML's printable set, and a lone carriage return, NEL,
/// LS or PS. It works from YamlDotNet's parsing events rather than its recursive document model, and rejects
/// frontmatter nested more than 32 levels deep, frontmatter that aliases expand to more than 10,000 nodes, and an
/// alias inside the node it refers to.
/// </para>
/// </remarks>
internal sealed class YamlFrontmatterReader
{
    private const string StringTag = "tag:yaml.org,2002:str";
    private const string SequenceTag = "tag:yaml.org,2002:seq";
    private const string MappingTag = "tag:yaml.org,2002:map";
    private const string NotAMappingMessage = "The frontmatter must be a YAML mapping of keys to values.";

    /// <summary>The deepest nesting of collections read, the same limit as the SDK's frontmatter reader.</summary>
    private const int MaxDepth = 32;

    /// <summary>The most nodes read, counting each alias as the nodes it expands to.</summary>
    private const int MaxNodes = 10_000;

    private readonly Regex _decimalInteger = new("^[-+]?[0-9]+$", RegexOptions.CultureInvariant);
    private readonly Regex _hexInteger = new("^0x[0-9a-fA-F]+$", RegexOptions.CultureInvariant);
    private readonly Regex _octalInteger = new("^0o[0-7]+$", RegexOptions.CultureInvariant);
    private readonly Regex _float = new(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$", RegexOptions.CultureInvariant);
    private readonly Regex _infinityOrNaN = new(@"^([-+]?\.(inf|Inf|INF)|\.(nan|NaN|NAN))$", RegexOptions.CultureInvariant);

    /// <summary>The latest node defined for each anchor name; an alias refers to it.</summary>
    private readonly Dictionary<string, AnchoredNode> _anchors = new(StringComparer.Ordinal);

    private int _nodeCount;

    /// <summary>Reads the frontmatter at the start of <paramref name="skillMarkdown"/>.</summary>
    /// <exception cref="YamlException">The frontmatter is not valid YAML.</exception>
    /// <exception cref="FormatException">
    /// The frontmatter is missing, has a raw character the server's reader also rejects, is not a mapping, has no JSON
    /// form, is beyond this reader's limits, or cannot be read by YamlDotNet.
    /// </exception>
    public JsonObject Read(string skillMarkdown)
    {
        _anchors.Clear();
        _nodeCount = 0;

        string frontmatter = ExtractFrontmatter(skillMarkdown);
        ThrowIfDisallowedCharacter(frontmatter);
        try
        {
            return ReadDocument(new Parser(new StringReader(frontmatter)));
        }
        catch (InvalidOperationException e)
        {
            // YamlDotNet's scanner reports some input this way rather than with a YamlException, including some valid
            // YAML, such as a comment inside a multi-line flow collection.
            throw new FormatException("YamlDotNet could not read the frontmatter.", e);
        }
    }

    private JsonObject ReadDocument(IParser parser)
    {
        parser.Consume<StreamStart>();
        if (!parser.TryConsume<DocumentStart>(out _) ||
            ReadNode(parser, depth: 0).Node is not JsonObject root)
        {
            throw new FormatException(NotAMappingMessage);
        }

        parser.Consume<DocumentEnd>();
        parser.Consume<StreamEnd>();
        return root;
    }

    /// <summary>
    /// Returns the lines between the opening <c>---</c> line and the next <c>---</c> or <c>...</c> line, each with its
    /// line break. Keeping the last one matters: a final block scalar that keeps its trailing line breaks (<c>|+</c>)
    /// reads the same as on the server.
    /// </summary>
    private string ExtractFrontmatter(string skillMarkdown)
    {
        // Like the server, drop one byte order mark; a second one is content, so the '---' check below fails.
        string text = skillMarkdown.StartsWith('\uFEFF') ? skillMarkdown.Substring(1) : skillMarkdown;
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines[0].TrimEnd(' ', '\t') != "---")
        {
            throw new FormatException("SKILL.md must begin with a '---' line that opens the YAML frontmatter.");
        }

        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd(' ', '\t') is "---" or "...")
            {
                return string.Join("\n", lines, 1, i - 1) + "\n";
            }
        }

        throw new FormatException("The YAML frontmatter of SKILL.md is not closed by a '---' line.");
    }

    /// <summary>
    /// Rejects the raw characters the server's reader rejects: one outside YAML's printable set (YAML 1.2.2
    /// <c>c-printable</c>), and a lone carriage return, NEL, LS or PS, which YamlDotNet reads as a line break and the
    /// server would not. Line feeds after carriage returns are already normalized, so any carriage return left is a lone
    /// one. YamlDotNet accepts most of the others and can skip a NUL, so the check runs before parsing.
    /// </summary>
    private void ThrowIfDisallowedCharacter(string frontmatter)
    {
        for (int i = 0; i < frontmatter.Length; i++)
        {
            char c = frontmatter[i];
            if (char.IsHighSurrogate(c) &&
                i + 1 < frontmatter.Length &&
                char.IsLowSurrogate(frontmatter[i + 1]))
            {
                i++;
            }
            else if (c is '\r' or '\u0085' or '\u2028' or '\u2029')
            {
                throw new FormatException($"The frontmatter contains U+{(int)c:X4}, which some YAML parsers read as a line break and others as text.");
            }
            else if (!IsPrintable(c))
            {
                throw new FormatException($"The frontmatter contains U+{(int)c:X4}, which is outside YAML's printable character set.");
            }
        }
    }

    /// <summary>Whether YAML's printable set contains <paramref name="c"/>, a character that is not a surrogate.</summary>
    private bool IsPrintable(char c) =>
        c is '\t' or '\n' or '\r' or (>= ' ' and <= '~') or '\u0085' or (>= '\u00A0' and <= '\uD7FF') or (>= '\uE000' and <= '\uFFFD');

    /// <summary>
    /// Reads the node at the parser's position, inside <paramref name="depth"/> collections. Returns it with the
    /// number of nodes it holds and how many collection levels it spans.
    /// </summary>
    private ReadResult ReadNode(IParser parser, int depth)
    {
        if (parser.TryConsume<AnchorAlias>(out var alias))
        {
            return ExpandAlias(alias.Value.Value, depth);
        }

        if (parser.TryConsume<Scalar>(out var scalar))
        {
            CountNodes(1);
            var value = new ReadResult(ToJsonScalar(scalar), Count: 1, Height: 0);
            if (!scalar.Anchor.IsEmpty)
            {
                _anchors[scalar.Anchor.Value] = new AnchoredNode { Value = value };
            }

            return value;
        }

        if (depth == MaxDepth)
        {
            throw new FormatException($"The frontmatter is nested more than {MaxDepth} levels deep, which this sample does not read.");
        }

        if (parser.TryConsume<SequenceStart>(out var sequenceStart))
        {
            ThrowIfTagChangesCollection(sequenceStart.Tag, SequenceTag);
            var anchored = Define(sequenceStart.Anchor);
            CountNodes(1);
            var array = new JsonArray();
            int count = 1;
            int height = 1;
            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                var item = ReadNode(parser, depth + 1);
                array.Add(item.Node);
                count += item.Count;
                height = Math.Max(height, item.Height + 1);
            }

            return Complete(anchored, new ReadResult(array, count, height));
        }

        if (parser.TryConsume<MappingStart>(out var mappingStart))
        {
            ThrowIfTagChangesCollection(mappingStart.Tag, MappingTag);
            var anchored = Define(mappingStart.Anchor);
            CountNodes(1);
            var mapping = new JsonObject();
            int count = 1;
            int height = 1;
            while (!parser.TryConsume<MappingEnd>(out _))
            {
                var key = ReadNode(parser, depth + 1);
                string name = ToJsonKey(key.Node);
                if (mapping.ContainsKey(name))
                {
                    throw new FormatException($"The frontmatter key '{name}' appears more than once.");
                }

                var value = ReadNode(parser, depth + 1);
                mapping[name] = value.Node;
                count += key.Count + value.Count;
                height = Math.Max(height, value.Height + 1);
            }

            return Complete(anchored, new ReadResult(mapping, count, height));
        }

        throw new FormatException("The frontmatter contains a YAML node this sample does not convert.");
    }

    /// <summary>
    /// Rejects a tag on a collection unless converting the collection keeps its meaning: the non-specific <c>!</c> or
    /// the tag for that kind of collection. <c>!!str [one]</c>, for example, is not a list of strings.
    /// </summary>
    private void ThrowIfTagChangesCollection(TagName tag, string collectionTag)
    {
        if (tag.IsEmpty ||
            tag.IsNonSpecific ||
            tag.Value == collectionTag)
        {
            return;
        }

        throw new FormatException($"A frontmatter collection has the tag '{tag.Value}', which this sample does not convert.");
    }

    /// <summary>Copies the node an alias refers to, within the depth and node limits.</summary>
    private ReadResult ExpandAlias(string anchor, int depth)
    {
        if (!_anchors.TryGetValue(anchor, out var anchored))
        {
            throw new FormatException($"The frontmatter alias '*{anchor}' has no anchor before it.");
        }

        // The node is still being read, so the alias is inside it and the frontmatter would refer to itself.
        var target = anchored.Value ??
            throw new FormatException($"The frontmatter alias '*{anchor}' is inside the node it refers to.");
        if (depth + target.Height > MaxDepth)
        {
            throw new FormatException($"The frontmatter is nested more than {MaxDepth} levels deep, which this sample does not read.");
        }

        CountNodes(target.Count);
        return target with { Node = target.Node?.DeepClone() };
    }

    /// <summary>Registers an anchor when its collection starts, so an alias inside the collection is recognized.</summary>
    private AnchoredNode? Define(AnchorName anchor)
    {
        if (anchor.IsEmpty)
        {
            return null;
        }

        var anchored = new AnchoredNode();
        _anchors[anchor.Value] = anchored;
        return anchored;
    }

    private ReadResult Complete(AnchoredNode? anchored, ReadResult result)
    {
        if (anchored is not null)
        {
            anchored.Value = result;
        }

        return result;
    }

    private void CountNodes(int count)
    {
        _nodeCount += count;
        if (_nodeCount > MaxNodes)
        {
            throw new FormatException($"The frontmatter expands to more than {MaxNodes} nodes, which this sample does not read.");
        }
    }

    /// <summary>JSON keys are strings: null becomes the empty string, and booleans and numbers their JSON text.</summary>
    private string ToJsonKey(JsonNode? key) => key switch
    {
        null => string.Empty,
        JsonValue value when value.TryGetValue(out string? text) => text,
        JsonValue value => value.ToJsonString(),
        _ => throw new FormatException("A frontmatter key is a collection, which JSON cannot represent."),
    };

    private JsonNode? ToJsonScalar(Scalar scalar)
    {
        string text = scalar.Value;
        if (!scalar.Tag.IsEmpty)
        {
            // '!' and '!!str' make the value a string; other tags are left to a host that knows them.
            if (scalar.Tag.IsNonSpecific ||
                scalar.Tag.Value == StringTag)
            {
                return JsonValue.Create(text);
            }

            throw new FormatException($"The frontmatter value '{text}' has the tag '{scalar.Tag.Value}', which this sample does not convert.");
        }

        return scalar.Style == ScalarStyle.Plain ? ResolvePlainScalar(text) : JsonValue.Create(text);
    }

    /// <summary>Types an unquoted scalar per the YAML 1.2 core schema.</summary>
    private JsonNode? ResolvePlainScalar(string text)
    {
        switch (text)
        {
            case "" or "~" or "null" or "Null" or "NULL":
                return null;
            case "true" or "True" or "TRUE":
                return JsonValue.Create(true);
            case "false" or "False" or "FALSE":
                return JsonValue.Create(false);
        }

        if (_infinityOrNaN.IsMatch(text))
        {
            throw new FormatException($"The frontmatter value '{text}' cannot be represented in JSON.");
        }

        if (_decimalInteger.IsMatch(text))
        {
            // Exact at any size, as JSON allows; "-0" is 0.
            bool negative = text[0] == '-';
            string digits = text.TrimStart('-', '+').TrimStart('0');
            return JsonNode.Parse(digits.Length == 0 ? "0" : negative ? "-" + digits : digits);
        }

        if (_hexInteger.IsMatch(text) ||
            _octalInteger.IsMatch(text))
        {
            return JsonNode.Parse(ParseUnsigned(text).ToString(CultureInfo.InvariantCulture));
        }

        if (_float.IsMatch(text))
        {
            double value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (double.IsInfinity(value))
            {
                throw new FormatException($"The frontmatter value '{text}' cannot be represented as a JSON number.");
            }

            // JSON has no negative zero worth keeping.
            return JsonValue.Create(value == 0 ? 0d : value);
        }

        return JsonValue.Create(text);
    }

    /// <summary>Reads a <c>0x</c> or <c>0o</c> integer exactly, up to 64 bits.</summary>
    private ulong ParseUnsigned(string text)
    {
        int bitsPerDigit = text[1] == 'x' ? 4 : 3;
        ulong value = 0;
        foreach (char c in text.Substring(2))
        {
            if (value >> (64 - bitsPerDigit) != 0)
            {
                throw new FormatException($"The frontmatter value '{text}' is wider than 64 bits, which this sample does not convert.");
            }

            value = (value << bitsPerDigit) + (ulong)Convert.ToInt32(c.ToString(), 16);
        }

        return value;
    }

    /// <summary>A node read from the frontmatter, with the number of nodes it holds and the collection levels it spans.</summary>
    private readonly record struct ReadResult(JsonNode? Node, int Count, int Height);

    /// <summary>The node defined for an anchor; <see cref="Value"/> is null while its collection is still being read.</summary>
    private sealed class AnchoredNode
    {
        public ReadResult? Value { get; set; }
    }
}

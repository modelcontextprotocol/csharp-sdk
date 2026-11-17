using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Reads a bounded subset of <c>SKILL.md</c> YAML frontmatter into a <see cref="JsonObject"/>, without a YAML library.
/// Server authoring uses it to read the frontmatter of the skills it builds.
/// </summary>
/// <remarks>
/// <para>
/// This reader handles a bounded subset of YAML: block mappings, nested mappings and sequences, plain, quoted and
/// block scalars, single-line flow collections, and comments. It is not a general-purpose YAML parser or validator.
/// The root must be a mapping; a root that is any other node is rejected as not a mapping.
/// </para>
/// <para>
/// It throws <see cref="FormatException"/> for malformed input it detects, including a raw character outside YAML's
/// printable set, such as a NUL, or a lone carriage return, NEL, LS or PS, and <see cref="UnsupportedYamlException"/>
/// for valid YAML outside its subset, such as anchors, aliases, tags, complex (<c>? </c>) and empty mapping keys,
/// nested or multi-line flow collections (a comment inside a flow collection makes it multi-line), nesting deeper
/// than 32 levels, and hexadecimal or octal integers wider than 64 bits. Reading can stop at the first unsupported
/// construct, so that exception says nothing about the rest of the frontmatter.
/// </para>
/// <para>
/// Unquoted scalars are resolved per the YAML 1.2 core schema (<c>null</c>, booleans, integers, finite floats,
/// otherwise strings). Hexadecimal (<c>0x</c>) and octal (<c>0o</c>) integers keep their exact value up to 64 bits,
/// leading zeros included. Values JSON cannot represent, such as <c>.inf</c>, are rejected.
/// </para>
/// </remarks>
internal static class SkillFrontmatter
{
    private const string Delimiter = "---";
    private const string NotAMappingMessage = "The frontmatter must be a YAML mapping of keys to values.";

    /// <summary>YAML's white space characters. Other Unicode spaces, such as U+00A0, are scalar content.</summary>
    private static readonly char[] s_yamlWhitespace = [' ', '\t'];
    private static readonly char[] s_flowIndicators = [',', '[', ']', '{', '}'];

    /// <summary>The deepest nesting of block collections the reader accepts. Frontmatter is shallow; this only exists to keep a hostile file from exhausting the stack.</summary>
    private const int MaxNestingDepth = 32;

    /// <summary>
    /// Thrown when the reader encounters an unsupported construct. Parsing may stop before the remaining
    /// frontmatter is examined; this exception does not establish whole-document validity or metadata equality.
    /// </summary>
    internal sealed class UnsupportedYamlException(string message) : FormatException(message);

    /// <summary>Thrown when a valid YAML scalar has no JSON form, such as <c>.inf</c> or an unpaired surrogate escape.</summary>
    private sealed class UnrepresentableValueException(string message) : FormatException(message);

    /// <summary>Thrown when a quoted scalar has no closing quote on its line; a later delimiter may indicate an unsupported multi-line form.</summary>
    private sealed class UnterminatedQuoteException(string message, char quote) : FormatException(message)
    {
        public char Quote { get; } = quote;
    }

    /// <summary>Thrown when a flow collection has no closing bracket on its line; a later delimiter may indicate an unsupported multi-line form.</summary>
    private sealed class UnterminatedFlowException(string message, char closer) : FormatException(message)
    {
        public char Closer { get; } = closer;
    }

    /// <summary>
    /// Parses the frontmatter at the start of <paramref name="skillMarkdown"/>.
    /// </summary>
    /// <param name="skillMarkdown">The full text of a <c>SKILL.md</c>.</param>
    /// <returns>The frontmatter as a JSON object.</returns>
    /// <exception cref="FormatException">
    /// The text does not begin with a <c>---</c>-delimited frontmatter block, the block is not a mapping, or it
    /// contains malformed YAML or YAML this reader does not support.
    /// </exception>
    public static JsonObject Parse(string skillMarkdown)
    {
        var lines = SplitLines(skillMarkdown);
        if (lines.Count == 0 || lines[0].TrimEnd(s_yamlWhitespace) != Delimiter)
        {
            throw new FormatException($"{SkillsProtocol.SkillFileName} must begin with a line containing only '{Delimiter}' that opens the YAML frontmatter.");
        }

        int end = -1;
        for (int i = 1; i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimEnd(s_yamlWhitespace);
            if (trimmed == Delimiter || trimmed == "...")
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            throw new FormatException($"The YAML frontmatter of {SkillsProtocol.SkillFileName} is not closed by a line containing only '{Delimiter}'.");
        }

        var body = new List<Line>(end - 1);
        for (int i = 1; i < end; i++)
        {
            ThrowIfDisallowedCharacter(lines[i], i + 1);
            body.Add(new Line(lines[i], i + 1));
        }

        var parser = new Parser(body);
        JsonNode? root;
        try
        {
            root = parser.ParseDocument();
        }
        catch (UnrepresentableValueException e)
        {
            // The subclass only lets the parser tell a scalar root from a mapping with a broken first entry.
            throw new FormatException(e.Message, e);
        }

        return root as JsonObject ?? throw new FormatException(NotAMappingMessage);
    }

    /// <summary>
    /// Rejects a raw character that YAML parsers do not all read the same way. It runs on the raw lines before parsing,
    /// so comments are checked too. That covers any character outside YAML's printable set (YAML 1.2.2
    /// <c>c-printable</c>): a C0 control other than tab, line feed and carriage return, DEL, a C1 control other than NEL,
    /// U+FFFE, U+FFFF or an unpaired surrogate. YAML 1.2.2 lets a quoted scalar hold such characters other than C0
    /// controls, but libyaml, Go's yaml.v3 and PyYAML reject them anywhere, so this reader does too. It also covers a
    /// lone carriage return (<see cref="SplitLines"/> removes the one before a line feed), NEL, LS and PS. YamlDotNet,
    /// libyaml and PyYAML read all four as line breaks, as YAML 1.2.2 reads a lone carriage return, while this reader
    /// and the npm <c>yaml</c> package would read them as text, so a comment could hide a key from one side. An escape
    /// such as <c>\0</c> or <c>\L</c> in a double-quoted scalar is not a raw character and stays valid.
    /// </summary>
    private static void ThrowIfDisallowedCharacter(string line, int lineNumber)
    {
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (char.IsHighSurrogate(c) &&
                i + 1 < line.Length &&
                char.IsLowSurrogate(line[i + 1]))
            {
                i++;
            }
            else if (c is '\r' or '\u0085' or '\u2028' or '\u2029')
            {
                throw new FormatException(
                    $"Line {lineNumber}: the frontmatter contains U+{(int)c:X4}, which some YAML parsers read as a line break and others as text. Remove it, or write it as an escape in a double-quoted string.");
            }
            else if (!IsPrintable(c))
            {
                throw new FormatException(
                    $"Line {lineNumber}: the frontmatter contains U+{(int)c:X4}, which is outside YAML's printable character set. Remove it, or write it as an escape in a double-quoted string.");
            }
        }
    }

    /// <summary>Whether YAML's printable set contains <paramref name="c"/>, a character that is not a surrogate.</summary>
    private static bool IsPrintable(char c) =>
        c is '\t' or '\n' or '\r' or (>= ' ' and <= '~') or '\u0085' or (>= '\u00A0' and <= '\uD7FF') or (>= '\uE000' and <= '\uFFFD');

    private static List<string> SplitLines(string text)
    {
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text.Substring(1);
        }

        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                int lineEnd = i > start && text[i - 1] == '\r' ? i - 1 : i;
                lines.Add(text.Substring(start, lineEnd - start));
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text.Substring(start));
        }

        return lines;
    }

    private readonly struct Line
    {
        public Line(string raw, int number)
        {
            Raw = raw;
            Number = number;

            int indent = 0;
            while (indent < raw.Length && raw[indent] == ' ')
            {
                indent++;
            }

            // Tabs cannot indent block structure, but they may separate a value from its indicator and are content
            // inside block scalars, so a leading tab only marks the line here; the parser rejects it where it matters.
            int contentStart = indent;
            while (contentStart < raw.Length && raw[contentStart] is ' ' or '\t')
            {
                contentStart++;
            }

            Indent = indent;
            HasTabIndentation = contentStart != indent;
            Content = raw.Substring(contentStart);
            IsBlank = Content.Length == 0 || Content[0] == '#';
        }

        public string Raw { get; }
        public int Number { get; }
        public int Indent { get; }
        public string Content { get; }

        /// <summary>Whether a tab appears in the line's leading white space, which YAML does not allow for indentation.</summary>
        public bool HasTabIndentation { get; }

        /// <summary>Whether the line is empty or a comment, and therefore structurally insignificant.</summary>
        public bool IsBlank { get; }
    }

    private sealed class Parser(List<Line> lines)
    {
        private readonly List<Line> _lines = lines;
        private int _pos;
        private int _depth;

        /// <summary>The first multi-line flow collection, reported only after the rest of the frontmatter is read.</summary>
        private UnsupportedYamlException? _multiLineFlow;

        public JsonNode? ParseDocument()
        {
            SkipBlank();
            if (_pos >= _lines.Count)
            {
                return new JsonObject();
            }

            var first = _lines[_pos];
            ThrowIfTabIndented(first);
            if (first.Content[0] == '*')
            {
                throw new FormatException($"Line {first.Number}: the frontmatter cannot start with an alias ('*'), because no anchor is defined before it.");
            }

            if (first.Content[0] is '&' or '!' &&
                SkipNodeProperties(first.Content) is { } afterProperties)
            {
                throw CreateRootPropertiesException(first, afterProperties);
            }

            bool isFlowCollection = first.Content[0] is '[' or '{' &&
                !IsFlowCollectionKey(first.Content, first.Number);
            if (IsSequenceEntry(first.Content) ||
                (isFlowCollection && first.Content[0] == '['))
            {
                CheckRootSequence(first);
                throw new FormatException(NotAMappingMessage);
            }

            JsonNode? node;
            if (isFlowCollection)
            {
                // A flow mapping is read like a flow value. One that spans lines is reported below, after the rest.
                _pos++;
                node = ParseValue(PrepareValue(first.Content, out _), commentEndedValue: false, parentIndent: -1, first.Number, allowSameIndentSequence: false);
            }
            else if (!StartsMappingEntry(first.Content, first.Number) &&
                IsScalarDocument(first))
            {
                throw new FormatException(NotAMappingMessage);
            }
            else
            {
                // The root mapping may be indented, as long as all of its keys share that indentation. A first line
                // that is neither a mapping entry nor a whole scalar document is reported by the mapping.
                node = ParseBlock(first.Indent);
            }

            SkipBlank();
            if (_pos < _lines.Count)
            {
                throw new FormatException($"Line {_lines[_pos].Number}: unexpected content after the frontmatter mapping.");
            }

            if (_multiLineFlow is not null)
            {
                throw _multiLineFlow;
            }

            return node;
        }

        private void SkipBlank()
        {
            while (_pos < _lines.Count && _lines[_pos].IsBlank)
            {
                _pos++;
            }
        }

        private Line? PeekSignificant()
        {
            for (int i = _pos; i < _lines.Count; i++)
            {
                if (!_lines[i].IsBlank)
                {
                    return _lines[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Classifies a root node that has an anchor or a tag, which this reader does not support: a mapping is valid
        /// YAML outside the subset, and any other node is not a mapping.
        /// </summary>
        private FormatException CreateRootPropertiesException(Line first, string afterProperties)
        {
            string node = afterProperties;
            int lineNumber = first.Number;
            if (node.Length == 0)
            {
                // The properties belong to the node that starts on the next line.
                _pos++;
                if (PeekSignificant() is { } next)
                {
                    node = SkipNodeProperties(next.Content) ?? next.Content;
                    lineNumber = next.Number;
                }
            }

            bool isMapping = node.Length > 0 &&
                (node[0] == '{' || StartsMappingEntry(node, lineNumber));
            return isMapping
                ? new UnsupportedYamlException($"Line {first.Number}: YAML anchors, aliases, and tags are not supported in frontmatter.")
                : new FormatException(NotAMappingMessage);
        }

        /// <summary>
        /// Reads a root sequence only to report malformed YAML in it. The frontmatter is rejected for not being a
        /// mapping either way, so valid YAML in it that this reader does not support does not matter.
        /// </summary>
        private void CheckRootSequence(Line first)
        {
            try
            {
                if (first.Content[0] == '[')
                {
                    _pos++;
                    ParseValue(PrepareValue(first.Content, out _), commentEndedValue: false, parentIndent: -1, first.Number, allowSameIndentSequence: false);
                }
                else
                {
                    ParseBlock(first.Indent);
                }
            }
            catch (UnsupportedYamlException)
            {
                // Still a sequence, so still not a mapping.
            }
        }

        /// <summary>Whether a line's content starts an entry of a block mapping, which the mapping then reads or reports.</summary>
        private bool StartsMappingEntry(string content, int lineNumber)
        {
            if (IsSequenceEntry(content))
            {
                return false;
            }

            if (content[0] == '?' &&
                (content.Length == 1 || content[1] is ' ' or '\t'))
            {
                // An explicit key, which the mapping reports as unsupported.
                return true;
            }

            if (content[0] is '[' or '{')
            {
                return IsFlowCollectionKey(content, lineNumber);
            }

            if (content[0] is '"' or '\'')
            {
                return IsCompactMappingEntry(content, lineNumber);
            }

            return FindKeySeparator(content) >= 0;
        }

        /// <summary>
        /// Whether the whole document is one scalar that starts on <paramref name="first"/>, which is valid YAML but
        /// not a mapping. The scalar is read only to tell it from a mapping whose first entry has a mistake.
        /// </summary>
        private bool IsScalarDocument(Line first)
        {
            int start = _pos;
            try
            {
                _pos++;
                string rest = PrepareValue(first.Content, out bool commentEndedValue);
                ParseValue(rest, commentEndedValue, parentIndent: -1, first.Number, allowSameIndentSequence: false);
                SkipBlank();
                return _pos >= _lines.Count;
            }
            catch (UnsupportedYamlException)
            {
                // A scalar form this reader does not support, such as a multi-line quoted scalar.
                return true;
            }
            catch (UnrepresentableValueException)
            {
                // A scalar that JSON cannot represent, such as .inf.
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            finally
            {
                _pos = start;
            }
        }

        /// <summary>Parses the block node formed by the lines at <paramref name="indent"/>.</summary>
        private JsonNode? ParseBlock(int indent, int parentIndent = -1)
        {
            SkipBlank();
            var line = _lines[_pos];
            if (_depth >= MaxNestingDepth)
            {
                throw new UnsupportedYamlException($"Line {line.Number}: the frontmatter is nested more than {MaxNestingDepth} levels deep, which is not supported in frontmatter.");
            }

            _depth++;
            try
            {
                if (IsSequenceEntry(line.Content))
                {
                    return ParseSequence(indent);
                }

                bool isExplicitMappingKey = line.Content[0] == '?' &&
                    (line.Content.Length == 1 || line.Content[1] is ' ' or '\t');
                bool isScalarValue = parentIndent >= 0 &&
                    line.Content[0] is not ('&' or '*' or '!') &&
                    !isExplicitMappingKey &&
                    !IsCompactMappingEntry(line.Content, line.Number) &&
                    !IsFlowCollectionKey(line.Content, line.Number);
                if (isScalarValue)
                {
                    ThrowIfTabIndented(line);
                    var rest = PrepareValue(line.Content, out bool commentEndedValue);
                    _pos++;
                    return ParseValue(rest, commentEndedValue, parentIndent, line.Number, allowSameIndentSequence: false);
                }

                return ParseMapping(indent);
            }
            finally
            {
                _depth--;
            }
        }

        private static bool IsSequenceEntry(string content) =>
            content.Length > 0 && content[0] == '-' && (content.Length == 1 || content[1] is ' ' or '\t');

        private bool IsFlowCollectionKey(string text, int lineNumber)
        {
            if (text[0] is not ('[' or '{'))
            {
                return false;
            }

            // Find the flow node's boundary without resolving its contents as JSON values.
            var closers = new Stack<char>();
            bool inPlainScalar = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '[' or '{':
                        if (closers.Count >= MaxNestingDepth)
                        {
                            return false;
                        }

                        closers.Push(c == '[' ? ']' : '}');
                        inPlainScalar = false;
                        break;

                    case ']' or '}':
                        if (closers.Count == 0 ||
                            closers.Pop() != c)
                        {
                            return false;
                        }

                        if (closers.Count == 0)
                        {
                            int colon = SkipSpaces(text, i + 1);
                            return colon < text.Length &&
                                text[colon] == ':' &&
                                (colon + 1 == text.Length || text[colon + 1] is ' ' or '\t');
                        }

                        inPlainScalar = false;
                        break;

                    case '"' or '\'' when !inPlainScalar:
                        try
                        {
                            ParseQuotedScalar(text, i, lineNumber, out int end);
                            i = end - 1;
                        }
                        catch (FormatException)
                        {
                            return false;
                        }

                        break;

                    case ',':
                    case ':' when !inPlainScalar ||
                        i + 1 == text.Length ||
                        text[i + 1] is ' ' or '\t':
                        inPlainScalar = false;
                        break;

                    case '#' when i == 0 || text[i - 1] is ' ' or '\t':
                        return false;

                    case ' ' or '\t':
                        break;

                    default:
                        inPlainScalar = true;
                        break;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a sequence item's text is a "key: value" line, with a plain or quoted key, that begins a nested
        /// block mapping.
        /// </summary>
        private static bool IsCompactMappingEntry(string item, int lineNumber)
        {
            if (item[0] is '"' or '\'')
            {
                try
                {
                    ParseQuotedScalar(item, 0, lineNumber, out int afterQuote);
                    int colon = SkipSpaces(item, afterQuote);
                    return colon < item.Length && item[colon] == ':' && (colon + 1 == item.Length || item[colon + 1] is ' ' or '\t');
                }
                catch (FormatException)
                {
                    // Not a well-formed quoted key; the item is parsed as a value and reported there.
                    return false;
                }
            }

            return item[0] is not ('[' or '{' or '|' or '>' or '&' or '*' or '!') && FindKeySeparator(item) >= 0;
        }

        private static void ThrowIfTabIndented(Line line)
        {
            if (line.HasTabIndentation)
            {
                throw new FormatException($"Line {line.Number}: tabs are not allowed for indentation in YAML frontmatter.");
            }
        }

        /// <summary>
        /// Returns what follows the anchor and tag at the start of <paramref name="text"/>: empty when the node starts
        /// on a later line, or null when an anchor has no name or has a flow indicator in it, which YAML does not allow.
        /// </summary>
        private static string? SkipNodeProperties(string text)
        {
            int pos = 0;
            while (pos < text.Length && text[pos] is '&' or '!')
            {
                // A property runs to the next white space; an anchor name may end in ':', as in YAML 1.2.
                int start = pos;
                while (pos < text.Length && text[pos] is not (' ' or '\t'))
                {
                    pos++;
                }

                if (text[start] == '&' &&
                    (pos == start + 1 || text.IndexOfAny(s_flowIndicators, start, pos - start) >= 0))
                {
                    return null;
                }

                pos = SkipSpaces(text, pos);
            }

            return pos < text.Length && text[pos] == '#' ? string.Empty : text.Substring(pos);
        }

        /// <summary>
        /// Rejects a plain scalar that starts with a character YAML gives another meaning: an anchor, alias, or tag
        /// (constructs this reader does not support), a reserved character, a flow indicator, a block scalar header,
        /// or a block indicator followed by white space.
        /// </summary>
        private static void ThrowIfReservedPlainStart(string text, int lineNumber)
        {
            if (text.Length == 0)
            {
                return;
            }

            char c = text[0];
            switch (c)
            {
                case '&' or '*' or '!':
                    throw new UnsupportedYamlException($"Line {lineNumber}: YAML anchors, aliases, and tags are not supported in frontmatter.");

                case '@' or '`' or '%':
                    throw new FormatException($"Line {lineNumber}: a plain scalar cannot start with '{c}', which YAML reserves. Quote the value.");

                case '#':
                    // Only a '#' after white space starts a comment; one that starts a scalar is not allowed.
                    throw new FormatException($"Line {lineNumber}: a plain scalar cannot start with '#'. Quote the value.");

                case ']' or '}' or ',':
                    throw new FormatException($"Line {lineNumber}: a plain scalar cannot start with the flow indicator '{c}'. Quote the value.");

                case '|' or '>':
                    throw new FormatException($"Line {lineNumber}: a plain scalar cannot start with '{c}', which YAML reads as a block scalar header. Quote the value.");

                case '-' or '?' or ':' when text.Length == 1 || text[1] is ' ' or '\t':
                    throw new FormatException($"Line {lineNumber}: a plain scalar cannot start with '{c}' followed by white space, which YAML reads as a block indicator. Quote the value.");
            }
        }

        /// <summary>
        /// Rejects a flow sequence entry or flow mapping key that starts with an explicit key: '?' followed by white
        /// space. That is valid YAML this reader does not support. '?' followed by any other character is not an
        /// explicit key, and '?' at the end of the line continues on a later line like an unterminated collection.
        /// </summary>
        private static void ThrowIfFlowExplicitKey(string text, int pos, char closer, int lineNumber)
        {
            if (text[pos] != '?')
            {
                return;
            }

            if (pos + 1 == text.Length)
            {
                string collection = closer == ']' ? "sequence" : "mapping";
                throw new UnterminatedFlowException($"Line {lineNumber}: unterminated flow {collection}.", closer);
            }

            if (text[pos + 1] is ' ' or '\t')
            {
                throw new UnsupportedYamlException($"Line {lineNumber}: explicit mapping keys ('? ') in flow collections are not supported in frontmatter.");
            }
        }

        private JsonObject ParseMapping(int indent)
        {
            var result = new JsonObject();
            while (true)
            {
                SkipBlank();
                if (_pos >= _lines.Count)
                {
                    break;
                }

                var line = _lines[_pos];
                if (line.Indent < indent)
                {
                    break;
                }

                if (line.Indent > indent)
                {
                    throw new FormatException($"Line {line.Number}: unexpected indentation.");
                }

                ThrowIfTabIndented(line);
                if (IsSequenceEntry(line.Content))
                {
                    throw new FormatException($"Line {line.Number}: a sequence entry cannot appear directly inside a mapping. Put it under a key.");
                }

                string content = line.Content;
                if (content[0] == '?' && (content.Length == 1 || content[1] is ' ' or '\t'))
                {
                    throw new UnsupportedYamlException($"Line {line.Number}: complex mapping keys ('? ') are not supported in frontmatter.");
                }

                if (content[0] is '[' or '{')
                {
                    throw new UnsupportedYamlException($"Line {line.Number}: flow collections are not supported as mapping keys in frontmatter.");
                }

                int consumed;
                string key;
                if (content[0] is '"' or '\'')
                {
                    key = ParseQuotedScalar(content, 0, line.Number, out consumed);
                    int colon = SkipSpaces(content, consumed);
                    if (colon >= content.Length || content[colon] != ':' || (colon + 1 < content.Length && content[colon + 1] is not (' ' or '\t')))
                    {
                        throw new FormatException($"Line {line.Number}: expected ':' after the quoted key.");
                    }

                    consumed = colon + 1;
                }
                else
                {
                    int separator = FindKeySeparator(content);
                    if (separator < 0)
                    {
                        throw new FormatException($"Line {line.Number}: expected a 'key: value' pair.");
                    }

                    string rawKey = content.Substring(0, separator).TrimEnd(s_yamlWhitespace);
                    if (rawKey.Length == 0)
                    {
                        throw new UnsupportedYamlException($"Line {line.Number}: empty mapping keys (': value') are not supported in frontmatter.");
                    }

                    ThrowIfReservedPlainStart(content, line.Number);

                    // Keys resolve like values: "TRUE:" is the key "true" and "0x10:" is "16" to a YAML parser.
                    key = KeyToString(ResolvePlainScalar(rawKey, line.Number));
                    consumed = separator + 1;
                }

                if (result.ContainsKey(key))
                {
                    throw new FormatException($"Line {line.Number}: duplicate key '{key}'.");
                }

                string rest = PrepareValue(content.Substring(consumed), out bool commentEndedValue);
                _pos++;
                result[key] = ParseValue(rest, commentEndedValue, indent, line.Number, allowSameIndentSequence: true);
            }

            return result;
        }

        private JsonArray ParseSequence(int indent)
        {
            var result = new JsonArray();
            while (true)
            {
                SkipBlank();
                if (_pos >= _lines.Count)
                {
                    break;
                }

                var line = _lines[_pos];
                if (line.Indent < indent || (line.Indent == indent && !IsSequenceEntry(line.Content)))
                {
                    break;
                }

                if (line.Indent > indent)
                {
                    throw new FormatException($"Line {line.Number}: unexpected indentation.");
                }

                ThrowIfTabIndented(line);
                // Everything after the '-', white space included, so that the item's column is known exactly.
                string item = line.Content.Substring(1);
                _pos++;

                string trimmedItem = item.TrimStart(s_yamlWhitespace);
                if (trimmedItem.Length == 0 || trimmedItem[0] == '#')
                {
                    // "- " followed by nothing: the value is the more-indented block that follows, or null.
                    var next = PeekSignificant();
                    result.Add(next is { } n && n.Indent > indent ? ParseBlock(n.Indent, indent) : null);
                    continue;
                }

                int itemIndent = indent + 1 + (item.Length - trimmedItem.Length);
                bool isExplicitKey = trimmedItem[0] == '?' &&
                    (trimmedItem.Length == 1 || trimmedItem[1] is ' ' or '\t');
                if (IsSequenceEntry(trimmedItem) ||
                    IsCompactMappingEntry(trimmedItem, line.Number) ||
                    isExplicitKey)
                {
                    if (item.IndexOf('\t', 0, item.Length - trimmedItem.Length) >= 0)
                    {
                        // The nested node's indentation would be set by a tab.
                        throw new FormatException($"Line {line.Number}: tabs are not allowed for indentation in YAML frontmatter.");
                    }

                    // A compact nested node ("- key: value", "- ? key" or "- - x"): re-read this line as if the item
                    // started on its own line at the item's column, then continue with the block at that indentation.
                    _pos--;
                    _lines[_pos] = new Line(new string(' ', itemIndent) + trimmedItem, line.Number);
                    result.Add(ParseBlock(itemIndent));
                    continue;
                }

                string itemValue = PrepareValue(trimmedItem, out bool itemCommentEndedValue);
                result.Add(ParseValue(itemValue, itemCommentEndedValue, indent, line.Number, allowSameIndentSequence: false));
            }

            return result;
        }

        /// <summary>
        /// Parses the value that follows a key or sequence dash. <paramref name="rest"/> is the remainder of the
        /// line, with comments removed; the line itself has already been consumed.
        /// </summary>
        private JsonNode? ParseValue(string rest, bool commentEndedValue, int parentIndent, int lineNumber, bool allowSameIndentSequence)
        {
            if (rest.Length == 0)
            {
                var next = PeekSignificant();
                if (next is { } n)
                {
                    if (n.Indent > parentIndent)
                    {
                        return ParseBlock(n.Indent, parentIndent);
                    }

                    if (allowSameIndentSequence && n.Indent == parentIndent && IsSequenceEntry(n.Content))
                    {
                        return ParseSequence(parentIndent);
                    }
                }

                return null;
            }

            switch (rest[0])
            {
                case '|' or '>':
                    return ParseBlockScalar(rest, parentIndent, lineNumber);

                case '&' or '*' or '!':
                    throw new UnsupportedYamlException($"Line {lineNumber}: YAML anchors, aliases, and tags are not supported in frontmatter.");

                case '[' or '{':
                    try
                    {
                        return rest[0] == '[' ? ParseFlowSequence(rest, lineNumber) : ParseFlowMapping(rest, lineNumber);
                    }
                    catch (UnterminatedFlowException)
                    {
                        // The collection continues on later lines, which this reader does not support. It is checked and
                        // skipped, and the frontmatter is reported as unsupported only after the rest of it is read, so
                        // that only valid YAML is reported that way.
                        _pos = CheckMultiLineFlow(rest, parentIndent, lineNumber);
                        _multiLineFlow ??= new UnsupportedYamlException($"Line {lineNumber}: multi-line flow collections are not supported in frontmatter.");
                        return null;
                    }

                case '"' or '\'':
                    string quoted;
                    int consumed;
                    try
                    {
                        quoted = ParseQuotedScalar(rest, 0, lineNumber, out consumed);
                    }
                    catch (UnterminatedQuoteException e) when (ClosesLater(e.Quote))
                    {
                        // A later delimiter may indicate an unsupported multi-line quoted scalar. This
                        // classification does not validate its contents or the rest of the frontmatter.
                        throw new UnsupportedYamlException($"Line {lineNumber}: multi-line quoted scalars are not supported in frontmatter.");
                    }

                    if (StripComment(rest.Substring(consumed), allowLeadingComment: false).Trim(s_yamlWhitespace).Length != 0)
                    {
                        throw new FormatException($"Line {lineNumber}: unexpected content after the quoted value.");
                    }

                    return JsonValue.Create(quoted);

                default:
                    if (IsSequenceEntry(rest))
                    {
                        throw new FormatException($"Line {lineNumber}: a sequence cannot start on the same line as its key. Put the '- ' entries on the following lines, or quote the value if '-' is meant literally.");
                    }

                    ThrowIfReservedPlainStart(rest, lineNumber);
                    if (FindKeySeparator(rest) >= 0)
                    {
                        throw new FormatException($"Line {lineNumber}: a plain scalar cannot contain ': '. Quote the value if it is meant literally.");
                    }

                    string plain = commentEndedValue ? rest : CollectPlainContinuation(rest, parentIndent, lineNumber, out commentEndedValue);

                    // A comment ends a plain scalar, on whichever of its lines it appears. A more-indented line after
                    // it cannot continue the scalar and cannot start a nested block either, so it is an error, as it
                    // is to a YAML parser.
                    if (commentEndedValue && PeekSignificant() is { } following && following.Indent > parentIndent)
                    {
                        throw new FormatException(
                            $"Line {following.Number}: this line cannot continue the value that started on line {lineNumber}, because a comment ended that value.");
                    }

                    return ResolvePlainScalar(plain, lineNumber);
            }
        }

        private bool ClosesLater(char quote)
        {
            for (int i = _pos; i < _lines.Count; i++)
            {
                if (_lines[i].Raw.IndexOf(quote) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks a flow collection that continues past its first line the way a YAML parser reads it, and throws
        /// <see cref="FormatException"/> if it is invalid. The rules that span lines are checked here: later lines are
        /// indented more than the parent, though the closing bracket may sit at the parent's indentation; a comment,
        /// or a white-space line whose tab falls short of that indentation, ends a plain scalar; and a key in a flow
        /// sequence stays on one line. The lines, without comments, are then joined and checked like a single-line
        /// collection. Valid YAML inside the collection that this reader does not support is not an error here.
        /// </summary>
        /// <returns>The index of the line after the collection.</returns>
        private int CheckMultiLineFlow(string first, int parentIndent, int lineNumber)
        {
            char outerCloser = first[0] == '[' ? ']' : '}';
            string collection = outerCloser == ']' ? "sequence" : "mapping";
            var joined = new StringBuilder();
            var closers = new Stack<char>();
            var entryLines = new Stack<int>();
            char quote = '\0';
            bool inPlain = false;
            bool afterQuoted = false;
            bool plainEnded = false;
            bool firstLineComment = false;
            string text = first;
            int number = lineNumber;
            int next = _pos;
            while (true)
            {
                int end = text.Length;
                bool closed = false;
                for (int i = 0; i < text.Length && !closed; i++)
                {
                    char c = text[i];
                    if (quote != '\0')
                    {
                        if (quote == '"' && c == '\\')
                        {
                            i++;
                        }
                        else if (c == quote &&
                            quote == '\'' &&
                            i + 1 < text.Length &&
                            text[i + 1] == '\'')
                        {
                            i++;
                        }
                        else if (c == quote)
                        {
                            quote = '\0';
                            afterQuoted = true;
                        }

                        continue;
                    }

                    if (c is ' ' or '\t')
                    {
                        continue;
                    }

                    if (c == '#' &&
                        i > 0 &&
                        text[i - 1] is ' ' or '\t')
                    {
                        // A comment runs to the end of the line and ends a plain scalar.
                        firstLineComment |= number == lineNumber;
                        plainEnded |= inPlain;
                        inPlain = false;
                        end = i;
                        break;
                    }

                    bool isValueIndicator = c == ':' &&
                        (afterQuoted ||
                         i + 1 == text.Length ||
                         text[i + 1] is ' ' or '\t' ||
                         Array.IndexOf(s_flowIndicators, text[i + 1]) >= 0);
                    if (plainEnded &&
                        !isValueIndicator &&
                        c is not (',' or ']' or '}'))
                    {
                        throw new FormatException($"Line {number}: the flow entry before this one already ended, so a ',' must separate them.");
                    }

                    plainEnded = false;
                    afterQuoted = false;
                    if (entryLines.Count > 0 &&
                        entryLines.Peek() < 0 &&
                        c is not (',' or ']' or '}'))
                    {
                        // The first token of an entry.
                        entryLines.Pop();
                        entryLines.Push(number);
                    }

                    switch (c)
                    {
                        case '[' or '{':
                            closers.Push(c == '[' ? ']' : '}');
                            entryLines.Push(-1);
                            inPlain = false;
                            break;

                        case ']' or '}':
                            closers.Pop();
                            entryLines.Pop();
                            closed = closers.Count == 0;
                            inPlain = false;
                            break;

                        case ',':
                            entryLines.Pop();
                            entryLines.Push(-1);
                            inPlain = false;
                            break;

                        case ':' when isValueIndicator:
                            if (closers.Peek() == ']' &&
                                entryLines.Peek() != number)
                            {
                                throw new FormatException($"Line {number}: a key in a flow sequence must be on a single line.");
                            }

                            inPlain = false;
                            break;

                        case '"' or '\'' when !inPlain:
                            quote = c;
                            break;

                        default:
                            inPlain = true;
                            break;
                    }
                }

                joined.Append(text, 0, end).Append(' ');
                if (closed)
                {
                    break;
                }

                if (next >= _lines.Count)
                {
                    throw new FormatException(firstLineComment
                        ? $"Line {lineNumber}: a comment inside a flow collection runs to the end of the line, leaving the collection unterminated. Move the comment after the closing {(outerCloser == ']' ? "bracket" : "brace")}."
                        : $"Line {lineNumber}: unterminated flow {collection}.");
                }

                var line = _lines[next++];
                number = line.Number;
                text = line.Content;
                bool emptyLine = text.Length == 0;
                bool indented = line.Indent > parentIndent;
                if (quote != '\0')
                {
                    // A quoted scalar continues on more-indented lines. An empty line may be shorter, unless it holds a tab.
                    if (!indented &&
                        (!emptyLine || line.HasTabIndentation))
                    {
                        throw new FormatException($"Line {number}: unterminated quoted scalar in a flow {collection}. Lines that continue it must be indented more than its parent.");
                    }

                    continue;
                }

                if (emptyLine ||
                    text[0] == '#')
                {
                    // Empty and comment lines may sit at any indentation. A comment line ends a plain scalar, and so
                    // does a white-space line whose tab falls short of the indentation.
                    if (inPlain &&
                        (!emptyLine || (line.HasTabIndentation && !indented)))
                    {
                        plainEnded = true;
                        inPlain = false;
                    }

                    text = string.Empty;
                    continue;
                }

                bool closesAtParentIndent = closers.Count == 1 &&
                    line.Indent == parentIndent &&
                    text[0] == outerCloser;
                if (!indented &&
                    !closesAtParentIndent)
                {
                    throw new FormatException($"Line {number}: unterminated flow {collection}. Lines that continue it must be indented more than its parent.");
                }
            }

            try
            {
                string joinedText = joined.ToString();
                if (outerCloser == ']')
                {
                    ParseFlowSequence(joinedText, lineNumber);
                }
                else
                {
                    ParseFlowMapping(joinedText, lineNumber);
                }
            }
            catch (UnsupportedYamlException)
            {
                // Valid YAML inside the collection that this reader does not support; the caller reports the collection.
            }
            catch (UnterminatedFlowException e)
            {
                throw new FormatException(e.Message);
            }

            return next;
        }

        /// <summary>
        /// Folds the more-indented continuation lines of a plain multi-line scalar into it. A trailing comment on a
        /// continuation line ends the scalar, which <paramref name="commentEndedValue"/> reports.
        /// </summary>
        private string CollectPlainContinuation(string first, int parentIndent, int lineNumber, out bool commentEndedValue)
        {
            commentEndedValue = false;
            var builder = new StringBuilder(first);
            int pendingNewlines = 0;
            while (_pos < _lines.Count)
            {
                var line = _lines[_pos];
                if (line.Content.Length == 0)
                {
                    pendingNewlines++;
                    _pos++;
                    continue;
                }

                if (line.Indent <= parentIndent || line.Content[0] == '#')
                {
                    break;
                }

                string stripped = StripComment(line.Content);
                string text = stripped.Trim(s_yamlWhitespace);
                if (text.Length == 0)
                {
                    break;
                }

                if (FindKeySeparator(text) >= 0)
                {
                    throw new FormatException(
                        $"Line {line.Number}: a plain scalar cannot contain ': '. If this line is meant to be a key, check its indentation; " +
                        $"the value that started on line {lineNumber} continues onto any more-indented line.");
                }

                builder.Append(pendingNewlines > 0 ? new string('\n', pendingNewlines) : " ");
                pendingNewlines = 0;
                builder.Append(text);
                _pos++;

                if (stripped.Length != line.Content.Length)
                {
                    commentEndedValue = true;
                    break;
                }
            }

            // Trailing blank lines belong to whatever follows, so give them back.
            _pos -= pendingNewlines;
            return builder.ToString();
        }

        private JsonNode? ParseBlockScalar(string header, int parentIndent, int lineNumber)
        {
            bool literal = header[0] == '|';
            char chomping = 'c';
            int explicitIndent = 0;
            int headerPos = 1;
            for (; headerPos < header.Length && header[headerPos] != ' '; headerPos++)
            {
                char c = header[headerPos];
                if (c is '-' or '+')
                {
                    if (chomping != 'c')
                    {
                        throw new FormatException($"Line {lineNumber}: invalid block scalar header '{header}': repeated chomping indicator.");
                    }

                    chomping = c == '-' ? 's' : 'k';
                }
                else if (c is >= '1' and <= '9')
                {
                    if (explicitIndent != 0)
                    {
                        throw new FormatException($"Line {lineNumber}: invalid block scalar header '{header}': repeated indentation indicator.");
                    }

                    explicitIndent = c - '0';
                }
                else
                {
                    throw new FormatException($"Line {lineNumber}: invalid block scalar header '{header}'.");
                }
            }

            if (StripComment(header.Substring(headerPos)).Trim(s_yamlWhitespace).Length != 0)
            {
                throw new FormatException($"Line {lineNumber}: invalid block scalar header '{header}': only a comment may follow the indicators.");
            }

            // Gather the raw lines of the block: everything blank, plus everything indented more than the parent.
            var raw = new List<string>();
            int contentIndent = explicitIndent > 0 ? parentIndent + explicitIndent : -1;
            int widestLeadingEmptyLine = 0;
            while (_pos < _lines.Count)
            {
                var line = _lines[_pos];
                if (line.Content.Length == 0)
                {
                    // A white-space-only line is empty unless it reaches past the content indentation, in which case
                    // the excess is content. Until the indentation is known, the widest such line is remembered:
                    // YAML rejects a leading empty line wider than the first content line. A tab short of the
                    // content indentation is neither indentation nor content.
                    if (line.HasTabIndentation && (contentIndent < 0 || line.Indent < contentIndent))
                    {
                        throw new FormatException($"Line {line.Number}: tabs are not allowed for indentation in YAML frontmatter.");
                    }

                    if (contentIndent < 0)
                    {
                        widestLeadingEmptyLine = Math.Max(widestLeadingEmptyLine, line.Raw.Length);
                        raw.Add(string.Empty);
                    }
                    else
                    {
                        raw.Add(line.Raw.Length > contentIndent ? line.Raw.Substring(contentIndent) : string.Empty);
                    }

                    _pos++;
                    continue;
                }

                if (line.Indent <= parentIndent)
                {
                    break;
                }

                if (contentIndent < 0)
                {
                    contentIndent = line.Indent;
                    if (widestLeadingEmptyLine > contentIndent)
                    {
                        throw new FormatException(
                            $"Line {line.Number}: a leading empty line of the block scalar is indented more than its first content line. Use an explicit indentation indicator, or remove the extra white space.");
                    }
                }
                else if (line.Indent < contentIndent)
                {
                    break;
                }

                raw.Add(line.Raw.Substring(contentIndent));
                _pos++;
            }

            // Trailing blank lines are subject to chomping; count and remove them.
            int trailing = 0;
            while (raw.Count > 0 && raw[raw.Count - 1].Length == 0)
            {
                raw.RemoveAt(raw.Count - 1);
                trailing++;
            }

            var builder = new StringBuilder();
            if (literal)
            {
                for (int i = 0; i < raw.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append('\n');
                    }

                    builder.Append(raw[i]);
                }
            }
            else
            {
                // Folded: adjacent non-empty, non-indented lines join with a space; empty lines become newlines;
                // more-indented lines keep their line breaks.
                bool previousMoreIndented = false;
                int emptyRun = 0;
                for (int i = 0; i < raw.Count; i++)
                {
                    string text = raw[i];
                    if (text.Length == 0)
                    {
                        emptyRun++;
                        continue;
                    }

                    bool moreIndented = text[0] is ' ' or '\t';
                    if (builder.Length == 0)
                    {
                        // Leading empty lines are content: each becomes a line break.
                        builder.Append('\n', emptyRun);
                    }
                    else if (emptyRun > 0)
                    {
                        builder.Append('\n', emptyRun + (moreIndented || previousMoreIndented ? 1 : 0));
                    }
                    else
                    {
                        builder.Append(moreIndented || previousMoreIndented ? '\n' : ' ');
                    }

                    emptyRun = 0;
                    builder.Append(text);
                    previousMoreIndented = moreIndented;
                }
            }

            if (builder.Length > 0 || trailing > 0)
            {
                switch (chomping)
                {
                    case 'c' when builder.Length > 0:
                        builder.Append('\n');
                        break;
                    case 'k':
                        builder.Append('\n', builder.Length > 0 ? trailing + 1 : trailing);
                        break;
                }
            }

            return JsonValue.Create(builder.ToString());
        }

        private static JsonArray ParseFlowSequence(string text, int lineNumber)
        {
            var result = new JsonArray();
            int pos = 1;
            while (true)
            {
                pos = SkipSpaces(text, pos);
                if (pos >= text.Length)
                {
                    throw new UnterminatedFlowException($"Line {lineNumber}: unterminated flow sequence.", ']');
                }

                if (text[pos] == ']')
                {
                    pos++;
                    break;
                }

                if (text[pos] == ',')
                {
                    throw new FormatException($"Line {lineNumber}: unexpected ',' in flow sequence; every entry needs a value.");
                }

                ThrowIfFlowExplicitKey(text, pos, ']', lineNumber);
                result.Add(ParseFlowScalar(text, ref pos, lineNumber, ",]"));
                pos = SkipSpaces(text, pos);
                if (pos < text.Length && text[pos] == ':')
                {
                    throw new UnsupportedYamlException($"Line {lineNumber}: compact mappings inside flow sequences ('[key: value]') are not supported in frontmatter.");
                }

                if (pos >= text.Length)
                {
                    throw new UnterminatedFlowException($"Line {lineNumber}: unterminated flow sequence.", ']');
                }

                if (text[pos] == '#' &&
                    text[pos - 1] is ' ' or '\t')
                {
                    // A comment runs to the end of the line, so the collection continues on a later line.
                    throw new UnterminatedFlowException($"Line {lineNumber}: unterminated flow sequence.", ']');
                }

                if (text[pos] == ',')
                {
                    pos++;
                }
                else if (text[pos] != ']')
                {
                    throw new FormatException($"Line {lineNumber}: expected ',' or ']' in flow sequence.");
                }
            }

            if (StripComment(text.Substring(pos), allowLeadingComment: false).Trim(s_yamlWhitespace).Length != 0)
            {
                throw new FormatException($"Line {lineNumber}: unexpected content after the flow sequence.");
            }

            return result;
        }

        private static JsonObject ParseFlowMapping(string text, int lineNumber)
        {
            var result = new JsonObject();
            int pos = 1;
            while (true)
            {
                pos = SkipSpaces(text, pos);
                if (pos >= text.Length)
                {
                    throw new UnterminatedFlowException($"Line {lineNumber}: unterminated flow mapping.", '}');
                }

                if (text[pos] == '}')
                {
                    pos++;
                    break;
                }

                if (text[pos] == ',')
                {
                    throw new FormatException($"Line {lineNumber}: unexpected ',' in flow mapping; every entry needs a key.");
                }

                JsonNode? keyNode;
                bool hasValue;
                if (text[pos] is '"' or '\'')
                {
                    int afterQuote;
                    try
                    {
                        keyNode = JsonValue.Create(ParseQuotedScalar(text, pos, lineNumber, out afterQuote));
                    }
                    catch (UnterminatedQuoteException e)
                    {
                        // The quoted key, and so the collection, continues on a later line.
                        throw new UnterminatedFlowException(e.Message, '}');
                    }

                    pos = SkipSpaces(text, afterQuote);
                    hasValue = pos < text.Length && text[pos] == ':';
                    if (hasValue)
                    {
                        pos++;
                    }
                }
                else
                {
                    ThrowIfFlowExplicitKey(text, pos, '}', lineNumber);

                    // A plain key ends at ',' or '}' (giving it a null value) or at a ':' that is followed by a
                    // space, a flow collection indicator, or the end of the text. A ':' followed by anything else is
                    // part of the key, so "{version:1}" is the key "version:1" with a null value.
                    int start = pos;
                    int colonAt = -1;
                    while (pos < text.Length && text[pos] is not (',' or '}'))
                    {
                        if (text[pos] == ':' &&
                            (pos + 1 == text.Length ||
                             text[pos + 1] is ' ' or '\t' ||
                             Array.IndexOf(s_flowIndicators, text[pos + 1]) >= 0))
                        {
                            colonAt = pos;
                            break;
                        }

                        if (text[pos] == '#' &&
                            text[pos - 1] is ' ' or '\t')
                        {
                            // A comment runs to the end of the line, so the collection continues on a later line.
                            throw new UnterminatedFlowException($"Line {lineNumber}: unterminated flow mapping.", '}');
                        }

                        pos++;
                    }

                    string rawKey = text.Substring(start, pos - start).Trim(s_yamlWhitespace);
                    if (rawKey.Length > 0 && rawKey[0] is '[' or '{')
                    {
                        throw new UnsupportedYamlException($"Line {lineNumber}: nested flow collections are not supported in frontmatter.");
                    }

                    if (rawKey.IndexOfAny(s_flowIndicators) >= 0)
                    {
                        throw new FormatException($"Line {lineNumber}: flow indicators in a mapping key must be quoted.");
                    }

                    // The key is trimmed, so the check below cannot see the ':' that makes "-:" the plain key "-".
                    // Followed by white space or the end of the entry, the indicator cannot start a plain key.
                    bool isIndicatorPlainKey = rawKey is "-" or "?" or ":" &&
                        start + 1 < text.Length &&
                        text[start + 1] == ':';
                    if (!isIndicatorPlainKey)
                    {
                        ThrowIfReservedPlainStart(rawKey, lineNumber);
                    }

                    keyNode = ResolvePlainScalar(rawKey, lineNumber);
                    hasValue = colonAt >= 0;
                    if (hasValue)
                    {
                        pos = colonAt + 1;
                    }
                }

                string key = KeyToString(keyNode);
                if (result.ContainsKey(key))
                {
                    throw new FormatException($"Line {lineNumber}: duplicate key '{key}'.");
                }

                result[key] = hasValue ? ParseFlowScalar(text, ref pos, lineNumber, ",}") : null;
                pos = SkipSpaces(text, pos);
                if (pos >= text.Length)
                {
                    throw new UnterminatedFlowException($"Line {lineNumber}: unterminated flow mapping.", '}');
                }

                if (text[pos] == '#' &&
                    text[pos - 1] is ' ' or '\t')
                {
                    // A comment runs to the end of the line, so the collection continues on a later line.
                    throw new UnterminatedFlowException($"Line {lineNumber}: unterminated flow mapping.", '}');
                }

                if (text[pos] == ',')
                {
                    pos++;
                }
                else if (text[pos] != '}')
                {
                    throw new FormatException($"Line {lineNumber}: expected ',' or '}}' in flow mapping.");
                }
            }

            if (StripComment(text.Substring(pos), allowLeadingComment: false).Trim(s_yamlWhitespace).Length != 0)
            {
                throw new FormatException($"Line {lineNumber}: unexpected content after the flow mapping.");
            }

            return result;
        }

        private static JsonNode? ParseFlowScalar(string text, ref int pos, int lineNumber, string terminators)
        {
            char closer = terminators.IndexOf(']') >= 0 ? ']' : '}';
            pos = SkipSpaces(text, pos);
            if (pos >= text.Length)
            {
                // The value is on a later line, so the collection continues there.
                throw new UnterminatedFlowException($"Line {lineNumber}: unexpected end of flow collection.", closer);
            }

            char c = text[pos];
            if (c is '[' or '{')
            {
                throw new UnsupportedYamlException($"Line {lineNumber}: nested flow collections are not supported in frontmatter.");
            }

            if (c is '"' or '\'')
            {
                string quoted;
                int consumed;
                try
                {
                    quoted = ParseQuotedScalar(text, pos, lineNumber, out consumed);
                }
                catch (UnterminatedQuoteException e)
                {
                    // The quoted scalar, and so the collection, continues on a later line.
                    throw new UnterminatedFlowException(e.Message, closer);
                }

                pos = consumed;
                return JsonValue.Create(quoted);
            }

            int start = pos;
            while (pos < text.Length && terminators.IndexOf(text[pos]) < 0)
            {
                if (text[pos] == '#' &&
                    text[pos - 1] is ' ' or '\t')
                {
                    // A comment runs to the end of the line, so the collection continues on a later line.
                    throw new UnterminatedFlowException($"Line {lineNumber}: unterminated flow collection.", closer);
                }

                pos++;
            }

            string plain = text.Substring(start, pos - start).Trim(s_yamlWhitespace);
            int separator = FindKeySeparator(plain, inFlow: true);
            if (separator >= 0)
            {
                // A flow mapping's value cannot itself be a mapping, and a single 'key: value' pair in a flow sequence
                // cannot hold another one; YAML rejects both. A single pair is valid YAML this reader does not support.
                if (closer == '}' ||
                    FindKeySeparator(plain.Substring(separator + 1), inFlow: true) >= 0)
                {
                    throw new FormatException($"Line {lineNumber}: a plain value in a flow collection cannot contain ': '. Quote the value if it is meant literally.");
                }

                throw new UnsupportedYamlException($"Line {lineNumber}: compact mappings inside flow sequences ('[key: value]') are not supported in frontmatter.");
            }

            if (plain.IndexOfAny(s_flowIndicators) >= 0)
            {
                throw new FormatException($"Line {lineNumber}: flow indicators in a plain scalar must be quoted.");
            }

            ThrowIfReservedPlainStart(plain, lineNumber);
            return ResolvePlainScalar(plain, lineNumber);
        }

        /// <summary>Parses a quoted scalar starting at <paramref name="start"/>; returns the index after the closing quote.</summary>
        private static string ParseQuotedScalar(string text, int start, int lineNumber, out int end)
        {
            char quote = text[start];
            var builder = new StringBuilder();
            int i = start + 1;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == quote)
                {
                    if (quote == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
                    {
                        builder.Append('\'');
                        i += 2;
                        continue;
                    }

                    end = i + 1;
                    string result = builder.ToString();
                    for (int k = 0; k < result.Length; k++)
                    {
                        if (char.IsHighSurrogate(result[k]) && k + 1 < result.Length && char.IsLowSurrogate(result[k + 1]))
                        {
                            k++;
                        }
                        else if (char.IsSurrogate(result[k]))
                        {
                            throw new UnrepresentableValueException($"Line {lineNumber}: the quoted scalar contains an unpaired surrogate escape, which is not a valid Unicode character.");
                        }
                    }

                    return result;
                }

                if (quote == '"' && c == '\\')
                {
                    if (++i >= text.Length)
                    {
                        break;
                    }

                    char e = text[i];
                    switch (e)
                    {
                        case '0': builder.Append('\0'); break;
                        case 'a': builder.Append('\a'); break;
                        case 'b': builder.Append('\b'); break;
                        case 't' or '\t': builder.Append('\t'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'v': builder.Append('\v'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'r': builder.Append('\r'); break;
                        case 'e': builder.Append('\u001B'); break;
                        case ' ': builder.Append(' '); break;
                        case '"': builder.Append('"'); break;
                        case '/': builder.Append('/'); break;
                        case '\\': builder.Append('\\'); break;
                        case 'N': builder.Append('\u0085'); break;
                        case '_': builder.Append('\u00A0'); break;
                        case 'L': builder.Append('\u2028'); break;
                        case 'P': builder.Append('\u2029'); break;
                        case 'x': builder.Append((char)ParseHex(text, i + 1, 2, lineNumber)); i += 2; break;
                        case 'u': builder.Append((char)ParseHex(text, i + 1, 4, lineNumber)); i += 4; break;
                        case 'U':
                            int scalar = ParseHex(text, i + 1, 8, lineNumber);
                            if (scalar is < 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
                            {
                                throw new FormatException($"Line {lineNumber}: '\\U{text.Substring(i + 1, 8)}' is not a valid Unicode scalar value.");
                            }

                            builder.Append(char.ConvertFromUtf32(scalar));
                            i += 8;
                            break;
                        default:
                            throw new FormatException($"Line {lineNumber}: unsupported escape sequence '\\{e}' in double-quoted scalar.");
                    }

                    i++;
                    continue;
                }

                builder.Append(c);
                i++;
            }

            throw new UnterminatedQuoteException($"Line {lineNumber}: unterminated quoted scalar.", quote);
        }

        private static int ParseHex(string text, int start, int length, int lineNumber)
        {
            if (start + length > text.Length ||
                !int.TryParse(text.Substring(start, length), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int value))
            {
                throw new FormatException($"Line {lineNumber}: invalid hexadecimal escape in double-quoted scalar.");
            }

            return value;
        }

        /// <summary>
        /// Renders a resolved key as a JSON object key the way reference parsers do: strings as themselves, null as
        /// the empty string, and booleans and numbers as their JSON text.
        /// </summary>
        private static string KeyToString(JsonNode? key) =>
            key is null ? string.Empty
            : key is JsonValue value && value.TryGetValue(out string? text) ? text
            : key.ToJsonString();

        /// <summary>Resolves an unquoted scalar per the YAML 1.2 core schema.</summary>
        private static JsonNode? ResolvePlainScalar(string text, int lineNumber)
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

            if (TryResolveNumber(text, lineNumber) is { } number)
            {
                return number;
            }

            return JsonValue.Create(text);
        }

        private static JsonNode? TryResolveNumber(string text, int lineNumber)
        {
            // Hexadecimal and octal integers take no sign in the core schema. As in yaml.v3, their values are exact up
            // to 64 bits; a wider value is reported as unsupported rather than converted to another type.
            int bitsPerDigit = 0;
            if (text.StartsWith("0x", StringComparison.Ordinal) && text.Length > 2 && IsAll(text, 2, IsHexDigit))
            {
                bitsPerDigit = 4;
            }
            else if (text.StartsWith("0o", StringComparison.Ordinal) && text.Length > 2 && IsAll(text, 2, static c => c is >= '0' and <= '7'))
            {
                bitsPerDigit = 3;
            }

            if (bitsPerDigit > 0)
            {
                ulong value = 0;
                for (int j = 2; j < text.Length; j++)
                {
                    // Shifting in another digit must not push set bits past 64. Leading zeros keep the value at 0.
                    if (value >> (64 - bitsPerDigit) != 0)
                    {
                        string literal = text.Length > 24 ? text.Substring(0, 24) + "..." : text;
                        throw new UnsupportedYamlException(
                            $"Line {lineNumber}: '{literal}' is a hexadecimal or octal integer wider than 64 bits, which this reader does not support. " +
                            "Supply the frontmatter explicitly, or parse it with a full YAML parser.");
                    }

                    char c = text[j];
                    int digit = c switch
                    {
                        <= '9' => c - '0',
                        <= 'F' => c - 'A' + 10,
                        _ => c - 'a' + 10,
                    };
                    value = (value << bitsPerDigit) + (ulong)digit;
                }

                return JsonNode.Parse(value.ToString(CultureInfo.InvariantCulture));
            }

            int i = 0;
            bool negative = false;
            if (text[0] is '+' or '-')
            {
                negative = text[0] == '-';
                i = 1;
            }

            if (i >= text.Length)
            {
                return null;
            }

            string body = text.Substring(i);

            if (body is ".inf" or ".Inf" or ".INF" or ".nan" or ".NaN" or ".NAN")
            {
                throw new UnrepresentableValueException($"Line {lineNumber}: '{text}' cannot be represented in JSON.");
            }

            // Integer: digits only.
            if (IsAll(body, 0, IsDigit))
            {
                string digits = body.TrimStart('0').PadLeft(1, '0');
                return JsonNode.Parse(negative && digits != "0" ? "-" + digits : digits);
            }

            // Float: [digits][.digits][e[+-]digits], with at least one digit somewhere in the mantissa.
            int mantissaEnd = 0;
            while (mantissaEnd < body.Length && IsDigit(body[mantissaEnd]))
            {
                mantissaEnd++;
            }

            int integerDigits = mantissaEnd;
            int fractionDigits = 0;
            if (mantissaEnd < body.Length && body[mantissaEnd] == '.')
            {
                mantissaEnd++;
                while (mantissaEnd < body.Length && IsDigit(body[mantissaEnd]))
                {
                    mantissaEnd++;
                    fractionDigits++;
                }
            }

            if (integerDigits + fractionDigits == 0)
            {
                return null;
            }

            int exponentEnd = mantissaEnd;
            if (exponentEnd < body.Length && body[exponentEnd] is 'e' or 'E')
            {
                exponentEnd++;
                if (exponentEnd < body.Length && body[exponentEnd] is '+' or '-')
                {
                    exponentEnd++;
                }

                int exponentDigits = 0;
                while (exponentEnd < body.Length && IsDigit(body[exponentEnd]))
                {
                    exponentEnd++;
                    exponentDigits++;
                }

                if (exponentDigits == 0)
                {
                    return null;
                }
            }

            if (exponentEnd != body.Length)
            {
                return null;
            }

            if (!double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) || double.IsInfinity(parsed))
            {
                throw new UnrepresentableValueException($"Line {lineNumber}: '{text}' cannot be represented as a JSON number.");
            }

            // JSON has no negative zero worth preserving: reference parsers render -0 and -0.0 as 0.
            return JsonValue.Create(negative && parsed != 0 ? -parsed : parsed);
        }

        private static bool IsDigit(char c) => c is >= '0' and <= '9';

        private static bool IsHexDigit(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

        private static bool IsAll(string text, int start, Func<char, bool> predicate)
        {
            if (start >= text.Length)
            {
                return false;
            }

            for (int i = start; i < text.Length; i++)
            {
                if (!predicate(text[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int SkipSpaces(string text, int pos)
        {
            while (pos < text.Length && text[pos] is ' ' or '\t')
            {
                pos++;
            }

            return pos;
        }

        /// <summary>Finds a plain key's separator; flow context also treats a following flow indicator as separation.</summary>
        private static int FindKeySeparator(string content, bool inFlow = false)
        {
            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] == ':' &&
                    (i + 1 == content.Length ||
                     content[i + 1] is ' ' or '\t' ||
                     (inFlow && Array.IndexOf(s_flowIndicators, content[i + 1]) >= 0)))
                {
                    return i;
                }

                if (content[i] is ' ' or '\t' && i + 1 < content.Length && content[i + 1] == '#')
                {
                    // Anything after " #" is a comment; a key cannot be separated inside one.
                    return -1;
                }
            }

            return -1;
        }

        /// <summary>
        /// Prepares the remainder of a line for <see cref="ParseValue"/>: quoted values and flow collections are
        /// returned as written, since their own parsers find their end and check what follows; anything else is a
        /// plain value, from which a trailing comment is removed.
        /// </summary>
        private static string PrepareValue(string rest, out bool commentEndedValue)
        {
            commentEndedValue = false;
            rest = rest.TrimStart(s_yamlWhitespace);
            if (rest.Length > 0 && rest[0] is '"' or '\'' or '[' or '{')
            {
                return rest.TrimEnd(s_yamlWhitespace);
            }

            string stripped = StripComment(rest);
            commentEndedValue = stripped.Length != rest.Length && stripped.Trim(s_yamlWhitespace).Length > 0;
            return stripped.Trim(s_yamlWhitespace);
        }

        /// <summary>
        /// Removes a trailing comment from plain text. A comment must follow whitespace unless the text already
        /// starts at a separation boundary; quotes inside plain text never suppress a comment.
        /// </summary>
        private static string StripComment(string text, bool allowLeadingComment = true)
        {
            for (int i = 0; i < text.Length; i++)
            {
                bool isSeparated = i == 0 ? allowLeadingComment : text[i - 1] is ' ' or '\t';
                if (text[i] == '#' &&
                    isSeparated)
                {
                    return text.Substring(0, i);
                }
            }

            return text;
        }
    }
}

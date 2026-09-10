using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Reads the YAML frontmatter of a <c>SKILL.md</c> into a <see cref="JsonObject"/>, without a YAML library.
/// Servers use it to publish a skill's entry from its file; hosts can use it to compare a fetched <c>SKILL.md</c> against the entry.
/// </summary>
/// <remarks>
/// <para>
/// Agent Skills frontmatter is a small, regular subset of YAML: a block mapping of scalars, with at most a nested
/// mapping (<c>metadata</c>) and the occasional sequence. This reader accepts that subset deliberately and
/// rejects everything else with a <see cref="FormatException"/> naming the construct, so that a skill whose
/// frontmatter it cannot represent faithfully is never published with a guessed rendering. Anchors, aliases,
/// tags, multi-document streams, complex keys, and tab indentation are rejected.
/// </para>
/// <para>
/// Unquoted scalars are resolved per the YAML 1.2 core schema (<c>null</c>, booleans, integers, finite floats,
/// otherwise strings), which is what the YAML libraries used by other MCP SDKs and hosts do. A host verifies a
/// skill by parsing the fetched <c>SKILL.md</c> with its own YAML library and comparing field by field against the
/// entry, so matching that resolution is what makes the published frontmatter verifiable.
/// </para>
/// </remarks>
public static class SkillFrontmatter
{
    private const string Delimiter = "---";

    /// <summary>YAML's white space characters. Other Unicode spaces, such as U+00A0, are scalar content.</summary>
    private static readonly char[] s_yamlWhitespace = [' ', '\t'];

    /// <summary>The deepest nesting of block collections the reader accepts. Frontmatter is shallow; this only exists to keep a hostile file from exhausting the stack.</summary>
    private const int MaxNestingDepth = 32;

    /// <summary>
    /// Thrown for YAML that is valid but outside the subset this reader supports, as opposed to malformed
    /// frontmatter. Callers that accept explicitly supplied frontmatter may fall back on it for this case only.
    /// </summary>
    internal sealed class UnsupportedYamlException(string message) : FormatException(message);

    /// <summary>Thrown when a quoted scalar has no closing quote on its line. Malformed unless a later line closes it.</summary>
    private sealed class UnterminatedQuoteException(string message, char quote) : FormatException(message)
    {
        public char Quote { get; } = quote;
    }

    /// <summary>
    /// Parses the frontmatter at the start of <paramref name="skillMarkdown"/>.
    /// </summary>
    /// <param name="skillMarkdown">The full text of a <c>SKILL.md</c>.</param>
    /// <returns>The frontmatter as a JSON object.</returns>
    /// <exception cref="FormatException">
    /// The text does not begin with a <c>---</c>-delimited frontmatter block, the block is not a mapping, or it
    /// uses YAML this reader does not support.
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
            body.Add(new Line(lines[i], i + 1));
        }

        var parser = new Parser(body);
        var root = parser.ParseDocument();
        return root as JsonObject ?? throw new FormatException("The frontmatter must be a YAML mapping of keys to values.");
    }

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

            if (indent < raw.Length && raw[indent] == '\t')
            {
                throw new FormatException($"Line {number}: tabs are not allowed for indentation in YAML frontmatter.");
            }

            Indent = indent;
            Content = raw.Substring(indent);
            IsBlank = Content.Length == 0 || Content[0] == '#';
        }

        public string Raw { get; }
        public int Number { get; }
        public int Indent { get; }
        public string Content { get; }

        /// <summary>Whether the line is empty or a comment, and therefore structurally insignificant.</summary>
        public bool IsBlank { get; }
    }

    private sealed class Parser(List<Line> lines)
    {
        private readonly List<Line> _lines = lines;
        private int _pos;
        private int _depth;

        public JsonNode? ParseDocument()
        {
            SkipBlank();
            if (_pos >= _lines.Count)
            {
                return new JsonObject();
            }

            var first = _lines[_pos];
            if (first.Indent != 0)
            {
                throw new FormatException($"Line {first.Number}: the frontmatter must start at column 1.");
            }

            var node = ParseBlock(0);
            SkipBlank();
            if (_pos < _lines.Count)
            {
                throw new FormatException($"Line {_lines[_pos].Number}: unexpected content after the frontmatter mapping.");
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

        /// <summary>Parses the block node formed by the lines at <paramref name="indent"/>.</summary>
        private JsonNode? ParseBlock(int indent)
        {
            SkipBlank();
            var line = _lines[_pos];
            if (_depth >= MaxNestingDepth)
            {
                throw new FormatException($"Line {line.Number}: the frontmatter is nested more than {MaxNestingDepth} levels deep.");
            }

            _depth++;
            try
            {
                return IsSequenceEntry(line.Content) ? ParseSequence(indent) : ParseMapping(indent);
            }
            finally
            {
                _depth--;
            }
        }

        private static bool IsSequenceEntry(string content) =>
            content.Length > 0 && content[0] == '-' && (content.Length == 1 || content[1] == ' ');

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

                if (IsSequenceEntry(line.Content))
                {
                    throw new FormatException($"Line {line.Number}: a sequence entry cannot appear directly inside a mapping. Put it under a key.");
                }

                string content = line.Content;
                if (content[0] == '?')
                {
                    throw new UnsupportedYamlException($"Line {line.Number}: complex mapping keys ('? ') are not supported in frontmatter.");
                }

                int consumed;
                string key;
                if (content[0] is '"' or '\'')
                {
                    key = ParseQuotedScalar(content, 0, line.Number, out consumed);
                    int colon = SkipSpaces(content, consumed);
                    if (colon >= content.Length || content[colon] != ':' || (colon + 1 < content.Length && content[colon + 1] != ' '))
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
                        throw new FormatException($"Line {line.Number}: empty mapping key.");
                    }

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

                string item = line.Content.Length > 1 ? line.Content.Substring(2) : string.Empty;
                int itemIndent = indent + 2;
                _pos++;

                string trimmedItem = item.TrimStart(s_yamlWhitespace);
                if (trimmedItem.Length == 0 || trimmedItem[0] == '#')
                {
                    // "- " followed by nothing: the value is the more-indented block that follows, or null.
                    var next = PeekSignificant();
                    result.Add(next is { } n && n.Indent > indent ? ParseBlock(n.Indent) : null);
                    continue;
                }

                itemIndent = indent + 2 + (item.Length - trimmedItem.Length);
                if (IsSequenceEntry(trimmedItem) || (trimmedItem[0] is not ('"' or '\'' or '[' or '{' or '|' or '>' or '&' or '*' or '!') && FindKeySeparator(trimmedItem) >= 0))
                {
                    // A compact nested node ("- key: value" or "- - x"): re-read this line as if the item started on
                    // its own line at the item's column, then continue with the block at that indentation.
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
                        return ParseBlock(n.Indent);
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

                case '[':
                    return ParseFlowSequence(rest, lineNumber);

                case '{':
                    return ParseFlowMapping(rest, lineNumber);

                case '"' or '\'':
                    string quoted;
                    int consumed;
                    try
                    {
                        quoted = ParseQuotedScalar(rest, 0, lineNumber, out consumed);
                    }
                    catch (UnterminatedQuoteException e) when (ClosesLater(e.Quote))
                    {
                        // Valid YAML (a quoted scalar spanning lines) that this reader does not support, as opposed
                        // to a quote that is never closed, which is malformed and stays a plain FormatException.
                        throw new UnsupportedYamlException($"Line {lineNumber}: multi-line quoted scalars are not supported in frontmatter.");
                    }

                    if (StripComment(rest.Substring(consumed)).Trim(s_yamlWhitespace).Length != 0)
                    {
                        throw new FormatException($"Line {lineNumber}: unexpected content after the quoted value.");
                    }

                    return JsonValue.Create(quoted);

                default:
                    if (rest[0] is '@' or '`' or '%')
                    {
                        throw new FormatException($"Line {lineNumber}: a plain scalar cannot start with '{rest[0]}', which YAML reserves. Quote the value.");
                    }

                    if (IsSequenceEntry(rest))
                    {
                        throw new FormatException($"Line {lineNumber}: a sequence cannot start on the same line as its key. Put the '- ' entries on the following lines, or quote the value if '-' is meant literally.");
                    }

                    if (FindKeySeparator(rest) >= 0)
                    {
                        throw new FormatException($"Line {lineNumber}: a plain scalar cannot contain ': '. Quote the value if it is meant literally.");
                    }

                    if (commentEndedValue)
                    {
                        // A comment ends a plain scalar. A more-indented line after it cannot continue the scalar
                        // and cannot start a nested block either, so it is an error, as it is to a YAML parser.
                        if (PeekSignificant() is { } following && following.Indent > parentIndent)
                        {
                            throw new FormatException(
                                $"Line {following.Number}: this line cannot continue the value on line {lineNumber}, because a comment ended that value.");
                        }

                        return ResolvePlainScalar(rest, lineNumber);
                    }

                    return ResolvePlainScalar(CollectPlainContinuation(rest, parentIndent, lineNumber), lineNumber);
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

        /// <summary>Folds the more-indented continuation lines of a plain multi-line scalar into it.</summary>
        private string CollectPlainContinuation(string first, int parentIndent, int lineNumber)
        {
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

                string text = StripComment(line.Content).Trim(s_yamlWhitespace);
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
            while (_pos < _lines.Count)
            {
                var line = _lines[_pos];
                if (line.Content.Length == 0)
                {
                    raw.Add(string.Empty);
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

                    bool moreIndented = text[0] == ' ';
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
                    throw new FormatException($"Line {lineNumber}: unterminated flow sequence; multi-line flow collections are not supported.");
                }

                if (text[pos] == ']')
                {
                    pos++;
                    break;
                }

                result.Add(ParseFlowScalar(text, ref pos, lineNumber, ",]"));
                pos = SkipSpaces(text, pos);
                if (pos < text.Length && text[pos] == ',')
                {
                    pos++;
                }
                else if (pos >= text.Length || text[pos] != ']')
                {
                    throw new FormatException($"Line {lineNumber}: expected ',' or ']' in flow sequence.");
                }
            }

            if (StripComment(text.Substring(pos)).Trim(s_yamlWhitespace).Length != 0)
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
                    throw new FormatException($"Line {lineNumber}: unterminated flow mapping; multi-line flow collections are not supported.");
                }

                if (text[pos] == '}')
                {
                    pos++;
                    break;
                }

                JsonNode? keyNode;
                bool hasValue;
                if (text[pos] is '"' or '\'')
                {
                    keyNode = JsonValue.Create(ParseQuotedScalar(text, pos, lineNumber, out int afterQuote));
                    pos = SkipSpaces(text, afterQuote);
                    hasValue = pos < text.Length && text[pos] == ':';
                    if (hasValue)
                    {
                        pos++;
                    }
                }
                else
                {
                    // A plain key ends at ',' or '}' (giving it a null value) or at a ':' that is followed by a
                    // space, a comma, a closing brace, or the end of the text. A ':' followed by anything else is
                    // part of the key, so "{version:1}" is the key "version:1" with a null value.
                    int start = pos;
                    int colonAt = -1;
                    while (pos < text.Length && text[pos] is not (',' or '}'))
                    {
                        if (text[pos] == ':' && (pos + 1 == text.Length || text[pos + 1] is ' ' or ',' or '}'))
                        {
                            colonAt = pos;
                            break;
                        }

                        if (text[pos] == '#' && (pos == start || text[pos - 1] is ' ' or '\t'))
                        {
                            throw new FormatException($"Line {lineNumber}: a comment inside a flow collection runs to the end of the line, leaving the collection unterminated. Move the comment after the closing brace.");
                        }

                        pos++;
                    }

                    string rawKey = text.Substring(start, pos - start).Trim(s_yamlWhitespace);
                    if (rawKey.Length > 0 && rawKey[0] is '[' or '{')
                    {
                        throw new UnsupportedYamlException($"Line {lineNumber}: nested flow collections are not supported in frontmatter.");
                    }

                    if (rawKey.Length > 0 && rawKey[0] is '&' or '*' or '!')
                    {
                        throw new UnsupportedYamlException($"Line {lineNumber}: YAML anchors, aliases, and tags are not supported in frontmatter.");
                    }

                    if (rawKey.Length > 0 && rawKey[0] is '@' or '`' or '%')
                    {
                        throw new FormatException($"Line {lineNumber}: a plain scalar cannot start with '{rawKey[0]}', which YAML reserves. Quote the value.");
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
                if (pos < text.Length && text[pos] == ',')
                {
                    pos++;
                }
                else if (pos >= text.Length || text[pos] != '}')
                {
                    throw new FormatException($"Line {lineNumber}: expected ',' or '}}' in flow mapping.");
                }
            }

            if (StripComment(text.Substring(pos)).Trim(s_yamlWhitespace).Length != 0)
            {
                throw new FormatException($"Line {lineNumber}: unexpected content after the flow mapping.");
            }

            return result;
        }

        private static JsonNode? ParseFlowScalar(string text, ref int pos, int lineNumber, string terminators)
        {
            pos = SkipSpaces(text, pos);
            if (pos >= text.Length)
            {
                throw new FormatException($"Line {lineNumber}: unexpected end of flow collection.");
            }

            char c = text[pos];
            if (c is '[' or '{')
            {
                throw new UnsupportedYamlException($"Line {lineNumber}: nested flow collections are not supported in frontmatter.");
            }

            if (c is '"' or '\'')
            {
                string quoted = ParseQuotedScalar(text, pos, lineNumber, out int consumed);
                pos = consumed;
                return JsonValue.Create(quoted);
            }

            int start = pos;
            while (pos < text.Length && terminators.IndexOf(text[pos]) < 0)
            {
                if (text[pos] == '#' && (pos == start || text[pos - 1] is ' ' or '\t'))
                {
                    throw new FormatException($"Line {lineNumber}: a comment inside a flow collection runs to the end of the line, leaving the collection unterminated. Move the comment after the closing bracket.");
                }

                pos++;
            }

            string plain = text.Substring(start, pos - start).Trim(s_yamlWhitespace);
            if (FindKeySeparator(plain) >= 0)
            {
                throw new UnsupportedYamlException($"Line {lineNumber}: compact mappings inside flow sequences ('[key: value]') are not supported in frontmatter.");
            }

            if (plain.Length > 0 && plain[0] is '&' or '*' or '!')
            {
                throw new UnsupportedYamlException($"Line {lineNumber}: YAML anchors, aliases, and tags are not supported in frontmatter.");
            }

            if (plain.Length > 0 && plain[0] is '@' or '`' or '%')
            {
                throw new FormatException($"Line {lineNumber}: a plain scalar cannot start with '{plain[0]}', which YAML reserves. Quote the value.");
            }

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
                            throw new FormatException($"Line {lineNumber}: the quoted scalar contains an unpaired surrogate escape, which is not a valid Unicode character.");
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
            // Hexadecimal and octal integers take no sign in the core schema, and may exceed 64 bits.
            if (text.StartsWith("0x", StringComparison.Ordinal) && text.Length > 2 && IsAll(text, 2, IsHexDigit))
            {
                var value = BigInteger.Parse("0" + text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                return JsonNode.Parse(value.ToString(CultureInfo.InvariantCulture));
            }

            if (text.StartsWith("0o", StringComparison.Ordinal) && text.Length > 2 && IsAll(text, 2, static c => c is >= '0' and <= '7'))
            {
                BigInteger value = BigInteger.Zero;
                for (int j = 2; j < text.Length; j++)
                {
                    value = (value * 8) + (text[j] - '0');
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
                throw new FormatException($"Line {lineNumber}: '{text}' cannot be represented in JSON.");
            }

            // Integer: digits only.
            if (IsAll(body, 0, IsDigit))
            {
                return JsonNode.Parse((negative ? "-" : string.Empty) + body.TrimStart('0').PadLeft(1, '0'));
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
                throw new FormatException($"Line {lineNumber}: '{text}' cannot be represented as a JSON number.");
            }

            return JsonValue.Create(negative ? -parsed : parsed);
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
            while (pos < text.Length && text[pos] == ' ')
            {
                pos++;
            }

            return pos;
        }

        /// <summary>Finds the ':' that separates a plain key from its value: the first ':' followed by a space or the end of the line.</summary>
        private static int FindKeySeparator(string content)
        {
            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] == ':' && (i + 1 == content.Length || content[i + 1] == ' '))
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
        /// Removes a trailing comment from plain text. A comment starts at a '#' that begins the text or follows
        /// whitespace; quotes inside plain text are ordinary characters, so an apostrophe never suppresses a comment.
        /// </summary>
        private static string StripComment(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '#' && (i == 0 || text[i - 1] is ' ' or '\t'))
                {
                    return text.Substring(0, i);
                }
            }

            return text;
        }
    }
}

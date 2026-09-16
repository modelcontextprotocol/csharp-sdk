#if NET
using System.Buffers;
#endif
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ModelContextProtocol;

/// <summary>Provides basic support for parsing and formatting URI templates.</summary>
/// <remarks>
/// This implementation should correctly handle valid URI templates, but it has undefined output for invalid templates,
/// e.g. it may treat portions of invalid templates as literals rather than throwing.
/// </remarks>
internal static partial class UriTemplate
{
    /// <summary>Regex pattern for finding URI template expressions and parsing out the operator and varname.</summary>
    private const string UriTemplateExpressionPattern = """
        {                                                       # opening brace
            (?<operator>[+#./;?&]?)                             # optional operator
            (?<varname>
                (?:[A-Za-z0-9_]|%[0-9A-Fa-f]{2})                # varchar: letter, digit, underscore, or pct-encoded
                (?:\.?(?:[A-Za-z0-9_]|%[0-9A-Fa-f]{2}))*        # optionally dot-separated subsequent varchars
            )
            (?: :[1-9][0-9]{0,3} )?                             # optional prefix modifier (1–4 digits)
            \*?                                                 # optional explode
            (?:,                                                # comma separator, followed by the same as above
                (?<varname>
                    (?:[A-Za-z0-9_]|%[0-9A-Fa-f]{2})
                    (?:\.?(?:[A-Za-z0-9_]|%[0-9A-Fa-f]{2}))*
                )
                (?: :[1-9][0-9]{0,3} )?
                \*?
            )*                                                  # zero or more additional vars
        }                                                       # closing brace
        """;

    /// <summary>Gets a regex for finding URI template expressions and parsing out the operator and varname.</summary>
    /// <remarks>
    /// This regex is for parsing a static URI template.
    /// It is not for parsing a URI according to a template.
    /// </remarks>
#if NET
    [GeneratedRegex(UriTemplateExpressionPattern, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex UriTemplateExpression();
#else
    private static Regex UriTemplateExpression() => s_uriTemplateExpression;
    private static readonly Regex s_uriTemplateExpression = new(UriTemplateExpressionPattern, RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);
#endif

#if NET
    /// <summary>SearchValues for characters that needn't be escaped when allowing reserved characters.</summary>
    private static readonly SearchValues<char> s_appendWhenAllowReserved = SearchValues.Create(
        "abcdefghijklmnopqrstuvwxyz" + // ASCII lowercase letters
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ" + // ASCII uppercase letters
        "0123456789" +                 // ASCII digits
        "-._~" +                       // unreserved characters
        ":/?#[]@!$&'()*+,;=");         // reserved characters
#endif

    /// <summary>Create a <see cref="Regex"/> for matching a URI against a URI template.</summary>
    /// <param name="uriTemplate">The template against which to match.</param>
    /// <returns>A regex pattern that can be used to match the specified URI template.</returns>
    public static Regex CreateParser(string uriTemplate)
    {
        DefaultInterpolatedStringHandler pattern = new(0, 0, CultureInfo.InvariantCulture, stackalloc char[256]);
        pattern.AppendLiteral("^");

        int lastIndex = 0;
        for (Match m = UriTemplateExpression().Match(uriTemplate); m.Success; m = m.NextMatch())
        {
            pattern.AppendFormatted(Regex.Escape(uriTemplate[lastIndex..m.Index]));
            lastIndex = m.Index + m.Length;

            var captures = m.Groups["varname"].Captures;
            List<string> paramNames = new(captures.Count);
            foreach (Capture c in captures)
            {
                paramNames.Add(c.Value);
            }

            switch (m.Groups["operator"].Value)
            {
                case "+": AppendExpression(ref pattern, paramNames, null, "[^?&#]*"); break;
                case "#": AppendExpression(ref pattern, paramNames, '#', ".*"); break;
                case ".": AppendExpression(ref pattern, paramNames, '.', "[^/?#]*"); break;
                case "/": AppendExpression(ref pattern, paramNames, '/', "[^/?#]*"); break;
                default:  AppendExpression(ref pattern, paramNames, null, "[^/?&#]*"); break;

                case "?": AppendQueryExpression(ref pattern, paramNames, '?'); break;
                case "&": AppendQueryExpression(ref pattern, paramNames, '&'); break;
                case ";": AppendPathParameterExpression(ref pattern, paramNames); break;
            }
        }

        pattern.AppendFormatted(Regex.Escape(uriTemplate.Substring(lastIndex)));
        pattern.AppendLiteral("$");

        return new Regex(
            pattern.ToStringAndClear(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant |
#if NET
            RegexOptions.NonBacktracking);
#else
            RegexOptions.Compiled, TimeSpan.FromSeconds(10));
#endif

        // Appends a regex fragment to `pattern` that matches an optional query string starting
        // with the given `prefix` (? or &), and up to one occurrence of each name in
        // `paramNames`. Each parameter is made optional and captured by a named group
        // of the form “paramName=value”.
        static void AppendQueryExpression(ref DefaultInterpolatedStringHandler pattern, List<string> paramNames, char prefix)
        {
            Debug.Assert(prefix is '?' or '&');

            pattern.AppendLiteral("(?:\\");
            pattern.AppendFormatted(prefix);

            if (paramNames.Count > 0)
            {
                AppendParameter(ref pattern, paramNames[0]);
                for (int i = 1; i < paramNames.Count; i++)
                {
                    pattern.AppendLiteral("\\&?");
                    AppendParameter(ref pattern, paramNames[i]);
                }

                static void AppendParameter(ref DefaultInterpolatedStringHandler pattern, string paramName)
                {
                    paramName = Regex.Escape(paramName);
                    pattern.AppendLiteral("(?:");
                    pattern.AppendFormatted(paramName);
                    pattern.AppendLiteral("=(?<");
                    pattern.AppendFormatted(paramName);
                    pattern.AppendLiteral(">[^/?&]*))?");
                }
            }

            pattern.AppendLiteral(")?");
        }

        // Chooses a regex character‐class (`valueChars`) based on the initial `prefix` to define which
        // characters make up a parameter value. Then, for each name in `paramNames`, it optionally
        // appends the escaped `prefix` (only on the first parameter, then switches to ','), and
        // adds an optional named capture group `(?<paramName>valueChars)` to match and capture that value.
        // Note: For "+" (reserved expansion) operator, prefix is null but valueChars allows "/" characters.
        // Note: For "." (label expansion) operator, the separator is "." instead of ",".
        static void AppendExpression(ref DefaultInterpolatedStringHandler pattern, List<string> paramNames, char? prefix, string valueChars)
        {
            Debug.Assert(prefix is '#' or '/' or '.' or null);

            if (paramNames.Count > 0)
            {
                if (prefix is not null)
                {
                    pattern.AppendLiteral("\\");
                    pattern.AppendFormatted(prefix);
                    pattern.AppendLiteral("?");
                }

                AppendParameter(ref pattern, paramNames[0], valueChars);

                // For label expansion (.), the separator between values is also a dot
                // For path segment expansion (/), the separator between values is also a slash
                string separator = prefix switch
                {
                    '.' => "\\.",
                    '/' => "\\/",
                    _ => "\\,"
                };
                for (int i = 1; i < paramNames.Count; i++)
                {
                    pattern.AppendFormatted(separator);
                    pattern.AppendLiteral("?");
                    AppendParameter(ref pattern, paramNames[i], valueChars);
                }

                static void AppendParameter(ref DefaultInterpolatedStringHandler pattern, string paramName, string valueChars)
                {
                    pattern.AppendLiteral("(?<");
                    pattern.AppendFormatted(Regex.Escape(paramName));
                    pattern.AppendLiteral(">");
                    pattern.AppendFormatted(valueChars);
                    pattern.AppendLiteral(")?");
                }
            }
        }

        // Appends a regex fragment for path-style parameter expansion (;).
        // Format: ;name=value or ;name (if value is empty), separated by semicolons.
        // Each parameter is made optional and captured by a named group.
        static void AppendPathParameterExpression(ref DefaultInterpolatedStringHandler pattern, List<string> paramNames)
        {
            if (paramNames.Count > 0)
            {
                AppendParameter(ref pattern, paramNames[0]);
                for (int i = 1; i < paramNames.Count; i++)
                {
                    AppendParameter(ref pattern, paramNames[i]);
                }

                static void AppendParameter(ref DefaultInterpolatedStringHandler pattern, string paramName)
                {
                    // Match ;name or ;name=value
                    paramName = Regex.Escape(paramName);
                    pattern.AppendLiteral("(?:;");
                    pattern.AppendFormatted(paramName);
                    pattern.AppendLiteral("(?:=(?<");
                    pattern.AppendFormatted(paramName);
                    pattern.AppendLiteral(">[^;/?&]*))?)?");
                }
            }
        }
    }

    /// <summary>
    /// Expand a URI template using the given variable values.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="uriTemplate"/> is <see langword="null"/>.</exception>
    public static string FormatUri(string uriTemplate, IReadOnlyDictionary<string, object?> arguments)
    {
        Throw.IfNull(uriTemplate);

        ReadOnlySpan<char> uriTemplateSpan = uriTemplate.AsSpan();
        DefaultInterpolatedStringHandler builder = new(0, 0, CultureInfo.InvariantCulture, stackalloc char[256]);
        while (!uriTemplateSpan.IsEmpty)
        {
            // Find the next expression.
            int openBracePos = uriTemplateSpan.IndexOf('{');
            if (openBracePos < 0)
            {
                if (uriTemplate.Length == uriTemplateSpan.Length)
                {
                    return uriTemplate;
                }

                builder.AppendFormatted(uriTemplateSpan);
                break;
            }

            // Append as a literal everything before the next expression.
            builder.AppendFormatted(uriTemplateSpan.Slice(0, openBracePos));
            uriTemplateSpan = uriTemplateSpan.Slice(openBracePos + 1);

            int closeBracePos = uriTemplateSpan.IndexOf('}');
            if (closeBracePos < 0)
            {
                throw new FormatException($"Unmatched '{{' in URI template '{uriTemplate}'");
            }

            ReadOnlySpan<char> expression = uriTemplateSpan.Slice(0, closeBracePos);
            uriTemplateSpan = uriTemplateSpan.Slice(closeBracePos + 1);
            if (expression.IsEmpty)
            {
                continue;
            }

            // The start of the expression may be a modifier; if it is, slice it off the expression.
            char modifier = expression[0];
            (string Prefix, string Separator, bool Named, bool IncludeNameIfEmpty, bool IncludeSeparatorIfEmpty, bool AllowReserved, bool PrefixEmptyExpansions, int ExpressionSlice) modifierBehavior = modifier switch
            {
                '+' => (string.Empty, ",", false, false, true, true, false, 1),
                '#' => ("#", ",", false, false, true, true, true, 1),
                '.' => (".", ".", false, false, true, false, true, 1),
                '/' => ("/", "/", false, false, true, false, false, 1),
                ';' => (";", ";", true, true, false, false, false, 1),
                '?' => ("?", "&", true, true, true, false, false, 1),
                '&' => ("&", "&", true, true, true, false, false, 1),
                _   => (string.Empty, ",", false, false, true, false, false, 0),
            };
            expression = expression.Slice(modifierBehavior.ExpressionSlice);

            List<string> expansions = [];

            // Process each varspec in the comma-delimited list in the expression (if it doesn't have any
            // commas, it will be the whole expression).
            while (!expression.IsEmpty)
            {
                // Find the next name.
                int commaPos = expression.IndexOf(',');
                ReadOnlySpan<char> name;
                if (commaPos < 0)
                {
                    name = expression;
                    expression = [];
                }
                else
                {
                    name = expression.Slice(0, commaPos);
                    expression = expression.Slice(commaPos + 1);
                }

                bool explode = false;
                int prefixLength = -1;

                // If the name ends with a *, it means we should explode the value into separate
                // name=value pairs. If it has a colon, it means we should only take the first N characters
                // of the value. If it has both, the * takes precedence and we ignore the colon.
                if (!name.IsEmpty && name[name.Length - 1] == '*')
                {
                    explode = true;
                    name = name.Slice(0, name.Length - 1);
                }
                else if (name.IndexOf(':') >= 0)
                {
                    int colonPos = name.IndexOf(':');
                    if (colonPos < 0)
                    {
                        throw new FormatException($"Invalid varspec '{name.ToString()}'");
                    }

                    if (!int.TryParse(name.Slice(colonPos + 1)
#if !NET
                        .ToString()
#endif
                        , out prefixLength))
                    {
                        throw new FormatException($"Invalid prefix length in varspec '{name.ToString()}'");
                    }

                    name = name.Slice(0, colonPos);
                }

                // Look up the value for this name. If it doesn't exist, skip it.
                string nameString = name.ToString();
                if (!arguments.TryGetValue(nameString, out var value) || value is null)
                {
                    continue;
                }

                if (value is IEnumerable<string> list)
                {
                    var items = list.Select(i => Encode(i, modifierBehavior.AllowReserved));
                    if (explode)
                    {
                        if (modifierBehavior.Named)
                        {
                            foreach (var item in items)
                            {
                                expansions.Add($"{nameString}={item}");
                            }
                        }
                        else
                        {
                            foreach (var item in items)
                            {
                                expansions.Add(item);
                            }
                        }
                    }
                    else
                    {
                        var joined = string.Join(",", items);
                        expansions.Add(joined.Length > 0 && modifierBehavior.Named ?
                            $"{nameString}={joined}" :
                            joined);
                    }
                }
                else if (value is IReadOnlyDictionary<string, string> assoc)
                {
                    var pairs = assoc.Select(kvp => (
                        Encode(kvp.Key, modifierBehavior.AllowReserved),
                        Encode(kvp.Value, modifierBehavior.AllowReserved)
                    ));

                    if (explode)
                    {
                        foreach (var (k, v) in pairs)
                        {
                            expansions.Add($"{k}={v}");
                        }
                    }
                    else
                    {
                        var joined = string.Join(",", pairs.Select(p => $"{p.Item1},{p.Item2}"));
                        if (joined.Length > 0)
                        {
                            expansions.Add(modifierBehavior.Named ? $"{nameString}={joined}" : joined);
                        }
                    }
                }
                else
                {
                    string s =
                        value as string ??
                        (value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString()) ??
                        string.Empty;

                    s = Encode((uint)prefixLength < s.Length ? s.Substring(0, prefixLength) : s, modifierBehavior.AllowReserved);
                    if (!modifierBehavior.Named)
                    {
                        expansions.Add(s);
                    }
                    else if (s.Length != 0 || modifierBehavior.IncludeNameIfEmpty)
                    {
                        expansions.Add(
                            s.Length != 0 ? $"{nameString}={s}" :
                            modifierBehavior.IncludeSeparatorIfEmpty ? $"{nameString}=" :
                            nameString);
                    }
                }
            }

            if (expansions.Count > 0 &&
                (modifierBehavior.PrefixEmptyExpansions || !expansions.All(string.IsNullOrEmpty)))
            {
                builder.AppendLiteral(modifierBehavior.Prefix);
                AppendJoin(ref builder, modifierBehavior.Separator, expansions);
            }
        }

        return builder.ToStringAndClear();
    }

    private static void AppendJoin(ref DefaultInterpolatedStringHandler builder, string separator, List<string> values)
    {
        int count = values.Count;
        if (count > 0)
        {
            builder.AppendLiteral(values[0]);
            for (int i = 1; i < count; i++)
            {
                builder.AppendLiteral(separator);
                builder.AppendLiteral(values[i]);
            }
        }
    }

    private static string Encode(string value, bool allowReserved)
    {
        if (!allowReserved)
        {
            return Uri.EscapeDataString(value);
        }

        DefaultInterpolatedStringHandler builder = new(0, 0, CultureInfo.InvariantCulture, stackalloc char[256]);
        int i = 0;
#if NET
        i = value.AsSpan().IndexOfAnyExcept(s_appendWhenAllowReserved);
        if (i < 0)
        {
            return value;
        }

        builder.AppendFormatted(value.AsSpan(0, i));
#endif

        for (; i < value.Length; ++i)
        {
            char c = value[i];
            if (((uint)((c | 0x20) - 'a') <= 'z' - 'a') ||
                ((uint)(c - '0') <= '9' - '0') ||
                "-._~:/?#[]@!$&'()*+,;=".Contains(c))
            {
                builder.AppendFormatted(c);
            }
            else if (c == '%' && i < value.Length - 2 && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                builder.AppendFormatted(value.AsSpan(i, 3));
                i += 2;
            }
            else
            {
                AppendHex(ref builder, c);
            }
        }

        return builder.ToStringAndClear();

        static void AppendHex(ref DefaultInterpolatedStringHandler builder, char c)
        {
            ReadOnlySpan<char> hexDigits = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F'];

            if (c <= 0x7F)
            {
                builder.AppendLiteral("%");
                builder.AppendFormatted(hexDigits[c >> 4]);
                builder.AppendFormatted(hexDigits[c & 0xF]);
            }
            else
            {
#if NET
                Span<byte> utf8 = stackalloc byte[Encoding.UTF8.GetMaxByteCount(1)];
                foreach (byte b in utf8.Slice(0, new Rune(c).EncodeToUtf8(utf8)))
#else
                foreach (byte b in Encoding.UTF8.GetBytes([c]))
#endif
                {
                    builder.AppendLiteral("%");
                    builder.AppendFormatted(hexDigits[b >> 4]);
                    builder.AppendFormatted(hexDigits[b & 0xF]);
                }
            }
        }
    }

    /// <summary>
    /// Defines an equality comparer for Uri templates as follows:
    /// 1. Non-templated Uris use regular System.Uri equality comparison (host name is case insensitive).
    /// 2. Templated Uris use regular string equality.
    ///
    /// We do this because non-templated resources are looked up directly from the resource dictionary
    /// and we need to make sure equality is implemented correctly. Templated Uris are resolved in a
    /// fallback step using linear traversal of the resource dictionary, so their equality is only
    /// there to distinguish between different templates.
    /// </summary>
    internal sealed class UriTemplateComparer : IEqualityComparer<string>
    {
        public static IEqualityComparer<string> Instance { get; } = new UriTemplateComparer();

        public bool Equals(string? uriTemplate1, string? uriTemplate2)
        {
            if (TryParseAsNonTemplatedUri(uriTemplate1, out Uri? uri1) &&
                TryParseAsNonTemplatedUri(uriTemplate2, out Uri? uri2))
            {
                return uri1 == uri2;
            }

            return string.Equals(uriTemplate1, uriTemplate2, StringComparison.Ordinal);
        }

        public int GetHashCode([DisallowNull] string uriTemplate)
        {
            if (TryParseAsNonTemplatedUri(uriTemplate, out Uri? uri))
            {
                return uri.GetHashCode();
            }
            else
            {
                return StringComparer.Ordinal.GetHashCode(uriTemplate);
            }
        }

        private static bool TryParseAsNonTemplatedUri(string? uriTemplate, [NotNullWhen(true)] out Uri? uri)
        {
            if (uriTemplate is null || uriTemplate.Contains('{'))
            {
                uri = null;
                return false;
            }

            return Uri.TryCreate(uriTemplate, UriKind.Absolute, out uri);
        }
    }
}
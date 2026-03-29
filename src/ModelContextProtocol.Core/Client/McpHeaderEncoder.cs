using System.Text;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>
/// Encodes and decodes parameter values for use in MCP HTTP headers according to the
/// HTTP Standardization SEP.
/// </summary>
/// <remarks>
/// <para>
/// This encoder handles conversion of parameter values to HTTP header-safe strings,
/// including Base64 encoding for values that cannot be safely transmitted as plain text.
/// </para>
/// <para>
/// Encoding rules:
/// <list type="bullet">
/// <item><description>Plain ASCII values (0x20-0x7E, tab 0x09): sent as-is</description></item>
/// <item><description>Values with leading/trailing whitespace: Base64 encoded with <c>=?base64?{value}?=</c> wrapper</description></item>
/// <item><description>Non-ASCII characters: Base64 encoded</description></item>
/// <item><description>Control characters: Base64 encoded</description></item>
/// </list>
/// </para>
/// </remarks>
public static class McpHeaderEncoder
{
    private const string Base64Prefix = "=?base64?";
    private const string Base64Suffix = "?=";

    /// <summary>
    /// Encodes a parameter value for use in an HTTP header.
    /// </summary>
    /// <param name="value">The value to encode. Can be string, number, or boolean.</param>
    /// <returns>
    /// The encoded header value, or <see langword="null"/> if the value cannot be encoded
    /// (e.g., is not a supported type).
    /// </returns>
    public static string? EncodeValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var stringValue = ConvertToString(value);
        if (stringValue is null)
        {
            return null;
        }

        if (RequiresBase64Encoding(stringValue))
        {
            return EncodeAsBase64(stringValue);
        }

        return stringValue;
    }

    /// <summary>
    /// Decodes a header value that may be Base64-encoded according to SEP rules.
    /// </summary>
    /// <param name="headerValue">The header value to decode.</param>
    /// <returns>
    /// The decoded string value, or <see langword="null"/> if decoding fails.
    /// If the value is not Base64-encoded, returns the original value.
    /// </returns>
    public static string? DecodeValue(string? headerValue)
    {
        if (headerValue is null || headerValue.Length == 0)
        {
            return headerValue;
        }

        // Check for Base64 wrapper (case-insensitive prefix check per SEP)
        if (headerValue.StartsWith(Base64Prefix, StringComparison.OrdinalIgnoreCase) &&
            headerValue.EndsWith(Base64Suffix, StringComparison.Ordinal))
        {
            var base64Content = headerValue.Substring(
                Base64Prefix.Length,
                headerValue.Length - Base64Prefix.Length - Base64Suffix.Length);

            try
            {
                var bytes = Convert.FromBase64String(base64Content);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return headerValue;
    }

    private static string? ConvertToString(object value)
    {
        return value switch
        {
            string s => s,
            bool b => b ? "true" : "false",
            byte n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sbyte n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            short n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ushort n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            int n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            uint n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ulong n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            float n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            decimal n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static bool RequiresBase64Encoding(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        // Check for leading/trailing whitespace (space or tab)
        if (value[0] is ' ' or '\t' || value[^1] is ' ' or '\t')
        {
            return true;
        }

        foreach (char c in value)
        {
            // Valid HTTP header field value characters: visible ASCII (0x21-0x7E), space (0x20), tab (0x09)
            if (c < 0x20 || c > 0x7E)
            {
                if (c != '\t')
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string EncodeAsBase64(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var base64 = Convert.ToBase64String(bytes);
        return $"{Base64Prefix}{base64}{Base64Suffix}";
    }
}

using ModelContextProtocol.Client;

namespace ModelContextProtocol.Tests.Client;

public class McpHeaderEncoderTests
{
    [Theory]
    [InlineData("us-west1", "us-west1")]
    [InlineData("hello-world", "hello-world")]
    [InlineData("my_tool_name", "my_tool_name")]
    [InlineData("us west 1", "us west 1")]
    [InlineData("", "")]
    public void EncodeValue_PlainAscii_PassesThrough(string input, string expected)
    {
        var result = McpHeaderEncoder.EncodeValue(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(" us-west1", "=?base64?IHVzLXdlc3Qx?=")]
    [InlineData("us-west1 ", "=?base64?dXMtd2VzdDEg?=")]
    [InlineData(" us-west1 ", "=?base64?IHVzLXdlc3QxIA==?=")]
    [InlineData("\tindented", "=?base64?CWluZGVudGVk?=")]
    public void EncodeValue_LeadingTrailingWhitespace_Base64Encodes(string input, string expected)
    {
        var result = McpHeaderEncoder.EncodeValue(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EncodeValue_NonAsciiCharacters_Base64Encodes()
    {
        var result = McpHeaderEncoder.EncodeValue("日本語");
        Assert.Equal("=?base64?5pel5pys6Kqe?=", result);
    }

    [Fact]
    public void EncodeValue_NewlineCharacter_Base64Encodes()
    {
        var result = McpHeaderEncoder.EncodeValue("line1\nline2");
        Assert.Equal("=?base64?bGluZTEKbGluZTI=?=", result);
    }

    [Fact]
    public void EncodeValue_CarriageReturnNewline_Base64Encodes()
    {
        var result = McpHeaderEncoder.EncodeValue("line1\r\nline2");
        Assert.Equal("=?base64?bGluZTENCmxpbmUy?=", result);
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void EncodeValue_Boolean_ConvertsToLowercase(bool input, string expected)
    {
        var result = McpHeaderEncoder.EncodeValue(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(42, "42")]
    [InlineData(3.14, "3.14")]
    [InlineData(0, "0")]
    [InlineData(-1, "-1")]
    public void EncodeValue_Number_ConvertsToString(object input, string expected)
    {
        var result = McpHeaderEncoder.EncodeValue(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EncodeValue_Null_ReturnsNull()
    {
        var result = McpHeaderEncoder.EncodeValue(null);
        Assert.Null(result);
    }

    [Fact]
    public void EncodeValue_UnsupportedType_ReturnsNull()
    {
        var result = McpHeaderEncoder.EncodeValue(new object());
        Assert.Null(result);
    }

    [Theory]
    [InlineData("us-west1", "us-west1")]
    [InlineData("", "")]
    public void DecodeValue_PlainAscii_ReturnsAsIs(string input, string expected)
    {
        var result = McpHeaderEncoder.DecodeValue(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DecodeValue_Null_ReturnsNull()
    {
        var result = McpHeaderEncoder.DecodeValue(null);
        Assert.Null(result);
    }

    [Fact]
    public void DecodeValue_ValidBase64_Decodes()
    {
        var result = McpHeaderEncoder.DecodeValue("=?base64?SGVsbG8=?=");
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void DecodeValue_CaseInsensitivePrefix_Decodes()
    {
        var result = McpHeaderEncoder.DecodeValue("=?BASE64?SGVsbG8=?=");
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void DecodeValue_InvalidBase64_ReturnsNull()
    {
        var result = McpHeaderEncoder.DecodeValue("=?base64?SGVs!!!bG8=?=");
        Assert.Null(result);
    }

    [Fact]
    public void DecodeValue_MissingPrefix_ReturnsLiteralValue()
    {
        var result = McpHeaderEncoder.DecodeValue("SGVsbG8=");
        Assert.Equal("SGVsbG8=", result);
    }

    [Fact]
    public void DecodeValue_MissingSuffix_ReturnsLiteralValue()
    {
        var result = McpHeaderEncoder.DecodeValue("=?base64?SGVsbG8=");
        Assert.Equal("=?base64?SGVsbG8=", result);
    }

    [Theory]
    [InlineData("us-west1")]
    [InlineData("Hello, 世界")]
    [InlineData(" padded ")]
    [InlineData("line1\nline2")]
    [InlineData("\tindented")]
    public void RoundTrip_EncodeDecode_PreservesValue(string original)
    {
        var encoded = McpHeaderEncoder.EncodeValue(original);
        Assert.NotNull(encoded);

        var decoded = McpHeaderEncoder.DecodeValue(encoded);
        Assert.Equal(original, decoded);
    }
}

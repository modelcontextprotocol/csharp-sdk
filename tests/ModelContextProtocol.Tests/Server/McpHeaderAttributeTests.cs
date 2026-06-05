using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public class McpHeaderAttributeTests
{
    [Theory]
    [InlineData("Region")]
    [InlineData("TenantId")]
    [InlineData("Priority")]
    [InlineData("X-Custom")]
    [InlineData("x!header")]
    [InlineData("x#header")]
    [InlineData("x~header")]
    public void Constructor_ValidHeaderName_Succeeds(string name)
    {
        var attr = new McpHeaderAttribute(name);
        Assert.Equal(name, attr.Name);
    }

    [Fact]
    public void Constructor_NameWithSpace_Throws()
    {
        Assert.Throws<ArgumentException>(() => new McpHeaderAttribute("My Region"));
    }

    [Fact]
    public void Constructor_NameWithColon_Throws()
    {
        Assert.Throws<ArgumentException>(() => new McpHeaderAttribute("Region:Primary"));
    }

    [Theory]
    [InlineData("Region(1)")]
    [InlineData("path/to")]
    [InlineData("key=value")]
    [InlineData("name@host")]
    [InlineData("with,comma")]
    [InlineData("with;semi")]
    [InlineData("with[bracket")]
    [InlineData("with{brace")]
    [InlineData("with\"quote")]
    [InlineData("with\\backslash")]
    [InlineData("with?question")]
    public void Constructor_NonTcharCharacter_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => new McpHeaderAttribute(name));
    }

    [Fact]
    public void Constructor_NullName_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new McpHeaderAttribute(null!));
    }

    [Fact]
    public void Constructor_EmptyName_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new McpHeaderAttribute(""));
    }

    [Fact]
    public void Constructor_WhitespaceName_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new McpHeaderAttribute("  "));
    }

    [Fact]
    public void Constructor_NameWithControlCharacter_Throws()
    {
        Assert.Throws<ArgumentException>(() => new McpHeaderAttribute("Region\t1"));
    }

    [Fact]
    public void Constructor_NameWithNonAscii_Throws()
    {
        Assert.Throws<ArgumentException>(() => new McpHeaderAttribute("Région"));
    }
}

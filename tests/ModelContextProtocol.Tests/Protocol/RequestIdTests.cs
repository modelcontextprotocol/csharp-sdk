using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public class RequestIdTests
{
    [Fact]
    public void StringCtor_Roundtrips()
    {
        RequestId id = new("test-id");
        Assert.Equal("test-id", id.ToString());
        Assert.Equal("\"test-id\"", JsonSerializer.Serialize(id, McpJsonUtilities.DefaultOptions));
        Assert.Same("test-id", id.Id);

        Assert.True(id.Equals(new("test-id")));
        Assert.False(id.Equals(new("tEst-id")));
        Assert.Equal("test-id".GetHashCode(), id.GetHashCode());

        Assert.Equal(id, JsonSerializer.Deserialize<RequestId>(JsonSerializer.Serialize(id, McpJsonUtilities.DefaultOptions), McpJsonUtilities.DefaultOptions));
    }

    [Fact]
    public void Int64Ctor_Roundtrips()
    {
        RequestId id = new(42);
        Assert.Equal("42", id.ToString());
        Assert.Equal("42", JsonSerializer.Serialize(id, McpJsonUtilities.DefaultOptions));
        Assert.Equal(42, Assert.IsType<long>(id.Id));

        Assert.True(id.Equals(new(42)));
        Assert.False(id.Equals(new(43)));
        Assert.False(id.Equals(new("42")));
        Assert.Equal(42L.GetHashCode(), id.GetHashCode());

        Assert.Equal(id, JsonSerializer.Deserialize<RequestId>(JsonSerializer.Serialize(id, McpJsonUtilities.DefaultOptions), McpJsonUtilities.DefaultOptions));
    }
}

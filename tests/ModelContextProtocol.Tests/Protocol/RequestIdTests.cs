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

    [Fact]
    public void Null_DeserializesAsDefault()
    {
        // Per JSON-RPC 2.0 §5.1, error responses produced before the request id can be determined
        // MUST carry id=null. Deserialization needs to tolerate that shape so callers can handle
        // such error envelopes (instead of throwing on the bare RequestId conversion).
        var id = JsonSerializer.Deserialize<RequestId>("null", McpJsonUtilities.DefaultOptions);
        Assert.Equal(default(RequestId), id);
        Assert.Null(id.Id);
    }

    [Fact]
    public void Null_SerializesAsJsonNull()
    {
        // The default RequestId (Id == null) is the id-less-error-response shape. It MUST serialize as
        // JSON null — not "" — so the wire form is spec-conformant and round-trips losslessly.
        Assert.Equal("null", JsonSerializer.Serialize(default(RequestId), McpJsonUtilities.DefaultOptions));
    }

    [Fact]
    public void Null_Roundtrips()
    {
        var json = JsonSerializer.Serialize(default(RequestId), McpJsonUtilities.DefaultOptions);
        var id = JsonSerializer.Deserialize<RequestId>(json, McpJsonUtilities.DefaultOptions);
        Assert.Equal(default(RequestId), id);
        Assert.Null(id.Id);
    }
}

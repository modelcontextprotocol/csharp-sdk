using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

public static class UrlElicitationRequiredErrorDataTests
{
    [Fact]
    public static void UrlElicitationRequiredErrorData_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new UrlElicitationRequiredErrorData
        {
            Elicitations =
            [
                new ElicitRequestParams
                {
                    Mode = "url",
                    ElicitationId = "elicit-1",
                    Url = "https://example.com/auth",
                    Message = "Please authenticate"
                },
                new ElicitRequestParams
                {
                    Mode = "url",
                    ElicitationId = "elicit-2",
                    Url = "https://example.com/consent",
                    Message = "Please consent"
                }
            ]
        };

        string json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<UrlElicitationRequiredErrorData>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Elicitations.Count);
        Assert.Equal("url", deserialized.Elicitations[0].Mode);
        Assert.Equal("elicit-1", deserialized.Elicitations[0].ElicitationId);
        Assert.Equal("https://example.com/auth", deserialized.Elicitations[0].Url);
        Assert.Equal("Please authenticate", deserialized.Elicitations[0].Message);
        Assert.Equal("elicit-2", deserialized.Elicitations[1].ElicitationId);
    }
}

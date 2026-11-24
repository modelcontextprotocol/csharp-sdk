using ModelContextProtocol.Protocol;
using System.Text;

namespace ModelContextProtocol.Tests.Protocol;

public class JsonRpcFrameScannerTests
{
    public static TheoryData<string, string?> IdCases => new()
    {
        { """{"jsonrpc":"2.0","id":1,"method":"ping"}""", "1" },
        { """{"jsonrpc":"2.0","method":"ping","params":{"id":99,"a":[1,{"id":98}]},"id":2}""", "2" },
        { """{"jsonrpc":"2.0","method":"ping","params":{},"id":"abc"}""", "abc" },
        { """{"jsonrpc":"2.0","method":"ping","params":{"s":"\"id\":5,}{]["},"id":"x\"y"}""", "x\"y" },
        { """{"jsonrpc":"2.0","id":"escaped-key","method":"ping"}""", "escaped-key" },
        { """{ "jsonrpc" : "2.0" , "id" : 42 , "method" : "ping" }""", "42" },
        { """{"jsonrpc":"2.0","method":"ping","id":-7}""", "-7" },
        { """{"jsonrpc":"2.0","method":"ping","id":1,"id":2}""", "1" },
        { """{"jsonrpc":"2.0","method":"ping","id":null}""", null },
        { """{"jsonrpc":"2.0","method":"ping","id":1.5}""", null },
        { """{"jsonrpc":"2.0","method":"ping","id":{"a":1}}""", null },
        { """{"jsonrpc":"2.0","method":"ping","id":[1]}""", null },
        { """{"jsonrpc":"2.0","method":"notifications/initialized"}""", null },
        { """{"jsonrpc":"2.0","params":{"id":3}}""", null },
        { """[{"id":1}]""", null },
        { "not json", null },
        { "", null },
    };

    [Theory]
    [MemberData(nameof(IdCases))]
    public void Finds_Top_Level_Id(string json, string? expectedId)
    {
        foreach (int chunkSize in new[] { 1, 2, 7, int.MaxValue })
        {
            var scanner = Scan(json, chunkSize);

            Assert.Equal(expectedId is not null, scanner.TryGetId(out RequestId id));
            if (expectedId is not null)
            {
                Assert.Equal(expectedId, id.ToString());
            }
        }
    }

    [Fact]
    public void Distinguishes_Numeric_And_String_Ids()
    {
        Assert.True(Scan("""{"id":5}""").TryGetId(out RequestId numeric));
        Assert.Equal(new RequestId(5), numeric);

        Assert.True(Scan("""{"id":"5"}""").TryGetId(out RequestId text));
        Assert.Equal(new RequestId("5"), text);
    }

    [Fact]
    public void Finds_Numeric_Id_At_The_End_Of_A_Truncated_Input()
    {
        // TryGetId ends the input, so a number with no delimiter after it is still complete.
        Assert.True(Scan("""{"method":"ping","id":12""").TryGetId(out RequestId id));
        Assert.Equal(new RequestId(12), id);
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{}}""", true, false)]
    [InlineData("""{"jsonrpc":"2.0","method":"notifications/message","params":{"result":1,"error":2}}""", true, false)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{"method":"x"}}""", false, true)]
    [InlineData("""{"jsonrpc":"2.0","error":{"code":-32603,"message":"m"},"id":1}""", false, true)]
    [InlineData("""{"jsonrpc":"2.0","method":"ping","id":1}""", true, false)]
    [InlineData("""{"jsonrpc":"2.0","id":1}""", false, false)]
    public void Detects_Method_And_Result_Or_Error(string json, bool hasMethod, bool hasResultOrError)
    {
        foreach (int chunkSize in new[] { 1, 3, int.MaxValue })
        {
            var scanner = Scan(json, chunkSize);

            Assert.Equal(hasMethod, scanner.HasMethod);
            Assert.Equal(hasResultOrError, scanner.HasResultOrError);
        }
    }

    [Fact]
    public void Scans_A_Large_Value_And_Finds_The_Id_After_It()
    {
        string value = string.Concat(Enumerable.Repeat("""a\\b\"c}{][ """, 100_000));
        string json = $$$"""{"jsonrpc":"2.0","method":"tools/call","params":{"arguments":{"data":"{{{value}}}"}},"id":"after"}""";

        var scanner = Scan(json, chunkSize: 4096);

        Assert.True(scanner.IsComplete);
        Assert.True(scanner.HasMethod);
        Assert.True(scanner.TryGetId(out RequestId id));
        Assert.Equal("after", id.ToString());
    }

    [Fact]
    public void Ignores_Input_After_The_Top_Level_Object_Ends()
    {
        var scanner = Scan("""{"method":"ping"} {"id":1}""");

        Assert.True(scanner.IsComplete);
        Assert.False(scanner.TryGetId(out _));
    }

    [Fact]
    public void Ignores_An_Overly_Long_Id()
    {
        string json = $$$"""{"method":"ping","id":"{{{new string('a', 2048)}}}"}""";

        Assert.False(Scan(json).TryGetId(out _));
    }

    [Fact]
    public void Reset_Starts_A_New_Scan()
    {
        var scanner = new JsonRpcFrameScanner();
        scanner.Reset();
        scanner.Feed(Encoding.UTF8.GetBytes("""{"id":1,"method":"ping"}"""));
        Assert.True(scanner.HasMethod);

        scanner.Reset();
        scanner.Feed(Encoding.UTF8.GetBytes("""{"result":{},"id":2}"""));

        Assert.False(scanner.HasMethod);
        Assert.True(scanner.HasResultOrError);
        Assert.True(scanner.TryGetId(out RequestId id));
        Assert.Equal(new RequestId(2), id);
    }

    private static JsonRpcFrameScanner Scan(string json, int chunkSize = int.MaxValue)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        var scanner = new JsonRpcFrameScanner();
        scanner.Reset();
        for (int offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            scanner.Feed(bytes.AsSpan(offset, Math.Min(chunkSize, bytes.Length - offset)));
        }

        return scanner;
    }
}

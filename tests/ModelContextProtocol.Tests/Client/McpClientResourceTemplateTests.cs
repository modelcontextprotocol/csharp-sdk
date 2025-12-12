using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Tests.Client;

public partial class McpClientResourceTemplateTests : ClientServerTestBase
{
    public McpClientResourceTemplateTests(ITestOutputHelper outputHelper) : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithReadResourceHandler((request, cancellationToken) =>
            new ValueTask<ReadResourceResult>(new ReadResourceResult
            {
                Contents = [new TextResourceContents { Text = request.Params?.Uri ?? "", Uri = request.Params?.Uri ?? "" }]
            }));
    }

    public static IEnumerable<object[]> UriTemplate_InputsProduceExpectedOutputs_MemberData()
    {
        string[] sources =
        [
            SpecExamples,
            SpecExamplesBySection,
            ExtendedTests,
        ];

        foreach (var source in sources)
        {
            var tests = JsonSerializer.Deserialize(source, JsonContext7.Default.DictionaryStringTestGroup);
            Assert.NotNull(tests);

            foreach (var testGroup in tests.Values)
            {
                Dictionary<string, object?> variables = [];
                foreach (var entry in testGroup.Variables)
                {
                    variables[entry.Key] = entry.Value.ValueKind switch
                    {
                        JsonValueKind.Null => null,
                        JsonValueKind.String => entry.Value.GetString(),
                        JsonValueKind.Number => entry.Value.GetDouble(),
                        JsonValueKind.Array => entry.Value.EnumerateArray().Select(i => i.GetString()).ToArray(),
                        JsonValueKind.Object => entry.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()),
                        _ => throw new Exception($"Invalid test case format: {entry.Value.ValueKind}")
                    };
                }

                foreach (var testCase in testGroup.TestCases)
                {
                    string uriTemplate = testCase[0].GetString() ?? throw new Exception("Invalid test case format.");
                    object expected = testCase[1].ValueKind switch
                    {
                        JsonValueKind.String => testCase[1].GetString()!,
                        JsonValueKind.Array => testCase[1].EnumerateArray().Select(i => i.GetString()).ToArray(),
                        JsonValueKind.False => false,
                        _ => throw new Exception("Invalid test case format.")
                    };

                    yield return new object[] { variables, uriTemplate, expected };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(UriTemplate_InputsProduceExpectedOutputs_MemberData))]
    public async Task UriTemplate_InputsProduceExpectedOutputs(
        IReadOnlyDictionary<string, object?> variables, string uriTemplate, object expected)
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.ReadResourceAsync(uriTemplate, variables, null, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        var actualUri = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text;

        if (expected is string expectedUri)
        {
            Assert.Equal(expectedUri, actualUri);
        }
        else
        {
            Assert.Contains(actualUri, Assert.IsType<string[]>(expected));
        }
    }

    public class TestGroup
    {
        [JsonPropertyName("level")]
        public int Level { get; set; } = 4;

        [JsonPropertyName("variables")]
        public IDictionary<string, JsonElement> Variables { get; set; } = new Dictionary<string, JsonElement>();

        [JsonPropertyName("testcases")]
        public IList<List<JsonElement>> TestCases { get; set; } = [];
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
    [JsonSerializable(typeof(Dictionary<string, TestGroup>))]
    internal partial class JsonContext7 : JsonSerializerContext;

    // The following data comes from:
    // https://github.com/uri-templates/uritemplate-test/tree/1eb27ab4462b9e5819dc47db99044f5fd1fa9bc7
    // The JSON from the test case files has been extracted below.

    // Copyright 2011- The Authors
    //
    // Licensed under the Apache License, Version 2.0 (the "License");
    // you may not use this file except in compliance with the License.
    // You may obtain a copy of the License at
    //
    //     http://www.apache.org/licenses/LICENSE-2.0
    //
    // Unless required by applicable law or agreed to in writing, software
    // distributed under the License is distributed on an "AS IS" BASIS,
    // WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
    // See the License for the specific language governing permissions and
    // limitations under the License.

    private const string SpecExamples = """
        {
          "Level 1 Examples" :
          {
            "level": 1,
            "variables": {
               "var"   : "value",
               "hello" : "Hello World!"
             },
             "testcases" : [
                ["{var}", "value"],
                ["'{var}'", "'value'"],
                ["{hello}", "Hello%20World%21"]
             ]
          },
          "Level 2 Examples" :
          {
            "level": 2,
            "variables": {
               "var"   : "value",
               "hello" : "Hello World!",
               "path"  : "/foo/bar"
             },
             "testcases" : [
                ["{+var}", "value"],
                ["{+hello}", "Hello%20World!"],
                ["{+path}/here", "/foo/bar/here"],
                ["here?ref={+path}", "here?ref=/foo/bar"]
             ]
          },
          "Level 3 Examples" :
          {
            "level": 3,
            "variables": {
               "var"   : "value",
               "hello" : "Hello World!",
               "empty" : "",
               "path"  : "/foo/bar",
               "x"     : "1024",
               "y"     : "768"
             },
             "testcases" : [
                ["map?{x,y}", "map?1024,768"],
                ["{x,hello,y}", "1024,Hello%20World%21,768"],
                ["{+x,hello,y}", "1024,Hello%20World!,768"],
                ["{+path,x}/here", "/foo/bar,1024/here"],
                ["{#x,hello,y}", "#1024,Hello%20World!,768"],
                ["{#path,x}/here", "#/foo/bar,1024/here"],
                ["X{.var}", "X.value"],
                ["X{.x,y}", "X.1024.768"],
                ["{/var}", "/value"],
                ["{/var,x}/here", "/value/1024/here"],
                ["{;x,y}", ";x=1024;y=768"],
                ["{;x,y,empty}", ";x=1024;y=768;empty"],
                ["{?x,y}", "?x=1024&y=768"],
                ["{?x,y,empty}", "?x=1024&y=768&empty="],
                ["?fixed=yes{&x}", "?fixed=yes&x=1024"],
                ["{&x,y,empty}", "&x=1024&y=768&empty="]
             ]
          },
          "Level 4 Examples" :
          {
            "level": 4,
            "variables": {
              "var": "value",
              "hello": "Hello World!",
              "path": "/foo/bar",
              "list": ["red", "green", "blue"],
              "keys": {"semi": ";", "dot": ".", "comma":","}
            },
            "testcases": [
              ["{var:3}", "val"],
              ["{var:30}", "value"],
              ["{list}", "red,green,blue"],
              ["{list*}", "red,green,blue"],
              ["{keys}", [
                "comma,%2C,dot,.,semi,%3B",
                "comma,%2C,semi,%3B,dot,.",
                "dot,.,comma,%2C,semi,%3B",
                "dot,.,semi,%3B,comma,%2C",
                "semi,%3B,comma,%2C,dot,.",
                "semi,%3B,dot,.,comma,%2C"
              ]],
              ["{keys*}", [
                "comma=%2C,dot=.,semi=%3B",
                "comma=%2C,semi=%3B,dot=.",
                "dot=.,comma=%2C,semi=%3B",
                "dot=.,semi=%3B,comma=%2C",
                "semi=%3B,comma=%2C,dot=.",
                "semi=%3B,dot=.,comma=%2C"
              ]],
              ["{+path:6}/here", "/foo/b/here"],
              ["{+list}", "red,green,blue"],
              ["{+list*}", "red,green,blue"],
              ["{+keys}", [
                "comma,,,dot,.,semi,;",
                "comma,,,semi,;,dot,.",
                "dot,.,comma,,,semi,;",
                "dot,.,semi,;,comma,,",
                "semi,;,comma,,,dot,.",
                "semi,;,dot,.,comma,,"
              ]],
              ["{+keys*}", [
                "comma=,,dot=.,semi=;",
                "comma=,,semi=;,dot=.",
                "dot=.,comma=,,semi=;",
                "dot=.,semi=;,comma=,",
                "semi=;,comma=,,dot=.",
                "semi=;,dot=.,comma=,"
              ]],
              ["{#path:6}/here", "#/foo/b/here"],
              ["{#list}", "#red,green,blue"],
              ["{#list*}", "#red,green,blue"],
              ["{#keys}", [
                "#comma,,,dot,.,semi,;",
                "#comma,,,semi,;,dot,.",
                "#dot,.,comma,,,semi,;",
                "#dot,.,semi,;,comma,,",
                "#semi,;,comma,,,dot,.",
                "#semi,;,dot,.,comma,,"
              ]],
              ["{#keys*}", [
                "#comma=,,dot=.,semi=;",
                "#comma=,,semi=;,dot=.",
                "#dot=.,comma=,,semi=;",
                "#dot=.,semi=;,comma=,",
                "#semi=;,comma=,,dot=.",
                "#semi=;,dot=.,comma=,"
              ]],
              ["X{.var:3}", "X.val"],
              ["X{.list}", "X.red,green,blue"],
              ["X{.list*}", "X.red.green.blue"],
              ["X{.keys}", [
                "X.comma,%2C,dot,.,semi,%3B",
                "X.comma,%2C,semi,%3B,dot,.",
                "X.dot,.,comma,%2C,semi,%3B",
                "X.dot,.,semi,%3B,comma,%2C",
                "X.semi,%3B,comma,%2C,dot,.",
                "X.semi,%3B,dot,.,comma,%2C"
              ]],
              ["{/var:1,var}", "/v/value"],
              ["{/list}", "/red,green,blue"],
              ["{/list*}", "/red/green/blue"],
              ["{/list*,path:4}", "/red/green/blue/%2Ffoo"],
              ["{/keys}", [
                "/comma,%2C,dot,.,semi,%3B",
                "/comma,%2C,semi,%3B,dot,.",
                "/dot,.,comma,%2C,semi,%3B",
                "/dot,.,semi,%3B,comma,%2C",
                "/semi,%3B,comma,%2C,dot,.",
                "/semi,%3B,dot,.,comma,%2C"
              ]],
              ["{/keys*}", [
                "/comma=%2C/dot=./semi=%3B",
                "/comma=%2C/semi=%3B/dot=.",
                "/dot=./comma=%2C/semi=%3B",
                "/dot=./semi=%3B/comma=%2C",
                "/semi=%3B/comma=%2C/dot=.",
                "/semi=%3B/dot=./comma=%2C"
              ]],
              ["{;hello:5}", ";hello=Hello"],
              ["{;list}", ";list=red,green,blue"],
              ["{;list*}", ";list=red;list=green;list=blue"],
              ["{;keys}", [
                ";keys=comma,%2C,dot,.,semi,%3B",
                ";keys=comma,%2C,semi,%3B,dot,.",
                ";keys=dot,.,comma,%2C,semi,%3B",
                ";keys=dot,.,semi,%3B,comma,%2C",
                ";keys=semi,%3B,comma,%2C,dot,.",
                ";keys=semi,%3B,dot,.,comma,%2C"
              ]],
              ["{;keys*}", [
                ";comma=%2C;dot=.;semi=%3B",
                ";comma=%2C;semi=%3B;dot=.",
                ";dot=.;comma=%2C;semi=%3B",
                ";dot=.;semi=%3B;comma=%2C",
                ";semi=%3B;comma=%2C;dot=.",
                ";semi=%3B;dot=.;comma=%2C"
              ]],
              ["{?var:3}", "?var=val"],
              ["{?list}", "?list=red,green,blue"],
              ["{?list*}", "?list=red&list=green&list=blue"],
              ["{?keys}", [
                "?keys=comma,%2C,dot,.,semi,%3B",
                "?keys=comma,%2C,semi,%3B,dot,.",
                "?keys=dot,.,comma,%2C,semi,%3B",
                "?keys=dot,.,semi,%3B,comma,%2C",
                "?keys=semi,%3B,comma,%2C,dot,.",
                "?keys=semi,%3B,dot,.,comma,%2C"
              ]],
              ["{?keys*}", [
                "?comma=%2C&dot=.&semi=%3B",
                "?comma=%2C&semi=%3B&dot=.",
                "?dot=.&comma=%2C&semi=%3B",
                "?dot=.&semi=%3B&comma=%2C",
                "?semi=%3B&comma=%2C&dot=.",
                "?semi=%3B&dot=.&comma=%2C"
              ]],
              ["{&var:3}", "&var=val"],
              ["{&list}", "&list=red,green,blue"],
              ["{&list*}", "&list=red&list=green&list=blue"],
              ["{&keys}", [
                "&keys=comma,%2C,dot,.,semi,%3B",
                "&keys=comma,%2C,semi,%3B,dot,.",
                "&keys=dot,.,comma,%2C,semi,%3B",
                "&keys=dot,.,semi,%3B,comma,%2C",
                "&keys=semi,%3B,comma,%2C,dot,.",
                "&keys=semi,%3B,dot,.,comma,%2C"
              ]],
              ["{&keys*}", [
                "&comma=%2C&dot=.&semi=%3B",
                "&comma=%2C&semi=%3B&dot=.",
                "&dot=.&comma=%2C&semi=%3B",
                "&dot=.&semi=%3B&comma=%2C",
                "&semi=%3B&comma=%2C&dot=.",
                "&semi=%3B&dot=.&comma=%2C"
              ]]
            ]
          }
        }
        """;

    private const string SpecExamplesBySection = """
        {
          "2.1 Literals" :
          {
            "variables": {
               "count"      : ["one", "two", "three"]
             },
             "testcases" : [
                ["'{count}'", "'one,two,three'"]
              ]
          },
          "3.2.1 Variable Expansion" :
          {
            "variables": {
               "count"      : ["one", "two", "three"],
               "dom"        : ["example", "com"],
               "dub"        : "me/too",
               "hello"      : "Hello World!",
               "half"       : "50%",
               "var"        : "value",
               "who"        : "fred",
               "base"       : "http://example.com/home/",
               "path"       : "/foo/bar",
               "list"       : ["red", "green", "blue"],
               "keys"       : { "semi" : ";", "dot" : ".", "comma" : ","},
               "v"          : "6",
               "x"          : "1024",
               "y"          : "768",
               "empty"      : "",
               "empty_keys" : {},
               "undef"      : null
             },
             "testcases" : [
                ["{count}", "one,two,three"],
                ["{count*}", "one,two,three"],
                ["{/count}", "/one,two,three"],
                ["{/count*}", "/one/two/three"],
                ["{;count}", ";count=one,two,three"],
                ["{;count*}", ";count=one;count=two;count=three"],
                ["{?count}", "?count=one,two,three"],
                ["{?count*}", "?count=one&count=two&count=three"],
                ["{&count*}", "&count=one&count=two&count=three"]
              ]
          },
          "3.2.2 Simple String Expansion" :
          {
            "variables": {
               "count"      : ["one", "two", "three"],
               "dom"        : ["example", "com"],
               "dub"        : "me/too",
               "hello"      : "Hello World!",
               "half"       : "50%",
               "var"        : "value",
               "who"        : "fred",
               "base"       : "http://example.com/home/",
               "path"       : "/foo/bar",
               "list"       : ["red", "green", "blue"],
               "keys"       : { "semi" : ";", "dot" : ".", "comma" : ","},
               "v"          : "6",
               "x"          : "1024",
               "y"          : "768",
               "empty"      : "",
               "empty_keys" : {},
               "undef"      : null
             },
             "testcases" : [
                ["{var}", "value"],
                ["{hello}", "Hello%20World%21"],
                ["{half}", "50%25"],
                ["O{empty}X", "OX"],
                ["O{undef}X", "OX"],
                ["{x,y}", "1024,768"],
                ["{x,hello,y}", "1024,Hello%20World%21,768"],
                ["?{x,empty}", "?1024,"],
                ["?{x,undef}", "?1024"],
                ["?{undef,y}", "?768"],
                ["{var:3}", "val"],
                ["{var:30}", "value"],
                ["{list}", "red,green,blue"],
                ["{list*}", "red,green,blue"],
                ["{keys}", [
                  "comma,%2C,dot,.,semi,%3B",
                  "comma,%2C,semi,%3B,dot,.",
                  "dot,.,comma,%2C,semi,%3B",
                  "dot,.,semi,%3B,comma,%2C",
                  "semi,%3B,comma,%2C,dot,.",
                  "semi,%3B,dot,.,comma,%2C"
                ]],
                ["{keys*}", [
                  "comma=%2C,dot=.,semi=%3B",
                  "comma=%2C,semi=%3B,dot=.",
                  "dot=.,comma=%2C,semi=%3B",
                  "dot=.,semi=%3B,comma=%2C",
                  "semi=%3B,comma=%2C,dot=.",
                  "semi=%3B,dot=.,comma=%2C"
                ]]
             ]
          },
          "3.2.3 Reserved Expansion" :
          {
            "variables": {
               "count"      : ["one", "two", "three"],
               "dom"        : ["example", "com"],
               "dub"        : "me/too",
               "hello"      : "Hello World!",
               "half"       : "50%",
               "var"        : "value",
               "who"        : "fred",
               "base"       : "http://example.com/home/",
               "path"       : "/foo/bar",
               "list"       : ["red", "green", "blue"],
               "keys"       : { "semi" : ";", "dot" : ".", "comma" : ","},
               "v"          : "6",
               "x"          : "1024",
               "y"          : "768",
               "empty"      : "",
               "empty_keys" : {},
               "undef"      : null
             },
             "testcases" : [
                ["{+var}", "value"],
                ["{/var,empty}", "/value/"],
                ["{/var,undef}", "/value"],
                ["{+hello}", "Hello%20World!"],
                ["{+half}", "50%25"],
                ["{base}index", "http%3A%2F%2Fexample.com%2Fhome%2Findex"],
                ["{+base}index", "http://example.com/home/index"],
                ["O{+empty}X", "OX"],
                ["O{+undef}X", "OX"],
                ["{+path}/here", "/foo/bar/here"],
                ["{+path:6}/here", "/foo/b/here"],
                ["here?ref={+path}", "here?ref=/foo/bar"],
                ["up{+path}{var}/here", "up/foo/barvalue/here"],
                ["{+x,hello,y}", "1024,Hello%20World!,768"],
                ["{+path,x}/here", "/foo/bar,1024/here"],
                ["{+list}", "red,green,blue"],
                ["{+list*}", "red,green,blue"],
                ["{+keys}", [
                  "comma,,,dot,.,semi,;",
                  "comma,,,semi,;,dot,.",
                  "dot,.,comma,,,semi,;",
                  "dot,.,semi,;,comma,,",
                  "semi,;,comma,,,dot,.",
                  "semi,;,dot,.,comma,,"
                ]],
                ["{+keys*}", [
                  "comma=,,dot=.,semi=;",
                  "comma=,,semi=;,dot=.",
                  "dot=.,comma=,,semi=;",
                  "dot=.,semi=;,comma=,",
                  "semi=;,comma=,,dot=.",
                  "semi=;,dot=.,comma=,"
                ]]
             ]
          },
          "3.2.4 Fragment Expansion" :
          {
            "variables": {
               "count"      : ["one", "two", "three"],
               "dom"        : ["example", "com"],
               "dub"        : "me/too",
               "hello"      : "Hello World!",
               "half"       : "50%",
               "var"        : "value",
               "who"        : "fred",
               "base"       : "http://example.com/home/",
               "path"       : "/foo/bar",
               "list"       : ["red", "green", "blue"],
               "keys"       : { "semi" : ";", "dot" : ".", "comma" : ","},
               "v"          : "6",
               "x"          : "1024",
               "y"          : "768",
               "empty"      : "",
               "empty_keys" : {},
               "undef"      : null
             },
             "testcases" : [
                ["{#var}", "#value"],
                ["{#hello}", "#Hello%20World!"],
                ["{#half}", "#50%25"],
                ["foo{#empty}", "foo#"],
                ["foo{#undef}", "foo"],
                ["{#x,hello,y}", "#1024,Hello%20World!,768"],
                ["{#path,x}/here", "#/foo/bar,1024/here"],
                ["{#path:6}/here", "#/foo/b/here"],
                ["{#list}", "#red,green,blue"],
                ["{#list*}", "#red,green,blue"],
                ["{#keys}", [
                  "#comma,,,dot,.,semi,;",
                  "#comma,,,semi,;,dot,.",
                  "#dot,.,comma,,,semi,;",
                  "#dot,.,semi,;,comma,,",
                  "#semi,;,comma,,,dot,.",
                  "#semi,;,dot,.,comma,,"
                ]]
            ]
          },
          "3.2.5 Label Expansion with Dot-Prefix" :
          {
            "variables": {
               "count"      : ["one", "two", "three"],
               "dom"        : ["example", "com"],
               "dub"        : "me/too",
               "hello"      : "Hello World!",
               "half"       : "50%",
               "var"        : "value",
               "who"        : "fred",
               "base"       : "http://example.com/home/",
               "path"       : "/foo/bar",
               "list"       : ["red", "green", "blue"],
               "keys"       : { "semi" : ";", "dot" : ".", "comma" : ","},
               "v"          : "6",
               "x"          : "1024",
               "y"          : "768",
               "empty"      : "",
               "empty_keys" : {},
               "undef"      : null
            },
            "testcases" : [
               ["{.who}", ".fred"],
               ["{.who,who}", ".fred.fred"],
               ["{.half,who}", ".50%25.fred"],
               ["www{.dom*}", "www.example.com"],
               ["X{.var}", "X.value"],
               ["X{.var:3}", "X.val"],
               ["X{.empty}", "X."],
               ["X{.undef}", "X"],
               ["X{.list}", "X.red,green,blue"],
               ["X{.list*}", "X.red.green.blue"],
               ["{#keys}", [
                "#comma,,,dot,.,semi,;",
                "#comma,,,semi,;,dot,.",
                "#dot,.,comma,,,semi,;",
                "#dot,.,semi,;,comma,,",
                "#semi,;,comma,,,dot,.",
                "#semi,;,dot,.,comma,,"
               ]],
               ["{#keys*}", [
                "#comma=,,dot=.,semi=;",
                "#comma=,,semi=;,dot=.",
                "#dot=.,comma=,,semi=;",
                "#dot=.,semi=;,comma=,",
                "#semi=;,comma=,,dot=.",
                "#semi=;,dot=.,comma=,"
               ]],
               ["X{.empty_keys}", "X"],
               ["X{.empty_keys*}", "X"]
            ]
          },
          "3.2.6 Path Segment Expansion" :
          {
            "variables": {
               "count"      : ["one", "two", "three"],
               "dom"        : ["example", "com"],
               "dub"        : "me/too",
               "hello"      : "Hello World!",
               "half"       : "50%",
               "var"        : "value",
               "who"        : "fred",
               "base"       : "http://example.com/home/",
               "path"       : "/foo/bar",
               "list"       : ["red", "green", "blue"],
               "keys"       : { "semi" : ";", "dot" : ".", "comma" : ","},
               "v"          : "6",
               "x"          : "1024",
               "y"          : "768",
               "empty"      : "",
               "empty_keys" : {},
               "undef"      : null
             },
             "testcases" : [
               ["{/who}", "/fred"],
               ["{/who,who}", "/fred/fred"],
               ["{/half,who}", "/50%25/fred"],
               ["{/who,dub}", "/fred/me%2Ftoo"],
               ["{/var}", "/value"],
               ["{/var,empty}", "/value/"],
               ["{/var,undef}", "/value"],
               ["{/var,x}/here", "/value/1024/here"],
               ["{/var:1,var}", "/v/value"],
               ["{/list}", "/red,green,blue"],
               ["{/list*}", "/red/green/blue"],
               ["{/list*,path:4}", "/red/green/blue/%2Ffoo"],
               ["{/keys}", [
                "/comma,%2C,dot,.,semi,%3B",
                "/comma,%2C,semi,%3B,dot,.",
                "/dot,.,comma,%2C,semi,%3B",
                "/dot,.,semi,%3B,comma,%2C",
                "/semi,%3B,comma,%2C,dot,.",
                "/semi,%3B,dot,.,comma,%2C"
               ]],
               ["{/keys*}", [
                "/comma=%2C/dot=./semi=%3B",
                "/comma=%2C/semi=%3B/dot=.",
                "/dot=./comma=%2C/semi=%3B",
                "/dot=./semi=%3B/comma=%2C",
                "/semi=%3B/comma=%2C/dot=.",
                "/semi=%3B/dot=./comma=%2C"
               ]]
             ]
          },
          "3.2.7 Path-Style Parameter Expansion" :
          {
            "variables": {
               "count"      : ["one", "two", "three"],
               "dom"        : ["example", "com"],
               "dub"        : "me/too",
               "hello"      : "Hello World!",
               "half"       : "50%",
               "var"        : "value",
               "who"        : "fred",
               "base"       : "http://example.com/home/",
               "path"       : "/foo/bar",
               "list"       : ["red", "green", "blue"],
               "keys"       : { "semi" : ";", "dot" : ".", "comma" : ","},
               "v"          : "6",
               "x"          : "1024",
               "y"          : "768",
               "empty"      : "",
               "empty_keys" : {},
               "undef"      : null
             },
             "testcases" : [
                ["{;who}", ";who=fred"],
                ["{;half}", ";half=50%25"],
                ["{;empty}", ";empty"],
                ["{;hello:5}", ";hello=Hello"],
                ["{;v,empty,who}", ";v=6;empty;who=fred"],
                ["{;v,bar,who}", ";v=6;who=fred"],
                ["{;x,y}", ";x=1024;y=768"],
                ["{;x,y,empty}", ";x=1024;y=768;empty"],
                ["{;x,y,undef}", ";x=1024;y=768"],
                ["{;list}", ";list=red,green,blue"],
                ["{;list*}", ";list=red;list=green;list=blue"],
                ["{;keys}", [
                  ";keys=comma,%2C,dot,.,semi,%3B",
                  ";keys=comma,%2C,semi,%3B,dot,.",
                  ";keys=dot,.,comma,%2C,semi,%3B",
                  ";keys=dot,.,semi,%3B,comma,%2C",
                  ";keys=semi,%3B,comma,%2C,dot,.",
                  ";keys=semi,%3B,dot,.,comma,%2C"
                ]],
                ["{;keys*}", [
                  ";comma=%2C;dot=.;semi=%3B",
                  ";comma=%2C;semi=%3B;dot=.",
                  ";dot=.;comma=%2C;semi=%3B",
                  ";dot=.;semi=%3B;comma=%2C",
                  ";semi=%3B;comma=%2C;dot=.",
                  ";semi=%3B;dot=.;comma=%2C"
                ]]
             ]
          },
          "3.2.8 Form-Style Query Expansion" :
          {
            "variables": {
               "count"      : ["one", "two", "three"],
               "dom"        : ["example", "com"],
               "dub"        : "me/too",
               "hello"      : "Hello World!",
               "half"       : "50%",
               "var"        : "value",
               "who"        : "fred",
               "base"       : "http://example.com/home/",
               "path"       : "/foo/bar",
               "list"       : ["red", "green", "blue"],
               "keys"       : { "semi" : ";", "dot" : ".", "comma" : ","},
               "v"          : "6",
               "x"          : "1024",
               "y"          : "768",
               "empty"      : "",
               "empty_keys" : {},
               "undef"      : null
             },
             "testcases" : [
                ["{?who}", "?who=fred"],
                ["{?half}", "?half=50%25"],
                ["{?x,y}", "?x=1024&y=768"],
                ["{?x,y,empty}", "?x=1024&y=768&empty="],
                ["{?x,y,undef}", "?x=1024&y=768"],
                ["{?var:3}", "?var=val"],
                ["{?list}", "?list=red,green,blue"],
                ["{?list*}", "?list=red&list=green&list=blue"],
                ["{?keys}", [
                  "?keys=comma,%2C,dot,.,semi,%3B",
                  "?keys=comma,%2C,semi,%3B,dot,.",
                  "?keys=dot,.,comma,%2C,semi,%3B",
                  "?keys=dot,.,semi,%3B,comma,%2C",
                  "?keys=semi,%3B,comma,%2C,dot,.",
                  "?keys=semi,%3B,dot,.,comma,%2C"
                ]],
                ["{?keys*}", [
                  "?comma=%2C&dot=.&semi=%3B",
                  "?comma=%2C&semi=%3B&dot=.",
                  "?dot=.&comma=%2C&semi=%3B",
                  "?dot=.&semi=%3B&comma=%2C",
                  "?semi=%3B&comma=%2C&dot=.",
                  "?semi=%3B&dot=.&comma=%2C"
                ]]
             ]
          },
          "3.2.9 Form-Style Query Continuation" :
          {
            "variables": {
               "count"      : ["one", "two", "three"],
               "dom"        : ["example", "com"],
               "dub"        : "me/too",
               "hello"      : "Hello World!",
               "half"       : "50%",
               "var"        : "value",
               "who"        : "fred",
               "base"       : "http://example.com/home/",
               "path"       : "/foo/bar",
               "list"       : ["red", "green", "blue"],
               "keys"       : { "semi" : ";", "dot" : ".", "comma" : ","},
               "v"          : "6",
               "x"          : "1024",
               "y"          : "768",
               "empty"      : "",
               "empty_keys" : {},
               "undef"      : null
             },
             "testcases" : [
                  ["{&who}", "&who=fred"],
                  ["{&half}", "&half=50%25"],
                  ["?fixed=yes{&x}", "?fixed=yes&x=1024"],
                  ["{&var:3}", "&var=val"],
                  ["{&x,y,empty}", "&x=1024&y=768&empty="],
                  ["{&x,y,undef}", "&x=1024&y=768"],
                  ["{&list}", "&list=red,green,blue"],
                  ["{&list*}", "&list=red&list=green&list=blue"],
                  ["{&keys}", [
                    "&keys=comma,%2C,dot,.,semi,%3B",
                    "&keys=comma,%2C,semi,%3B,dot,.",
                    "&keys=dot,.,comma,%2C,semi,%3B",
                    "&keys=dot,.,semi,%3B,comma,%2C",
                    "&keys=semi,%3B,comma,%2C,dot,.",
                    "&keys=semi,%3B,dot,.,comma,%2C"
                  ]],
                  ["{&keys*}", [
                    "&comma=%2C&dot=.&semi=%3B",
                    "&comma=%2C&semi=%3B&dot=.",
                    "&dot=.&comma=%2C&semi=%3B",
                    "&dot=.&semi=%3B&comma=%2C",
                    "&semi=%3B&comma=%2C&dot=.",
                    "&semi=%3B&dot=.&comma=%2C"
                  ]]
             ]
          }
        }
        """;

    private const string ExtendedTests = """
        {
            "Additional Examples 1":{
                "level":4,
                "variables":{
                    "id"           : "person",
                    "token"        : "12345",
                    "fields"       : ["id", "name", "picture"],
                    "format"       : "json",
                    "q"            : "URI Templates",
                    "page"         : "5",
                    "lang"         : "en",
                    "geocode"      : ["37.76","-122.427"],
                    "first_name"   : "John",
                    "last.name"    : "Doe",
                    "Some%20Thing" : "foo",
                    "number"       : 6,
                    "long"         : 37.76,
                    "lat"          : -122.427,
                    "group_id"     : "12345",
                    "query"        : "PREFIX dc: <http://purl.org/dc/elements/1.1/> SELECT ?book ?who WHERE { ?book dc:creator ?who }",
                    "uri"          : "http://example.org/?uri=http%3A%2F%2Fexample.org%2F",
                    "word"         : "drücken",
                    "Stra%C3%9Fe"  : "Grüner Weg",
                    "random"       : "šöäŸœñê€£¥‡ÑÒÓÔÕÖ×ØÙÚàáâãäåæçÿ",
                    "assoc_special_chars"  :
                      { "šöäŸœñê€£¥‡ÑÒÓÔÕ" : "Ö×ØÙÚàáâãäåæçÿ" }
                },
                "testcases":[

                    [ "{/id*}" , "/person" ],
                    [ "{/id*}{?fields,first_name,last.name,token}","/person?fields=id,name,picture&first_name=John&last.name=Doe&token=12345"],
                    ["/search.{format}{?q,geocode,lang,locale,page,result_type}","/search.json?q=URI%20Templates&geocode=37.76,-122.427&lang=en&page=5"],
                    ["/test{/Some%20Thing}", "/test/foo" ],
                    ["/set{?number}", "/set?number=6"],
                    ["/loc{?long,lat}" , "/loc?long=37.76&lat=-122.427"],
                    ["/base{/group_id,first_name}/pages{/page,lang}{?format,q}","/base/12345/John/pages/5/en?format=json&q=URI%20Templates"],
                    ["/sparql{?query}", "/sparql?query=PREFIX%20dc%3A%20%3Chttp%3A%2F%2Fpurl.org%2Fdc%2Felements%2F1.1%2F%3E%20SELECT%20%3Fbook%20%3Fwho%20WHERE%20%7B%20%3Fbook%20dc%3Acreator%20%3Fwho%20%7D"],
                    ["/go{?uri}", "/go?uri=http%3A%2F%2Fexample.org%2F%3Furi%3Dhttp%253A%252F%252Fexample.org%252F"],
                    ["/service{?word}", "/service?word=dr%C3%BCcken"],
                    ["/lookup{?Stra%C3%9Fe}", "/lookup?Stra%C3%9Fe=Gr%C3%BCner%20Weg"],
                    ["{random}" , "%C5%A1%C3%B6%C3%A4%C5%B8%C5%93%C3%B1%C3%AA%E2%82%AC%C2%A3%C2%A5%E2%80%A1%C3%91%C3%92%C3%93%C3%94%C3%95%C3%96%C3%97%C3%98%C3%99%C3%9A%C3%A0%C3%A1%C3%A2%C3%A3%C3%A4%C3%A5%C3%A6%C3%A7%C3%BF"],
                    ["{?assoc_special_chars*}", "?%C5%A1%C3%B6%C3%A4%C5%B8%C5%93%C3%B1%C3%AA%E2%82%AC%C2%A3%C2%A5%E2%80%A1%C3%91%C3%92%C3%93%C3%94%C3%95=%C3%96%C3%97%C3%98%C3%99%C3%9A%C3%A0%C3%A1%C3%A2%C3%A3%C3%A4%C3%A5%C3%A6%C3%A7%C3%BF"]
                ]
            },
            "Additional Examples 2":{
                "level":4,
                "variables":{
                    "id" : ["person","albums"],
                    "token" : "12345",
                    "fields" : ["id", "name", "picture"],
                    "format" : "atom",
                    "q" : "URI Templates",
                    "page" : "10",
                    "start" : "5",
                    "lang" : "en",
                    "geocode" : ["37.76","-122.427"]
                },
                "testcases":[

                    [ "{/id*}" , "/person/albums" ],
                    [ "{/id*}{?fields,token}" , "/person/albums?fields=id,name,picture&token=12345" ]
                ]
            },
            "Additional Examples 3: Empty Variables":{
                "variables" : {
                    "empty_list" : [],
                    "empty_assoc" : {}
                },
                "testcases":[
                    [ "{/empty_list}", [ "" ] ],
                    [ "{/empty_list*}", [ "" ] ],
                    [ "{?empty_list}", [ ""] ],
                    [ "{?empty_list*}", [ "" ] ],
                    [ "{?empty_assoc}", [ "" ] ],
                    [ "{?empty_assoc*}", [ "" ] ]
                ]
            },
            "Additional Examples 4: Numeric Keys":{
                "variables" : {
                    "42" : "The Answer to the Ultimate Question of Life, the Universe, and Everything",
                    "1337" : ["leet", "as","it", "can","be"],
                    "german" : {
                        "11": "elf",
                        "12": "zwölf"
                    }
                },
                "testcases":[
                    [ "{42}", "The%20Answer%20to%20the%20Ultimate%20Question%20of%20Life%2C%20the%20Universe%2C%20and%20Everything"],
                    [ "{?42}", "?42=The%20Answer%20to%20the%20Ultimate%20Question%20of%20Life%2C%20the%20Universe%2C%20and%20Everything"],
                    [ "{1337}", "leet,as,it,can,be"],
                    [ "{?1337*}", "?1337=leet&1337=as&1337=it&1337=can&1337=be"],
                    [ "{?german*}", [ "?11=elf&12=zw%C3%B6lf", "?12=zw%C3%B6lf&11=elf"] ]
                ]
            },
            "Additional Examples 5: Explode Combinations":{
                "variables" : {
                    "id" : "admin",
                    "token" : "12345",
                    "tab" : "overview",
                    "keys" : {
                        "key1": "val1",
                        "key2": "val2"
                    }
                },
                "testcases":[
                    [ "{?id,token,keys*}", [
                        "?id=admin&token=12345&key1=val1&key2=val2",
                        "?id=admin&token=12345&key2=val2&key1=val1"]
                    ],
                    [ "{/id}{?token,keys*}", [
                        "/admin?token=12345&key1=val1&key2=val2",
                        "/admin?token=12345&key2=val2&key1=val1"]
                    ],
                    [ "{?id,token}{&keys*}", [
                        "?id=admin&token=12345&key1=val1&key2=val2",
                        "?id=admin&token=12345&key2=val2&key1=val1"]
                    ],
                    [ "/user{/id}{?token,tab}{&keys*}", [
                        "/user/admin?token=12345&tab=overview&key1=val1&key2=val2",
                        "/user/admin?token=12345&tab=overview&key2=val2&key1=val1"]
                    ]
                ]
            },
            "Additional Examples 6: Reserved Expansion":{
                "variables" : {
                    "id" : "admin%2F",
                    "not_pct" : "%foo",
                    "list" : ["red%25", "%2Fgreen", "blue "],
                    "keys" : {
                        "key1": "val1%2F",
                        "key2": "val2%2F"
                    }
                },
                "testcases": [
        			["{+id}", "admin%2F"],
        			["{#id}", "#admin%2F"],
        			["{id}", "admin%252F"],
        			["{+not_pct}", "%25foo"],
        			["{#not_pct}", "#%25foo"],
        			["{not_pct}", "%25foo"],
        			["{+list}", "red%25,%2Fgreen,blue%20"],
        			["{#list}", "#red%25,%2Fgreen,blue%20"],
        			["{list}", "red%2525,%252Fgreen,blue%20"],
        			["{+keys}", "key1,val1%2F,key2,val2%2F"],
        			["{#keys}", "#key1,val1%2F,key2,val2%2F"],
        			["{keys}", "key1,val1%252F,key2,val2%252F"]
                ]
            }
        }
        """;
}
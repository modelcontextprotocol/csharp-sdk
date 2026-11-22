using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Tests.Server;

public partial class InjectedParameterSchemaTests
{
    private RequestContext<CallToolRequestParams>? _toolRequest;
    private RequestContext<GetPromptRequestParams>? _promptRequest;
    private InjectedService? _service;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tool_ContextRemainsInjectedWhenParametersAreReenumerated(bool freshParameters)
    {
        var method = GetMethod(nameof(EchoTool), freshParameters);
        var tool = McpServerTool.Create(method, this, new() { SerializerOptions = WireJsonContext.Default.Options });
        var properties = tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(["value"], properties);

        var request = new RequestContext<CallToolRequestParams>(
            new Mock<McpServer>().Object,
            new JsonRpcRequest { Id = new RequestId("tool"), Method = RequestMethods.ToolsCall },
            new()
            {
                Name = tool.ProtocolTool.Name,
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["value"] = JsonSerializer.SerializeToElement("hello", WireJsonContext.Default.String),
                },
            });
        var result = await tool.InvokeAsync(request, TestContext.Current.CancellationToken);

        Assert.Same(request, _toolRequest);
        Assert.Equal("hello", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prompt_ContextRemainsInjectedWhenParametersAreReenumerated(bool freshParameters)
    {
        var method = GetMethod(nameof(EchoPrompt), freshParameters);
        var prompt = McpServerPrompt.Create(method, this, new() { SerializerOptions = WireJsonContext.Default.Options });
        Assert.Equal(["value"], Assert.IsAssignableFrom<IEnumerable<PromptArgument>>(prompt.ProtocolPrompt.Arguments).Select(argument => argument.Name).ToArray());
        var request = new RequestContext<GetPromptRequestParams>(
            new Mock<McpServer>().Object,
            new JsonRpcRequest { Id = new RequestId("prompt"), Method = RequestMethods.PromptsGet },
            new()
            {
                Name = prompt.ProtocolPrompt.Name,
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["value"] = JsonSerializer.SerializeToElement("hello", WireJsonContext.Default.String),
                },
            });

        await prompt.GetAsync(request, TestContext.Current.CancellationToken);

        Assert.Same(request, _promptRequest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tool_RegisteredServiceRemainsInjectedWhenParametersAreReenumerated(bool freshParameters)
    {
        var expected = new InjectedService();
        await using var services = new ServiceCollection().AddSingleton(expected).BuildServiceProvider();
        var tool = McpServerTool.Create(GetMethod(nameof(EchoService), freshParameters), this,
            new() { Services = services, SerializerOptions = WireJsonContext.Default.Options });
        Assert.Equal(["value"], tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray());
        var request = new RequestContext<CallToolRequestParams>(
            new Mock<McpServer>().Object,
            new JsonRpcRequest { Id = new RequestId("service"), Method = RequestMethods.ToolsCall },
            new()
            {
                Name = tool.ProtocolTool.Name,
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["value"] = JsonSerializer.SerializeToElement("hello", WireJsonContext.Default.String),
                },
            })
        {
            Services = services,
        };

        await tool.InvokeAsync(request, TestContext.Current.CancellationToken);
        Assert.Same(expected, _service);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tool_CustomSchemaFilterIsPreservedWithoutMutatingOptions(bool freshParameters)
    {
        var schemaOptions = new AIJsonSchemaCreateOptions { IncludeParameter = parameter => parameter.Name != "value" };
        var filter = schemaOptions.IncludeParameter;
        var tool = McpServerTool.Create(GetMethod(nameof(EchoTool), freshParameters), this, new()
        {
            SerializerOptions = WireJsonContext.Default.Options,
            SchemaCreateOptions = schemaOptions,
        });

        Assert.Empty(tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject());
        Assert.Same(filter, schemaOptions.IncludeParameter);
    }

    private string EchoTool(string value, RequestContext<CallToolRequestParams> context)
    {
        _toolRequest = context;
        return value;
    }

    private string EchoPrompt(string value, RequestContext<GetPromptRequestParams> context)
    {
        _promptRequest = context;
        return value;
    }

    private string EchoService(string value, InjectedService service)
    {
        _service = service;
        return value;
    }

    private sealed class InjectedService;

    private MethodInfo GetMethod(string name, bool freshParameters)
    {
        var method = Assert.IsAssignableFrom<MethodInfo>(GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic));
        return freshParameters ? new ReenumeratingMethodInfo(method) : method;
    }

    // ParameterInfo identity is not part of the method signature. Return equivalent but distinct
    // instances to deterministically exercise separate binding and schema-enumeration passes.
    private sealed class ReenumeratingMethodInfo(MethodInfo method) : MethodInfo
    {
        public override string Name => method.Name;
        public override Type? DeclaringType => method.DeclaringType;
        public override Type? ReflectedType => method.ReflectedType;
        public override Module Module => method.Module;
        public override int MetadataToken => method.MetadataToken;
        public override MethodAttributes Attributes => method.Attributes;
        public override RuntimeMethodHandle MethodHandle => method.MethodHandle;
        public override Type ReturnType => method.ReturnType;
        public override ParameterInfo ReturnParameter => method.ReturnParameter;
        public override ICustomAttributeProvider ReturnTypeCustomAttributes => method.ReturnTypeCustomAttributes;
        public override bool ContainsGenericParameters => method.ContainsGenericParameters;
        public override bool IsGenericMethod => method.IsGenericMethod;
        public override bool IsGenericMethodDefinition => method.IsGenericMethodDefinition;
        public override MethodInfo GetBaseDefinition() => method.GetBaseDefinition();
        public override MethodImplAttributes GetMethodImplementationFlags() => method.GetMethodImplementationFlags();
        public override Type[] GetGenericArguments() => method.GetGenericArguments();
        public override object[] GetCustomAttributes(bool inherit) => method.GetCustomAttributes(inherit);
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => method.GetCustomAttributes(attributeType, inherit);
        public override IList<CustomAttributeData> GetCustomAttributesData() => method.GetCustomAttributesData();
        public override bool IsDefined(Type attributeType, bool inherit) => method.IsDefined(attributeType, inherit);
        public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture) =>
            method.Invoke(obj, invokeAttr, binder, parameters, culture);
        public override ParameterInfo[] GetParameters() =>
            Array.ConvertAll(method.GetParameters(), parameter => new ReenumeratedParameter(parameter));
    }

    private sealed class ReenumeratedParameter(ParameterInfo parameter) : ParameterInfo
    {
        public override string? Name => parameter.Name;
        public override Type ParameterType => parameter.ParameterType;
        public override int Position => parameter.Position;
        public override MemberInfo Member => parameter.Member;
        public override ParameterAttributes Attributes => parameter.Attributes;
        public override object? DefaultValue => parameter.DefaultValue;
        public override object? RawDefaultValue => parameter.RawDefaultValue;
        public override bool HasDefaultValue => parameter.HasDefaultValue;
        public override object[] GetCustomAttributes(bool inherit) => parameter.GetCustomAttributes(inherit);
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => parameter.GetCustomAttributes(attributeType, inherit);
        public override IList<CustomAttributeData> GetCustomAttributesData() => parameter.GetCustomAttributesData();
        public override bool IsDefined(Type attributeType, bool inherit) => parameter.IsDefined(attributeType, inherit);
    }

    [JsonSerializable(typeof(string))]
    private partial class WireJsonContext : JsonSerializerContext;
}

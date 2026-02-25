using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ModelContextProtocol.Server;

/// <summary>Provides an <see cref="AIFunctionMcpServerResource"/> that's implemented via an <see cref="AIFunction"/>.</summary>
internal sealed class AIFunctionMcpServerResource : McpServerResource
{
    private readonly Regex? _uriParser;
    private readonly string[] _templateVariableNames = [];
    private readonly IReadOnlyList<object> _metadata;

    /// <summary>
    /// Creates an <see cref="AIFunctionMcpServerResource"/> instance for a method, specified via a <see cref="Delegate"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerResource Create(
        Delegate method,
        McpServerResourceCreateOptions? options)
    {
        Throw.IfNull(method);

        options = DeriveOptions(method.Method, options);

        return Create(method.Method, method.Target, options);
    }

    /// <summary>
    /// Creates an <see cref="McpServerResource"/> instance for a method, specified via a <see cref="MethodInfo"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerResource Create(
        MethodInfo method,
        object? target,
        McpServerResourceCreateOptions? options)
    {
        Throw.IfNull(method);

        options = DeriveOptions(method, options);

        return Create(
            AIFunctionFactory.Create(method, target, CreateAIFunctionFactoryOptions(method, options)),
            options);
    }

    /// <summary>
    /// Creates an <see cref="McpServerResource"/> instance for a method, specified via a <see cref="MethodInfo"/> instance.
    /// </summary>
    public static new AIFunctionMcpServerResource Create(
        MethodInfo method,
        Func<RequestContext<ReadResourceRequestParams>, object> createTargetFunc,
        McpServerResourceCreateOptions? options)
    {
        Throw.IfNull(method);
        Throw.IfNull(createTargetFunc);

        options = DeriveOptions(method, options);

        return Create(
            AIFunctionFactory.Create(method, args =>
            {
                Debug.Assert(args.Services is RequestServiceProvider<ReadResourceRequestParams>, $"The service provider should be a {nameof(RequestServiceProvider<>)} for this method to work correctly.");
                return createTargetFunc(((RequestServiceProvider<ReadResourceRequestParams>)args.Services!).Request);
            }, CreateAIFunctionFactoryOptions(method, options)),
            options);
    }

    private static AIFunctionFactoryOptions CreateAIFunctionFactoryOptions(
        MethodInfo method, McpServerResourceCreateOptions? options) =>
        new()
        {
            Name = options?.Name ?? method.GetCustomAttribute<McpServerResourceAttribute>()?.Name ?? AIFunctionMcpServerTool.DeriveName(method),
            Description = options?.Description,
            MarshalResult = static (result, _, cancellationToken) => new ValueTask<object?>(result),
            SerializerOptions = options?.SerializerOptions ?? McpJsonUtilities.DefaultOptions,
            JsonSchemaCreateOptions = options?.SchemaCreateOptions,
            ConfigureParameterBinding = pi =>
            {
                if (RequestServiceProvider<ReadResourceRequestParams>.IsAugmentedWith(pi.ParameterType) ||
                    (options?.Services?.GetService<IServiceProviderIsService>() is { } ispis &&
                     ispis.IsService(pi.ParameterType)))
                {
                    return new()
                    {
                        ExcludeFromSchema = true,
                        BindParameter = (pi, args) =>
                            args.Services?.GetService(pi.ParameterType) ??
                            (pi.HasDefaultValue ? null :
                             throw new ArgumentException("No service of the requested type was found.")),
                    };
                }

                if (pi.GetCustomAttribute<FromKeyedServicesAttribute>() is { } keyedAttr)
                {
                    return new()
                    {
                        ExcludeFromSchema = true,
                        BindParameter = (pi, args) =>
                            (args?.Services as IKeyedServiceProvider)?.GetKeyedService(pi.ParameterType, keyedAttr.Key) ??
                            (pi.HasDefaultValue ? null :
                             throw new ArgumentException("No service of the requested type was found.")),
                    };
                }

                // These parameters are the ones and only ones to include in the schema. The schema
                // won't be consumed by anyone other than this instance, which will use it to determine
                // which properties should show up in the URI template.
                if (pi.Name is not null && GetConverter(pi.ParameterType) is { } converter)
                {
                    return new()
                    {
                        ExcludeFromSchema = false,
                        BindParameter = (pi, args) =>
                        {
                            if (args.TryGetValue(pi.Name!, out var value))
                            {
                                return
                                    value is null || pi.ParameterType.IsInstanceOfType(value) ? value :
                                    value is string stringValue ? converter(stringValue) :
                                    throw new ArgumentException($"Parameter '{pi.Name}' is of type '{pi.ParameterType}', but value '{value}' is of type '{value.GetType()}'.");
                            }

                            return
                                pi.HasDefaultValue ? pi.DefaultValue :
                                throw new ArgumentException($"Missing a value for the required parameter '{pi.Name}'.");
                        },
                    };
                }

                return default;
            },
        };

    private static readonly ConcurrentDictionary<Type, Func<string, object?>> s_convertersCache = [];

    private static Func<string, object?>? GetConverter(Type type)
    {
        Type key = type;

        if (s_convertersCache.TryGetValue(key, out var converter))
        {
            return converter;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlyingType)
        {
            // We will have already screened out null values by the time the converter is used,
            // so we can parse just the underlying type.
            type = underlyingType;
        }

        if (type == typeof(string) || type == typeof(object)) converter = static s => s;
        if (type == typeof(bool)) converter = static s => bool.Parse(s);
        if (type == typeof(char)) converter = static s => char.Parse(s);
        if (type == typeof(byte)) converter = static s => byte.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(sbyte)) converter = static s => sbyte.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(ushort)) converter = static s => ushort.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(short)) converter = static s => short.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(uint)) converter = static s => uint.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(int)) converter = static s => int.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(ulong)) converter = static s => ulong.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(long)) converter = static s => long.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(float)) converter = static s => float.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(double)) converter = static s => double.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(decimal)) converter = static s => decimal.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(TimeSpan)) converter = static s => TimeSpan.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(DateTime)) converter = static s => DateTime.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(DateTimeOffset)) converter = static s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(Uri)) converter = static s => new Uri(s, UriKind.RelativeOrAbsolute);
        if (type == typeof(Guid)) converter = static s => Guid.Parse(s);
        if (type == typeof(Version)) converter = static s => Version.Parse(s);
#if NET
        if (type == typeof(Half)) converter = static s => Half.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(Int128)) converter = static s => Int128.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(UInt128)) converter = static s => UInt128.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(IntPtr)) converter = static s => IntPtr.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(UIntPtr)) converter = static s => UIntPtr.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(DateOnly)) converter = static s => DateOnly.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(TimeOnly)) converter = static s => TimeOnly.Parse(s, CultureInfo.InvariantCulture);
#endif
        if (type.IsEnum) converter = s => Enum.Parse(type, s);

        if (type.GetCustomAttribute<TypeConverterAttribute>() is TypeConverterAttribute tca &&
            Type.GetType(tca.ConverterTypeName, throwOnError: false) is { } converterType &&
            Activator.CreateInstance(converterType) is TypeConverter typeConverter &&
            typeConverter.CanConvertFrom(typeof(string)))
        {
            converter = s => typeConverter.ConvertFrom(null, CultureInfo.InvariantCulture, s);
        }

        if (converter is not null)
        {
            s_convertersCache.TryAdd(key, converter);
        }

        return converter;
    }

    /// <summary>Creates an <see cref="McpServerResource"/> that wraps the specified <see cref="AIFunction"/>.</summary>
    public static new AIFunctionMcpServerResource Create(AIFunction function, McpServerResourceCreateOptions? options)
    {
        Throw.IfNull(function);

        string name = options?.Name ?? function.Name;

        ResourceTemplate resource = new()
        {
            UriTemplate = options?.UriTemplate ?? DeriveUriTemplate(name, function),
            Name = name,
            Title = options?.Title,
            Description = options?.Description,
            MimeType = options?.MimeType ?? "application/octet-stream",
            Icons = options?.Icons,
            Meta = function.UnderlyingMethod is not null ?
                AIFunctionMcpServerTool.CreateMetaFromAttributes(function.UnderlyingMethod, options?.Meta) :
                options?.Meta,
        };

        return new AIFunctionMcpServerResource(function, resource, options?.Metadata ?? []);
    }

    private static McpServerResourceCreateOptions DeriveOptions(MemberInfo member, McpServerResourceCreateOptions? options)
    {
        McpServerResourceCreateOptions newOptions = options?.Clone() ?? new();

        if (member.GetCustomAttribute<McpServerResourceAttribute>() is { } resourceAttr)
        {
            newOptions.UriTemplate ??= resourceAttr.UriTemplate;
            newOptions.Name ??= resourceAttr.Name;
            newOptions.Title ??= resourceAttr.Title;
            newOptions.MimeType ??= resourceAttr.MimeType;

            // Handle icon from attribute if not already specified in options
            if (newOptions.Icons is null && resourceAttr.IconSource is { Length: > 0 } iconSource)
            {
                newOptions.Icons = [new() { Source = iconSource }];
            }
        }

        if (member.GetCustomAttribute<DescriptionAttribute>() is { } descAttr)
        {
            newOptions.Description ??= descAttr.Description;
        }

        // Set metadata if not already provided and the member is a MethodInfo
        if (member is MethodInfo method)
        {
            newOptions.Metadata ??= AIFunctionMcpServerTool.CreateMetadata(method);
        }

        return newOptions;
    }

    /// <summary>Derives a name to be used as a resource name.</summary>
    private static string DeriveUriTemplate(string name, AIFunction function)
    {
        StringBuilder template = new();

        template.Append("resource://mcp/").Append(Uri.EscapeDataString(name));

        if (function.JsonSchema.TryGetProperty("properties", out JsonElement properties))
        {
            string separator = "{?";
            foreach (var prop in properties.EnumerateObject())
            {
                template.Append(separator).Append(prop.Name);
                separator = ",";
            }

            if (separator == ",")
            {
                template.Append('}');
            }
        }

        return template.ToString();
    }

    /// <summary>Gets the <see cref="AIFunction"/> wrapped by this resource.</summary>
    internal AIFunction AIFunction { get; }

    /// <summary>Initializes a new instance of the <see cref="McpServerResource"/> class.</summary>
    private AIFunctionMcpServerResource(AIFunction function, ResourceTemplate resourceTemplate, IReadOnlyList<object> metadata)
    {
        AIFunction = function;
        ProtocolResourceTemplate = resourceTemplate;
        ProtocolResource = resourceTemplate.AsResource();
        _metadata = metadata;

        if (ProtocolResource is null)
        {
            _uriParser = UriTemplate.CreateParser(resourceTemplate.UriTemplate);
            _templateVariableNames = _uriParser.GetGroupNames().Where(n => n != "0").ToArray();
        }
    }

    /// <inheritdoc />
    public override ResourceTemplate ProtocolResourceTemplate { get; }

    /// <inheritdoc />
    public override Resource? ProtocolResource { get; }

    /// <inheritdoc />
    public override IReadOnlyList<object> Metadata => _metadata;

    /// <inheritdoc />
    public override bool IsMatch(string uri)
    {
        Throw.IfNull(uri);

        // For templates, use the Regex to parse. For static resources, we can just compare the URIs.
        if (_uriParser is null)
        {
            // This resource is not templated.
            return UriTemplate.UriTemplateComparer.Instance.Equals(uri, ProtocolResourceTemplate.UriTemplate);
        }

        return _uriParser.IsMatch(uri);
    }

    private bool TryMatch(string uri, out Match? match)
    {
        if (_uriParser is null)
        {
            match = null;
            return UriTemplate.UriTemplateComparer.Instance.Equals(uri, ProtocolResourceTemplate.UriTemplate);
        }

        match = _uriParser.Match(uri);
        return match.Success;
    }

    /// <inheritdoc />
    public override async ValueTask<ReadResourceResult> ReadAsync(
        RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(request);
        Throw.IfNull(request.Params);
        Throw.IfNull(request.Params.Uri);

        cancellationToken.ThrowIfCancellationRequested();

        if (!TryMatch(request.Params.Uri, out Match? match))
        {
            throw new InvalidOperationException($"Resource '{ProtocolResourceTemplate.UriTemplate}' does not match the provided URI '{request.Params.Uri}'.");
        }

        // Build up the arguments for the AIFunction call, including all of the name/value pairs from the URI.
        request.Services = new RequestServiceProvider<ReadResourceRequestParams>(request);
        AIFunctionArguments arguments = new() { Services = request.Services };

        // For templates, populate the arguments from the URI template.
        if (match is not null)
        {
            foreach (string varName in _templateVariableNames)
            {
                if (match.Groups[varName] is { Success: true } value)
                {
                    arguments[varName] = Uri.UnescapeDataString(value.Value);
                }
            }
        }

        // Invoke the function.
        object? result = await AIFunction.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);

        // And process the result.
        return result switch
        {
            ReadResourceResult readResourceResult => readResourceResult,

            ResourceContents content => new()
            {
                Contents = [content],
            },

            TextContent tc => new()
            {
                Contents = [new TextResourceContents { Uri = request.Params!.Uri, MimeType = ProtocolResourceTemplate.MimeType, Text = tc.Text }],
            },

            DataContent dc => new()
            {
                Contents = [new BlobResourceContents 
                { 
                    Uri = request.Params!.Uri, 
                    MimeType = dc.MediaType, 
                    Blob = EncodingUtilities.GetUtf8Bytes(dc.Base64Data.Span)
                }],
            },

            string text => new()
            {
                Contents = [new TextResourceContents { Uri = request.Params!.Uri, MimeType = ProtocolResourceTemplate.MimeType, Text = text }],
            },

            IEnumerable<ResourceContents> contents => new()
            {
                Contents = contents.ToList(),
            },

            IEnumerable<AIContent> aiContents => new()
            {
                Contents = aiContents.Select<AIContent, ResourceContents>(
                    ac => ac switch
                    {
                        TextContent tc => new TextResourceContents
                        {
                            Uri = request.Params!.Uri,
                            MimeType = ProtocolResourceTemplate.MimeType,
                            Text = tc.Text
                        },

                        DataContent dc => new BlobResourceContents
                        {
                            Uri = request.Params!.Uri,
                            MimeType = dc.MediaType,
                            Blob = EncodingUtilities.GetUtf8Bytes(dc.Base64Data.Span)
                        },

                        _ => throw new InvalidOperationException($"Unsupported AIContent type '{ac.GetType()}' returned from resource function."),
                    }).ToList(),
            },

            IEnumerable<string> strings => new()
            {
                Contents = strings.Select<string, ResourceContents>(text => new TextResourceContents
                {
                    Uri = request.Params!.Uri,
                    MimeType = ProtocolResourceTemplate.MimeType,
                    Text = text
                }).ToList(),
            },

            null => throw new InvalidOperationException("Null result returned from resource function."),

            _ => throw new InvalidOperationException($"Unsupported result type '{result.GetType()}' returned from resource function."),
        };
    }
}
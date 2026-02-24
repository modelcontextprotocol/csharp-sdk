using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a message issued from the server to elicit additional information from the user via the client.
/// </summary>
public sealed class ElicitRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the elicitation mode: "form" for in-band data collection or "url" for out-of-band URL navigation.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description><b>form</b>: Client collects structured data via a form interface. Data is exposed to the client.</description></item>
    ///   <item><description><b>url</b>: Client navigates user to a URL for out-of-band interaction. Sensitive data is not exposed to the client.</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentException">The value is not "form" or "url".</exception>
    [JsonPropertyName("mode")]
    [field: MaybeNull]
    public string Mode
    {
        get => field ??= "form";
        set
        {
            if (value is not ("form" or "url"))
            {
                throw new ArgumentException("Mode must be 'form' or 'url'.", nameof(value));
            }
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets a unique identifier for this elicitation request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used to track and correlate the elicitation across multiple messages, especially for out-of-band flows
    /// that may complete asynchronously.
    /// </para>
    /// <para>
    /// Required for url mode elicitation to enable progress tracking and completion detection.
    /// </para>
    /// </remarks>
    [JsonPropertyName("elicitationId")]
    public string? ElicitationId { get; set; }

    /// <summary>
    /// Gets or sets the URL to navigate to for out-of-band elicitation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Required when <see cref="Mode"/> is "url". The client should prompt the user for consent
    /// and then navigate to this URL in a user-agent (browser) where the user completes
    /// the required interaction.
    /// </para>
    /// <para>
    /// URLs must not appear in any other field of the elicitation request for security reasons.
    /// </para>
    /// </remarks>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the message to present to the user.
    /// </summary>
    /// <remarks>
    /// For form mode, this describes what information is being requested.
    /// For url mode, this explains why the user needs to navigate to the URL.
    /// </remarks>
    [JsonPropertyName("message")]
    public required string Message { get; set; }

    /// <summary>
    /// Gets or sets the requested schema for form mode elicitation.
    /// </summary>
    /// <remarks>
    /// Only applicable when <see cref="Mode"/> is "form".
    /// </remarks>
    /// <value>
    /// Possible values are <see cref="StringSchema"/>, <see cref="NumberSchema"/>, <see cref="BooleanSchema"/>,
    /// <see cref="UntitledSingleSelectEnumSchema"/>, <see cref="TitledSingleSelectEnumSchema"/>,
    /// <see cref="UntitledMultiSelectEnumSchema"/>, <see cref="TitledMultiSelectEnumSchema"/>,
    /// and <see cref="LegacyTitledEnumSchema"/> (deprecated).
    /// </value>
    [JsonPropertyName("requestedSchema")]
    public RequestSchema? RequestedSchema { get; set; }

    /// <summary>
    /// Gets or sets optional task metadata to augment this request with task execution.
    /// </summary>
    /// <remarks>
    /// When present, indicates that the requestor wants this operation executed as a task.
    /// The receiver must support task augmentation for this specific request type.
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    [JsonIgnore]
    public McpTaskMetadata? Task
    {
        get => TaskCore;
        set => TaskCore = value;
    }

    // See ExperimentalInternalPropertyTests.cs before modifying this property.
    [JsonInclude]
    [JsonPropertyName("task")]
    internal McpTaskMetadata? TaskCore { get; set; }

    /// <summary>Represents a request schema used in a form mode elicitation request.</summary>
    public sealed class RequestSchema
    {
        /// <summary>Gets the type of the schema.</summary>
        /// <remarks>This value is always "object".</remarks>
        [JsonPropertyName("type")]
        public string Type => "object";

        /// <summary>Gets or sets the properties of the schema.</summary>
        [JsonPropertyName("properties")]
        [field: MaybeNull]
        public IDictionary<string, PrimitiveSchemaDefinition> Properties
        {
            get => field ??= new Dictionary<string, PrimitiveSchemaDefinition>();
            set
            {
                Throw.IfNull(value);
                field = value;
            }
        }

        /// <summary>Gets or sets the required properties of the schema.</summary>
        [JsonPropertyName("required")]
        public IList<string>? Required { get; set; }
    }

    /// <summary>
    /// Represents a restricted subset of JSON Schema:
    /// <see cref="StringSchema"/>, <see cref="NumberSchema"/>, <see cref="BooleanSchema"/>,
    /// <see cref="UntitledSingleSelectEnumSchema"/>, <see cref="TitledSingleSelectEnumSchema"/>,
    /// <see cref="UntitledMultiSelectEnumSchema"/>, <see cref="TitledMultiSelectEnumSchema"/>,
    /// or <see cref="LegacyTitledEnumSchema"/> (deprecated).
    /// </summary>
    [JsonConverter(typeof(Converter))]
    public abstract class PrimitiveSchemaDefinition
    {
        /// <summary>Prevents external derivations.</summary>
        protected private PrimitiveSchemaDefinition()
        {
        }

        /// <summary>Gets the default value for this schema as a <see cref="JsonElement"/>, if one is defined.</summary>
        internal JsonElement? GetDefaultAsJsonElement() => this switch
        {
            StringSchema { Default: { } s } => JsonSerializer.SerializeToElement(s, McpJsonUtilities.JsonContext.Default.String),
            NumberSchema { Default: { } n } => JsonSerializer.SerializeToElement(n, McpJsonUtilities.JsonContext.Default.Double),
            BooleanSchema { Default: { } b } => JsonSerializer.SerializeToElement(b, McpJsonUtilities.JsonContext.Default.Boolean),
            UntitledSingleSelectEnumSchema { Default: { } s } => JsonSerializer.SerializeToElement(s, McpJsonUtilities.JsonContext.Default.String),
            TitledSingleSelectEnumSchema { Default: { } s } => JsonSerializer.SerializeToElement(s, McpJsonUtilities.JsonContext.Default.String),
            UntitledMultiSelectEnumSchema { Default: { } a } => JsonSerializer.SerializeToElement(a, McpJsonUtilities.JsonContext.Default.IListString),
            TitledMultiSelectEnumSchema { Default: { } a } => JsonSerializer.SerializeToElement(a, McpJsonUtilities.JsonContext.Default.IListString),
#pragma warning disable MCP9001 // LegacyTitledEnumSchema is deprecated but supported for backward compatibility
            LegacyTitledEnumSchema { Default: { } s } => JsonSerializer.SerializeToElement(s, McpJsonUtilities.JsonContext.Default.String),
#pragma warning restore MCP9001
            _ => null,
        };

        /// <summary>Gets or sets the type of the schema.</summary>
        [JsonPropertyName("type")]
        public abstract string Type { get; set; }

        /// <summary>Gets or sets a title for the schema.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>Gets or sets a description for the schema.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Provides a <see cref="JsonConverter"/> for <see cref="PrimitiveSchemaDefinition"/>.
        /// </summary>
        /// <remarks>
        /// Provides a polymorphic converter for the <see cref="PrimitiveSchemaDefinition"/> class that doesn't require
        /// setting <see cref="JsonSerializerOptions.AllowOutOfOrderMetadataProperties"/> explicitly.
        /// </remarks>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public sealed class Converter : JsonConverter<PrimitiveSchemaDefinition>
        {
            /// <inheritdoc/>
            public override PrimitiveSchemaDefinition? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException();
                }

                string? type = null;
                string? title = null;
                string? description = null;
                int? minLength = null;
                int? maxLength = null;
                string? format = null;
                double? minimum = null;
                double? maximum = null;
                bool? defaultBool = null;
                double? defaultNumber = null;
                string? defaultString = null;
                IList<string>? defaultStringArray = null;
                IList<string>? enumValues = null;
                IList<string>? enumNames = null;
                IList<EnumSchemaOption>? oneOf = null;
                int? minItems = null;
                int? maxItems = null;
                object? items = null; // Can be UntitledEnumItemsSchema or TitledEnumItemsSchema

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    string? propertyName = reader.GetString();
                    bool success = reader.Read();
                    Debug.Assert(success, "STJ must have buffered the entire object for us.");

                    switch (propertyName)
                    {
                        case "type":
                            type = reader.GetString();
                            break;

                        case "title":
                            title = reader.GetString();
                            break;

                        case "description":
                            description = reader.GetString();
                            break;

                        case "minLength":
                            minLength = reader.GetInt32();
                            break;

                        case "maxLength":
                            maxLength = reader.GetInt32();
                            break;

                        case "format":
                            format = reader.GetString();
                            break;

                        case "minimum":
                            minimum = reader.GetDouble();
                            break;

                        case "maximum":
                            maximum = reader.GetDouble();
                            break;

                        case "minItems":
                            minItems = reader.GetInt32();
                            break;

                        case "maxItems":
                            maxItems = reader.GetInt32();
                            break;

                        case "default":
                            // We need to handle different types for default values
                            // Store the value based on the JSON token type
                            switch (reader.TokenType)
                            {
                                case JsonTokenType.True:
                                    defaultBool = true;
                                    break;
                                case JsonTokenType.False:
                                    defaultBool = false;
                                    break;
                                case JsonTokenType.Number:
                                    defaultNumber = reader.GetDouble();
                                    break;
                                case JsonTokenType.String:
                                    defaultString = reader.GetString();
                                    break;
                                case JsonTokenType.StartArray:
                                    defaultStringArray = JsonSerializer.Deserialize(ref reader, McpJsonUtilities.JsonContext.Default.IListString);
                                    break;
                            }
                            break;

                        case "enum":
                            enumValues = JsonSerializer.Deserialize(ref reader, McpJsonUtilities.JsonContext.Default.IListString);
                            break;

                        case "enumNames":
                            enumNames = JsonSerializer.Deserialize(ref reader, McpJsonUtilities.JsonContext.Default.IListString);
                            break;

                        case "oneOf":
                            oneOf = DeserializeEnumOptions(ref reader);
                            break;

                        case "items":
                            items = DeserializeEnumItemsSchema(ref reader);
                            break;

                        default:
                            reader.Skip();
                            break;
                    }
                }

                if (type is null)
                {
                    throw new JsonException("The 'type' property is required.");
                }

                PrimitiveSchemaDefinition? psd = null;
                switch (type)
                {
                    case "string":
                        if (oneOf is not null)
                        {
                            // TitledSingleSelectEnumSchema
                            psd = new TitledSingleSelectEnumSchema
                            {
                                OneOf = oneOf,
                                Default = defaultString,
                            };
                        }
                        else if (enumValues is not null)
                        {
                            if (enumNames is not null)
                            {
                                // LegacyTitledEnumSchema is deprecated but supported for backward compatibility.
#pragma warning disable MCP9001
                                psd = new LegacyTitledEnumSchema
#pragma warning restore MCP9001
                                {
                                    Enum = enumValues,
                                    EnumNames = enumNames,
                                    Default = defaultString,
                                };
                            }
                            else
                            {
                                // UntitledSingleSelectEnumSchema
                                psd = new UntitledSingleSelectEnumSchema
                                {
                                    Enum = enumValues,
                                    Default = defaultString,
                                };
                            }
                        }
                        else
                        {
                            psd = new StringSchema
                            {
                                MinLength = minLength,
                                MaxLength = maxLength,
                                Format = format,
                                Default = defaultString,
                            };
                        }
                        break;

                    case "array":
                        if (items is TitledEnumItemsSchema titledItems)
                        {
                            // TitledMultiSelectEnumSchema
                            psd = new TitledMultiSelectEnumSchema
                            {
                                MinItems = minItems,
                                MaxItems = maxItems,
                                Items = titledItems,
                                Default = defaultStringArray,
                            };
                        }
                        else if (items is UntitledEnumItemsSchema untitledItems)
                        {
                            // UntitledMultiSelectEnumSchema
                            psd = new UntitledMultiSelectEnumSchema
                            {
                                MinItems = minItems,
                                MaxItems = maxItems,
                                Items = untitledItems,
                                Default = defaultStringArray,
                            };
                        }
                        break;

                    case "integer":
                    case "number":
                        psd = new NumberSchema
                        {
                            Minimum = minimum,
                            Maximum = maximum,
                            Default = defaultNumber,
                        };
                        break;

                    case "boolean":
                        psd = new BooleanSchema
                        {
                            Default = defaultBool,
                        };
                        break;
                }

                if (psd is not null)
                {
                    psd.Type = type;
                    psd.Title = title;
                    psd.Description = description;
                }

                return psd;
            }

            private static List<EnumSchemaOption> DeserializeEnumOptions(ref Utf8JsonReader reader)
            {
                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    throw new JsonException("Expected array for oneOf property.");
                }

                var options = new List<EnumSchemaOption>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        throw new JsonException("Expected object in oneOf array.");
                    }

                    string? constValue = null;
                    string? titleValue = null;

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName)
                        {
                            string? propertyName = reader.GetString();
                            reader.Read();

                            switch (propertyName)
                            {
                                case "const":
                                    constValue = reader.GetString();
                                    break;
                                case "title":
                                    titleValue = reader.GetString();
                                    break;
                                default:
                                    reader.Skip();
                                    break;
                            }
                        }
                    }

                    if (constValue is null || titleValue is null)
                    {
                        throw new JsonException("Each option in oneOf must have both 'const' and 'title' properties.");
                    }

                    options.Add(new EnumSchemaOption { Const = constValue, Title = titleValue });
                }

                return options;
            }

            private static object DeserializeEnumItemsSchema(ref Utf8JsonReader reader)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException("Expected object for items property.");
                }

                string? type = null;
                IList<string>? enumValues = null;
                IList<EnumSchemaOption>? anyOf = null;

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        string? propertyName = reader.GetString();
                        reader.Read();

                        switch (propertyName)
                        {
                            case "type":
                                type = reader.GetString();
                                break;
                            case "enum":
                                enumValues = JsonSerializer.Deserialize(ref reader, McpJsonUtilities.JsonContext.Default.IListString);
                                break;
                            case "anyOf":
                                anyOf = DeserializeEnumOptions(ref reader);
                                break;
                            default:
                                reader.Skip();
                                break;
                        }
                    }
                }

                // Determine which type to create based on the properties
                if (anyOf is not null)
                {
                    return new TitledEnumItemsSchema { AnyOf = anyOf };
                }
                else if (enumValues is not null)
                {
                    return new UntitledEnumItemsSchema { Type = type ?? "string", Enum = enumValues };
                }
                else
                {
                    throw new JsonException("Items schema must have either 'enum' or 'anyOf' property.");
                }
            }

            /// <inheritdoc/>
            public override void Write(Utf8JsonWriter writer, PrimitiveSchemaDefinition value, JsonSerializerOptions options)
            {
                if (value is null)
                {
                    writer.WriteNullValue();
                    return;
                }

                writer.WriteStartObject();

                writer.WriteString("type", value.Type);
                if (value.Title is not null)
                {
                    writer.WriteString("title", value.Title);
                }
                if (value.Description is not null)
                {
                    writer.WriteString("description", value.Description);
                }

                switch (value)
                {
                    case StringSchema stringSchema:
                        if (stringSchema.MinLength.HasValue)
                        {
                            writer.WriteNumber("minLength", stringSchema.MinLength.Value);
                        }
                        if (stringSchema.MaxLength.HasValue)
                        {
                            writer.WriteNumber("maxLength", stringSchema.MaxLength.Value);
                        }
                        if (stringSchema.Format is not null)
                        {
                            writer.WriteString("format", stringSchema.Format);
                        }
                        if (stringSchema.Default is not null)
                        {
                            writer.WriteString("default", stringSchema.Default);
                        }
                        break;

                    case NumberSchema numberSchema:
                        if (numberSchema.Minimum.HasValue)
                        {
                            writer.WriteNumber("minimum", numberSchema.Minimum.Value);
                        }
                        if (numberSchema.Maximum.HasValue)
                        {
                            writer.WriteNumber("maximum", numberSchema.Maximum.Value);
                        }
                        if (numberSchema.Default is not null)
                        {
                            writer.WriteNumber("default", numberSchema.Default.Value);
                        }
                        break;

                    case BooleanSchema booleanSchema:
                        if (booleanSchema.Default is not null)
                        {
                            writer.WriteBoolean("default", booleanSchema.Default.Value);
                        }
                        break;

                    case UntitledSingleSelectEnumSchema untitledSingleSelect:
                        if (untitledSingleSelect.Enum is not null)
                        {
                            writer.WritePropertyName("enum");
                            JsonSerializer.Serialize(writer, untitledSingleSelect.Enum, McpJsonUtilities.JsonContext.Default.IListString);
                        }
                        if (untitledSingleSelect.Default is not null)
                        {
                            writer.WriteString("default", untitledSingleSelect.Default);
                        }
                        break;

                    case TitledSingleSelectEnumSchema titledSingleSelect:
                        if (titledSingleSelect.OneOf is not null && titledSingleSelect.OneOf.Count > 0)
                        {
                            writer.WritePropertyName("oneOf");
                            SerializeEnumOptions(writer, titledSingleSelect.OneOf);
                        }
                        if (titledSingleSelect.Default is not null)
                        {
                            writer.WriteString("default", titledSingleSelect.Default);
                        }
                        break;

                    case UntitledMultiSelectEnumSchema untitledMultiSelect:
                        if (untitledMultiSelect.MinItems.HasValue)
                        {
                            writer.WriteNumber("minItems", untitledMultiSelect.MinItems.Value);
                        }
                        if (untitledMultiSelect.MaxItems.HasValue)
                        {
                            writer.WriteNumber("maxItems", untitledMultiSelect.MaxItems.Value);
                        }
                        writer.WritePropertyName("items");
                        SerializeUntitledEnumItemsSchema(writer, untitledMultiSelect.Items);
                        if (untitledMultiSelect.Default is not null)
                        {
                            writer.WritePropertyName("default");
                            JsonSerializer.Serialize(writer, untitledMultiSelect.Default, McpJsonUtilities.JsonContext.Default.IListString);
                        }
                        break;

                    case TitledMultiSelectEnumSchema titledMultiSelect:
                        if (titledMultiSelect.MinItems.HasValue)
                        {
                            writer.WriteNumber("minItems", titledMultiSelect.MinItems.Value);
                        }
                        if (titledMultiSelect.MaxItems.HasValue)
                        {
                            writer.WriteNumber("maxItems", titledMultiSelect.MaxItems.Value);
                        }
                        writer.WritePropertyName("items");
                        SerializeTitledEnumItemsSchema(writer, titledMultiSelect.Items);
                        if (titledMultiSelect.Default is not null)
                        {
                            writer.WritePropertyName("default");
                            JsonSerializer.Serialize(writer, titledMultiSelect.Default, McpJsonUtilities.JsonContext.Default.IListString);
                        }
                        break;

#pragma warning disable MCP9001 // LegacyTitledEnumSchema is deprecated but supported for backward compatibility
                    case LegacyTitledEnumSchema legacyTitledEnum:
#pragma warning restore MCP9001
                        if (legacyTitledEnum.Enum is not null)
                        {
                            writer.WritePropertyName("enum");
                            JsonSerializer.Serialize(writer, legacyTitledEnum.Enum, McpJsonUtilities.JsonContext.Default.IListString);
                        }
                        if (legacyTitledEnum.EnumNames is not null)
                        {
                            writer.WritePropertyName("enumNames");
                            JsonSerializer.Serialize(writer, legacyTitledEnum.EnumNames, McpJsonUtilities.JsonContext.Default.IListString);
                        }
                        if (legacyTitledEnum.Default is not null)
                        {
                            writer.WriteString("default", legacyTitledEnum.Default);
                        }
                        break;

                    default:
                        throw new JsonException($"Unexpected schema type: {value.GetType().Name}");
                }

                writer.WriteEndObject();
            }

            private static void SerializeEnumOptions(Utf8JsonWriter writer, IList<EnumSchemaOption> options)
            {
                writer.WriteStartArray();
                foreach (var option in options)
                {
                    writer.WriteStartObject();
                    writer.WriteString("const", option.Const);
                    writer.WriteString("title", option.Title);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            private static void SerializeUntitledEnumItemsSchema(Utf8JsonWriter writer, UntitledEnumItemsSchema itemsSchema)
            {
                writer.WriteStartObject();
                writer.WriteString("type", itemsSchema.Type);
                writer.WritePropertyName("enum");
                JsonSerializer.Serialize(writer, itemsSchema.Enum, McpJsonUtilities.JsonContext.Default.IListString);
                writer.WriteEndObject();
            }

            private static void SerializeTitledEnumItemsSchema(Utf8JsonWriter writer, TitledEnumItemsSchema itemsSchema)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("anyOf");
                SerializeEnumOptions(writer, itemsSchema.AnyOf);
                writer.WriteEndObject();
            }
        }
    }

    /// <summary>Represents a schema for a string type.</summary>
    public sealed class StringSchema : PrimitiveSchemaDefinition
    {
        /// <inheritdoc/>
        [JsonPropertyName("type")]
        public override string Type
        {
            get => "string";
            set
            {
                if (value is not "string")
                {
                    throw new ArgumentException("Type must be 'string'.", nameof(value));
                }
            }
        }

        /// <summary>Gets or sets the minimum length for the string.</summary>
        [JsonPropertyName("minLength")]
        public int? MinLength
        {
            get => field;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Minimum length cannot be negative.");
                }

                field = value;
            }
        }

        /// <summary>Gets or sets the maximum length for the string.</summary>
        [JsonPropertyName("maxLength")]
        public int? MaxLength
        {
            get => field;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Maximum length cannot be negative.");
                }

                field = value;
            }
        }

        /// <summary>Gets or sets a specific format for the string ("email", "uri", "date", or "date-time").</summary>
        [JsonPropertyName("format")]
        public string? Format
        {
            get => field;
            set
            {
                if (value is not (null or "email" or "uri" or "date" or "date-time"))
                {
                    throw new ArgumentException("Format must be 'email', 'uri', 'date', or 'date-time'.", nameof(value));
                }

                field = value;
            }
        }

        /// <summary>Gets or sets the default value for the string.</summary>
        [JsonPropertyName("default")]
        public string? Default { get; set; }
    }

    /// <summary>Represents a schema for a number or integer type.</summary>
    public sealed class NumberSchema : PrimitiveSchemaDefinition
    {
        /// <inheritdoc/>
        [field: MaybeNull]
        public override string Type
        {
            get => field ??= "number";
            set
            {
                if (value is not ("number" or "integer"))
                {
                    throw new ArgumentException("Type must be 'number' or 'integer'.", nameof(value));
                }

                field = value;
            }
        }

        /// <summary>Gets or sets the minimum allowed value.</summary>
        [JsonPropertyName("minimum")]
        public double? Minimum { get; set; }

        /// <summary>Gets or sets the maximum allowed value.</summary>
        [JsonPropertyName("maximum")]
        public double? Maximum { get; set; }

        /// <summary>Gets or sets the default value for the number.</summary>
        [JsonPropertyName("default")]
        public double? Default { get; set; }
    }

    /// <summary>Represents a schema for a Boolean type.</summary>
    public sealed class BooleanSchema : PrimitiveSchemaDefinition
    {
        /// <inheritdoc/>
        [JsonPropertyName("type")]
        public override string Type
        {
            get => "boolean";
            set
            {
                if (value is not "boolean")
                {
                    throw new ArgumentException("Type must be 'boolean'.", nameof(value));
                }
            }
        }

        /// <summary>Gets or sets the default value for the Boolean.</summary>
        [JsonPropertyName("default")]
        public bool? Default { get; set; }
    }

    /// <summary>
    /// Represents a schema for single-selection enumeration without display titles for options.
    /// </summary>
    public sealed class UntitledSingleSelectEnumSchema : PrimitiveSchemaDefinition
    {
        /// <inheritdoc/>
        [JsonPropertyName("type")]
        public override string Type
        {
            get => "string";
            set
            {
                if (value is not "string")
                {
                    throw new ArgumentException("Type must be 'string'.", nameof(value));
                }
            }
        }

        /// <summary>Gets or sets the list of allowed string values for the enum.</summary>
        [JsonPropertyName("enum")]
        [field: MaybeNull]
        public IList<string> Enum
        {
            get => field ??= [];
            set
            {
                Throw.IfNull(value);
                field = value;
            }
        }

        /// <summary>Gets or sets the default value for the enum.</summary>
        [JsonPropertyName("default")]
        public string? Default { get; set; }
    }

    /// <summary>
    /// Represents a single option in a titled enum schema with a constant value and display title.
    /// </summary>
    public sealed class EnumSchemaOption
    {
        /// <summary>Gets or sets the constant value for this option.</summary>
        [JsonPropertyName("const")]
        public required string Const { get; set; }

        /// <summary>Gets or sets the display title for this option.</summary>
        [JsonPropertyName("title")]
        public required string Title { get; set; }
    }

    /// <summary>
    /// Represents a schema for single-selection enumeration with display titles for each option.
    /// </summary>
    public sealed class TitledSingleSelectEnumSchema : PrimitiveSchemaDefinition
    {
        /// <inheritdoc/>
        [JsonPropertyName("type")]
        public override string Type
        {
            get => "string";
            set
            {
                if (value is not "string")
                {
                    throw new ArgumentException("Type must be 'string'.", nameof(value));
                }
            }
        }

        /// <summary>Gets or sets the list of enum options with their constant values and display titles.</summary>
        [JsonPropertyName("oneOf")]
        [field: MaybeNull]
        public IList<EnumSchemaOption> OneOf
        {
            get => field ??= [];
            set
            {
                Throw.IfNull(value);
                field = value;
            }
        }

        /// <summary>Gets or sets the default value for the enum.</summary>
        [JsonPropertyName("default")]
        public string? Default { get; set; }
    }

    /// <summary>
    /// Represents the items schema for untitled multi-select enum arrays.
    /// </summary>
    public sealed class UntitledEnumItemsSchema
    {
        /// <summary>Gets or sets the type of the items.</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "string";

        /// <summary>Gets or sets the list of allowed string values.</summary>
        [JsonPropertyName("enum")]
        public required IList<string> Enum { get; set; }
    }

    /// <summary>
    /// Represents the items schema for titled multi-select enum arrays.
    /// </summary>
    public sealed class TitledEnumItemsSchema
    {
        /// <summary>Gets or sets the list of enum options with constant values and display titles.</summary>
        [JsonPropertyName("anyOf")]
        public required IList<EnumSchemaOption> AnyOf { get; set; }
    }

    /// <summary>
    /// Represents a schema for multiple-selection enumeration without display titles for options.
    /// </summary>
    public sealed class UntitledMultiSelectEnumSchema : PrimitiveSchemaDefinition
    {
        /// <inheritdoc/>
        [JsonPropertyName("type")]
        public override string Type
        {
            get => "array";
            set
            {
                if (value is not "array")
                {
                    throw new ArgumentException("Type must be 'array'.", nameof(value));
                }
            }
        }

        /// <summary>Gets or sets the minimum number of items that can be selected.</summary>
        [JsonPropertyName("minItems")]
        public int? MinItems { get; set; }

        /// <summary>Gets or sets the maximum number of items that can be selected.</summary>
        [JsonPropertyName("maxItems")]
        public int? MaxItems { get; set; }

        /// <summary>Gets or sets the schema for items in the array.</summary>
        [JsonPropertyName("items")]
        public required UntitledEnumItemsSchema Items { get; set; }

        /// <summary>Gets or sets the default values for the enum.</summary>
        [JsonPropertyName("default")]
        public IList<string>? Default { get; set; }
    }

    /// <summary>
    /// Represents a schema for multiple-selection enumeration with display titles for each option.
    /// </summary>
    public sealed class TitledMultiSelectEnumSchema : PrimitiveSchemaDefinition
    {
        /// <inheritdoc/>
        [JsonPropertyName("type")]
        public override string Type
        {
            get => "array";
            set
            {
                if (value is not "array")
                {
                    throw new ArgumentException("Type must be 'array'.", nameof(value));
                }
            }
        }

        /// <summary>Gets or sets the minimum number of items that can be selected.</summary>
        [JsonPropertyName("minItems")]
        public int? MinItems { get; set; }

        /// <summary>Gets or sets the maximum number of items that can be selected.</summary>
        [JsonPropertyName("maxItems")]
        public int? MaxItems { get; set; }

        /// <summary>Gets or sets the schema for items in the array.</summary>
        [JsonPropertyName("items")]
        public required TitledEnumItemsSchema Items { get; set; }

        /// <summary>Gets or sets the default values for the enum.</summary>
        [JsonPropertyName("default")]
        public IList<string>? Default { get; set; }
    }

    /// <summary>
    /// Represents a legacy schema for an enum type with enumNames.
    /// </summary>
    /// <remarks>
    /// This schema is deprecated in favor of <see cref="TitledSingleSelectEnumSchema"/>.
    /// </remarks>
    [Obsolete(Obsoletions.LegacyTitledEnumSchema_Message, DiagnosticId = Obsoletions.LegacyTitledEnumSchema_DiagnosticId, UrlFormat = Obsoletions.LegacyTitledEnumSchema_Url)]
    public sealed class LegacyTitledEnumSchema : PrimitiveSchemaDefinition
    {
        /// <inheritdoc/>
        [JsonPropertyName("type")]
        public override string Type
        {
            get => "string";
            set
            {
                if (value is not "string")
                {
                    throw new ArgumentException("Type must be 'string'.", nameof(value));
                }
            }
        }

        /// <summary>Gets or sets the list of allowed string values for the enum.</summary>
        [JsonPropertyName("enum")]
        [field: MaybeNull]
        public IList<string> Enum
        {
            get => field ??= [];
            set
            {
                Throw.IfNull(value);
                field = value;
            }
        }

        /// <summary>Gets or sets optional display names corresponding to the enum values.</summary>
        [JsonPropertyName("enumNames")]
        public IList<string>? EnumNames { get; set; }

        /// <summary>Gets or sets the default value for the enum.</summary>
        [JsonPropertyName("default")]
        public string? Default { get; set; }
    }
}

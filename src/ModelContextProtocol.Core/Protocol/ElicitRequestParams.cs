using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a message issued from the server to elicit additional information from the user via the client.
/// </summary>
public sealed class ElicitRequestParams
{
    /// <summary>
    /// Gets or sets the message to present to the user.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the requested schema.
    /// </summary>
    /// <remarks>
    /// May be one of <see cref="StringSchema"/>, <see cref="NumberSchema"/>, <see cref="BooleanSchema"/>, or <see cref="EnumSchema"/>.
    /// </remarks>
    [JsonPropertyName("requestedSchema")]
    [field: MaybeNull]
    public RequestSchema RequestedSchema
    {
        get => field ??= new RequestSchema();
        set => field = value;
    }

    /// <summary>Represents a request schema used in an elicitation request.</summary>
    public class RequestSchema
    {
        /// <summary>Gets the type of the schema.</summary>
        /// <remarks>This is always "object".</remarks>
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
    /// Represents restricted subset of JSON Schema: 
    /// <see cref="StringSchema"/>, <see cref="NumberSchema"/>, <see cref="BooleanSchema"/>, or <see cref="EnumSchema"/>.
    /// </summary>
    [JsonConverter(typeof(Converter))] // TODO: This converter exists due to the lack of downlevel support for AllowOutOfOrderMetadataProperties.
    public abstract class PrimitiveSchemaDefinition
    {
        /// <summary>Prevent external derivations.</summary>
        protected private PrimitiveSchemaDefinition()
        {
        }

        /// <summary>Gets the type of the schema.</summary>
        [JsonPropertyName("type")]
        public abstract string Type { get; set; }

        /// <summary>Gets or sets a title for the schema.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>Gets or sets a description for the schema.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Provides a <see cref="JsonConverter"/> for <see cref="ResourceContents"/>.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public class Converter : JsonConverter<PrimitiveSchemaDefinition>
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
                IList<string>? enumValues = null;
                IList<string>? enumNames = null;

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

                        case "default":
                            defaultBool = reader.GetBoolean();
                            break;

                        case "enum":
                            enumValues = JsonSerializer.Deserialize(ref reader, McpJsonUtilities.JsonContext.Default.IListString);
                            break;

                        case "enumNames":
                            enumNames = JsonSerializer.Deserialize(ref reader, McpJsonUtilities.JsonContext.Default.IListString);
                            break;

                        default:
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
                        if (enumValues is not null)
                        {
                            psd = new EnumSchema
                            {
                                Enum = enumValues,
                                EnumNames = enumNames
                            };
                        }
                        else
                        {
                            psd = new StringSchema
                            {
                                MinLength = minLength,
                                MaxLength = maxLength,
                                Format = format,
                            };
                        }
                        break;

                    case "integer":
                    case "number":
                        psd = new NumberSchema
                        {
                            Minimum = minimum,
                            Maximum = maximum,
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
                        break;

                    case BooleanSchema booleanSchema:
                        if (booleanSchema.Default.HasValue)
                        {
                            writer.WriteBoolean("default", booleanSchema.Default.Value);
                        }
                        break;

                    case EnumSchema enumSchema:
                        if (enumSchema.Enum is not null)
                        {
                            writer.WritePropertyName("enum");
                            JsonSerializer.Serialize(writer, enumSchema.Enum, McpJsonUtilities.JsonContext.Default.IListString);
                        }
                        if (enumSchema.EnumNames is not null)
                        {
                            writer.WritePropertyName("enumNames");
                            JsonSerializer.Serialize(writer, enumSchema.EnumNames, McpJsonUtilities.JsonContext.Default.IListString);
                        }
                        break;

                    default:
                        throw new JsonException($"Unexpected schema type: {value.GetType().Name}");
                }

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

    /// <summary>Represents a schema for an enum type.</summary>
    public sealed class EnumSchema : PrimitiveSchemaDefinition
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
    }
}

using System.Diagnostics.CodeAnalysis;
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
    [JsonDerivedType(typeof(BooleanSchema))]
    [JsonDerivedType(typeof(EnumSchema))]
    [JsonDerivedType(typeof(NumberSchema))]
    [JsonDerivedType(typeof(StringSchema))]
    public abstract class PrimitiveSchemaDefinition
    {
        /// <summary>Prevent external derivations.</summary>
        protected private PrimitiveSchemaDefinition()
        {
        }
    }

    /// <summary>Represents a schema for a string type.</summary>
    public sealed class StringSchema : PrimitiveSchemaDefinition
    {
        /// <summary>Gets the type of the schema.</summary>
        /// <remarks>This is always "string".</remarks>
        [JsonPropertyName("type")]
        public string Type => "string";

        /// <summary>Gets or sets a title for the string.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>Gets or sets a description for the string.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

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
        /// <summary>Gets the type of the schema.</summary>
        /// <remarks>This should be "number" or "integer".</remarks>
        [JsonPropertyName("type")]
        [field: MaybeNull]
        public string Type
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

        /// <summary>Gets or sets a title for the number input.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>Gets or sets a description for the number input.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

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
        /// <summary>Gets the type of the schema.</summary>
        /// <remarks>This is always "boolean".</remarks>
        [JsonPropertyName("type")]
        public string Type => "boolean";

        /// <summary>Gets or sets a title for the Boolean.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>Gets or sets a description for the Boolean.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>Gets or sets the default value for the Boolean.</summary>
        [JsonPropertyName("default")]
        public bool? Default { get; set; }
    }

    /// <summary>Represents a schema for an enum type.</summary>
    public sealed class EnumSchema : PrimitiveSchemaDefinition
    {
        /// <summary>Gets the type of the schema.</summary>
        /// <remarks>This is always "string".</remarks>
        [JsonPropertyName("type")]
        public string Type => "string";

        /// <summary>Gets or sets a title for the enum.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>Gets or sets a description for the enum.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

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

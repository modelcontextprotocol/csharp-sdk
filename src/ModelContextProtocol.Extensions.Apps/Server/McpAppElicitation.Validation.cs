using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Mail;
using System.Text.Json;

namespace ModelContextProtocol.Extensions.Apps;

public static partial class McpAppElicitation
{
    /// <summary>
    /// Validates a standard elicitation result against the original form request.
    /// </summary>
    /// <param name="request">The original form elicitation request.</param>
    /// <param name="result">The result returned by an MCP App or another untrusted form renderer.</param>
    /// <returns>
    /// A validation result containing a normalized elicitation result when valid, or actionable
    /// validation errors that do not include submitted values.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Accepted results are validated against <see cref="ElicitRequestParams.RequestedSchema"/>.
    /// Missing properties with schema defaults are populated before validation, matching the core
    /// SDK's elicitation default behavior. Submitted values are never coerced.
    /// </para>
    /// <para>
    /// Declined and cancelled results are valid without content and are returned unchanged.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> or <paramref name="result"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="request"/> is not a form elicitation with a requested schema.</exception>
    public static McpAppElicitationValidationResult ValidateResult(
        ElicitRequestParams request,
        ElicitResult result)
    {
#if NET
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
#else
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (result is null) throw new ArgumentNullException(nameof(result));
#endif

        if (!string.Equals(request.Mode, "form", StringComparison.Ordinal) ||
            request.RequestedSchema is not { } requestedSchema)
        {
            throw new ArgumentException(
                "Result validation requires a form elicitation with a requested schema.",
                nameof(request));
        }

        if (!IsStandardAction(result.Action))
        {
            return McpAppElicitationValidationResult.Invalid(
                new McpAppElicitationValidationError(
                    "/action",
                    "Action must be 'accept', 'decline', or 'cancel'."));
        }

        if (!result.IsAccepted)
        {
            return McpAppElicitationValidationResult.Valid(result);
        }

        var content = result.Content is not null ?
            new Dictionary<string, JsonElement>(result.Content, StringComparer.Ordinal) :
            new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        ApplyDefaults(requestedSchema.Properties, content);

        List<McpAppElicitationValidationError> errors = [];
        if (result.Content is null && content.Count == 0)
        {
            errors.Add(new(
                "/content",
                "Accepted elicitation results must include content."));
        }

        if (requestedSchema.Required is { } required)
        {
            foreach (string propertyName in required)
            {
                if (!content.ContainsKey(propertyName))
                {
                    errors.Add(new(
                        GetPropertyPath(propertyName),
                        "Required property is missing."));
                }
            }
        }

        foreach (KeyValuePair<string, JsonElement> property in content)
        {
            if (!requestedSchema.Properties.TryGetValue(property.Key, out var propertySchema))
            {
                errors.Add(new(
                    GetPropertyPath(property.Key),
                    "Property is not declared by the requested schema."));
                continue;
            }

            ValidateProperty(
                GetPropertyPath(property.Key),
                property.Value,
                propertySchema,
                errors);
        }

        if (errors.Count > 0)
        {
            return McpAppElicitationValidationResult.Invalid(errors);
        }

        return McpAppElicitationValidationResult.Valid(new ElicitResult
        {
            Action = result.Action,
            Content = content,
            Meta = result.Meta,
        });
    }

    private static void ApplyDefaults(
        IDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> properties,
        IDictionary<string, JsonElement> content)
    {
        foreach (KeyValuePair<string, ElicitRequestParams.PrimitiveSchemaDefinition> property in properties)
        {
            if (content.ContainsKey(property.Key) ||
                GetDefaultValue(property.Value) is not { } defaultValue)
            {
                continue;
            }

            content[property.Key] = defaultValue;
        }
    }

    private static JsonElement? GetDefaultValue(
        ElicitRequestParams.PrimitiveSchemaDefinition schema) =>
        schema switch
        {
            ElicitRequestParams.StringSchema { Default: { } value } =>
                JsonSerializer.SerializeToElement(value, McpAppsJsonContext.Default.String),
            ElicitRequestParams.NumberSchema { Default: { } value } =>
                JsonSerializer.SerializeToElement(value, McpAppsJsonContext.Default.Double),
            ElicitRequestParams.BooleanSchema { Default: { } value } =>
                JsonSerializer.SerializeToElement(value, McpAppsJsonContext.Default.Boolean),
            ElicitRequestParams.UntitledSingleSelectEnumSchema { Default: { } value } =>
                JsonSerializer.SerializeToElement(value, McpAppsJsonContext.Default.String),
            ElicitRequestParams.TitledSingleSelectEnumSchema { Default: { } value } =>
                JsonSerializer.SerializeToElement(value, McpAppsJsonContext.Default.String),
            ElicitRequestParams.UntitledMultiSelectEnumSchema { Default: { } value } =>
                JsonSerializer.SerializeToElement(value, McpAppsJsonContext.Default.IListString),
            ElicitRequestParams.TitledMultiSelectEnumSchema { Default: { } value } =>
                JsonSerializer.SerializeToElement(value, McpAppsJsonContext.Default.IListString),
#pragma warning disable MCP9001
            ElicitRequestParams.LegacyTitledEnumSchema { Default: { } value } =>
                JsonSerializer.SerializeToElement(value, McpAppsJsonContext.Default.String),
#pragma warning restore MCP9001
            _ => null,
        };

    private static void ValidateProperty(
        string path,
        JsonElement value,
        ElicitRequestParams.PrimitiveSchemaDefinition schema,
        ICollection<McpAppElicitationValidationError> errors)
    {
        switch (schema)
        {
            case ElicitRequestParams.StringSchema stringSchema:
                ValidateString(path, value, stringSchema, errors);
                break;

            case ElicitRequestParams.NumberSchema numberSchema:
                ValidateNumber(path, value, numberSchema, errors);
                break;

            case ElicitRequestParams.BooleanSchema:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    errors.Add(new(path, "Expected a boolean."));
                }
                break;

            case ElicitRequestParams.UntitledSingleSelectEnumSchema enumSchema:
                ValidateSingleSelect(path, value, enumSchema.Enum, errors);
                break;

            case ElicitRequestParams.TitledSingleSelectEnumSchema enumSchema:
                ValidateSingleSelect(path, value, enumSchema.OneOf.Select(option => option.Const), errors);
                break;

            case ElicitRequestParams.UntitledMultiSelectEnumSchema enumSchema:
                ValidateMultiSelect(
                    path,
                    value,
                    enumSchema.Items.Enum,
                    enumSchema.MinItems,
                    enumSchema.MaxItems,
                    errors);
                break;

            case ElicitRequestParams.TitledMultiSelectEnumSchema enumSchema:
                ValidateMultiSelect(
                    path,
                    value,
                    enumSchema.Items.AnyOf.Select(option => option.Const),
                    enumSchema.MinItems,
                    enumSchema.MaxItems,
                    errors);
                break;

#pragma warning disable MCP9001
            case ElicitRequestParams.LegacyTitledEnumSchema enumSchema:
#pragma warning restore MCP9001
                ValidateSingleSelect(path, value, enumSchema.Enum, errors);
                break;

            default:
                errors.Add(new(path, "The requested schema type is not supported."));
                break;
        }
    }

    private static void ValidateString(
        string path,
        JsonElement value,
        ElicitRequestParams.StringSchema schema,
        ICollection<McpAppElicitationValidationError> errors)
    {
        if (value.ValueKind is not JsonValueKind.String)
        {
            errors.Add(new(path, "Expected a string."));
            return;
        }

        string stringValue = value.GetString()!;
        int length = GetUnicodeScalarLength(stringValue);
        if (schema.MinLength is { } minLength && length < minLength)
        {
            errors.Add(new(path, $"String length must be at least {minLength} characters."));
        }

        if (schema.MaxLength is { } maxLength && length > maxLength)
        {
            errors.Add(new(path, $"String length must be at most {maxLength} characters."));
        }

        if (schema.Format is { } format && !MatchesFormat(stringValue, format))
        {
            errors.Add(new(path, $"String must match the '{format}' format."));
        }
    }

    private static void ValidateNumber(
        string path,
        JsonElement value,
        ElicitRequestParams.NumberSchema schema,
        ICollection<McpAppElicitationValidationError> errors)
    {
        string expectedType = string.Equals(schema.Type, "integer", StringComparison.Ordinal) ?
            "an integer" :
            "a number";

        if (value.ValueKind is not JsonValueKind.Number ||
            !value.TryGetDouble(out double number) ||
            double.IsNaN(number) ||
            double.IsInfinity(number) ||
            (string.Equals(schema.Type, "integer", StringComparison.Ordinal) && Math.Truncate(number) != number))
        {
            errors.Add(new(path, $"Expected {expectedType}."));
            return;
        }

        if (schema.Minimum is { } minimum && number < minimum)
        {
            errors.Add(new(path, $"Number must be greater than or equal to {minimum.ToString(CultureInfo.InvariantCulture)}."));
        }

        if (schema.Maximum is { } maximum && number > maximum)
        {
            errors.Add(new(path, $"Number must be less than or equal to {maximum.ToString(CultureInfo.InvariantCulture)}."));
        }
    }

    private static void ValidateSingleSelect(
        string path,
        JsonElement value,
        IEnumerable<string> choices,
        ICollection<McpAppElicitationValidationError> errors)
    {
        if (value.ValueKind is not JsonValueKind.String)
        {
            errors.Add(new(path, "Expected a string choice."));
            return;
        }

        string selectedValue = value.GetString()!;
        if (!choices.Contains(selectedValue, StringComparer.Ordinal))
        {
            errors.Add(new(path, "Value is not one of the allowed choices."));
        }
    }

    private static void ValidateMultiSelect(
        string path,
        JsonElement value,
        IEnumerable<string> choices,
        int? minItems,
        int? maxItems,
        ICollection<McpAppElicitationValidationError> errors)
    {
        if (value.ValueKind is not JsonValueKind.Array)
        {
            errors.Add(new(path, "Expected an array of string choices."));
            return;
        }

        HashSet<string> allowedChoices = new(choices, StringComparer.Ordinal);
        int itemCount = value.GetArrayLength();
        if (minItems is { } minimum && itemCount < minimum)
        {
            errors.Add(new(path, $"At least {minimum} choices must be selected."));
        }

        if (maxItems is { } maximum && itemCount > maximum)
        {
            errors.Add(new(path, $"At most {maximum} choices may be selected."));
        }

        int index = 0;
        foreach (JsonElement item in value.EnumerateArray())
        {
            string itemPath = $"{path}/{index}";
            if (item.ValueKind is not JsonValueKind.String)
            {
                errors.Add(new(itemPath, "Expected a string choice."));
            }
            else if (!allowedChoices.Contains(item.GetString()!))
            {
                errors.Add(new(itemPath, "Value is not one of the allowed choices."));
            }

            index++;
        }
    }

    private static bool MatchesFormat(string value, string format) =>
        format switch
        {
            "email" => IsValidEmail(value),
            "uri" => Uri.TryCreate(value, UriKind.Absolute, out _),
            "date" => DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _),
            "date-time" => IsValidDateTime(value),
            _ => true,
        };

    private static bool IsValidEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsValidDateTime(string value)
    {
        if (value.Length < 20 || value[10] is not ('T' or 't'))
        {
            return false;
        }

        bool hasUtcDesignator = value[value.Length - 1] is 'Z' or 'z';
        bool hasOffset =
            value.Length >= 25 &&
            value[value.Length - 6] is '+' or '-' &&
            value[value.Length - 3] == ':';
        if (!hasUtcDesignator && !hasOffset)
        {
            return false;
        }

        string normalized = value
            .Replace('t', 'T')
            .Replace('z', 'Z');
        return DateTimeOffset.TryParseExact(
            normalized,
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private static int GetUnicodeScalarLength(string value)
    {
        int length = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) &&
                i + 1 < value.Length &&
                char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }

            length++;
        }

        return length;
    }

    private static bool IsStandardAction(string action) =>
        string.Equals(action, "accept", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(action, "decline", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(action, "cancel", StringComparison.OrdinalIgnoreCase);

    private static string GetPropertyPath(string propertyName) =>
        $"/content/{propertyName.Replace("~", "~0").Replace("/", "~1")}";
}

/// <summary>Represents the outcome of validating an MCP App elicitation result.</summary>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public sealed class McpAppElicitationValidationResult
{
    private McpAppElicitationValidationResult(
        ElicitResult? validatedResult,
        IReadOnlyList<McpAppElicitationValidationError> errors)
    {
        ValidatedResult = validatedResult;
        Errors = errors;
    }

    /// <summary>Gets whether the elicitation result is valid.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Gets the validated result, including any schema defaults applied to accepted content,
    /// or <see langword="null"/> when validation failed.
    /// </summary>
    public ElicitResult? ValidatedResult { get; }

    /// <summary>Gets the validation errors. Submitted values are not included.</summary>
    public IReadOnlyList<McpAppElicitationValidationError> Errors { get; }

    internal static McpAppElicitationValidationResult Valid(ElicitResult result) =>
        new(result, []);

    internal static McpAppElicitationValidationResult Invalid(
        McpAppElicitationValidationError error) =>
        new(null, [error]);

    internal static McpAppElicitationValidationResult Invalid(
        IReadOnlyList<McpAppElicitationValidationError> errors) =>
        new(null, errors);
}

/// <summary>Describes one MCP App elicitation validation failure.</summary>
[Experimental(Experimentals.Apps_DiagnosticId, UrlFormat = Experimentals.Apps_Url)]
public sealed class McpAppElicitationValidationError
{
    internal McpAppElicitationValidationError(string path, string message)
    {
        Path = path;
        Message = message;
    }

    /// <summary>Gets the JSON Pointer path to the invalid result member.</summary>
    public string Path { get; }

    /// <summary>Gets an actionable error message that does not include the submitted value.</summary>
    public string Message { get; }
}

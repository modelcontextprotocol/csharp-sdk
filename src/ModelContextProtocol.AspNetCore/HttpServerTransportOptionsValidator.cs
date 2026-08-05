using Microsoft.Extensions.Options;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Validates <see cref="HttpServerTransportOptions"/>.
/// </summary>
internal sealed class HttpServerTransportOptionsValidator : IValidateOptions<HttpServerTransportOptions>
{
    public ValidateOptionsResult Validate(string? name, HttpServerTransportOptions options)
    {
        if (!Enum.IsDefined(typeof(HttpServerSessionMode), options.SessionMode))
        {
            return ValidateOptionsResult.Fail(
                $"The '{nameof(HttpServerTransportOptions)}.{nameof(HttpServerTransportOptions.SessionMode)}' value " +
                $"'{options.SessionMode}' is not valid.");
        }

        return ValidateOptionsResult.Success;
    }
}

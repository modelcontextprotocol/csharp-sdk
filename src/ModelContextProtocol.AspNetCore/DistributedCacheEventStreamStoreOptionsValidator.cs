using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Validates that <see cref="DistributedCacheEventStreamStoreOptions.Cache"/> is set.
/// </summary>
internal sealed class DistributedCacheEventStreamStoreOptionsValidator : IValidateOptions<DistributedCacheEventStreamStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, DistributedCacheEventStreamStoreOptions options)
    {
        if (options.Cache is null)
        {
            return ValidateOptionsResult.Fail(
                $"The '{nameof(DistributedCacheEventStreamStoreOptions)}.{nameof(DistributedCacheEventStreamStoreOptions.Cache)}' property must be set. " +
                $"Register an {nameof(IDistributedCache)} in DI or set the {nameof(DistributedCacheEventStreamStoreOptions.Cache)} property " +
                $"in the '{nameof(HttpMcpServerBuilderExtensions.WithDistributedCacheEventStreamStore)}' configure callback.");
        }

        return ValidateOptionsResult.Success;
    }
}

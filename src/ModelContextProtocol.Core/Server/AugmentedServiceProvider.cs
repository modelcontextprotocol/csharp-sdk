using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>Augments a service provider with additional request-related services.</summary>
internal sealed class RequestServiceProvider<TRequestParams>(
    RequestContext<TRequestParams> request, IServiceProvider? innerServices) :
    IServiceProvider,  IKeyedServiceProvider,
    IServiceProviderIsService, IServiceProviderIsKeyedService,
    IDisposable,  IAsyncDisposable
    where TRequestParams : RequestParams
{
    /// <summary>Gets the request associated with this instance.</summary>
    public RequestContext<TRequestParams> Request => request;

    /// <summary>Gets whether the specified type is in the list of additional types this service provider wraps around the one in a provided request's services.</summary>
    public static bool IsAugmentedWith(Type serviceType) =>
        serviceType == typeof(RequestContext<TRequestParams>) ||
        serviceType == typeof(IMcpServer) ||
        serviceType == typeof(IProgress<ProgressNotificationValue>);

    /// <inheritdoc />
    public object? GetService(Type serviceType) =>
        serviceType == typeof(RequestContext<TRequestParams>) ? request :
        serviceType == typeof(IMcpServer) ? request.Server :
        serviceType == typeof(IProgress<ProgressNotificationValue>) ?
            (request.Params?.ProgressToken is { } progressToken ? new TokenProgress(request.Server, progressToken) : NullProgress.Instance) :
        innerServices?.GetService(serviceType);

    /// <inheritdoc />
    public bool IsService(Type serviceType) =>
        IsAugmentedWith(serviceType) ||
        (innerServices as IServiceProviderIsService)?.IsService(serviceType) is true;

    /// <inheritdoc />
    public bool IsKeyedService(Type serviceType, object? serviceKey) =>
        (serviceKey is null && IsService(serviceType)) ||
        (innerServices as IServiceProviderIsKeyedService)?.IsKeyedService(serviceType, serviceKey) is true;

    /// <inheritdoc />
    public object? GetKeyedService(Type serviceType, object? serviceKey) =>
        serviceKey is null ? GetService(serviceType) :
        (innerServices as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);

    /// <inheritdoc />
    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
        GetKeyedService(serviceType, serviceKey) ??
        throw new InvalidOperationException($"No service of type '{serviceType}' with key '{serviceKey}' is registered.");

    /// <inheritdoc />
    public void Dispose() =>
        (innerServices as IDisposable)?.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync() =>
        innerServices is IAsyncDisposable asyncDisposable ? asyncDisposable.DisposeAsync() : default;
}
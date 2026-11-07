using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.AspNetCore.Authentication;

internal sealed class McpOAuthProxyProtectedStore
{
    private const string KeyPrefix = "mcp-oauth-proxy:";
    private readonly IMcpOAuthProxyStore _store;
    private readonly IDataProtector _protector;

    public McpOAuthProxyProtectedStore(IMcpOAuthProxyStore store, IDataProtectionProvider dataProtectionProvider)
    {
        _store = store;
        _protector = dataProtectionProvider.CreateProtector("ModelContextProtocol.AspNetCore.Authentication.McpOAuthProxy", "v1");
    }

    public async ValueTask SetAsync<T>(
        string category,
        string identifier,
        T value,
        JsonTypeInfo<T> typeInfo,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        var serialized = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        var protectedValue = _protector.Protect(serialized);
        await _store.SetAsync(CreateKey(category, identifier), protectedValue, lifetime, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<T?> GetAsync<T>(
        string category,
        string identifier,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        var protectedValue = await _store.GetAsync(CreateKey(category, identifier), cancellationToken).ConfigureAwait(false);
        return protectedValue is null ? default : Deserialize(protectedValue.Value, typeInfo);
    }

    public async ValueTask<T?> TakeAsync<T>(
        string category,
        string identifier,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        var protectedValue = await _store.TakeAsync(CreateKey(category, identifier), cancellationToken).ConfigureAwait(false);
        return protectedValue is null ? default : Deserialize(protectedValue.Value, typeInfo);
    }

    public ValueTask RemoveAsync(
        string category,
        string identifier,
        CancellationToken cancellationToken) =>
        _store.RemoveAsync(CreateKey(category, identifier), cancellationToken);

    private T Deserialize<T>(ReadOnlyMemory<byte> protectedValue, JsonTypeInfo<T> typeInfo)
    {
        var serialized = _protector.Unprotect(protectedValue.ToArray());
        return JsonSerializer.Deserialize(serialized, typeInfo) ??
            throw new InvalidOperationException("The OAuth proxy store contained an invalid record.");
    }

    private static string CreateKey(string category, string identifier)
    {
        var input = Encoding.UTF8.GetBytes($"{category}\0{identifier}");
        return $"{KeyPrefix}{category}:{WebEncoders.Base64UrlEncode(SHA256.HashData(input))}";
    }
}

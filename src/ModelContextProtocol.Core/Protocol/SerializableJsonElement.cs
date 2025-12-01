using System.Text.Json;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// A serializable wrapper for <see cref="JsonElement"/> that can be stored in <see cref="Exception.Data"/>
/// on .NET Framework, where values must be marked with <see cref="SerializableAttribute"/>.
/// </summary>
/// <remarks>
/// On .NET Core/.NET 5+, <see cref="JsonElement"/> can be stored directly in <see cref="Exception.Data"/>,
/// but on .NET Framework the underlying <c>ListDictionaryInternal</c> requires values to be serializable.
/// This wrapper stores the JSON as a string and deserializes it back to a <see cref="JsonElement"/> on demand.
/// </remarks>
[Serializable]
public sealed class SerializableJsonElement
{
    private readonly string _json;

    private SerializableJsonElement(string json)
    {
        _json = json;
    }

    /// <summary>
    /// Gets the <see cref="JsonElement"/> value.
    /// </summary>
    public JsonElement Value => JsonDocument.Parse(_json).RootElement;

    /// <summary>
    /// Creates a serializable wrapper for the specified <see cref="JsonElement"/>.
    /// </summary>
    /// <param name="element">The JSON element to wrap.</param>
    /// <returns>
    /// On .NET Core/.NET 5+, returns the <see cref="JsonElement"/> directly.
    /// On .NET Framework, returns a <see cref="SerializableJsonElement"/> wrapper.
    /// </returns>
    internal static object Wrap(JsonElement element)
    {
#if NET
        return element;
#else
        return new SerializableJsonElement(element.GetRawText());
#endif
    }

    /// <inheritdoc/>
    public override string ToString() => _json;
}

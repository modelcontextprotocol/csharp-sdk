using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the result of a request that may return either the standard result or an alternate
/// <see cref="Result"/> subtype for scenarios like asynchronous task execution.
/// </summary>
/// <typeparam name="TResult">The standard result type for the request (e.g., <see cref="CallToolResult"/>).</typeparam>
/// <remarks>
/// <para>
/// Extensions that augment request handling (such as the Tasks extension) use this type to indicate
/// that the server returned an alternate result instead of the normal one. The alternate carries its
/// own <see cref="JsonTypeInfo"/> so the transport layer can serialize it without compile-time knowledge
/// of the concrete type.
/// </para>
/// <para>
/// Use <see cref="IsAlternate"/> to determine which variant was returned, then access either
/// <see cref="Result"/> for the immediate result or <see cref="Alternate"/> for the alternate.
/// </para>
/// </remarks>
[Experimental(Experimentals.Subclassing_DiagnosticId, UrlFormat = Experimentals.Subclassing_Url)]
public class ResultOrAlternate<TResult> where TResult : Result
{
    private readonly TResult? _result;
    private readonly Result? _alternate;
    private readonly JsonTypeInfo? _alternateTypeInfo;

    /// <summary>
    /// Initializes a new instance of <see cref="ResultOrAlternate{TResult}"/> with an immediate result.
    /// </summary>
    /// <param name="result">The standard result returned by the server.</param>
    public ResultOrAlternate(TResult result)
    {
        Throw.IfNull(result);
        _result = result;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ResultOrAlternate{TResult}"/> with an alternate result.
    /// </summary>
    /// <param name="alternate">The alternate result.</param>
    /// <param name="alternateTypeInfo">The <see cref="JsonTypeInfo"/> used to serialize the alternate result.</param>
    private ResultOrAlternate(Result alternate, JsonTypeInfo alternateTypeInfo)
    {
        Throw.IfNull(alternate);
        Throw.IfNull(alternateTypeInfo);
        _alternate = alternate;
        _alternateTypeInfo = alternateTypeInfo;
    }

    /// <summary>
    /// Creates a <see cref="ResultOrAlternate{TResult}"/> that carries an alternate <see cref="Result"/> subtype
    /// (for example an <c>InputRequiredResult</c> or a task-creation result) in place of the standard result.
    /// </summary>
    /// <typeparam name="TAlternate">The concrete alternate result type.</typeparam>
    /// <param name="alternate">The alternate result to return instead of the standard result.</param>
    /// <param name="alternateTypeInfo">
    /// The <see cref="JsonTypeInfo{T}"/> used to serialize <paramref name="alternate"/>. Requiring the strongly-typed
    /// contract keeps the alternate value paired with matching serializer metadata rather than an unrelated type.
    /// </param>
    /// <returns>A <see cref="ResultOrAlternate{TResult}"/> that wraps the alternate result.</returns>
    public static ResultOrAlternate<TResult> FromAlternate<TAlternate>(TAlternate alternate, JsonTypeInfo<TAlternate> alternateTypeInfo)
        where TAlternate : Result
        => new(alternate, alternateTypeInfo);

    /// <summary>
    /// Gets a value indicating whether the server returned an alternate result instead of the standard result.
    /// </summary>
    public bool IsAlternate => _alternate is not null;

    /// <summary>
    /// Gets the immediate result, or <see langword="null"/> if the server returned an alternate.
    /// </summary>
    public TResult? Result => _result;

    /// <summary>
    /// Gets the alternate result, or <see langword="null"/> if the server returned the standard result.
    /// </summary>
    public Result? Alternate => _alternate;

    /// <summary>
    /// Gets the <see cref="JsonTypeInfo"/> for serializing the alternate result, or <see langword="null"/>
    /// if the server returned the standard result.
    /// </summary>
    public JsonTypeInfo? AlternateTypeInfo => _alternateTypeInfo;

    /// <summary>
    /// Implicitly converts a <typeparamref name="TResult"/> to a <see cref="ResultOrAlternate{TResult}"/>
    /// wrapping the immediate result.
    /// </summary>
    /// <param name="result">The result to wrap.</param>
    public static implicit operator ResultOrAlternate<TResult>(TResult result) => new(result);
}

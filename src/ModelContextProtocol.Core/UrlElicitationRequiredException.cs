using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol;

/// <summary>
/// Represents an exception used to indicate that URL-mode elicitation must be completed before the request can proceed.
/// </summary>
public sealed class UrlElicitationRequiredException : McpProtocolException
{
    private readonly List<ElicitRequestParams> _elicitations;

    /// <summary>
    /// Initializes a new instance of the <see cref="UrlElicitationRequiredException"/> class with the specified message and pending elicitations.
    /// </summary>
    /// <param name="message">A description of why the elicitation is required.</param>
    /// <param name="elicitations">One or more URL-mode elicitation requests that must complete before retrying the original request.</param>
    /// <exception cref="ArgumentNullException"><paramref name="elicitations"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="elicitations"/> is empty or contains invalid elicitations.</exception>
    public UrlElicitationRequiredException(string message, IEnumerable<ElicitRequestParams> elicitations)
        : base(message, McpErrorCode.UrlElicitationRequired)
    {
        Throw.IfNull(elicitations);
        _elicitations = Validate(elicitations);
    }

    /// <summary>
    /// Gets the collection of pending URL-mode elicitation requests that must be completed.
    /// </summary>
    public IReadOnlyList<ElicitRequestParams> Elicitations => _elicitations;

    internal JsonNode CreateErrorDataNode()
    {
        var payload = new UrlElicitationRequiredErrorData
        {
            Elicitations = _elicitations,
        };

        return JsonSerializer.SerializeToNode(
            payload,
            McpJsonUtilities.JsonContext.Default.UrlElicitationRequiredErrorData)!;
    }

    internal static bool TryCreateFromError(
        string formattedMessage,
        JsonRpcErrorDetail detail,
        [NotNullWhen(true)] out UrlElicitationRequiredException? exception)
    {
        exception = null;

        if (detail.Data is not JsonElement dataElement)
        {
            return false;
        }

        if (!TryParseElicitations(dataElement, out var elicitations))
        {
            return false;
        }

        exception = new UrlElicitationRequiredException(formattedMessage, elicitations);
        return true;
    }

    private static bool TryParseElicitations(JsonElement dataElement, out IList<ElicitRequestParams> elicitations)
    {
        elicitations = [];

        if (dataElement.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        var payload = dataElement.Deserialize(McpJsonUtilities.JsonContext.Default.UrlElicitationRequiredErrorData);
        if (payload?.Elicitations is not { Count: > 0 } elicitationsFromPayload)
        {
            return false;
        }

        foreach (var elicitation in elicitationsFromPayload)
        {
            if (!IsValidUrlElicitation(elicitation))
            {
                return false;
            }
        }

        elicitations = elicitationsFromPayload;
        return true;
    }

    private static List<ElicitRequestParams> Validate(IEnumerable<ElicitRequestParams> elicitations)
    {
        var list = new List<ElicitRequestParams>();
        foreach (var elicitation in elicitations)
        {
            Throw.IfNull(elicitation);

            if (!IsValidUrlElicitation(elicitation))
            {
                throw new ArgumentException(
                    "Elicitations must be URL-mode requests that include an elicitationId, message, and url.",
                    nameof(elicitations));
            }

            list.Add(elicitation);
        }

        if (list.Count == 0)
        {
            throw new ArgumentException("At least one elicitation must be provided.", nameof(elicitations));
        }

        return list;
    }

    private static bool IsValidUrlElicitation(ElicitRequestParams elicitation)
    {
        return string.Equals(elicitation.Mode, "url", StringComparison.Ordinal) &&
            elicitation.Url is not null &&
            elicitation.ElicitationId is not null;
    }
}

using System.Net;
using System.Net.Http;

namespace ModelContextProtocol;

/// <summary>
/// Extension methods for <see cref="HttpResponseMessage"/>.
/// </summary>
internal static class HttpResponseMessageExtensions
{
    private const int MaxResponseBodyLength = 1024;

    /// <summary>
    /// Throws an <see cref="HttpRequestException"/> if the <see cref="HttpResponseMessage.IsSuccessStatusCode"/> property is <see langword="false"/>.
    /// Unlike <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>, this method includes the response body in the exception message
    /// to help diagnose issues when the server returns error details in the response body.
    /// </summary>
    /// <param name="response">The HTTP response message to check.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="HttpRequestException">The response status code does not indicate success.</exception>
    public static async Task EnsureSuccessStatusCodeWithResponseBodyAsync(this HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpRequestExceptionWithBodyAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates an <see cref="HttpRequestException"/> for a non-success response, making a best-effort attempt to
    /// include the server's response body in the exception message so the diagnostic isn't lost.
    /// </summary>
    /// <param name="response">The non-success HTTP response message.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>An <see cref="HttpRequestException"/> with the response status and, when available, its body.</returns>
    public static async Task<HttpRequestException> CreateHttpRequestExceptionWithBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        string? responseBody = null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            responseBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

            if (responseBody.Length > MaxResponseBodyLength)
            {
                responseBody = responseBody.Substring(0, MaxResponseBodyLength) + "...";
            }
        }
        catch
        {
            // Ignore all errors reading the response body (e.g., stream closed, timeout, cancellation) - we'll throw without it.
        }

        return CreateHttpRequestException(response, responseBody);
    }

    /// <summary>
    /// Creates an <see cref="HttpRequestException"/> for a non-success response, including the response body in the message.
    /// </summary>
    /// <param name="response">The HTTP response message.</param>
    /// <param name="responseBody">The response body content, if available.</param>
    /// <returns>An <see cref="HttpRequestException"/> with the response details.</returns>
    public static HttpRequestException CreateHttpRequestException(HttpResponseMessage response, string? responseBody)
    {
        int statusCodeInt = (int)response.StatusCode;
        string message = string.IsNullOrEmpty(responseBody)
            ? $"Response status code does not indicate success: {statusCodeInt} ({response.ReasonPhrase})."
            : $"Response status code does not indicate success: {statusCodeInt} ({response.ReasonPhrase}). Response body: {responseBody}";

        return HttpRequestExceptionExtensions.Create(message, innerException: null, response.StatusCode);
    }
}

/// <summary>
/// Helpers for preserving HTTP status codes on <see cref="HttpRequestException"/> across all target frameworks.
/// </summary>
internal static class HttpRequestExceptionExtensions
{
    internal const string StatusCodeDataKey = "ModelContextProtocol.HttpStatusCode";

    /// <summary>
    /// Creates an <see cref="HttpRequestException"/> and preserves its HTTP status code.
    /// </summary>
    public static HttpRequestException Create(string message, Exception? innerException, HttpStatusCode? statusCode)
    {
#if NET
        var exception = new HttpRequestException(message, innerException, statusCode);
#else
        var exception = new HttpRequestException(message, innerException);
#endif

        if (statusCode is not null)
        {
            exception.Data[StatusCodeDataKey] = statusCode.Value;
        }

        return exception;
    }

    /// <summary>
    /// Gets the preserved HTTP status code from an <see cref="HttpRequestException"/>.
    /// </summary>
    public static HttpStatusCode? GetStatusCode(this HttpRequestException exception)
    {
#if NET
        if (exception.StatusCode is { } statusCode)
        {
            return statusCode;
        }
#endif

        return exception.Data[StatusCodeDataKey] switch
        {
            HttpStatusCode storedStatusCode => storedStatusCode,
            _ => null,
        };
    }
}

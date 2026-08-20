using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Runs request handlers that can require one or more rounds of additional input.
/// </summary>
internal static class InputRequiredRequestRunner
{
    private const int MaxRetries = 10;

    private static readonly JsonTypeInfo<IDictionary<string, InputResponse>> s_inputResponsesTypeInfo =
        (JsonTypeInfo<IDictionary<string, InputResponse>>)McpJsonUtilities.DefaultOptions.GetTypeInfo(
            typeof(IDictionary<string, InputResponse>));

    private static readonly JsonTypeInfo<InputRequiredResult> s_inputRequiredResultTypeInfo =
        (JsonTypeInfo<InputRequiredResult>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(InputRequiredResult));

    /// <summary>
    /// Invokes a handler until it returns a final result, normalizing both thrown and returned
    /// <see cref="InputRequiredResult"/> values into the same retry flow.
    /// </summary>
    internal static async Task<TResult> RunAsync<TRequest, TResult>(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TResult>> invoke,
        Func<TResult, InputRequiredResult?> getReturnedInputRequiredResult,
        Func<InputRequiredResult, TResult>? createDirectResult,
        Func<TRequest, InputRequiredResult, Exception?, CancellationToken, Task<TRequest>> prepareRetry,
        Func<string, Exception?, Exception> createFailure,
        CancellationToken cancellationToken)
    {
        for (int retry = 0; ; retry++)
        {
            InputRequiredResult inputRequiredResult;
            Exception? inputRequiredException = null;

            try
            {
                TResult result = await invoke(request, cancellationToken).ConfigureAwait(false);
                if (getReturnedInputRequiredResult(result) is not { } returnedInputRequiredResult)
                {
                    return result;
                }

                inputRequiredResult = returnedInputRequiredResult;
            }
            catch (InputRequiredException ex)
            {
                inputRequiredResult = ex.Result;
                inputRequiredException = ex;
            }

            if (createDirectResult is not null)
            {
                return createDirectResult(inputRequiredResult);
            }

            if (inputRequiredResult.InputRequests is not { Count: > 0 } &&
                inputRequiredResult.RequestState is null)
            {
                throw createFailure(
                    "A tool returned an input-required result without input requests or request state.",
                    inputRequiredException);
            }

            if (retry >= MaxRetries)
            {
                throw createFailure(
                    $"MRTR-native tool exceeded {MaxRetries} retry rounds without completing.",
                    inputRequiredException);
            }

            request = await prepareRetry(
                request,
                inputRequiredResult,
                inputRequiredException,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves a batch concurrently, cancelling sibling requests when any resolver fails.
    /// </summary>
    internal static async Task<IDictionary<string, InputResponse>> ResolveInputRequestsAsync(
        IDictionary<string, InputRequest> inputRequests,
        Func<InputRequest, CancellationToken, Task<InputResponse>> resolveInputRequest,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var keyedTasks = new (string Key, Task<InputResponse> ResponseTask)[inputRequests.Count];

        int index = 0;
        foreach (var pair in inputRequests)
        {
            keyedTasks[index++] = (pair.Key, ResolveAndCancelSiblingsAsync(pair.Value));
        }

        await Task.WhenAll(Array.ConvertAll(keyedTasks, static item => item.ResponseTask)).ConfigureAwait(false);

        var responses = new Dictionary<string, InputResponse>(keyedTasks.Length);
        foreach (var (key, responseTask) in keyedTasks)
        {
            responses[key] = responseTask.Result;
        }

        return responses;

        async Task<InputResponse> ResolveAndCancelSiblingsAsync(InputRequest inputRequest)
        {
            try
            {
                return await resolveInputRequest(inputRequest, linkedCts.Token).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    linkedCts.Cancel();
                }
                catch
                {
                    // Preserve the resolver failure. Awaiting Task.WhenAll observes every sibling outcome.
                }

                throw;
            }
        }
    }

    /// <summary>
    /// Clones request parameters and applies the response and state for the next round, removing
    /// values left over from the previous round when the current result omits them.
    /// </summary>
    internal static JsonObject CreateRetryParams(
        JsonNode? requestParams,
        IDictionary<string, InputResponse>? inputResponses,
        string? requestState)
    {
        var paramsObject = requestParams?.DeepClone() as JsonObject ?? new JsonObject();

        if (inputResponses is not null)
        {
            paramsObject["inputResponses"] = JsonSerializer.SerializeToNode(inputResponses, s_inputResponsesTypeInfo);
        }
        else
        {
            paramsObject.Remove("inputResponses");
        }

        if (requestState is not null)
        {
            paramsObject["requestState"] = requestState;
        }
        else
        {
            paramsObject.Remove("requestState");
        }

        return paramsObject;
    }

    /// <summary>
    /// Detects a serialized <see cref="InputRequiredResult"/> returned through an alternate-result path.
    /// </summary>
    internal static InputRequiredResult? GetReturnedInputRequiredResult(JsonNode? result)
    {
        if (result is JsonObject resultObject &&
            resultObject.TryGetPropertyValue("resultType", out var resultTypeNode) &&
            resultTypeNode?.GetValueKind() == JsonValueKind.String &&
            resultTypeNode.GetValue<string>() == "input_required")
        {
            return JsonSerializer.Deserialize(result, s_inputRequiredResultTypeInfo);
        }

        return null;
    }
}

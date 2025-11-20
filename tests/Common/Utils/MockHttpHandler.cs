namespace ModelContextProtocol.Tests.Utils;

public class MockHttpHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, Task<HttpResponseMessage>>? RequestHandler { get; set; }

    protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (RequestHandler == null)
            throw new InvalidOperationException($"No {nameof(RequestHandler)} was set! Please set handler first and make request afterwards.");
        
        cancellationToken.ThrowIfCancellationRequested();

        var result = await RequestHandler.Invoke(request);
        
        cancellationToken.ThrowIfCancellationRequested();

        return result;
    }
}

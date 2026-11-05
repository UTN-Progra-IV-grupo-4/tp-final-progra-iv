using System.Collections.Concurrent;

namespace ServiceAuthentication.Tests;

// Replays queued responses in order and records every request it receives.
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<(HttpResponseMessage Response, TimeSpan Delay)> responses = new();
    private readonly ConcurrentQueue<RecordedRequest> requests = new();

    public IReadOnlyCollection<RecordedRequest> Requests => requests;

    public void Enqueue(HttpResponseMessage response, TimeSpan delay = default) =>
        responses.Enqueue((response, delay));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        requests.Enqueue(new RecordedRequest(
            request.Method,
            request.RequestUri!.ToString(),
            body,
            request.Headers.Authorization?.ToString()));

        if (!responses.TryDequeue(out var scripted))
        {
            throw new InvalidOperationException("No scripted response left.");
        }

        if (scripted.Delay > TimeSpan.Zero)
        {
            await Task.Delay(scripted.Delay, cancellationToken);
        }

        return scripted.Response;
    }

    internal sealed record RecordedRequest(HttpMethod Method, string Uri, string Body, string? Authorization);
}

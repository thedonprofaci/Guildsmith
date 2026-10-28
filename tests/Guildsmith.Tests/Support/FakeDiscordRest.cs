using System.Net;
using System.Text;

using Discord.Net.Rest;
using Discord.Rest;

namespace Guildsmith.Tests.Support;

/// <summary>
/// Discord's HTTP layer, replaced: a real <see cref="DiscordRestClient"/> built with <see cref="Config"/>
/// sends every request here instead of to the network. Each endpoint answers with the response
/// <see cref="Respond"/> set for it; an endpoint with none throws, so a request nobody expected
/// fails the test instead of passing on an empty answer.
/// </summary>
internal sealed class FakeDiscordRest : IRestClient
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Json)> _responses = new(StringComparer.Ordinal);
    private readonly List<string> _requests = [];

    /// <summary>Every request so far, as <c>"&lt;METHOD&gt; &lt;endpoint&gt;"</c>, in order.</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>Whether a client disposed this fake.</summary>
    public bool Disposed { get; private set; }

    /// <summary>A client configuration that sends every request to this fake.</summary>
    public DiscordRestConfig Config() => new() { RestClientProvider = _ => this };

    /// <summary>Answers <paramref name="endpoint"/> (exactly as Discord.Net spells it) with <paramref name="json"/>.</summary>
    public void Respond(string endpoint, HttpStatusCode status, string json) => _responses[endpoint] = (status, json);

    public void SetHeader(string key, string value)
    {
    }

    public void SetCancelToken(CancellationToken cancelToken)
    {
    }

    public Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken cancelToken, bool headerOnly = false,
        string? reason = null, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null) =>
        Answer(method, endpoint);

    public Task<RestResponse> SendAsync(string method, string endpoint, string json, CancellationToken cancelToken, bool headerOnly = false,
        string? reason = null, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null) =>
        Answer(method, endpoint);

    public Task<RestResponse> SendAsync(string method, string endpoint, IReadOnlyDictionary<string, object> multipartParams,
        CancellationToken cancelToken, bool headerOnly = false, string? reason = null,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null) =>
        Answer(method, endpoint);

    public void Dispose() => Disposed = true;

    private Task<RestResponse> Answer(string method, string endpoint)
    {
        lock (_requests)
        {
            _requests.Add($"{method} {endpoint}");
        }

        if (!_responses.TryGetValue(endpoint, out (HttpStatusCode Status, string Json) response))
        {
            throw new InvalidOperationException($"FakeDiscordRest has no response for {method} {endpoint}.");
        }

        return Task.FromResult(new RestResponse(
            response.Status, new Dictionary<string, string>(), new MemoryStream(Encoding.UTF8.GetBytes(response.Json))));
    }
}

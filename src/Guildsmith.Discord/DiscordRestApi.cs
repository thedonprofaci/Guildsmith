using Discord;
using Discord.Rest;

using Microsoft.Extensions.Logging;

namespace Guildsmith.Discord;

/// <summary>
/// <see cref="IDiscordRestApi"/> over one <see cref="DiscordRestClient"/>, which is created and
/// logged in on the first call, not at startup: the server starts, and answers <c>initialize</c> and
/// <c>tools/list</c>, without a network or a valid token.
/// </summary>
public sealed class DiscordRestApi : IDiscordRestApi, IDisposable
{
    /// <summary>The line logged when the first Discord call logs the client in.</summary>
    public const string LoginMessage = "Logging in to Discord on the first Discord call.";

    private readonly string _token;
    private readonly DiscordRestConfig _config;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private DiscordRestClient? _client;

    /// <summary>An API that logs in with the bot <paramref name="token"/> on its first call.</summary>
    public DiscordRestApi(string token, DiscordRestConfig config, ILogger<DiscordRestApi> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        _token = token;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<IUserGuild>> GetGuildSummariesAsync(RequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        DiscordRestClient client = await LoggedInClientAsync(options.CancelToken).ConfigureAwait(false);
        return (await client.GetGuildSummariesAsync(options).FlattenAsync().ConfigureAwait(false)).ToList();
    }

    /// <inheritdoc />
    public async Task<IGuild?> GetGuildAsync(ulong guildId, RequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        DiscordRestClient client = await LoggedInClientAsync(options.CancelToken).ConfigureAwait(false);
        return await client.GetGuildAsync(guildId, withCounts: true, options).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IChannel?> GetChannelAsync(ulong channelId, RequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        DiscordRestClient client = await LoggedInClientAsync(options.CancelToken).ConfigureAwait(false);
        return await client.GetChannelAsync(channelId, options).ConfigureAwait(false);
    }

    /// <summary>Disposes the client, if one was logged in.</summary>
    public void Dispose()
    {
        _client?.Dispose();
        _loginLock.Dispose();
    }

    // A failed login is not kept: the next call tries again, so a network that came back is not
    // answered with a cached failure.
    private async Task<DiscordRestClient> LoggedInClientAsync(CancellationToken cancellationToken)
    {
        await _loginLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is null)
            {
                _logger.LogInformation(LoginMessage);
                DiscordRestClient client = new(_config);
                try
                {
                    await client.LoginAsync(TokenType.Bot, _token).ConfigureAwait(false);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }

                _client = client;
            }

            return _client;
        }
        finally
        {
            _loginLock.Release();
        }
    }
}

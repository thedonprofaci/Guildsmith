using Discord;

using Guildsmith.Core.Arguments;
using Guildsmith.Core.Errors;
using Guildsmith.Core.Gateway;
using Guildsmith.Core.Models;

namespace Guildsmith.Discord;

/// <summary>
/// The Discord.Net REST implementation of <see cref="IDiscordGateway"/>, over <see cref="IDiscordRestApi"/>.
/// Every Discord failure <see cref="DiscordFailureMapping"/> maps is thrown as a
/// <see cref="DiscordFailureException"/>; any other propagates unchanged.
/// </summary>
public sealed class DiscordGateway : IDiscordGateway
{
    private readonly IDiscordRestApi _api;

    /// <summary>A gateway making its Discord calls through <paramref name="api"/>.</summary>
    public DiscordGateway(IDiscordRestApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    /// <summary>Not implemented before the channel tools (#7): throws <see cref="NotSupportedException"/>.</summary>
    public Task<ulong?> GetChannelGuildIdAsync(ulong channelId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Resolving a channel's guild arrives with the channel tools (#7).");

    /// <summary>Not implemented before the role tools (#8): throws <see cref="NotSupportedException"/>.</summary>
    public Task<ulong?> GetRoleGuildIdAsync(ulong roleId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Resolving a role's guild arrives with the role tools (#8).");

    /// <inheritdoc />
    public Task<IReadOnlyList<GuildSummary>> GetGuildsAsync(CancellationToken cancellationToken) =>
        MapFailuresAsync<IReadOnlyList<GuildSummary>>(async () =>
            (await _api.GetGuildSummariesAsync(ReadOptions(cancellationToken)).ConfigureAwait(false))
                .Select(guild => new GuildSummary(guild.Id, guild.Name))
                .ToList());

    /// <inheritdoc />
    public Task<GuildInfo> GetGuildInfoAsync(ulong guildId, CancellationToken cancellationToken) =>
        MapFailuresAsync(async () =>
        {
            IGuild guild = await _api.GetGuildAsync(guildId, ReadOptions(cancellationToken)).ConfigureAwait(false)
                ?? throw new DiscordFailureException(
                    new ToolError(ErrorCode.NotFound, $"Guild {Snowflake.Format(guildId)} was not found."), null);
            IReadOnlyCollection<IGuildChannel> channels =
                await guild.GetChannelsAsync(CacheMode.AllowDownload, ReadOptions(cancellationToken)).ConfigureAwait(false);
            int memberCount = guild.ApproximateMemberCount
                ?? throw new InvalidOperationException("Discord returned the guild without its approximate member count.");
            return new GuildInfo(guild.Id, guild.Name, memberCount, channels.Count, guild.Roles.Count);
        });

    // A new instance per request: RequestOptions is mutable, and Discord.Net writes into it.
    private static RequestOptions ReadOptions(CancellationToken cancellationToken) => new() { CancelToken = cancellationToken };

    private static async Task<T> MapFailuresAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (Exception exception) when (DiscordFailureMapping.Map(exception) is ToolError error)
        {
            throw new DiscordFailureException(error, exception);
        }
    }
}

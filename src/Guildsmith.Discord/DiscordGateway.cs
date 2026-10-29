using Discord;

using Guildsmith.Core.Arguments;
using Guildsmith.Core.Errors;
using Guildsmith.Core.Gateway;
using Guildsmith.Core.Models;

namespace Guildsmith.Discord;

/// <summary>
/// The Discord.Net REST implementation of <see cref="IDiscordGateway"/>, over <see cref="IDiscordRestApi"/>.
/// Every Discord failure <see cref="DiscordFailureMapping"/> maps is thrown as a
/// <see cref="DiscordFailureException"/>; any other propagates unchanged. Every Write and Destructive
/// request carries <see cref="WriteRequestOptions.Create(CancellationToken)"/>.
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

    /// <inheritdoc />
    public Task<ulong?> GetChannelGuildIdAsync(ulong channelId, CancellationToken cancellationToken) =>
        MapFailuresAsync(async () =>
            (await FindGuildChannelAsync(channelId, cancellationToken).ConfigureAwait(false))?.GuildId);

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

    /// <inheritdoc />
    public Task<ChannelInfo?> GetChannelAsync(ulong channelId, CancellationToken cancellationToken) =>
        MapFailuresAsync(async () =>
            await FindGuildChannelAsync(channelId, cancellationToken).ConfigureAwait(false) is IGuildChannel channel
                ? ToChannelInfo(channel)
                : null);

    /// <inheritdoc />
    public Task<IReadOnlyList<ChannelInfo>> GetChannelsAsync(ulong guildId, CancellationToken cancellationToken) =>
        MapFailuresAsync<IReadOnlyList<ChannelInfo>>(async () =>
        {
            IGuild guild = await RequireGuildAsync(guildId, cancellationToken).ConfigureAwait(false);
            IReadOnlyCollection<IGuildChannel> channels =
                await guild.GetChannelsAsync(CacheMode.AllowDownload, ReadOptions(cancellationToken)).ConfigureAwait(false);
            return channels.Select(ToChannelInfo).ToList();
        });

    /// <inheritdoc />
    public Task<ChannelInfo> CreateChannelAsync(ulong guildId, NewChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return MapFailuresAsync(async () =>
        {
            IGuild guild = await RequireGuildAsync(guildId, cancellationToken).ConfigureAwait(false);
            RequestOptions options = WriteRequestOptions.Create(cancellationToken);
            IGuildChannel created = channel.Kind switch
            {
                ChannelKind.Text => await guild.CreateTextChannelAsync(channel.Name, properties => ApplyNew(properties, channel), options).ConfigureAwait(false),
                ChannelKind.Voice => await guild.CreateVoiceChannelAsync(channel.Name, properties => ApplyNew(properties, channel), options).ConfigureAwait(false),
                ChannelKind.Category => await guild.CreateCategoryAsync(channel.Name, properties => ApplyNew(properties, channel), options).ConfigureAwait(false),
                ChannelKind.Announcement => await guild.CreateNewsChannelAsync(channel.Name, properties => ApplyNew(properties, channel), options).ConfigureAwait(false),
                ChannelKind.Forum => await guild.CreateForumChannelAsync(channel.Name, properties => ApplyNew(properties, channel), options).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(channel), channel.Kind, "Not a defined channel kind."),
            };
            return ToChannelInfo(created);
        });
    }

    /// <inheritdoc />
    public Task<ChannelInfo> EditChannelAsync(ulong channelId, ChannelEdit edit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);

        return MapFailuresAsync(async () =>
        {
            IGuildChannel channel = await RequireGuildChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
            RequestOptions options = WriteRequestOptions.Create(cancellationToken);

            // Each type is modified through its own interface, so Discord.Net builds the request from
            // that type's property class. A voice channel is an ITextChannel too, so its arm comes first.
            Task modify = channel switch
            {
                IVoiceChannel voice => voice.ModifyAsync((VoiceChannelProperties properties) => ApplyEdit(properties, edit), options),
                ITextChannel text => text.ModifyAsync((TextChannelProperties properties) => ApplyEdit(properties, edit), options),
                IForumChannel forum => forum.ModifyAsync((ForumChannelProperties properties) => ApplyEdit(properties, edit), options),
                _ => channel.ModifyAsync((GuildChannelProperties properties) => ApplyEdit(properties, edit), options),
            };
            await modify.ConfigureAwait(false);
            return ToChannelInfo(channel);
        });
    }

    /// <inheritdoc />
    public Task DeleteChannelAsync(ulong channelId, CancellationToken cancellationToken) =>
        MapFailuresAsync(async () =>
        {
            IGuildChannel channel = await RequireGuildChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
            await channel.DeleteAsync(WriteRequestOptions.Create(cancellationToken)).ConfigureAwait(false);
            return channelId;
        });

    // A DM or a group DM is not a guild channel: the channel tools treat it as missing.
    private async Task<IGuildChannel?> FindGuildChannelAsync(ulong channelId, CancellationToken cancellationToken) =>
        await _api.GetChannelAsync(channelId, ReadOptions(cancellationToken)).ConfigureAwait(false) as IGuildChannel;

    private async Task<IGuildChannel> RequireGuildChannelAsync(ulong channelId, CancellationToken cancellationToken) =>
        await FindGuildChannelAsync(channelId, cancellationToken).ConfigureAwait(false)
            ?? throw new DiscordFailureException(
                new ToolError(ErrorCode.NotFound, $"Channel {Snowflake.Format(channelId)} was not found."), null);

    private async Task<IGuild> RequireGuildAsync(ulong guildId, CancellationToken cancellationToken) =>
        await _api.GetGuildAsync(guildId, ReadOptions(cancellationToken)).ConfigureAwait(false)
            ?? throw new DiscordFailureException(
                new ToolError(ErrorCode.NotFound, $"Guild {Snowflake.Format(guildId)} was not found."), null);

    private static ChannelInfo ToChannelInfo(IGuildChannel channel) =>
        new(channel.Id, channel.GuildId, channel.Name, KindOf(channel), (channel as INestedChannel)?.CategoryId, channel.Position);

    // Discord.Net's own reading of the channel's interfaces; a type outside the five maps to null.
    private static ChannelKind? KindOf(IChannel channel) => channel.GetChannelType() switch
    {
        ChannelType.Text => ChannelKind.Text,
        ChannelType.Voice => ChannelKind.Voice,
        ChannelType.Category => ChannelKind.Category,
        ChannelType.News => ChannelKind.Announcement,
        ChannelType.Forum => ChannelKind.Forum,
        _ => null,
    };

    // Only the fields given are set: a property left alone stays unspecified, and Discord.Net then
    // leaves it out of the request.
    private static void ApplyNew(GuildChannelProperties properties, NewChannel channel)
    {
        if (channel.ParentId is ulong parentId)
        {
            properties.CategoryId = parentId;
        }

        if (channel.Position is int position)
        {
            properties.Position = position;
        }

        if (channel.Topic is not null && properties is TextChannelProperties text)
        {
            text.Topic = channel.Topic;
        }
    }

    private static void ApplyEdit(GuildChannelProperties properties, ChannelEdit edit)
    {
        if (edit.Name is not null)
        {
            properties.Name = edit.Name;
        }

        if (edit.ParentId is ulong parentId)
        {
            properties.CategoryId = parentId;
        }

        if (edit.Position is int position)
        {
            properties.Position = position;
        }

        if (properties is TextChannelProperties text)
        {
            if (edit.Topic is not null)
            {
                text.Topic = edit.Topic;
            }

            if (edit.SlowModeSeconds is int slowMode)
            {
                text.SlowModeInterval = slowMode;
            }

            if (edit.Nsfw is bool nsfw)
            {
                text.IsNsfw = nsfw;
            }
        }
    }

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

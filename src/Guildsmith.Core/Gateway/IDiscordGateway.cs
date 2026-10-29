using Guildsmith.Core.Models;

namespace Guildsmith.Core.Gateway;

/// <summary>
/// The Discord operations Guildsmith performs, behind an interface so that Core is unit-testable
/// with the gateway mocked (spec section 4).
/// A Discord failure with a code of spec section 8 is thrown as <see cref="DiscordFailureException"/>;
/// any other failure is thrown as it is, and <see cref="Guards.GuardPipeline"/> returns it as
/// <c>internal_error</c>.
/// Every Write or Destructive method added to this interface sends its Discord request with the
/// audit-log reason <see cref="Audit.AuditReason.Value"/> (spec section 7.8). A mock of this
/// interface cannot see that reason, so it is asserted one layer down, in the tests of the
/// <c>Guildsmith.Discord</c> implementation: on the <c>RequestOptions</c> that each Discord.Net call
/// made through its <c>IDiscordRestApi</c> seam receives (a mocked <c>IGuild</c>, <c>IGuildChannel</c>, …).
/// </summary>
public interface IDiscordGateway
{
    /// <summary>
    /// The guild the channel belongs to, or <see langword="null"/> when no such channel exists or it
    /// belongs to no guild (a DM or a group DM).
    /// </summary>
    Task<ulong?> GetChannelGuildIdAsync(ulong channelId, CancellationToken cancellationToken);

    /// <summary>The guild the role belongs to, or <see langword="null"/> when no such role exists.</summary>
    Task<ulong?> GetRoleGuildIdAsync(ulong roleId, CancellationToken cancellationToken);

    /// <summary>Every guild the bot has joined, allowed or not.</summary>
    Task<IReadOnlyList<GuildSummary>> GetGuildsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The overview of <paramref name="guildId"/>. A guild Discord does not return is a
    /// <see cref="DiscordFailureException"/> with <see cref="Errors.ErrorCode.NotFound"/>.
    /// </summary>
    Task<GuildInfo> GetGuildInfoAsync(ulong guildId, CancellationToken cancellationToken);

    /// <summary>
    /// The channel <paramref name="channelId"/>, or <see langword="null"/> when no such channel exists
    /// or it belongs to no guild (a DM or a group DM).
    /// </summary>
    Task<ChannelInfo?> GetChannelAsync(ulong channelId, CancellationToken cancellationToken);

    /// <summary>
    /// Every channel of <paramref name="guildId"/>, of any type, in the order Discord returns them. A
    /// guild Discord does not return is a <see cref="DiscordFailureException"/> with
    /// <see cref="Errors.ErrorCode.NotFound"/>.
    /// </summary>
    Task<IReadOnlyList<ChannelInfo>> GetChannelsAsync(ulong guildId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates <paramref name="channel"/> in <paramref name="guildId"/> and returns it (Write). A guild
    /// Discord does not return is a <see cref="DiscordFailureException"/> with <see cref="Errors.ErrorCode.NotFound"/>.
    /// </summary>
    Task<ChannelInfo> CreateChannelAsync(ulong guildId, NewChannel channel, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the fields of <paramref name="edit"/> that are not <see langword="null"/> on the channel
    /// <paramref name="channelId"/>, and leaves every other field as it is (Write). A channel Discord
    /// does not return is a <see cref="DiscordFailureException"/> with <see cref="Errors.ErrorCode.NotFound"/>.
    /// </summary>
    Task<ChannelInfo> EditChannelAsync(ulong channelId, ChannelEdit edit, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the channel <paramref name="channelId"/> (Destructive). A channel Discord does not return
    /// is a <see cref="DiscordFailureException"/> with <see cref="Errors.ErrorCode.NotFound"/>.
    /// </summary>
    Task DeleteChannelAsync(ulong channelId, CancellationToken cancellationToken);
}

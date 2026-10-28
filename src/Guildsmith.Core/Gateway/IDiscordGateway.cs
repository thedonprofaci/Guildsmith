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
    /// <summary>The guild the channel belongs to, or <see langword="null"/> when no such channel exists.</summary>
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
}

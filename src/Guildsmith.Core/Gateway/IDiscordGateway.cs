namespace Guildsmith.Core.Gateway;

/// <summary>
/// The Discord operations Guildsmith performs, behind an interface so that Core is unit-testable
/// with the gateway mocked (spec section 4). This round holds only what the guards need.
/// </summary>
public interface IDiscordGateway
{
    /// <summary>The guild the channel belongs to, or <see langword="null"/> when no such channel exists.</summary>
    Task<ulong?> GetChannelGuildIdAsync(ulong channelId, CancellationToken cancellationToken);

    /// <summary>The guild the role belongs to, or <see langword="null"/> when no such role exists.</summary>
    Task<ulong?> GetRoleGuildIdAsync(ulong roleId, CancellationToken cancellationToken);
}

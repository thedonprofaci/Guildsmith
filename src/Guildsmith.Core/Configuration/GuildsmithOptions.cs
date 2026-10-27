using Microsoft.Extensions.Logging;

namespace Guildsmith.Core.Configuration;

/// <summary>
/// Every startup setting of spec section 5 plus <c>ALLOW_ADMIN_GRANT</c> (spec section 7.4),
/// validated once by <see cref="GuildsmithOptionsReader"/>.
/// </summary>
/// <remarks>
/// A class rather than a record on purpose: a record's generated <c>ToString</c> prints every
/// property, so logging or interpolating the options object would print the bot token.
/// </remarks>
public sealed class GuildsmithOptions
{
    /// <summary><c>DISCORD_TOKEN</c>: the bot token. Required.</summary>
    public required string DiscordToken { get; init; }

    /// <summary><c>ALLOWED_GUILD_IDS</c>: the guilds a tool may act on. Required, never empty.</summary>
    public required IReadOnlySet<ulong> AllowedGuildIds { get; init; }

    /// <summary><c>DISCORD_MCP_MODE</c>, default <see cref="DiscordMcpMode.ReadWrite"/>.</summary>
    public required DiscordMcpMode Mode { get; init; }

    /// <summary><c>ALLOW_DESTRUCTIVE</c>, default <see langword="false"/>.</summary>
    public required bool AllowDestructive { get; init; }

    /// <summary><c>ALLOW_ADMIN_GRANT</c>, default <see langword="false"/>.</summary>
    public required bool AllowAdminGrant { get; init; }

    /// <summary><c>MAX_MESSAGES_PER_READ</c>, 1 to 100, default 50.</summary>
    public required int MaxMessagesPerRead { get; init; }

    /// <summary><c>MCP_TRANSPORT</c>, default <see cref="McpTransport.Stdio"/>.</summary>
    public required McpTransport Transport { get; init; }

    /// <summary><c>LOG_LEVEL</c>, default <see cref="LogLevel.Information"/>.</summary>
    public required LogLevel LogLevel { get; init; }
}

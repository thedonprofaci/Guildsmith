using Guildsmith.Core.Arguments;
using Guildsmith.Core.Errors;

namespace Guildsmith.Core.Guards;

/// <summary>The first guard: the guild a call acts on must be in <c>ALLOWED_GUILD_IDS</c> (spec section 7.2).</summary>
public static class GuildAllowlistGuard
{
    /// <summary>
    /// <see cref="ErrorCode.ForbiddenGuild"/> when <paramref name="guildId"/>, which the caller
    /// supplied as <c>guild_id</c>, is not allowed, otherwise <see langword="null"/>. The message
    /// names the guild: it echoes the caller's own input.
    /// </summary>
    public static ToolError? Check(ulong guildId, IReadOnlySet<ulong> allowedGuildIds)
    {
        ArgumentNullException.ThrowIfNull(allowedGuildIds);

        return allowedGuildIds.Contains(guildId)
            ? null
            : new ToolError(ErrorCode.ForbiddenGuild, $"Guild {Snowflake.Format(guildId)} is not in ALLOWED_GUILD_IDS.");
    }

    /// <summary>
    /// <see cref="ErrorCode.ForbiddenGuild"/> when <paramref name="guildId"/>, which the server
    /// resolved from a channel, role or message ID, is not allowed, otherwise <see langword="null"/>.
    /// The message does not name the guild: the caller never supplied it, and naming it would tell
    /// the model which foreign guild the ID belongs to (issue #3).
    /// </summary>
    public static ToolError? CheckResolved(ulong guildId, IReadOnlySet<ulong> allowedGuildIds)
    {
        ArgumentNullException.ThrowIfNull(allowedGuildIds);

        return allowedGuildIds.Contains(guildId)
            ? null
            : new ToolError(ErrorCode.ForbiddenGuild, "The guild this ID belongs to is not in ALLOWED_GUILD_IDS.");
    }
}

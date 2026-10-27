using Guildsmith.Core.Arguments;
using Guildsmith.Core.Errors;

namespace Guildsmith.Core.Guards;

/// <summary>The first guard: the guild a call acts on must be in <c>ALLOWED_GUILD_IDS</c> (spec section 7.2).</summary>
public static class GuildAllowlistGuard
{
    /// <summary><see cref="ErrorCode.ForbiddenGuild"/> when <paramref name="guildId"/> is not allowed, otherwise <see langword="null"/>.</summary>
    public static ToolError? Check(ulong guildId, IReadOnlySet<ulong> allowedGuildIds)
    {
        ArgumentNullException.ThrowIfNull(allowedGuildIds);

        return allowedGuildIds.Contains(guildId)
            ? null
            : new ToolError(ErrorCode.ForbiddenGuild, $"Guild {Snowflake.Format(guildId)} is not in ALLOWED_GUILD_IDS.");
    }
}

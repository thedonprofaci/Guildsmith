using Guildsmith.Core.Errors;

namespace Guildsmith.Core.Arguments;

/// <summary>The <c>guild_id</c> argument, optional when exactly one guild is allowed (spec section 6.1).</summary>
public static class GuildArgument
{
    /// <summary>The argument's name as a tool declares it.</summary>
    public const string Name = "guild_id";

    /// <summary>
    /// The guild a call addresses: the parsed <paramref name="guildId"/> when it is supplied, the
    /// only allowed guild when it is not. With any other number of allowed guilds a missing
    /// <c>guild_id</c> is <see cref="ErrorCode.InvalidArgument"/>, listing the allowed IDs in
    /// ascending numeric order. Whether a supplied guild is allowed is the allowlist guard's
    /// decision, not this one's.
    /// </summary>
    public static ToolResult<ulong> Resolve(string? guildId, IReadOnlySet<ulong> allowedGuildIds)
    {
        ArgumentNullException.ThrowIfNull(allowedGuildIds);

        if (guildId is not null)
        {
            return Snowflake.ParseArgument(guildId, Name);
        }

        if (allowedGuildIds.Count == 1)
        {
            return ToolResult<ulong>.Success(allowedGuildIds.Single());
        }

        return ToolResult<ulong>.Failure(ToolError.InvalidArgument(
            $"{Name} is required when more than one guild is allowed.",
            allowedGuildIds.Order().Select(Snowflake.Format)));
    }
}

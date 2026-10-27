namespace Guildsmith.Core.Guards;

/// <summary>
/// The raw ID arguments of one tool call, exactly as the client sent them. <see cref="GuardPipeline"/>
/// parses them and resolves the guild they belong to.
/// </summary>
public sealed class ToolTarget
{
    private ToolTarget(ToolTargetKind kind, string? guildId, string? channelId, string? roleId, string? messageId)
    {
        Kind = kind;
        GuildId = guildId;
        ChannelId = channelId;
        RoleId = roleId;
        MessageId = messageId;
    }

    /// <summary>How the call names its object.</summary>
    public ToolTargetKind Kind { get; }

    /// <summary>The <c>guild_id</c> argument, <see langword="null"/> when it was not supplied.</summary>
    public string? GuildId { get; }

    /// <summary>The <c>channel_id</c> argument.</summary>
    public string? ChannelId { get; }

    /// <summary>The <c>role_id</c> argument.</summary>
    public string? RoleId { get; }

    /// <summary>The <c>message_id</c> argument.</summary>
    public string? MessageId { get; }

    /// <summary>A call addressed by <c>guild_id</c>.</summary>
    public static ToolTarget Guild(string? guildId) =>
        new(ToolTargetKind.Guild, guildId, null, null, null);

    /// <summary>A call addressed by <c>channel_id</c>, optionally also carrying a <c>guild_id</c>.</summary>
    public static ToolTarget Channel(string? channelId, string? guildId = null) =>
        new(ToolTargetKind.Channel, guildId, channelId, null, null);

    /// <summary>A call addressed by <c>role_id</c>, optionally also carrying a <c>guild_id</c>.</summary>
    public static ToolTarget Role(string? roleId, string? guildId = null) =>
        new(ToolTargetKind.Role, guildId, null, roleId, null);

    /// <summary>A call addressed by <c>channel_id</c> plus <c>message_id</c> (spec section 6.6).</summary>
    public static ToolTarget Message(string? channelId, string? messageId, string? guildId = null) =>
        new(ToolTargetKind.Message, guildId, channelId, null, messageId);
}

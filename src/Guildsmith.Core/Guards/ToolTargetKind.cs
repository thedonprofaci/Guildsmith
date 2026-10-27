namespace Guildsmith.Core.Guards;

/// <summary>How a tool call names the object it acts on, and so how its guild is found.</summary>
public enum ToolTargetKind
{
    /// <summary>By <c>guild_id</c> alone.</summary>
    Guild,

    /// <summary>By <c>channel_id</c>; the guild is resolved from the channel.</summary>
    Channel,

    /// <summary>By <c>role_id</c>; the guild is resolved from the role.</summary>
    Role,

    /// <summary>By <c>channel_id</c> plus <c>message_id</c>; the guild is resolved from the channel.</summary>
    Message,
}

namespace Guildsmith.Core.Models;

/// <summary>The five channel types the channel tools create, filter by and edit (spec section 6.4).</summary>
public enum ChannelKind
{
    /// <summary>A text channel, <c>text</c>.</summary>
    Text,

    /// <summary>A voice channel, <c>voice</c>.</summary>
    Voice,

    /// <summary>A category, <c>category</c>.</summary>
    Category,

    /// <summary>An announcement (news) channel, <c>announcement</c>.</summary>
    Announcement,

    /// <summary>A forum channel, <c>forum</c>.</summary>
    Forum,
}

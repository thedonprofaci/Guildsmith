using Guildsmith.Core.Models;

namespace Guildsmith.Core.Services;

/// <summary>
/// Which optional fields of <c>create_channel</c> and <c>edit_channel</c> each channel type can carry.
/// Read from Discord.Net 3.20.2's property classes: a category is modified through
/// <c>GuildChannelProperties</c>, which has no topic, NSFW flag or slow mode; <c>VoiceChannelProperties</c>
/// hides <c>Topic</c> ("Not supported in voice channels") and <c>ForumChannelProperties</c> hides
/// <c>SlowModeInterval</c> ("Not available in forum channels"), both as get-only properties. One rule
/// is Discord's own rather than the property classes': a category cannot sit under another category,
/// so it takes no <c>parent_id</c>.
/// </summary>
public static class ChannelFieldRules
{
    /// <summary>The argument names a channel of <paramref name="kind"/> cannot carry, in the tools' parameter order.</summary>
    public static IReadOnlyList<string> UnsupportedFields(ChannelKind kind) => kind switch
    {
        ChannelKind.Text => [],
        ChannelKind.Voice => ["topic"],
        ChannelKind.Category => ["topic", "parent_id", "slowmode_seconds", "nsfw"],
        ChannelKind.Announcement => [],
        ChannelKind.Forum => ["slowmode_seconds"],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a defined channel kind."),
    };

    /// <summary>
    /// The first of <paramref name="givenFields"/> a channel of <paramref name="kind"/> cannot carry, or
    /// <see langword="null"/> when it can carry all of them.
    /// </summary>
    public static string? FirstUnsupported(ChannelKind kind, IEnumerable<string> givenFields)
    {
        ArgumentNullException.ThrowIfNull(givenFields);

        IReadOnlyList<string> unsupported = UnsupportedFields(kind);
        return givenFields.FirstOrDefault(unsupported.Contains);
    }
}

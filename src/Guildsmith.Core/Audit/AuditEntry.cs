using System.Text;

using Guildsmith.Core.Arguments;
using Guildsmith.Core.Guards;

namespace Guildsmith.Core.Audit;

/// <summary>
/// The audit log entry of a Write or Destructive tool call (spec section 7.8), written to stderr
/// under <see cref="Category"/>.
/// </summary>
public static class AuditEntry
{
    /// <summary>
    /// The logger category of every audit entry. It is separate from the category of the
    /// <see cref="GuardPipeline"/> detail log, so the host can keep it on whatever <c>LOG_LEVEL</c> says.
    /// </summary>
    public const string Category = "Guildsmith.Audit";

    /// <summary>
    /// One line naming the tool, its level, the IDs of the call and the outcome, for example
    /// <c>tool=send_message level=write guild=111 channel=5001 outcome=success</c>. An ID that is
    /// <see langword="null"/> is left out, never written as <c>0</c>. The IDs are parsed snowflakes,
    /// so no argument text the client sent can reach the entry.
    /// </summary>
    public static string Render(
        string toolName,
        ToolLevel level,
        ulong? guildId,
        ulong? channelId,
        ulong? roleId,
        ulong? messageId,
        AuditOutcome outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(outcome);

        StringBuilder entry = new();
        entry.Append("tool=").Append(toolName).Append(" level=").Append(LevelText(level));
        AppendId(entry, "guild", guildId);
        AppendId(entry, "channel", channelId);
        AppendId(entry, "role", roleId);
        AppendId(entry, "message", messageId);
        entry.Append(" outcome=").Append(outcome.Text);
        return entry.ToString();
    }

    private static string LevelText(ToolLevel level) => level switch
    {
        ToolLevel.Read => "read",
        ToolLevel.Write => "write",
        ToolLevel.Destructive => "destructive",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Not a defined tool level."),
    };

    private static void AppendId(StringBuilder entry, string name, ulong? id)
    {
        if (id is ulong value)
        {
            entry.Append(' ').Append(name).Append('=').Append(Snowflake.Format(value));
        }
    }
}

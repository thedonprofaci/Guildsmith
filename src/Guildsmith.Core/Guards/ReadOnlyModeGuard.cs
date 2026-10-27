using Guildsmith.Core.Configuration;
using Guildsmith.Core.Errors;

namespace Guildsmith.Core.Guards;

/// <summary>The second guard: only a Read tool runs unless the mode is ReadWrite (spec section 6.2).</summary>
public static class ReadOnlyModeGuard
{
    /// <summary>
    /// <see cref="ErrorCode.ReadOnlyMode"/> for any level but Read in any mode but ReadWrite,
    /// otherwise <see langword="null"/>. An undefined level or mode is refused, not let through.
    /// </summary>
    public static ToolError? Check(ToolLevel level, DiscordMcpMode mode) =>
        level != ToolLevel.Read && mode != DiscordMcpMode.ReadWrite
            ? new ToolError(ErrorCode.ReadOnlyMode, "This tool changes the server, and DISCORD_MCP_MODE is ReadOnly.")
            : null;
}

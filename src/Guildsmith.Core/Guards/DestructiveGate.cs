using Guildsmith.Core.Errors;

namespace Guildsmith.Core.Guards;

/// <summary>The third guard: a Destructive tool runs only with <c>ALLOW_DESTRUCTIVE=true</c> (spec section 6.2).</summary>
public static class DestructiveGate
{
    /// <summary>
    /// <see cref="ErrorCode.DestructiveDisabled"/> for any level but Read and Write while
    /// destructive tools are disabled, otherwise <see langword="null"/>. An undefined level counts
    /// as destructive.
    /// </summary>
    public static ToolError? Check(ToolLevel level, bool allowDestructive) =>
        level is not (ToolLevel.Read or ToolLevel.Write) && !allowDestructive
            ? new ToolError(ErrorCode.DestructiveDisabled, "This tool is destructive, and ALLOW_DESTRUCTIVE is false.")
            : null;
}

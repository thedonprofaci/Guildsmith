namespace Guildsmith.Core.Audit;

/// <summary>The Discord audit-log reason every Write and Destructive request carries (spec section 7.8).</summary>
public static class AuditReason
{
    /// <summary>The reason text, shown in the guild's own audit log next to the change.</summary>
    public const string Value = "via Discord MCP";
}

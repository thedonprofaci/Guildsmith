namespace Guildsmith.Core.Guards;

/// <summary>What a tool may do, which decides the guards it must pass (spec section 6.2).</summary>
public enum ToolLevel
{
    /// <summary>Available in ReadOnly mode too.</summary>
    Read,

    /// <summary>Needs ReadWrite mode.</summary>
    Write,

    /// <summary>Needs ReadWrite mode and <c>ALLOW_DESTRUCTIVE=true</c>.</summary>
    Destructive,
}

using Guildsmith.Core.Guards;

namespace Guildsmith.Server.Tools;

/// <summary>
/// The level of an MCP tool method (spec section 6.2), next to its <c>McpServerTool</c> attribute so
/// that the tool registry test can check the attribute's hints against it.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class DeclaredLevelAttribute : Attribute
{
    /// <summary>Declares <paramref name="level"/>.</summary>
    public DeclaredLevelAttribute(ToolLevel level)
    {
        Level = level;
    }

    /// <summary>The declared level.</summary>
    public ToolLevel Level { get; }
}

namespace Guildsmith.Core.Configuration;

/// <summary>
/// The <c>MCP_TRANSPORT</c> setting. Only stdio exists in phase 1; <c>http</c> is rejected at
/// startup until the phase 3 transport is built, so it deliberately has no member here.
/// </summary>
public enum McpTransport
{
    Stdio,
}

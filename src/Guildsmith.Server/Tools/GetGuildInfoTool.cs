using System.ComponentModel;

using Guildsmith.Core.Guards;
using Guildsmith.Core.Services;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Guildsmith.Server.Tools;

/// <summary>The <c>get_guild_info</c> tool (spec section 6.3).</summary>
[McpServerToolType]
public sealed class GetGuildInfoTool
{
    /// <summary>The tool's name.</summary>
    public const string Name = "get_guild_info";

    /// <summary>The tool's level, declared once: on the method, and handed to the pipeline.</summary>
    public const ToolLevel Level = ToolLevel.Read;

    private readonly GuildService _service;

    /// <summary>A tool calling <paramref name="service"/>.</summary>
    public GetGuildInfoTool(GuildService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>Runs the tool.</summary>
    [McpServerTool(Name = Name, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [DeclaredLevel(Level)]
    [Description("Gets one Discord server's (guild's) name, member count, channel count and role count. Read-only.")]
    public async Task<CallToolResult> GetGuildInfoAsync(
        CancellationToken cancellationToken,
        [Description("The guild's ID, as a string. Optional when exactly one guild is allowed.")] string? guild_id = null) =>
        ToolResponse.From(
            await _service.GetGuildInfoAsync(Name, Level, guild_id, cancellationToken).ConfigureAwait(false),
            info => info.ToJson());
}

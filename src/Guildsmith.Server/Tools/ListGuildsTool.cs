using System.ComponentModel;

using Guildsmith.Core.Guards;
using Guildsmith.Core.Models;
using Guildsmith.Core.Services;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Guildsmith.Server.Tools;

/// <summary>The <c>list_guilds</c> tool (spec section 6.3).</summary>
[McpServerToolType]
public sealed class ListGuildsTool
{
    /// <summary>The tool's name.</summary>
    public const string Name = "list_guilds";

    /// <summary>The tool's level, declared once: on the method, and handed to the pipeline.</summary>
    public const ToolLevel Level = ToolLevel.Read;

    private readonly GuildService _service;

    /// <summary>A tool calling <paramref name="service"/>.</summary>
    public ListGuildsTool(GuildService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>Runs the tool.</summary>
    [McpServerTool(Name = Name, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [DeclaredLevel(Level)]
    [Description("Lists the Discord servers (guilds) the bot is a member of and ALLOWED_GUILD_IDS allows, as id and name. Read-only.")]
    public async Task<CallToolResult> ListGuildsAsync(CancellationToken cancellationToken) =>
        ToolResponse.From(await _service.ListGuildsAsync(Name, Level, cancellationToken).ConfigureAwait(false), GuildSummary.ToJson);
}

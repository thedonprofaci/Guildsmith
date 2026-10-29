using System.ComponentModel;

using Guildsmith.Core.Guards;
using Guildsmith.Core.Models;
using Guildsmith.Core.Services;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Guildsmith.Server.Tools;

/// <summary>The <c>list_channels</c> tool (spec section 6.4).</summary>
[McpServerToolType]
public sealed class ListChannelsTool
{
    /// <summary>The tool's name.</summary>
    public const string Name = "list_channels";

    /// <summary>The tool's level, declared once: on the method, and handed to the pipeline.</summary>
    public const ToolLevel Level = ToolLevel.Read;

    private readonly ChannelService _service;

    /// <summary>A tool calling <paramref name="service"/>.</summary>
    public ListChannelsTool(ChannelService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>Runs the tool.</summary>
    [McpServerTool(Name = Name, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [DeclaredLevel(Level)]
    [Description("Lists one Discord server's (guild's) text, voice, category, announcement and forum channels as id, name, type, parent_id and position, in ascending ID order. Read-only.")]
    public async Task<CallToolResult> ListChannelsAsync(
        CancellationToken cancellationToken,
        [Description("The guild's ID, as a string. Optional when exactly one guild is allowed.")] string? guild_id = null,
        [Description("Only channels of this type: text, voice, category, announcement or forum.")] string? type = null) =>
        ToolResponse.From(
            await _service.ListChannelsAsync(Name, Level, guild_id, type, cancellationToken).ConfigureAwait(false),
            ChannelInfo.ToJson);
}

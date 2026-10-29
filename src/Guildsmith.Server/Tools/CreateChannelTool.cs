using System.ComponentModel;

using Guildsmith.Core.Guards;
using Guildsmith.Core.Services;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Guildsmith.Server.Tools;

/// <summary>The <c>create_channel</c> tool (spec section 6.4).</summary>
[McpServerToolType]
public sealed class CreateChannelTool
{
    /// <summary>The tool's name.</summary>
    public const string Name = "create_channel";

    /// <summary>The tool's level, declared once: on the method, and handed to the pipeline.</summary>
    public const ToolLevel Level = ToolLevel.Write;

    private readonly ChannelService _service;

    /// <summary>A tool calling <paramref name="service"/>.</summary>
    public CreateChannelTool(ChannelService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>Runs the tool.</summary>
    [McpServerTool(Name = Name, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [DeclaredLevel(Level)]
    [Description("Creates a channel in a Discord server (guild) and returns it as id, name, type, parent_id and position. Not destructive; calling it twice creates two channels. Refused in ReadOnly mode.")]
    public async Task<CallToolResult> CreateChannelAsync(
        CancellationToken cancellationToken,
        [Description("The new channel's name.")] string name,
        [Description("The channel type: text, voice, category, announcement or forum.")] string type,
        [Description("The guild's ID, as a string. Optional when exactly one guild is allowed.")] string? guild_id = null,
        [Description("The ID of a category of the same guild to create the channel under. Not for a category.")] string? parent_id = null,
        [Description("The channel topic. Only for text, announcement and forum channels.")] string? topic = null,
        [Description("The channel's position in the channel list.")] int? position = null) =>
        ToolResponse.From(
            await _service.CreateChannelAsync(Name, Level, guild_id, name, type, parent_id, topic, position, cancellationToken).ConfigureAwait(false),
            channel => channel.ToJson());
}

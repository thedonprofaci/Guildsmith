using System.ComponentModel;

using Guildsmith.Core.Guards;
using Guildsmith.Core.Services;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Guildsmith.Server.Tools;

/// <summary>The <c>edit_channel</c> tool (spec section 6.4).</summary>
[McpServerToolType]
public sealed class EditChannelTool
{
    /// <summary>The tool's name.</summary>
    public const string Name = "edit_channel";

    /// <summary>The tool's level, declared once: on the method, and handed to the pipeline.</summary>
    public const ToolLevel Level = ToolLevel.Write;

    private readonly ChannelService _service;

    /// <summary>A tool calling <paramref name="service"/>.</summary>
    public EditChannelTool(ChannelService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>Runs the tool.</summary>
    [McpServerTool(Name = Name, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [DeclaredLevel(Level)]
    [Description("Changes the given fields of a Discord channel, leaves every other field as it is, and returns the channel as id, name, type, parent_id and position. At least one field is required. Not destructive. Refused in ReadOnly mode.")]
    public async Task<CallToolResult> EditChannelAsync(
        CancellationToken cancellationToken,
        [Description("The channel's ID, as a string.")] string channel_id,
        [Description("The new name.")] string? name = null,
        [Description("The new topic. Only for text, announcement and forum channels.")] string? topic = null,
        [Description("The ID of a category of the same guild to move the channel under. Not for a category.")] string? parent_id = null,
        [Description("The new position in the channel list.")] int? position = null,
        [Description("The slow mode interval in seconds, 0 to turn it off. Not for a category or a forum.")] int? slowmode_seconds = null,
        [Description("Whether the channel is age-restricted (NSFW). Not for a category.")] bool? nsfw = null) =>
        ToolResponse.From(
            await _service.EditChannelAsync(Name, Level, channel_id, name, topic, parent_id, position, slowmode_seconds, nsfw, cancellationToken)
                .ConfigureAwait(false),
            channel => channel.ToJson());
}

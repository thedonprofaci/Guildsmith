using System.ComponentModel;

using Guildsmith.Core.Arguments;
using Guildsmith.Core.Guards;
using Guildsmith.Core.Services;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Guildsmith.Server.Tools;

/// <summary>The <c>delete_channel</c> tool (spec section 6.4).</summary>
[McpServerToolType]
public sealed class DeleteChannelTool
{
    /// <summary>The tool's name.</summary>
    public const string Name = "delete_channel";

    /// <summary>The tool's level, declared once: on the method, and handed to the pipeline.</summary>
    public const ToolLevel Level = ToolLevel.Destructive;

    private readonly ChannelService _service;

    /// <summary>A tool calling <paramref name="service"/>.</summary>
    public DeleteChannelTool(ChannelService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <summary>Runs the tool.</summary>
    // Idempotent: a second call with the same channel_id changes nothing more on Discord; the channel
    // is already gone.
    [McpServerTool(Name = Name, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [DeclaredLevel(Level)]
    [Description("Destructive: permanently deletes a Discord channel with its messages. Runs only when ALLOW_DESTRUCTIVE=true and not in ReadOnly mode. Returns the deleted channel's id.")]
    public async Task<CallToolResult> DeleteChannelAsync(
        CancellationToken cancellationToken,
        [Description("The channel's ID, as a string.")] string channel_id) =>
        ToolResponse.From(
            await _service.DeleteChannelAsync(Name, Level, channel_id, cancellationToken).ConfigureAwait(false),
            id => $$"""{"id":"{{Snowflake.Format(id)}}","deleted":true}""");
}

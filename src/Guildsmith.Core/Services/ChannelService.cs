using Guildsmith.Core.Arguments;
using Guildsmith.Core.Errors;
using Guildsmith.Core.Gateway;
using Guildsmith.Core.Guards;
using Guildsmith.Core.Models;

namespace Guildsmith.Core.Services;

/// <summary>The channel tools of spec section 6.4, each one call through <see cref="GuardPipeline"/>.</summary>
public sealed class ChannelService
{
    /// <summary>The <c>parent_id</c> argument's name as the tools declare it.</summary>
    public const string ParentIdName = "parent_id";

    private readonly GuardPipeline _pipeline;

    /// <summary>A service running every call through <paramref name="pipeline"/>.</summary>
    public ChannelService(GuardPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
    }

    /// <summary>
    /// The channels of the guild <paramref name="guildId"/> names (or of the only allowed guild), in
    /// ascending numeric ID order, restricted to the type <paramref name="type"/> names when it is
    /// given. A channel whose type is not one of the five of spec section 6.4 is left out. An unknown
    /// <paramref name="type"/> is <see cref="ErrorCode.InvalidArgument"/>, with no gateway call.
    /// </summary>
    public Task<ToolResult<IReadOnlyList<ChannelInfo>>> ListChannelsAsync(
        string toolName, ToolLevel level, string? guildId, string? type, CancellationToken cancellationToken)
    {
        ToolResult<ChannelKind>? kind = type is null ? null : ChannelTypeArgument.Parse(type);

        return _pipeline.RunCheckedAsync<IReadOnlyList<ChannelInfo>>(
            toolName,
            level,
            ToolTarget.Guild(guildId),
            kind is { IsSuccess: false } ? kind.Error : null,
            async (gateway, target, token) => ToolResult<IReadOnlyList<ChannelInfo>>.Success(
                (await gateway.GetChannelsAsync(target.GuildId, token).ConfigureAwait(false))
                    .Where(channel => channel.Kind is not null && (kind is null || channel.Kind == kind.Value))
                    .OrderBy(channel => channel.Id)
                    .ToList()),
            cancellationToken);
    }

    /// <summary>
    /// Creates a channel named <paramref name="name"/> of the type <paramref name="type"/> names in the
    /// guild <paramref name="guildId"/> names (or in the only allowed guild). Without a gateway call,
    /// <see cref="ErrorCode.InvalidArgument"/> is the answer to an unknown type, a malformed
    /// <paramref name="parentId"/>, and a field the type cannot carry (<see cref="ChannelFieldRules"/>);
    /// after the guards, a <paramref name="parentId"/> that is not a category of the same guild.
    /// </summary>
    public Task<ToolResult<ChannelInfo>> CreateChannelAsync(
        string toolName,
        ToolLevel level,
        string? guildId,
        string name,
        string type,
        string? parentId,
        string? topic,
        int? position,
        CancellationToken cancellationToken)
    {
        ToolResult<ChannelKind> kind = ChannelTypeArgument.Parse(type);
        ToolResult<ulong>? parent = parentId is null ? null : Snowflake.ParseArgument(parentId, ParentIdName);
        ToolError? argumentError = !kind.IsSuccess ? kind.Error
            : parent is { IsSuccess: false } ? parent.Error
            : UnsupportedFieldError(kind.Value, Given(("topic", topic is not null), (ParentIdName, parent is not null)));

        return _pipeline.RunCheckedAsync(
            toolName,
            level,
            ToolTarget.Guild(guildId),
            argumentError,
            async (gateway, target, token) =>
            {
                if (parent is not null
                    && await CheckParentAsync(gateway, parent.Value, target.GuildId, token).ConfigureAwait(false) is ToolError parentError)
                {
                    return ToolResult<ChannelInfo>.Failure(parentError);
                }

                NewChannel channel = new(name, kind.Value, parent?.Value, topic, position);
                return ToolResult<ChannelInfo>.Success(await gateway.CreateChannelAsync(target.GuildId, channel, token).ConfigureAwait(false));
            },
            cancellationToken);
    }

    /// <summary>
    /// Changes the given fields of the channel <paramref name="channelId"/> and no other. Without a
    /// gateway call, <see cref="ErrorCode.InvalidArgument"/> is the answer to a call giving no field at
    /// all and to a malformed <paramref name="parentId"/>; after the guards (which resolve the channel's
    /// guild), to a channel whose type is not one of the five of spec section 6.4, to a field its type
    /// cannot carry (<see cref="ChannelFieldRules"/>), and to a <paramref name="parentId"/> that is not a
    /// category of the same guild. None of those reaches Discord with a write.
    /// </summary>
    public Task<ToolResult<ChannelInfo>> EditChannelAsync(
        string toolName,
        ToolLevel level,
        string? channelId,
        string? name,
        string? topic,
        string? parentId,
        int? position,
        int? slowModeSeconds,
        bool? nsfw,
        CancellationToken cancellationToken)
    {
        ToolResult<ulong>? parent = parentId is null ? null : Snowflake.ParseArgument(parentId, ParentIdName);
        ToolError? argumentError = parent is { IsSuccess: false } ? parent.Error
            : name is null && topic is null && parent is null && position is null && slowModeSeconds is null && nsfw is null
                ? ToolError.InvalidArgument("edit_channel needs at least one field to change.", EditableFields)
                : null;

        return _pipeline.RunCheckedAsync(
            toolName,
            level,
            ToolTarget.Channel(channelId),
            argumentError,
            async (gateway, target, token) =>
            {
                ulong id = target.ChannelId!.Value;
                ChannelInfo? channel = await gateway.GetChannelAsync(id, token).ConfigureAwait(false);
                if (channel is null)
                {
                    return ToolResult<ChannelInfo>.Failure(new ToolError(ErrorCode.NotFound, $"Channel {Snowflake.Format(id)} was not found."));
                }

                if (channel.Kind is not ChannelKind kind)
                {
                    return ToolResult<ChannelInfo>.Failure(ToolError.InvalidArgument(
                        "edit_channel changes only channels of the types listed.", ChannelTypeArgument.ValidValues));
                }

                ToolError? error = UnsupportedFieldError(kind, Given(
                    ("topic", topic is not null),
                    (ParentIdName, parent is not null),
                    ("slowmode_seconds", slowModeSeconds is not null),
                    ("nsfw", nsfw is not null)));
                if (error is null && parent is not null)
                {
                    error = await CheckParentAsync(gateway, parent.Value, target.GuildId, token).ConfigureAwait(false);
                }

                if (error is not null)
                {
                    return ToolResult<ChannelInfo>.Failure(error);
                }

                ChannelEdit edit = new(name, topic, parent?.Value, position, slowModeSeconds, nsfw);
                return ToolResult<ChannelInfo>.Success(await gateway.EditChannelAsync(id, edit, token).ConfigureAwait(false));
            },
            cancellationToken);
    }

    /// <summary>Deletes the channel <paramref name="channelId"/> and returns its ID.</summary>
    public Task<ToolResult<ulong>> DeleteChannelAsync(string toolName, ToolLevel level, string? channelId, CancellationToken cancellationToken) =>
        _pipeline.RunAsync(
            toolName,
            level,
            ToolTarget.Channel(channelId),
            async (gateway, target, token) =>
            {
                ulong id = target.ChannelId!.Value;
                await gateway.DeleteChannelAsync(id, token).ConfigureAwait(false);
                return id;
            },
            cancellationToken);

    private static IReadOnlyList<string> EditableFields { get; } = ["name", "topic", ParentIdName, "position", "slowmode_seconds", "nsfw"];

    private static IEnumerable<string> Given(params (string Field, bool IsGiven)[] fields) =>
        fields.Where(field => field.IsGiven).Select(field => field.Field);

    private static ToolError? UnsupportedFieldError(ChannelKind kind, IEnumerable<string> givenFields) =>
        ChannelFieldRules.FirstUnsupported(kind, givenFields) is string field
            ? new ToolError(ErrorCode.InvalidArgument, $"{field} cannot be set on a {ChannelTypeArgument.Format(kind)} channel.")
            : null;

    // The parent is looked up by its own ID, never by listing another guild's channels. A missing
    // parent is a wrong argument here, not a not_found: the channel being created or edited exists.
    private static async Task<ToolError?> CheckParentAsync(IDiscordGateway gateway, ulong parentId, ulong guildId, CancellationToken cancellationToken)
    {
        ChannelInfo? parent = await gateway.GetChannelAsync(parentId, cancellationToken).ConfigureAwait(false);
        if (parent is null)
        {
            return new ToolError(ErrorCode.InvalidArgument, $"{ParentIdName} {Snowflake.Format(parentId)} was not found.");
        }

        if (parent.Kind != ChannelKind.Category)
        {
            return new ToolError(ErrorCode.InvalidArgument, $"{ParentIdName} {Snowflake.Format(parentId)} is not a category.");
        }

        if (parent.GuildId != guildId)
        {
            return new ToolError(ErrorCode.InvalidArgument, $"{ParentIdName} {Snowflake.Format(parentId)} is a category of another guild.");
        }

        return null;
    }
}

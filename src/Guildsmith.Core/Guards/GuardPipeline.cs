using Guildsmith.Core.Arguments;
using Guildsmith.Core.Configuration;
using Guildsmith.Core.Errors;
using Guildsmith.Core.Gateway;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Guildsmith.Core.Guards;

/// <summary>
/// The server-side defence every tool call passes before it reaches Discord (spec sections 7.2
/// and 7.3): the guild allowlist, then ReadOnly mode, then the destructive gate. The pipeline
/// holds the gateway, and hands it to the operation only after every guard has passed. It is also
/// the exception boundary of every tool call (spec section 7.1): an unexpected failure reaches the
/// model as <c>internal_error</c> with a correlation id, and its detail reaches the log only.
/// </summary>
public sealed class GuardPipeline
{
    private readonly GuildsmithOptions _options;
    private readonly IDiscordGateway _gateway;
    private readonly ILogger _logger;

    /// <summary>A pipeline that logs nothing; an unexpected failure is still returned as <c>internal_error</c>.</summary>
    public GuardPipeline(GuildsmithOptions options, IDiscordGateway gateway)
        : this(options, gateway, NullLogger<GuardPipeline>.Instance)
    {
    }

    /// <summary>A pipeline that logs the detail of an unexpected failure to <paramref name="logger"/>.</summary>
    public GuardPipeline(GuildsmithOptions options, IDiscordGateway gateway, ILogger<GuardPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _gateway = gateway;
        _logger = logger;
    }

    /// <summary>
    /// Runs <paramref name="operation"/> when the call passes every guard, and returns the first
    /// refusal otherwise, in which case the operation is never invoked. A call addressed by
    /// <c>guild_id</c> makes no gateway call before the operation; a call addressed by a channel,
    /// role or message ID makes exactly one, to resolve the guild that ID belongs to.
    /// An exception the gateway or the operation throws is logged at <see cref="LogLevel.Error"/> and
    /// returned as <see cref="ErrorCode.InternalError"/>; only an <see cref="OperationCanceledException"/>
    /// thrown while <paramref name="cancellationToken"/> is cancelled propagates.
    /// </summary>
    public async Task<ToolResult<T>> RunAsync<T>(
        ToolLevel level,
        ToolTarget target,
        Func<IDiscordGateway, GuardedTarget, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            return await GuardAndRunAsync(level, target, operation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // The exception's message can carry anything the gateway saw, the token included, so
            // the model gets a fixed text and an id; the detail goes to the log, which redacts it.
            string correlationId = Guid.NewGuid().ToString("N");
            _logger.LogError(exception, "A tool call failed with an unexpected error, correlation_id {CorrelationId}.", correlationId);
            return ToolResult<T>.Failure(ToolError.Internal(correlationId));
        }
    }

    private async Task<ToolResult<T>> GuardAndRunAsync<T>(
        ToolLevel level,
        ToolTarget target,
        Func<IDiscordGateway, GuardedTarget, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        Resolution resolution = await ResolveAsync(target, cancellationToken).ConfigureAwait(false);
        if (resolution.Error is not null)
        {
            return ToolResult<T>.Failure(resolution.Error);
        }

        GuardedTarget guarded = resolution.Target!;
        ToolError? refusal = (target.Kind == ToolTargetKind.Guild
                ? GuildAllowlistGuard.Check(guarded.GuildId, _options.AllowedGuildIds)
                : GuildAllowlistGuard.CheckResolved(guarded.GuildId, _options.AllowedGuildIds))
            ?? CheckSuppliedGuild(guarded.GuildId, resolution.SuppliedGuildId)
            ?? ReadOnlyModeGuard.Check(level, _options.Mode)
            ?? DestructiveGate.Check(level, _options.AllowDestructive);
        if (refusal is not null)
        {
            return ToolResult<T>.Failure(refusal);
        }

        T value = await operation(_gateway, guarded, cancellationToken).ConfigureAwait(false);
        return ToolResult<T>.Success(value);
    }

    // Every argument is parsed before the gateway is asked anything, so a malformed ID costs no
    // Discord request.
    private async Task<Resolution> ResolveAsync(ToolTarget target, CancellationToken cancellationToken)
    {
        if (target.Kind == ToolTargetKind.Guild)
        {
            ToolResult<ulong> guild = GuildArgument.Resolve(target.GuildId, _options.AllowedGuildIds);
            return guild.IsSuccess
                ? new Resolution(new GuardedTarget(guild.Value, null, null, null), null, null)
                : Resolution.Failed(guild.Error);
        }

        ulong? supplied = null;
        if (target.GuildId is not null)
        {
            ToolResult<ulong> guild = Snowflake.ParseArgument(target.GuildId, GuildArgument.Name);
            if (!guild.IsSuccess)
            {
                return Resolution.Failed(guild.Error);
            }

            supplied = guild.Value;
        }

        return target.Kind switch
        {
            ToolTargetKind.Channel => await ResolveChannelAsync(target.ChannelId, null, supplied, cancellationToken).ConfigureAwait(false),
            ToolTargetKind.Message => await ResolveMessageAsync(target, supplied, cancellationToken).ConfigureAwait(false),
            ToolTargetKind.Role => await ResolveRoleAsync(target.RoleId, supplied, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target.Kind, "Not a defined target kind."),
        };
    }

    private async Task<Resolution> ResolveMessageAsync(ToolTarget target, ulong? supplied, CancellationToken cancellationToken)
    {
        ToolResult<ulong> message = Snowflake.ParseArgument(target.MessageId, "message_id");
        if (!message.IsSuccess)
        {
            return Resolution.Failed(message.Error);
        }

        return await ResolveChannelAsync(target.ChannelId, message.Value, supplied, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Resolution> ResolveChannelAsync(string? channelId, ulong? messageId, ulong? supplied, CancellationToken cancellationToken)
    {
        ToolResult<ulong> channel = Snowflake.ParseArgument(channelId, "channel_id");
        if (!channel.IsSuccess)
        {
            return Resolution.Failed(channel.Error);
        }

        ulong? guildId = await _gateway.GetChannelGuildIdAsync(channel.Value, cancellationToken).ConfigureAwait(false);
        return guildId is ulong resolved
            ? new Resolution(new GuardedTarget(resolved, channel.Value, null, messageId), supplied, null)
            : Resolution.Failed(new ToolError(ErrorCode.NotFound, $"Channel {Snowflake.Format(channel.Value)} was not found."));
    }

    private async Task<Resolution> ResolveRoleAsync(string? roleId, ulong? supplied, CancellationToken cancellationToken)
    {
        ToolResult<ulong> role = Snowflake.ParseArgument(roleId, "role_id");
        if (!role.IsSuccess)
        {
            return Resolution.Failed(role.Error);
        }

        ulong? guildId = await _gateway.GetRoleGuildIdAsync(role.Value, cancellationToken).ConfigureAwait(false);
        return guildId is ulong resolved
            ? new Resolution(new GuardedTarget(resolved, null, role.Value, null), supplied, null)
            : Resolution.Failed(new ToolError(ErrorCode.NotFound, $"Role {Snowflake.Format(role.Value)} was not found."));
    }

    // Runs after the allowlist has checked the resolved guild: a supplied guild_id never decides
    // which guild is checked, it can only make the call fail when it names a different one.
    private static ToolError? CheckSuppliedGuild(ulong resolvedGuildId, ulong? suppliedGuildId) =>
        suppliedGuildId is ulong supplied && supplied != resolvedGuildId
            ? new ToolError(ErrorCode.InvalidArgument,
                $"The ID belongs to guild {Snowflake.Format(resolvedGuildId)}, not to the supplied {GuildArgument.Name} {Snowflake.Format(supplied)}.")
            : null;

    private sealed record Resolution(GuardedTarget? Target, ulong? SuppliedGuildId, ToolError? Error)
    {
        public static Resolution Failed(ToolError error) => new(null, null, error);
    }
}

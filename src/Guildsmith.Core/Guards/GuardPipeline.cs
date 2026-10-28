using Guildsmith.Core.Arguments;
using Guildsmith.Core.Audit;
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
/// model as <c>internal_error</c> with a correlation id, and its detail reaches the log only. And it
/// is the audit point (spec section 7.8): every Write and Destructive call leaves exactly one
/// <see cref="AuditEntry"/> once its outcome is known, so no tool has to remember to write one.
/// </summary>
public sealed class GuardPipeline
{
    private readonly GuildsmithOptions _options;
    private readonly IDiscordGateway _gateway;
    private readonly ILogger _logger;
    private readonly ILogger _auditLogger;

    /// <summary>
    /// A pipeline that logs nothing, neither the detail of an unexpected failure nor the audit
    /// entries; an unexpected failure is still returned as <c>internal_error</c>.
    /// </summary>
    public GuardPipeline(GuildsmithOptions options, IDiscordGateway gateway)
        : this(options, gateway, NullLoggerFactory.Instance)
    {
    }

    /// <summary>
    /// A pipeline that logs the detail of an unexpected failure under the <see cref="GuardPipeline"/>
    /// category, and the audit entries under <see cref="AuditEntry.Category"/>, both through
    /// <paramref name="loggerFactory"/>.
    /// </summary>
    public GuardPipeline(GuildsmithOptions options, IDiscordGateway gateway, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _options = options;
        _gateway = gateway;
        _logger = loggerFactory.CreateLogger<GuardPipeline>();
        _auditLogger = loggerFactory.CreateLogger(AuditEntry.Category);
    }

    /// <summary>
    /// Runs <paramref name="operation"/> when the call passes every guard, and returns the first
    /// refusal otherwise, in which case the operation is never invoked. A call addressed by
    /// <c>guild_id</c> makes no gateway call before the operation; a call addressed by a channel,
    /// role or message ID makes exactly one, to resolve the guild that ID belongs to.
    /// An exception the gateway or the operation throws is logged at <see cref="LogLevel.Error"/> and
    /// returned as <see cref="ErrorCode.InternalError"/>; only an <see cref="OperationCanceledException"/>
    /// thrown while <paramref name="cancellationToken"/> is cancelled propagates.
    /// A <see cref="ToolLevel.Write"/> or <see cref="ToolLevel.Destructive"/> call writes one audit
    /// entry naming <paramref name="toolName"/>: on success, on a refusal, on <c>internal_error</c>,
    /// and as <c>cancelled</c> before that cancellation propagates. A <see cref="ToolLevel.Read"/>
    /// call writes none.
    /// </summary>
    public async Task<ToolResult<T>> RunAsync<T>(
        string toolName,
        ToolLevel level,
        ToolTarget target,
        Func<IDiscordGateway, GuardedTarget, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(operation);

        AuditIds ids = new();
        ToolResult<T> result;
        try
        {
            result = await GuardAndRunAsync(level, target, operation, ids, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // The exception's message can carry anything the gateway saw, the token included, so
            // the model gets a fixed text and an id; the detail goes to the log, which redacts it.
            string correlationId = Guid.NewGuid().ToString("N");
            _logger.LogError(exception, "A tool call failed with an unexpected error, correlation_id {CorrelationId}.", correlationId);
            result = ToolResult<T>.Failure(ToolError.Internal(correlationId));
        }
        catch (OperationCanceledException)
        {
            // Only the caller's own cancellation gets here. A cancelled write may already have
            // reached Discord, so an audit log that dropped it would have a hole.
            Audit(toolName, level, ids, AuditOutcome.Cancelled);
            throw;
        }

        Audit(toolName, level, ids, result.IsSuccess ? AuditOutcome.Success : AuditOutcome.Failed(result.Error.Code));
        return result;
    }

    private void Audit(string toolName, ToolLevel level, AuditIds ids, AuditOutcome outcome)
    {
        if (level == ToolLevel.Read)
        {
            return;
        }

        _auditLogger.LogInformation("{AuditEntry}",
            AuditEntry.Render(toolName, level, ids.GuildId, ids.ChannelId, ids.RoleId, ids.MessageId, outcome));
    }

    private async Task<ToolResult<T>> GuardAndRunAsync<T>(
        ToolLevel level,
        ToolTarget target,
        Func<IDiscordGateway, GuardedTarget, CancellationToken, Task<T>> operation,
        AuditIds ids,
        CancellationToken cancellationToken)
    {
        Resolution resolution = await ResolveAsync(target, ids, cancellationToken).ConfigureAwait(false);
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
    // Discord request. Each ID goes into ids as soon as it parses, so a refusal raised before the
    // resolution completes is still audited with the IDs known by then.
    private async Task<Resolution> ResolveAsync(ToolTarget target, AuditIds ids, CancellationToken cancellationToken)
    {
        if (target.Kind == ToolTargetKind.Guild)
        {
            ToolResult<ulong> guild = GuildArgument.Resolve(target.GuildId, _options.AllowedGuildIds);
            if (!guild.IsSuccess)
            {
                return Resolution.Failed(guild.Error);
            }

            ids.GuildId = guild.Value;
            return new Resolution(new GuardedTarget(guild.Value, null, null, null), null, null);
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
            ids.GuildId = supplied;
        }

        return target.Kind switch
        {
            ToolTargetKind.Channel => await ResolveChannelAsync(target.ChannelId, null, supplied, ids, cancellationToken).ConfigureAwait(false),
            ToolTargetKind.Message => await ResolveMessageAsync(target, supplied, ids, cancellationToken).ConfigureAwait(false),
            ToolTargetKind.Role => await ResolveRoleAsync(target.RoleId, supplied, ids, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target.Kind, "Not a defined target kind."),
        };
    }

    private async Task<Resolution> ResolveMessageAsync(ToolTarget target, ulong? supplied, AuditIds ids, CancellationToken cancellationToken)
    {
        ToolResult<ulong> message = Snowflake.ParseArgument(target.MessageId, "message_id");
        if (!message.IsSuccess)
        {
            return Resolution.Failed(message.Error);
        }

        ids.MessageId = message.Value;
        return await ResolveChannelAsync(target.ChannelId, message.Value, supplied, ids, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Resolution> ResolveChannelAsync(string? channelId, ulong? messageId, ulong? supplied, AuditIds ids, CancellationToken cancellationToken)
    {
        ToolResult<ulong> channel = Snowflake.ParseArgument(channelId, "channel_id");
        if (!channel.IsSuccess)
        {
            return Resolution.Failed(channel.Error);
        }

        ids.ChannelId = channel.Value;
        ulong? guildId = await _gateway.GetChannelGuildIdAsync(channel.Value, cancellationToken).ConfigureAwait(false);
        if (guildId is not ulong resolved)
        {
            return Resolution.Failed(new ToolError(ErrorCode.NotFound, $"Channel {Snowflake.Format(channel.Value)} was not found."));
        }

        ids.GuildId = resolved;
        return new Resolution(new GuardedTarget(resolved, channel.Value, null, messageId), supplied, null);
    }

    private async Task<Resolution> ResolveRoleAsync(string? roleId, ulong? supplied, AuditIds ids, CancellationToken cancellationToken)
    {
        ToolResult<ulong> role = Snowflake.ParseArgument(roleId, "role_id");
        if (!role.IsSuccess)
        {
            return Resolution.Failed(role.Error);
        }

        ids.RoleId = role.Value;
        ulong? guildId = await _gateway.GetRoleGuildIdAsync(role.Value, cancellationToken).ConfigureAwait(false);
        if (guildId is not ulong resolved)
        {
            return Resolution.Failed(new ToolError(ErrorCode.NotFound, $"Role {Snowflake.Format(role.Value)} was not found."));
        }

        ids.GuildId = resolved;
        return new Resolution(new GuardedTarget(resolved, null, role.Value, null), supplied, null);
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

    // The IDs of one call, as far as they parsed. GuildId is the guild a channel or role ID resolved
    // to; until one has, it is the guild_id the caller supplied, when that parsed.
    private sealed class AuditIds
    {
        public ulong? GuildId { get; set; }

        public ulong? ChannelId { get; set; }

        public ulong? RoleId { get; set; }

        public ulong? MessageId { get; set; }
    }
}

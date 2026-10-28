using Guildsmith.Core.Audit;
using Guildsmith.Core.Configuration;
using Guildsmith.Core.Errors;
using Guildsmith.Core.Gateway;
using Guildsmith.Core.Guards;
using Guildsmith.Server;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NSubstitute;
using NSubstitute.Core;

namespace Guildsmith.Tests.Core.Guards;

public sealed class GuardPipelineTests
{
    private const ulong GuildA = 111;
    private const ulong GuildB = 222;
    private const ulong ForeignGuild = 999;

    private const ulong ChannelInA = 5001;
    private const ulong ChannelInForeign = 5009;
    private const ulong UnknownChannel = 5404;
    private const ulong RoleInB = 6002;
    private const ulong RoleInForeign = 6009;
    private const ulong UnknownRole = 6404;
    private const ulong MessageId = 7001;

    private const string LogToken = "FAKE-TOKEN-guildsmith-test-0003";
    private const string AuditedTool = "audited_tool";

    private static readonly HashSet<ulong> BothGuilds = [GuildA, GuildB];

    private readonly IDiscordGateway _gateway = Substitute.For<IDiscordGateway>();
    private readonly CancellationToken _token = new CancellationTokenSource().Token;

    private bool _invoked;
    private IDiscordGateway? _operationGateway;
    private GuardedTarget? _operationTarget;
    private CancellationToken _operationToken;

    public GuardPipelineTests()
    {
        _gateway.GetChannelGuildIdAsync(ChannelInA, Arg.Any<CancellationToken>()).Returns((ulong?)GuildA);
        _gateway.GetChannelGuildIdAsync(ChannelInForeign, Arg.Any<CancellationToken>()).Returns((ulong?)ForeignGuild);
        _gateway.GetChannelGuildIdAsync(UnknownChannel, Arg.Any<CancellationToken>()).Returns((ulong?)null);
        _gateway.GetRoleGuildIdAsync(RoleInB, Arg.Any<CancellationToken>()).Returns((ulong?)GuildB);
        _gateway.GetRoleGuildIdAsync(RoleInForeign, Arg.Any<CancellationToken>()).Returns((ulong?)ForeignGuild);
        _gateway.GetRoleGuildIdAsync(UnknownRole, Arg.Any<CancellationToken>()).Returns((ulong?)null);
    }

    // ---- construction and arguments ----

    [Fact]
    public void Constructor_rejects_null_options()
    {
        Assert.Throws<ArgumentNullException>(() => new GuardPipeline(null!, _gateway));
    }

    [Fact]
    public void Constructor_rejects_a_null_gateway()
    {
        Assert.Throws<ArgumentNullException>(() => new GuardPipeline(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), null!));
    }

    [Fact]
    public async Task RunAsync_rejects_a_null_target()
    {
        GuardPipeline pipeline = new(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), _gateway);

        await Assert.ThrowsAsync<ArgumentNullException>(() => pipeline.RunAsync("test_tool", ToolLevel.Read, null!, Operation));
    }

    [Fact]
    public async Task RunAsync_rejects_a_null_operation()
    {
        GuardPipeline pipeline = new(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), _gateway);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => pipeline.RunAsync<string>("test_tool", ToolLevel.Read, ToolTarget.Guild("111"), null!));
    }

    // ---- passing calls reach the operation, with the gateway ----

    [Fact]
    public async Task RunAsync_hands_the_gateway_and_the_guild_to_the_operation_of_an_allowed_guild_id_call()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Guild("222"));

        Assert.True(result.IsSuccess);
        Assert.Equal("done", result.Value);
        Assert.Same(_gateway, _operationGateway);
        Assert.Equal(new GuardedTarget(GuildB, null, null, null), _operationTarget);
        Assert.Equal(_token, _operationToken);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task RunAsync_resolves_a_missing_guild_id_to_the_only_allowed_guild()
    {
        ToolResult<string> result = await Run(Options(new HashSet<ulong> { GuildA }, DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Guild(null));

        Assert.True(result.IsSuccess);
        Assert.Equal(new GuardedTarget(GuildA, null, null, null), _operationTarget);
    }

    [Fact]
    public async Task RunAsync_hands_the_resolved_guild_and_the_channel_to_the_operation_of_an_allowed_channel_call()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Channel("5001"));

        Assert.True(result.IsSuccess);
        Assert.Equal(new GuardedTarget(GuildA, ChannelInA, null, null), _operationTarget);
        AssertOnlyResolution(nameof(IDiscordGateway.GetChannelGuildIdAsync), ChannelInA);
    }

    [Fact]
    public async Task RunAsync_hands_the_channel_and_the_message_to_the_operation_of_an_allowed_message_call()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Message("5001", "7001"));

        Assert.True(result.IsSuccess);
        Assert.Equal(new GuardedTarget(GuildA, ChannelInA, null, MessageId), _operationTarget);
        AssertOnlyResolution(nameof(IDiscordGateway.GetChannelGuildIdAsync), ChannelInA);
    }

    [Fact]
    public async Task RunAsync_hands_the_resolved_guild_and_the_role_to_the_operation_of_an_allowed_role_call()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, true), ToolLevel.Destructive, ToolTarget.Role("6002"));

        Assert.True(result.IsSuccess);
        Assert.Equal(new GuardedTarget(GuildB, null, RoleInB, null), _operationTarget);
        AssertOnlyResolution(nameof(IDiscordGateway.GetRoleGuildIdAsync), RoleInB);
    }

    [Fact]
    public async Task RunAsync_passes_a_supplied_guild_id_that_matches_the_resolved_guild()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, true), ToolLevel.Destructive, ToolTarget.Role("6002", "222"));

        Assert.True(result.IsSuccess);
        Assert.Equal(new GuardedTarget(GuildB, null, RoleInB, null), _operationTarget);
    }

    // ---- guild_id arguments ----

    [Fact]
    public async Task RunAsync_gives_invalid_argument_with_the_allowed_guilds_for_a_missing_guild_id_among_several()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Guild(null));

        AssertRefused(result, ErrorCode.InvalidArgument);
        Assert.Equal(["111", "222"], result.Error!.ValidValues);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    // ---- guild allowlist ----

    [Fact]
    public async Task RunAsync_refuses_a_foreign_guild_id_with_forbidden_guild_and_no_gateway_call()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Guild("999"));

        AssertRefused(result, ErrorCode.ForbiddenGuild);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task RunAsync_refuses_an_allowlist_bypass_through_a_foreign_guilds_channel_id()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Channel("5009"));

        AssertRefused(result, ErrorCode.ForbiddenGuild);
        AssertOnlyResolution(nameof(IDiscordGateway.GetChannelGuildIdAsync), ChannelInForeign);
    }

    [Fact]
    public async Task RunAsync_checks_the_resolved_guild_not_an_allowed_guild_id_supplied_next_to_a_foreign_channel()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Channel("5009", "111"));

        AssertRefused(result, ErrorCode.ForbiddenGuild);
        AssertOnlyResolution(nameof(IDiscordGateway.GetChannelGuildIdAsync), ChannelInForeign);
    }

    [Fact]
    public async Task RunAsync_refuses_a_message_in_a_foreign_guilds_channel_by_resolving_the_channel()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Message("5009", "7001"));

        AssertRefused(result, ErrorCode.ForbiddenGuild);
        AssertOnlyResolution(nameof(IDiscordGateway.GetChannelGuildIdAsync), ChannelInForeign);
    }

    [Fact]
    public async Task RunAsync_refuses_a_foreign_guilds_role_id_even_with_an_allowed_guild_id_supplied()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, true), ToolLevel.Destructive, ToolTarget.Role("6009", "111"));

        AssertRefused(result, ErrorCode.ForbiddenGuild);
        AssertOnlyResolution(nameof(IDiscordGateway.GetRoleGuildIdAsync), RoleInForeign);
    }

    [Fact]
    public async Task RunAsync_gives_not_found_for_a_channel_the_gateway_cannot_resolve()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Channel("5404"));

        AssertRefused(result, ErrorCode.NotFound);
        AssertOnlyResolution(nameof(IDiscordGateway.GetChannelGuildIdAsync), UnknownChannel);
    }

    [Fact]
    public async Task RunAsync_gives_not_found_for_a_message_whose_channel_the_gateway_cannot_resolve()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Message("5404", "7001"));

        AssertRefused(result, ErrorCode.NotFound);
        AssertOnlyResolution(nameof(IDiscordGateway.GetChannelGuildIdAsync), UnknownChannel);
    }

    [Fact]
    public async Task RunAsync_gives_not_found_for_a_role_the_gateway_cannot_resolve()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Role("6404"));

        AssertRefused(result, ErrorCode.NotFound);
        AssertOnlyResolution(nameof(IDiscordGateway.GetRoleGuildIdAsync), UnknownRole);
    }

    // ---- a supplied guild_id never decides the check ----

    [Fact]
    public async Task RunAsync_gives_invalid_argument_when_the_supplied_guild_id_differs_from_the_resolved_allowed_guild()
    {
        // Both 111 and 222 are allowed; role 6002 belongs to 222.
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, true), ToolLevel.Destructive, ToolTarget.Role("6002", "111"));

        AssertRefused(result, ErrorCode.InvalidArgument);
        AssertOnlyResolution(nameof(IDiscordGateway.GetRoleGuildIdAsync), RoleInB);
    }

    [Fact]
    public async Task RunAsync_gives_invalid_argument_when_a_foreign_guild_id_is_supplied_next_to_an_allowed_channel()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Channel("5001", "999"));

        AssertRefused(result, ErrorCode.InvalidArgument);
        AssertOnlyResolution(nameof(IDiscordGateway.GetChannelGuildIdAsync), ChannelInA);
    }

    // ---- malformed IDs are refused before the gateway is asked ----

    public static TheoryData<ToolTarget> MalformedTargets() => new()
    {
        ToolTarget.Guild("abc"),
        ToolTarget.Channel(null),
        ToolTarget.Channel("-1"),
        ToolTarget.Channel("5001", "0"),
        ToolTarget.Role("1,000"),
        ToolTarget.Role("6002", " 222"),
        ToolTarget.Message("5001", null),
        ToolTarget.Message("5001", "18446744073709551616"),
        ToolTarget.Message("", "7001"),
        ToolTarget.Message("5001", "7001", "x"),
        // An empty guild_id is supplied, not absent: it must be parsed and refused, not skipped
        // so that the ID alone decides the guild.
        ToolTarget.Channel("5001", ""),
        ToolTarget.Role("6002", ""),
        ToolTarget.Message("5001", "7001", ""),
    };

    [Theory]
    [MemberData(nameof(MalformedTargets))]
    public async Task RunAsync_gives_invalid_argument_for_a_malformed_id_and_makes_no_gateway_call(ToolTarget target)
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Read, target);

        AssertRefused(result, ErrorCode.InvalidArgument);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    // ---- ReadOnly mode ----

    [Theory]
    [InlineData(ToolLevel.Write)]
    [InlineData(ToolLevel.Destructive)]
    public async Task RunAsync_refuses_write_and_destructive_in_read_only_mode_with_no_gateway_call(ToolLevel level)
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadOnly, true), level, ToolTarget.Guild("111"));

        AssertRefused(result, ErrorCode.ReadOnlyMode);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task RunAsync_passes_read_in_read_only_mode()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadOnly, false), ToolLevel.Read, ToolTarget.Guild("111"));

        Assert.True(result.IsSuccess);
        Assert.True(_invoked);
    }

    [Fact]
    public async Task RunAsync_refuses_write_in_read_only_mode_after_exactly_the_one_resolution_call()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadOnly, false), ToolLevel.Write, ToolTarget.Channel("5001"));

        AssertRefused(result, ErrorCode.ReadOnlyMode);
        AssertOnlyResolution(nameof(IDiscordGateway.GetChannelGuildIdAsync), ChannelInA);
    }

    // ---- destructive gate ----

    [Fact]
    public async Task RunAsync_refuses_destructive_while_destructive_tools_are_disabled()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Destructive, ToolTarget.Guild("111"));

        AssertRefused(result, ErrorCode.DestructiveDisabled);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task RunAsync_passes_write_while_destructive_tools_are_disabled()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Guild("111"));

        Assert.True(result.IsSuccess);
        Assert.True(_invoked);
    }

    [Fact]
    public async Task RunAsync_passes_destructive_in_read_write_mode_when_destructive_tools_are_allowed()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, true), ToolLevel.Destructive, ToolTarget.Guild("111"));

        Assert.True(result.IsSuccess);
        Assert.True(_invoked);
    }

    [Fact]
    public async Task RunAsync_refuses_destructive_after_exactly_the_one_resolution_call()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Destructive, ToolTarget.Role("6002"));

        AssertRefused(result, ErrorCode.DestructiveDisabled);
        AssertOnlyResolution(nameof(IDiscordGateway.GetRoleGuildIdAsync), RoleInB);
    }

    // ---- the order, pinned pair by pair ----

    [Fact]
    public async Task RunAsync_reports_forbidden_guild_before_read_only_mode()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadOnly, false), ToolLevel.Write, ToolTarget.Guild("999"));

        AssertRefused(result, ErrorCode.ForbiddenGuild);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task RunAsync_reports_forbidden_guild_of_a_resolved_channel_before_read_only_mode()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadOnly, false), ToolLevel.Write, ToolTarget.Channel("5009"));

        AssertRefused(result, ErrorCode.ForbiddenGuild);
        AssertOnlyResolution(nameof(IDiscordGateway.GetChannelGuildIdAsync), ChannelInForeign);
    }

    [Fact]
    public async Task RunAsync_reports_a_supplied_guild_mismatch_before_read_only_mode()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadOnly, false), ToolLevel.Write, ToolTarget.Role("6002", "111"));

        AssertRefused(result, ErrorCode.InvalidArgument);
    }

    [Fact]
    public async Task RunAsync_reports_read_only_mode_before_destructive_disabled()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadOnly, false), ToolLevel.Destructive, ToolTarget.Guild("111"));

        AssertRefused(result, ErrorCode.ReadOnlyMode);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    // ---- forbidden_guild does not name a guild the server resolved ----

    [Fact]
    public async Task RunAsync_does_not_name_the_guild_resolved_from_a_channel_id_in_the_refusal()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Channel("5009"));

        AssertRefused(result, ErrorCode.ForbiddenGuild);
        Assert.DoesNotContain("999", ToolErrorSerializer.Serialize(result.Error!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_does_not_name_the_guild_resolved_from_a_role_id_in_the_refusal()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Role("6009"));

        AssertRefused(result, ErrorCode.ForbiddenGuild);
        Assert.DoesNotContain("999", ToolErrorSerializer.Serialize(result.Error!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_does_not_name_the_guild_resolved_from_a_message_in_the_refusal()
    {
        ToolResult<string> result = await Run(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Message("5009", "7001"));

        AssertRefused(result, ErrorCode.ForbiddenGuild);
        Assert.DoesNotContain("999", ToolErrorSerializer.Serialize(result.Error!), StringComparison.Ordinal);
    }

    // ---- the exception boundary: internal_error, a correlation id, a redacted log ----

    [Fact]
    public void Constructor_rejects_a_null_logger()
    {
        Assert.Throws<ArgumentNullException>(
            () => new GuardPipeline(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), _gateway, null!));
    }

    [Fact]
    public async Task RunAsync_returns_internal_error_and_logs_the_exception_at_error_level_under_the_same_correlation_id()
    {
        StringWriter log = new();

        ToolResult<string> result = await RunLogged(log, ToolTarget.Guild("111"),
            (_, _, _) => throw new InvalidOperationException("operation-marker"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InternalError, result.Error.Code);
        string correlationId = Assert.IsType<string>(result.Error.CorrelationId);
        Assert.Matches("^[0-9a-f]{32}$", correlationId);

        string text = log.ToString();
        Assert.StartsWith("Error: ", text, StringComparison.Ordinal);
        Assert.Contains(correlationId, text, StringComparison.Ordinal);
        Assert.Contains("operation-marker", text, StringComparison.Ordinal);

        // The model reads the fixed text, never the exception's own message.
        Assert.DoesNotContain("operation-marker", ToolErrorSerializer.Serialize(result.Error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_gives_each_internal_error_its_own_correlation_id()
    {
        StringWriter log = new();
        Func<IDiscordGateway, GuardedTarget, CancellationToken, Task<string>> fail = (_, _, _) => throw new InvalidOperationException("x");

        ToolResult<string> first = await RunLogged(log, ToolTarget.Guild("111"), fail);
        ToolResult<string> second = await RunLogged(log, ToolTarget.Guild("111"), fail);

        Assert.NotEqual(first.Error!.CorrelationId, second.Error!.CorrelationId);
    }

    [Fact]
    public async Task RunAsync_keeps_the_token_out_of_the_response_and_the_log_when_the_operation_throws_it()
    {
        StringWriter log = new();

        ToolResult<string> result = await RunLogged(log, ToolTarget.Guild("111"),
            (_, _, _) => throw new InvalidOperationException($"outer-marker {LogToken}"));

        AssertInternalErrorWithoutToken(result, log, "outer-marker");
    }

    [Fact]
    public async Task RunAsync_keeps_the_token_out_of_the_response_and_the_log_when_only_the_inner_exception_holds_it()
    {
        StringWriter log = new();

        ToolResult<string> result = await RunLogged(log, ToolTarget.Guild("111"),
            (_, _, _) => throw new InvalidOperationException("outer", new HttpRequestException($"inner-marker {LogToken}")));

        AssertInternalErrorWithoutToken(result, log, "inner-marker");
    }

    [Fact]
    public async Task RunAsync_keeps_the_token_out_of_the_response_and_the_log_when_the_message_holds_an_authorization_header()
    {
        StringWriter log = new();

        ToolResult<string> result = await RunLogged(log, ToolTarget.Guild("111"),
            (_, _, _) => throw new HttpRequestException($"header-marker Authorization: Bot {LogToken}"));

        AssertInternalErrorWithoutToken(result, log, "header-marker");
    }

    [Fact]
    public async Task RunAsync_returns_internal_error_without_the_token_when_the_channel_resolution_throws_it()
    {
        StringWriter log = new();
        _gateway.GetChannelGuildIdAsync(ChannelInA, Arg.Any<CancellationToken>())
            .Returns<Task<ulong?>>(_ => throw new HttpRequestException($"gateway-marker {LogToken}"));

        ToolResult<string> result = await RunLogged(log, ToolTarget.Channel("5001"), Operation);

        AssertInternalErrorWithoutToken(result, log, "gateway-marker");
        Assert.False(_invoked);
    }

    [Fact]
    public async Task RunAsync_returns_internal_error_without_the_token_when_the_role_resolution_throws_it()
    {
        StringWriter log = new();
        _gateway.GetRoleGuildIdAsync(RoleInB, Arg.Any<CancellationToken>())
            .Returns<Task<ulong?>>(_ => throw new HttpRequestException($"gateway-marker {LogToken}"));

        ToolResult<string> result = await RunLogged(log, ToolTarget.Role("6002"), Operation);

        AssertInternalErrorWithoutToken(result, log, "gateway-marker");
        Assert.False(_invoked);
    }

    [Fact]
    public async Task RunAsync_lets_a_cancellation_of_the_callers_token_propagate()
    {
        StringWriter log = new();
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunLogged(log, ToolTarget.Guild("111"),
            (_, _, token) => throw new OperationCanceledException(token), cancel.Token));

        Assert.Equal("", log.ToString());
    }

    [Fact]
    public async Task RunAsync_returns_internal_error_for_a_cancellation_the_callers_token_did_not_request()
    {
        StringWriter log = new();

        ToolResult<string> result = await RunLogged(log, ToolTarget.Guild("111"),
            (_, _, _) => throw new TaskCanceledException("timeout-marker"));

        Assert.Equal(ErrorCode.InternalError, result.Error!.Code);
        Assert.Contains("timeout-marker", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_returns_internal_error_for_a_non_cancellation_exception_after_the_callers_token_was_cancelled()
    {
        StringWriter log = new();
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        ToolResult<string> result = await RunLogged(log, ToolTarget.Guild("111"),
            (_, _, _) => throw new InvalidOperationException("late-marker"), cancel.Token);

        Assert.Equal(ErrorCode.InternalError, result.Error!.Code);
        Assert.Contains("late-marker", log.ToString(), StringComparison.Ordinal);
    }

    // ---- the audit entry of a Write or Destructive call (spec section 7.8) ----
    //
    // The expected entries are AuditEntry.Render over IDs typed into each test: Render's own exact
    // strings are pinned in AuditEntryTests, and these tests pin what the pipeline hands it.

    [Fact]
    public async Task RunAsync_audits_a_successful_write_call_once_with_the_tool_level_guild_channel_and_message()
    {
        StringWriter log = new();

        ToolResult<string> result = await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Message("5001", "7001"));

        Assert.True(result.IsSuccess);
        Assert.Equal([Entry(ToolLevel.Write, GuildA, ChannelInA, null, MessageId, AuditOutcome.Success)], AuditEntries(log));
    }

    [Fact]
    public async Task RunAsync_audits_a_successful_destructive_call_once_with_the_tool_level_guild_and_role()
    {
        StringWriter log = new();

        ToolResult<string> result = await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, true), ToolLevel.Destructive, ToolTarget.Role("6002"));

        Assert.True(result.IsSuccess);
        Assert.Equal([Entry(ToolLevel.Destructive, GuildB, null, RoleInB, null, AuditOutcome.Success)], AuditEntries(log));
    }

    [Fact]
    public async Task RunAsync_writes_no_audit_entry_for_a_successful_read_call()
    {
        StringWriter log = new();

        ToolResult<string> result = await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Channel("5001"));

        Assert.True(result.IsSuccess);
        Assert.Equal("", log.ToString());
    }

    [Fact]
    public async Task RunAsync_writes_no_audit_entry_for_a_refused_read_call()
    {
        StringWriter log = new();

        ToolResult<string> result = await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Read, ToolTarget.Guild("999"));

        Assert.Equal(ErrorCode.ForbiddenGuild, result.Error!.Code);
        Assert.Equal("", log.ToString());
    }

    [Fact]
    public async Task RunAsync_audits_a_forbidden_supplied_guild_id_with_forbidden_guild()
    {
        StringWriter log = new();

        await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Guild("999"));

        Assert.Equal([Entry(ToolLevel.Write, ForeignGuild, null, null, null, AuditOutcome.Failed(ErrorCode.ForbiddenGuild))], AuditEntries(log));
    }

    [Fact]
    public async Task RunAsync_audits_a_forbidden_resolved_guild_with_forbidden_guild_and_the_resolved_guild()
    {
        StringWriter log = new();

        await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Channel("5009"));

        Assert.Equal([Entry(ToolLevel.Write, ForeignGuild, ChannelInForeign, null, null, AuditOutcome.Failed(ErrorCode.ForbiddenGuild))], AuditEntries(log));
    }

    [Fact]
    public async Task RunAsync_audits_a_write_refused_in_read_only_mode_with_read_only_mode()
    {
        StringWriter log = new();

        await RunAudited(log, AuditOptions(DiscordMcpMode.ReadOnly, false), ToolLevel.Write, ToolTarget.Guild("111"));

        Assert.Equal([Entry(ToolLevel.Write, GuildA, null, null, null, AuditOutcome.Failed(ErrorCode.ReadOnlyMode))], AuditEntries(log));
    }

    [Fact]
    public async Task RunAsync_audits_a_destructive_call_refused_by_the_destructive_gate_with_destructive_disabled()
    {
        StringWriter log = new();

        await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Destructive, ToolTarget.Role("6002"));

        Assert.Equal([Entry(ToolLevel.Destructive, GuildB, null, RoleInB, null, AuditOutcome.Failed(ErrorCode.DestructiveDisabled))], AuditEntries(log));
    }

    [Fact]
    public async Task RunAsync_audits_an_unresolved_channel_with_not_found_and_the_channel_id()
    {
        StringWriter log = new();

        await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Channel("5404"));

        Assert.Equal([Entry(ToolLevel.Write, null, UnknownChannel, null, null, AuditOutcome.Failed(ErrorCode.NotFound))], AuditEntries(log));
    }

    [Fact]
    public async Task RunAsync_audits_an_unresolved_role_with_not_found_and_the_role_id()
    {
        StringWriter log = new();

        await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Role("6404"));

        Assert.Equal([Entry(ToolLevel.Write, null, null, UnknownRole, null, AuditOutcome.Failed(ErrorCode.NotFound))], AuditEntries(log));
    }

    [Fact]
    public async Task RunAsync_audits_an_unresolved_channel_with_the_supplied_guild_id_it_parsed()
    {
        StringWriter log = new();

        await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Channel("5404", "222"));

        Assert.Equal([Entry(ToolLevel.Write, GuildB, UnknownChannel, null, null, AuditOutcome.Failed(ErrorCode.NotFound))], AuditEntries(log));
    }

    [Fact]
    public async Task RunAsync_audits_a_malformed_channel_id_with_invalid_argument_and_only_the_message_id_that_parsed()
    {
        StringWriter log = new();

        await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Message("x", "7001"));

        Assert.Equal([Entry(ToolLevel.Write, null, null, null, MessageId, AuditOutcome.Failed(ErrorCode.InvalidArgument))], AuditEntries(log));
    }

    [Fact]
    public async Task RunAsync_audits_an_empty_supplied_guild_id_with_invalid_argument()
    {
        StringWriter log = new();

        await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Channel("5001", ""));

        Assert.Equal([Entry(ToolLevel.Write, null, null, null, null, AuditOutcome.Failed(ErrorCode.InvalidArgument))], AuditEntries(log));
    }

    [Fact]
    public async Task RunAsync_audits_a_supplied_guild_id_that_differs_from_the_resolved_guild_with_invalid_argument_and_the_resolved_guild()
    {
        StringWriter log = new();

        await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Channel("5001", "222"));

        Assert.Equal([Entry(ToolLevel.Write, GuildA, ChannelInA, null, null, AuditOutcome.Failed(ErrorCode.InvalidArgument))], AuditEntries(log));
    }

    [Fact]
    public async Task RunAsync_audits_an_internal_error_once_next_to_the_error_level_detail_under_its_own_category()
    {
        StringWriter log = new();

        ToolResult<string> result = await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Guild("111"),
            (_, _, _) => throw new InvalidOperationException("detail-marker"));

        Assert.Equal(ErrorCode.InternalError, result.Error!.Code);
        Assert.Equal([Entry(ToolLevel.Write, GuildA, null, null, null, AuditOutcome.Failed(ErrorCode.InternalError))], AuditEntries(log));

        // The detail stays on the GuardPipeline category, at Error, and the audit entry does not carry it.
        string text = log.ToString();
        Assert.StartsWith("Error: Guildsmith.Core.Guards.GuardPipeline[", text, StringComparison.Ordinal);
        Assert.Contains("detail-marker", text, StringComparison.Ordinal);
        Assert.DoesNotContain("detail-marker", Assert.Single(AuditEntries(log)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_audits_an_internal_error_of_the_channel_resolution_with_the_channel_id()
    {
        StringWriter log = new();
        _gateway.GetChannelGuildIdAsync(ChannelInA, Arg.Any<CancellationToken>())
            .Returns<Task<ulong?>>(_ => throw new HttpRequestException("gateway-marker"));

        await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Channel("5001"));

        Assert.Equal([Entry(ToolLevel.Write, null, ChannelInA, null, null, AuditOutcome.Failed(ErrorCode.InternalError))], AuditEntries(log));
    }

    [Fact]
    public async Task RunAsync_audits_a_cancelled_write_as_cancelled_and_lets_the_cancellation_propagate()
    {
        StringWriter log = new();
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write,
            ToolTarget.Guild("111"), (_, _, token) => throw new OperationCanceledException(token), cancel.Token));

        Assert.Equal([Entry(ToolLevel.Write, GuildA, null, null, null, AuditOutcome.Cancelled)], AuditEntries(log));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RunAsync_rejects_a_null_empty_or_blank_tool_name_before_the_operation_runs(string? toolName)
    {
        GuardPipeline pipeline = new(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), _gateway);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => pipeline.RunAsync(toolName!, ToolLevel.Read, ToolTarget.Guild("111"), Operation, _token));

        Assert.False(_invoked);
    }

    // ---- what never reaches the audit entry ----

    [Fact]
    public async Task RunAsync_keeps_a_malformed_channel_ids_text_out_of_the_audit_entry()
    {
        StringWriter log = new();

        await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Channel("AUDIT-MARKER-channel"));

        Assert.Single(AuditEntries(log));
        Assert.DoesNotContain("AUDIT-MARKER-channel", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_keeps_the_operations_return_value_out_of_the_audit_entry()
    {
        StringWriter log = new();

        await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Guild("111"),
            (_, _, _) => Task.FromResult("AUDIT-MARKER-result"));

        Assert.Single(AuditEntries(log));
        Assert.DoesNotContain("AUDIT-MARKER-result", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_keeps_the_token_out_of_the_audit_entry_when_the_operation_throws_it()
    {
        StringWriter log = new();

        await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false), ToolLevel.Write, ToolTarget.Guild("111"),
            (_, _, _) => throw new InvalidOperationException($"token-marker {LogToken}"));

        Assert.DoesNotContain(LogToken, Assert.Single(AuditEntries(log)), StringComparison.Ordinal);
        Assert.DoesNotContain(LogToken, log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_writes_the_audit_entry_redacted_to_stderr_at_log_level_error_and_nothing_to_stdout()
    {
        StringWriter log = new();
        StringWriter stdout = new();
        TextWriter original = Console.Out;
        Console.SetOut(stdout);
        try
        {
            // The token inside the tool name is the one way to put it into an audit entry, so its
            // redaction shows the entry went through the redacting provider.
            await RunAudited(log, AuditOptions(DiscordMcpMode.ReadWrite, false, LogLevel.Error), ToolLevel.Write, ToolTarget.Guild("111"),
                Operation, _token, $"tool_{LogToken}");
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal([AuditEntry.Render("tool_[REDACTED]", ToolLevel.Write, GuildA, null, null, null, AuditOutcome.Success)], AuditEntries(log));
        Assert.DoesNotContain(LogToken, log.ToString(), StringComparison.Ordinal);
        Assert.Equal("", stdout.ToString());
    }

    // ---- helpers ----

    // The real logging pipeline ServerStartup.ConfigureServices builds, writing to log, around a
    // pipeline that audits through it.
    private async Task<ToolResult<string>> RunAudited(
        StringWriter log,
        GuildsmithOptions options,
        ToolLevel level,
        ToolTarget target,
        Func<IDiscordGateway, GuardedTarget, CancellationToken, Task<string>>? operation = null,
        CancellationToken cancellationToken = default,
        string toolName = AuditedTool)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        ServerStartup.ConfigureServices(builder, options, log);
        using IHost host = builder.Build();

        GuardPipeline pipeline = new(options, _gateway, host.Services.GetRequiredService<ILoggerFactory>());
        return await pipeline.RunAsync(toolName, level, target, operation ?? Operation, cancellationToken);
    }

    private static GuildsmithOptions AuditOptions(DiscordMcpMode mode, bool allowDestructive, LogLevel logLevel = LogLevel.Information) => new()
    {
        DiscordToken = LogToken,
        AllowedGuildIds = BothGuilds,
        Mode = mode,
        AllowDestructive = allowDestructive,
        AllowAdminGrant = false,
        MaxMessagesPerRead = 50,
        Transport = McpTransport.Stdio,
        LogLevel = logLevel,
    };

    private static string Entry(ToolLevel level, ulong? guildId, ulong? channelId, ulong? roleId, ulong? messageId, AuditOutcome outcome) =>
        AuditEntry.Render(AuditedTool, level, guildId, channelId, roleId, messageId, outcome);

    // The message of every line RedactingLoggerProvider wrote at Information under the audit
    // category, in order. The header and the six-space indent are that provider's line format;
    // the category is typed in, not read from AuditEntry.Category.
    private static List<string> AuditEntries(StringWriter log)
    {
        string[] lines = log.ToString().Split('\n');
        List<string> entries = [];
        for (int i = 0; i + 1 < lines.Length; i++)
        {
            if (lines[i].StartsWith("Information: Guildsmith.Audit[", StringComparison.Ordinal))
            {
                entries.Add(lines[i + 1]["      ".Length..]);
            }
        }

        return entries;
    }

    // The real logging pipeline ServerStartup.ConfigureServices builds, writing to log.
    private async Task<ToolResult<string>> RunLogged(
        StringWriter log,
        ToolTarget target,
        Func<IDiscordGateway, GuardedTarget, CancellationToken, Task<string>> operation,
        CancellationToken cancellationToken = default)
    {
        GuildsmithOptions options = Options(BothGuilds, DiscordMcpMode.ReadWrite, false, LogToken);
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        ServerStartup.ConfigureServices(builder, options, log);
        using IHost host = builder.Build();

        GuardPipeline pipeline = new(options, _gateway, host.Services.GetRequiredService<ILoggerFactory>());
        return await pipeline.RunAsync("test_tool", ToolLevel.Read, target, operation, cancellationToken);
    }

    private static void AssertInternalErrorWithoutToken(ToolResult<string> result, StringWriter log, string markerWord)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InternalError, result.Error.Code);
        Assert.DoesNotContain(LogToken, ToolErrorSerializer.Serialize(result.Error), StringComparison.Ordinal);

        string text = log.ToString();
        Assert.Contains(markerWord, text, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", text, StringComparison.Ordinal);
        Assert.DoesNotContain(LogToken, text, StringComparison.Ordinal);
    }

    private static GuildsmithOptions Options(IReadOnlySet<ulong> allowedGuildIds, DiscordMcpMode mode, bool allowDestructive) =>
        Options(allowedGuildIds, mode, allowDestructive, "FAKE-TOKEN-guildsmith-test-0002");

    private static GuildsmithOptions Options(IReadOnlySet<ulong> allowedGuildIds, DiscordMcpMode mode, bool allowDestructive, string token) => new()
    {
        DiscordToken = token,
        AllowedGuildIds = allowedGuildIds,
        Mode = mode,
        AllowDestructive = allowDestructive,
        AllowAdminGrant = false,
        MaxMessagesPerRead = 50,
        Transport = McpTransport.Stdio,
        LogLevel = LogLevel.Information,
    };

    private Task<ToolResult<string>> Run(GuildsmithOptions options, ToolLevel level, ToolTarget target) =>
        new GuardPipeline(options, _gateway).RunAsync("test_tool", level, target, Operation, _token);

    private Task<string> Operation(IDiscordGateway gateway, GuardedTarget target, CancellationToken cancellationToken)
    {
        _invoked = true;
        _operationGateway = gateway;
        _operationTarget = target;
        _operationToken = cancellationToken;
        return Task.FromResult("done");
    }

    private void AssertRefused(ToolResult<string> result, ErrorCode expected)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Error.Code);
        Assert.False(_invoked);
    }

    // Exactly one gateway call, and it is the resolution of this ID with the caller's token.
    private void AssertOnlyResolution(string method, ulong id)
    {
        ICall call = Assert.Single(_gateway.ReceivedCalls());
        Assert.Equal(method, call.GetMethodInfo().Name);
        Assert.Equal(new object?[] { id, _token }, call.GetArguments());
    }
}

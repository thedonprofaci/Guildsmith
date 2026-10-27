using Guildsmith.Core.Configuration;
using Guildsmith.Core.Errors;
using Guildsmith.Core.Gateway;
using Guildsmith.Core.Guards;

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

        await Assert.ThrowsAsync<ArgumentNullException>(() => pipeline.RunAsync(ToolLevel.Read, null!, Operation));
    }

    [Fact]
    public async Task RunAsync_rejects_a_null_operation()
    {
        GuardPipeline pipeline = new(Options(BothGuilds, DiscordMcpMode.ReadWrite, false), _gateway);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => pipeline.RunAsync<string>(ToolLevel.Read, ToolTarget.Guild("111"), null!));
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

    // ---- helpers ----

    private static GuildsmithOptions Options(IReadOnlySet<ulong> allowedGuildIds, DiscordMcpMode mode, bool allowDestructive) => new()
    {
        DiscordToken = "FAKE-TOKEN-guildsmith-test-0002",
        AllowedGuildIds = allowedGuildIds,
        Mode = mode,
        AllowDestructive = allowDestructive,
        AllowAdminGrant = false,
        MaxMessagesPerRead = 50,
        Transport = McpTransport.Stdio,
        LogLevel = LogLevel.Information,
    };

    private Task<ToolResult<string>> Run(GuildsmithOptions options, ToolLevel level, ToolTarget target) =>
        new GuardPipeline(options, _gateway).RunAsync(level, target, Operation, _token);

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

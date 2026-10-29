using Guildsmith.Core.Configuration;
using Guildsmith.Core.Errors;
using Guildsmith.Core.Gateway;
using Guildsmith.Core.Guards;
using Guildsmith.Core.Models;
using Guildsmith.Core.Services;

using Microsoft.Extensions.Logging;

using NSubstitute;

namespace Guildsmith.Tests.Core.Services;

/// <summary>The service over the real <see cref="GuardPipeline"/>, with the gateway mocked.</summary>
public sealed class GuildServiceTests
{
    private readonly IDiscordGateway _gateway = Substitute.For<IDiscordGateway>();
    private readonly CancellationToken _token = new CancellationTokenSource().Token;

    [Fact]
    public void Constructor_rejects_a_null_pipeline()
    {
        Assert.Throws<ArgumentNullException>(() => new GuildService(null!, Options([1])));
    }

    [Fact]
    public void Constructor_rejects_null_options()
    {
        Assert.Throws<ArgumentNullException>(() => new GuildService(new GuardPipeline(Options([1]), _gateway), null!));
    }

    [Fact]
    public async Task ListGuildsAsync_leaves_out_a_joined_guild_that_is_not_allowed()
    {
        Joined(new GuildSummary(111, "Allowed"), new GuildSummary(999, "Joined only"));

        ToolResult<IReadOnlyList<GuildSummary>> result = await Service([111]).ListGuildsAsync("list_guilds", ToolLevel.Read, _token);

        Assert.Equal([new GuildSummary(111, "Allowed")], result.Value);
    }

    [Fact]
    public async Task ListGuildsAsync_leaves_out_an_allowed_guild_the_bot_has_not_joined()
    {
        Joined(new GuildSummary(111, "Joined"));

        ToolResult<IReadOnlyList<GuildSummary>> result = await Service([111, 333]).ListGuildsAsync("list_guilds", ToolLevel.Read, _token);

        Assert.Equal([new GuildSummary(111, "Joined")], result.Value);
    }

    [Fact]
    public async Task ListGuildsAsync_returns_every_guild_that_is_both_allowed_and_joined()
    {
        Joined(new GuildSummary(111, "First"), new GuildSummary(222, "Second"));

        ToolResult<IReadOnlyList<GuildSummary>> result = await Service([111, 222]).ListGuildsAsync("list_guilds", ToolLevel.Read, _token);

        Assert.Equal([new GuildSummary(111, "First"), new GuildSummary(222, "Second")], result.Value);
    }

    [Fact]
    public async Task ListGuildsAsync_orders_the_guilds_by_ascending_numeric_id()
    {
        // 10 after 9: a text sort would put "10" first, and the gateway's own order is reversed.
        Joined(new GuildSummary(10, "Ten"), new GuildSummary(9, "Nine"));

        ToolResult<IReadOnlyList<GuildSummary>> result = await Service([9, 10]).ListGuildsAsync("list_guilds", ToolLevel.Read, _token);

        Assert.Equal([new GuildSummary(9, "Nine"), new GuildSummary(10, "Ten")], result.Value);
    }

    [Fact]
    public async Task ListGuildsAsync_hands_the_callers_token_to_the_gateway()
    {
        Joined();

        await Service([111]).ListGuildsAsync("list_guilds", ToolLevel.Read, _token);

        await _gateway.Received(1).GetGuildsAsync(_token);
    }

    [Fact]
    public async Task ListGuildsAsync_hands_the_tool_level_to_the_pipeline()
    {
        Joined(new GuildSummary(111, "Allowed"));

        ToolResult<IReadOnlyList<GuildSummary>> result =
            await Service([111], DiscordMcpMode.ReadOnly).ListGuildsAsync("list_guilds", ToolLevel.Write, _token);

        Assert.Equal(ErrorCode.ReadOnlyMode, result.Error!.Code);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task GetGuildInfoAsync_returns_the_gateways_overview_of_an_allowed_guild()
    {
        GuildInfo info = new(222, "Guild", 42, 7, 3);
        _gateway.GetGuildInfoAsync(222, _token).Returns(info);

        ToolResult<GuildInfo> result = await Service([111, 222]).GetGuildInfoAsync("get_guild_info", ToolLevel.Read, "222", _token);

        Assert.Same(info, result.Value);
    }

    [Fact]
    public async Task GetGuildInfoAsync_uses_the_only_allowed_guild_when_guild_id_is_omitted()
    {
        GuildInfo info = new(111, "Only", 1, 1, 1);
        _gateway.GetGuildInfoAsync(111, _token).Returns(info);

        ToolResult<GuildInfo> result = await Service([111]).GetGuildInfoAsync("get_guild_info", ToolLevel.Read, null, _token);

        Assert.Same(info, result.Value);
    }

    [Fact]
    public async Task GetGuildInfoAsync_refuses_a_guild_that_is_not_allowed_with_forbidden_guild_and_no_gateway_call()
    {
        ToolResult<GuildInfo> result = await Service([111]).GetGuildInfoAsync("get_guild_info", ToolLevel.Read, "999", _token);

        Assert.Equal(ErrorCode.ForbiddenGuild, result.Error!.Code);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task GetGuildInfoAsync_hands_the_tool_level_to_the_pipeline()
    {
        ToolResult<GuildInfo> result =
            await Service([111], DiscordMcpMode.ReadOnly).GetGuildInfoAsync("get_guild_info", ToolLevel.Write, "111", _token);

        Assert.Equal(ErrorCode.ReadOnlyMode, result.Error!.Code);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    private void Joined(params GuildSummary[] guilds) =>
        _gateway.GetGuildsAsync(Arg.Any<CancellationToken>()).Returns(guilds);

    private GuildService Service(ulong[] allowed, DiscordMcpMode mode = DiscordMcpMode.ReadWrite)
    {
        GuildsmithOptions options = Options(allowed, mode);
        return new GuildService(new GuardPipeline(options, _gateway), options);
    }

    private static GuildsmithOptions Options(ulong[] allowed, DiscordMcpMode mode = DiscordMcpMode.ReadWrite) => new()
    {
        DiscordToken = "FAKE-TOKEN-guildsmith-test-0004",
        AllowedGuildIds = allowed.ToHashSet(),
        Mode = mode,
        AllowDestructive = false,
        AllowAdminGrant = false,
        MaxMessagesPerRead = 50,
        Transport = McpTransport.Stdio,
        LogLevel = LogLevel.Information,
    };
}

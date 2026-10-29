using System.Net;
using System.Text.Json;

using Discord;
using Discord.Net;

using Guildsmith.Core.Configuration;
using Guildsmith.Core.Guards;
using Guildsmith.Core.Services;
using Guildsmith.Discord;
using Guildsmith.Server.Tools;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;

using NSubstitute;

namespace Guildsmith.Tests.Server.Tools;

/// <summary>
/// The tool over every real layer below it (service, <see cref="GuardPipeline"/>, <see cref="DiscordGateway"/>),
/// with only Discord.Net's side, <see cref="IDiscordRestApi"/>, substituted.
/// </summary>
public sealed class GetGuildInfoToolTests
{
    private readonly IDiscordRestApi _api = Substitute.For<IDiscordRestApi>();

    [Fact]
    public void Constructor_rejects_a_null_service()
    {
        Assert.Throws<ArgumentNullException>(() => new GetGuildInfoTool(null!));
    }

    [Fact]
    public async Task GetGuildInfoAsync_returns_the_exact_json_of_the_guilds_overview()
    {
        Guild(222, "Beta", memberCount: 42, channelCount: 7, roleCount: 3);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite, 111, 222).GetGuildInfoAsync(CancellationToken.None, "222");

        Assert.False(result.IsError);
        Assert.Equal("""{"id":"222","name":"Beta","member_count":42,"channel_count":7,"role_count":3}""", Text(result));
    }

    [Fact]
    public async Task GetGuildInfoAsync_uses_the_only_allowed_guild_when_guild_id_is_omitted()
    {
        Guild(111, "Alpha", memberCount: 1, channelCount: 2, roleCount: 3);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite, 111).GetGuildInfoAsync(CancellationToken.None);

        Assert.Equal("""{"id":"111","name":"Alpha","member_count":1,"channel_count":2,"role_count":3}""", Text(result));
    }

    [Fact]
    public async Task GetGuildInfoAsync_refuses_a_guild_that_is_not_allowed_with_forbidden_guild_and_no_discord_call()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite, 111).GetGuildInfoAsync(CancellationToken.None, "999");

        Assert.True(result.IsError);
        Assert.Equal("forbidden_guild", ErrorCodeOf(result));
        Assert.Empty(_api.ReceivedCalls());
    }

    [Fact]
    public async Task GetGuildInfoAsync_returns_not_found_for_a_discord_404_rather_than_internal_error()
    {
        _api.GetGuildAsync(111, Arg.Any<RequestOptions>())
            .Returns<Task<IGuild?>>(_ => throw new HttpException(HttpStatusCode.NotFound, Substitute.For<IRequest>()));

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite, 111).GetGuildInfoAsync(CancellationToken.None, "111");

        Assert.True(result.IsError);
        Assert.Equal("not_found", ErrorCodeOf(result));
    }

    [Fact]
    public async Task GetGuildInfoAsync_succeeds_in_read_only_mode()
    {
        // Pins the level the tool hands the pipeline: Write or Destructive is refused here.
        Guild(111, "Alpha", memberCount: 1, channelCount: 2, roleCount: 3);

        CallToolResult result = await Tool(DiscordMcpMode.ReadOnly, 111).GetGuildInfoAsync(CancellationToken.None, "111");

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task GetGuildInfoAsync_hands_the_callers_token_down_to_the_discord_request()
    {
        using CancellationTokenSource cancel = new();
        Guild(111, "Alpha", memberCount: 1, channelCount: 2, roleCount: 3);

        await Tool(DiscordMcpMode.ReadWrite, 111).GetGuildInfoAsync(cancel.Token, "111");

        await _api.Received(1).GetGuildAsync(111, Arg.Is<RequestOptions>(options => options.CancelToken == cancel.Token));
    }

    private GetGuildInfoTool Tool(DiscordMcpMode mode, params ulong[] allowed)
    {
        GuildsmithOptions options = new()
        {
            DiscordToken = "FAKE-TOKEN-guildsmith-test-0007",
            AllowedGuildIds = allowed.ToHashSet(),
            Mode = mode,
            AllowDestructive = false,
            AllowAdminGrant = false,
            MaxMessagesPerRead = 50,
            Transport = McpTransport.Stdio,
            LogLevel = LogLevel.Information,
        };
        return new GetGuildInfoTool(new GuildService(new GuardPipeline(options, new DiscordGateway(_api)), options));
    }

    private void Guild(ulong id, string name, int memberCount, int channelCount, int roleCount)
    {
        IGuild guild = Substitute.For<IGuild>();
        guild.Id.Returns(id);
        guild.Name.Returns(name);
        guild.ApproximateMemberCount.Returns(memberCount);
        guild.Roles.Returns(Enumerable.Range(0, roleCount).Select(_ => Substitute.For<IRole>()).ToArray());
        guild.GetChannelsAsync(Arg.Any<CacheMode>(), Arg.Any<RequestOptions>())
            .Returns(Enumerable.Range(0, channelCount).Select(_ => Substitute.For<IGuildChannel>()).ToArray());
        _api.GetGuildAsync(id, Arg.Any<RequestOptions>()).Returns(guild);
    }

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static string? ErrorCodeOf(CallToolResult result)
    {
        using JsonDocument error = JsonDocument.Parse(Text(result));
        return error.RootElement.GetProperty("error").GetString();
    }
}

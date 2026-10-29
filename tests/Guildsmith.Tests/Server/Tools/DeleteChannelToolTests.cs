using System.Text.Json;

using Discord;

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
/// with only Discord.Net's side, <see cref="IDiscordRestApi"/>, substituted. Every refusal here comes
/// after the pipeline resolved the channel's guild (one GET); "no write request" means no
/// <c>DeleteAsync</c> reached the channel.
/// </summary>
public sealed class DeleteChannelToolTests
{
    private readonly IDiscordRestApi _api = Substitute.For<IDiscordRestApi>();
    private readonly ITextChannel _text;

    public DeleteChannelToolTests()
    {
        _text = Channel(5001, guildId: 111);
    }

    [Fact]
    public void Constructor_rejects_a_null_service()
    {
        Assert.Throws<ArgumentNullException>(() => new DeleteChannelTool(null!));
    }

    [Fact]
    public async Task DeleteChannelAsync_deletes_the_channel_and_returns_the_exact_json_naming_it()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite, allowDestructive: true).DeleteChannelAsync(CancellationToken.None, "5001");

        Assert.False(result.IsError);
        Assert.Equal("""{"id":"5001","deleted":true}""", Text(result));
        Assert.Single(Deletions(_text));
    }

    [Fact]
    public async Task DeleteChannelAsync_is_refused_by_the_closed_destructive_gate_with_no_write_request()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite, allowDestructive: false).DeleteChannelAsync(CancellationToken.None, "5001");

        Assert.Equal("destructive_disabled", ErrorCodeOf(result));
        Assert.Empty(Deletions(_text));
    }

    [Fact]
    public async Task DeleteChannelAsync_is_refused_in_read_only_mode_with_no_write_request()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadOnly, allowDestructive: true).DeleteChannelAsync(CancellationToken.None, "5001");

        Assert.Equal("read_only_mode", ErrorCodeOf(result));
        Assert.Empty(Deletions(_text));
    }

    [Fact]
    public async Task DeleteChannelAsync_refuses_a_channel_of_a_guild_that_is_not_allowed_with_forbidden_guild_and_no_write_request()
    {
        ITextChannel foreign = Channel(5009, guildId: 999);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite, allowDestructive: true).DeleteChannelAsync(CancellationToken.None, "5009");

        Assert.Equal("forbidden_guild", ErrorCodeOf(result));
        Assert.Empty(Deletions(foreign));
    }

    [Fact]
    public async Task DeleteChannelAsync_returns_not_found_for_a_missing_channel()
    {
        _api.GetChannelAsync(5404, Arg.Any<RequestOptions>()).Returns((IChannel?)null);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite, allowDestructive: true).DeleteChannelAsync(CancellationToken.None, "5404");

        Assert.Equal("not_found", ErrorCodeOf(result));
    }

    [Fact]
    public async Task DeleteChannelAsync_hands_the_callers_token_down_to_the_delete_request()
    {
        using CancellationTokenSource cancel = new();

        await Tool(DiscordMcpMode.ReadWrite, allowDestructive: true).DeleteChannelAsync(cancel.Token, "5001");

        await _text.Received(1).DeleteAsync(
            Arg.Is<RequestOptions>(options => options.CancelToken == cancel.Token && options.AuditLogReason == "via Discord MCP"));
    }

    private DeleteChannelTool Tool(DiscordMcpMode mode, bool allowDestructive)
    {
        GuildsmithOptions options = new()
        {
            DiscordToken = "FAKE-TOKEN-guildsmith-test-0013",
            AllowedGuildIds = new HashSet<ulong> { 111, 222 },
            Mode = mode,
            AllowDestructive = allowDestructive,
            AllowAdminGrant = false,
            MaxMessagesPerRead = 50,
            Transport = McpTransport.Stdio,
            LogLevel = LogLevel.Information,
        };
        return new DeleteChannelTool(new ChannelService(new GuardPipeline(options, new DiscordGateway(_api))));
    }

    private static IEnumerable<NSubstitute.Core.ICall> Deletions(IGuildChannel channel) =>
        channel.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IDeletable.DeleteAsync));

    private ITextChannel Channel(ulong id, ulong guildId)
    {
        ITextChannel channel = Substitute.For<ITextChannel>();
        channel.Id.Returns(id);
        channel.GuildId.Returns(guildId);
        channel.Name.Returns("channel");
        _api.GetChannelAsync(id, Arg.Any<RequestOptions>()).Returns(channel);
        return channel;
    }

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static string? ErrorCodeOf(CallToolResult result)
    {
        Assert.True(result.IsError);
        using JsonDocument error = JsonDocument.Parse(Text(result));
        return error.RootElement.GetProperty("error").GetString();
    }
}

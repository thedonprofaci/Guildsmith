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
/// with only Discord.Net's side, <see cref="IDiscordRestApi"/>, substituted.
/// </summary>
public sealed class ListChannelsToolTests
{
    private readonly IDiscordRestApi _api = Substitute.For<IDiscordRestApi>();

    [Fact]
    public void Constructor_rejects_a_null_service()
    {
        Assert.Throws<ArgumentNullException>(() => new ListChannelsTool(null!));
    }

    [Fact]
    public async Task ListChannelsAsync_returns_the_exact_json_of_the_channels_in_ascending_id_order()
    {
        Guild(111,
            Nested<ITextChannel>(5002, 111, "general", 900, 1),
            Category(900, 111, "Projektek", 0),
            Nested<IVoiceChannel>(5001, 111, "lounge", 900, 2));

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).ListChannelsAsync(CancellationToken.None, "111");

        Assert.False(result.IsError);
        Assert.Equal(
            """[{"id":"900","name":"Projektek","type":"category","parent_id":null,"position":0},"""
            + """{"id":"5001","name":"lounge","type":"voice","parent_id":"900","position":2},"""
            + """{"id":"5002","name":"general","type":"text","parent_id":"900","position":1}]""",
            Text(result));
    }

    [Fact]
    public async Task ListChannelsAsync_leaves_out_a_channel_whose_type_is_not_one_of_the_five()
    {
        Guild(111, Nested<IStageChannel>(5003, 111, "stage", null, 0), Nested<INewsChannel>(5004, 111, "news", null, 1));

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).ListChannelsAsync(CancellationToken.None, "111");

        Assert.Equal("""[{"id":"5004","name":"news","type":"announcement","parent_id":null,"position":1}]""", Text(result));
    }

    [Fact]
    public async Task ListChannelsAsync_returns_only_the_channels_of_the_given_type()
    {
        Guild(111, Nested<ITextChannel>(5002, 111, "general", null, 0), Nested<IForumChannel>(5005, 111, "ideas", null, 1));

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).ListChannelsAsync(CancellationToken.None, "111", "forum");

        Assert.Equal("""[{"id":"5005","name":"ideas","type":"forum","parent_id":null,"position":1}]""", Text(result));
    }

    [Fact]
    public async Task ListChannelsAsync_refuses_an_unknown_type_with_invalid_argument_listing_the_five_and_no_discord_call()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).ListChannelsAsync(CancellationToken.None, "111", "thread");

        Assert.True(result.IsError);
        using JsonDocument error = JsonDocument.Parse(Text(result));
        Assert.Equal("invalid_argument", error.RootElement.GetProperty("error").GetString());
        Assert.Equal(
            ["text", "voice", "category", "announcement", "forum"],
            error.RootElement.GetProperty("valid_values").EnumerateArray().Select(value => value.GetString()));
        Assert.Empty(_api.ReceivedCalls());
    }

    [Fact]
    public async Task ListChannelsAsync_uses_the_only_allowed_guild_when_guild_id_is_omitted()
    {
        Guild(111, Nested<ITextChannel>(5002, 111, "general", null, 0));

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite, 111).ListChannelsAsync(CancellationToken.None);

        Assert.Equal("""[{"id":"5002","name":"general","type":"text","parent_id":null,"position":0}]""", Text(result));
    }

    [Fact]
    public async Task ListChannelsAsync_succeeds_in_read_only_mode()
    {
        // Pins the level the tool hands the pipeline: Write or Destructive is refused here.
        Guild(111);

        CallToolResult result = await Tool(DiscordMcpMode.ReadOnly).ListChannelsAsync(CancellationToken.None, "111");

        Assert.False(result.IsError);
        Assert.Equal("[]", Text(result));
    }

    [Fact]
    public async Task ListChannelsAsync_hands_the_callers_token_down_to_the_discord_requests()
    {
        using CancellationTokenSource cancel = new();
        IGuild guild = Guild(111);

        await Tool(DiscordMcpMode.ReadWrite).ListChannelsAsync(cancel.Token, "111");

        await _api.Received(1).GetGuildAsync(111, Arg.Is<RequestOptions>(options => options.CancelToken == cancel.Token));
        await guild.Received(1).GetChannelsAsync(CacheMode.AllowDownload, Arg.Is<RequestOptions>(options => options.CancelToken == cancel.Token));
    }

    private ListChannelsTool Tool(DiscordMcpMode mode, params ulong[] allowed)
    {
        GuildsmithOptions options = new()
        {
            DiscordToken = "FAKE-TOKEN-guildsmith-test-0010",
            AllowedGuildIds = (allowed.Length == 0 ? [111, 222] : allowed).ToHashSet(),
            Mode = mode,
            AllowDestructive = false,
            AllowAdminGrant = false,
            MaxMessagesPerRead = 50,
            Transport = McpTransport.Stdio,
            LogLevel = LogLevel.Information,
        };
        return new ListChannelsTool(new ChannelService(new GuardPipeline(options, new DiscordGateway(_api))));
    }

    private IGuild Guild(ulong id, params IGuildChannel[] channels)
    {
        IGuild guild = Substitute.For<IGuild>();
        guild.Id.Returns(id);
        guild.GetChannelsAsync(Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(channels);
        _api.GetGuildAsync(id, Arg.Any<RequestOptions>()).Returns(guild);
        return guild;
    }

    private static T Nested<T>(ulong id, ulong guildId, string name, ulong? parentId, int position)
        where T : class, INestedChannel
    {
        T channel = Substitute.For<T>();
        channel.Id.Returns(id);
        channel.GuildId.Returns(guildId);
        channel.Name.Returns(name);
        channel.Position.Returns(position);
        channel.CategoryId.Returns(parentId);
        return channel;
    }

    private static ICategoryChannel Category(ulong id, ulong guildId, string name, int position)
    {
        ICategoryChannel category = Substitute.For<ICategoryChannel>();
        category.Id.Returns(id);
        category.GuildId.Returns(guildId);
        category.Name.Returns(name);
        category.Position.Returns(position);
        return category;
    }

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
}

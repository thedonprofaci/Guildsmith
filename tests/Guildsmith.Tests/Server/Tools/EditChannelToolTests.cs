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
/// with only Discord.Net's side, <see cref="IDiscordRestApi"/>, substituted. A refusal that needs the
/// channel's guild or type comes after the channel lookups (GETs); "no write request" means no
/// <c>ModifyAsync</c> reached the channel.
/// </summary>
public sealed class EditChannelToolTests
{
    private readonly IDiscordRestApi _api = Substitute.For<IDiscordRestApi>();
    private readonly ITextChannel _text;

    public EditChannelToolTests()
    {
        _text = Nested<ITextChannel>(5001, 111, "general", 900, 4);
        Category(900, guildId: 111);
    }

    [Fact]
    public void Constructor_rejects_a_null_service()
    {
        Assert.Throws<ArgumentNullException>(() => new EditChannelTool(null!));
    }

    [Fact]
    public async Task EditChannelAsync_returns_the_exact_json_of_the_channel()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "5001", name: "general");

        Assert.False(result.IsError);
        Assert.Equal("""{"id":"5001","name":"general","type":"text","parent_id":"900","position":4}""", Text(result));
        Assert.Single(Modifications(_text));
    }

    [Fact]
    public async Task EditChannelAsync_refuses_a_call_with_no_field_with_invalid_argument_and_no_discord_request()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "5001");

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(_api.ReceivedCalls());
    }

    [Fact]
    public async Task EditChannelAsync_refuses_a_channel_of_a_guild_that_is_not_allowed_with_forbidden_guild_and_no_write_request()
    {
        ITextChannel foreign = Nested<ITextChannel>(5009, 999, "elsewhere", null, 0);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "5009", name: "renamed");

        Assert.Equal("forbidden_guild", ErrorCodeOf(result));
        Assert.Empty(Modifications(foreign));
    }

    [Fact]
    public async Task EditChannelAsync_returns_not_found_for_a_missing_channel()
    {
        _api.GetChannelAsync(5404, Arg.Any<RequestOptions>()).Returns((IChannel?)null);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "5404", name: "renamed");

        Assert.Equal("not_found", ErrorCodeOf(result));
    }

    [Fact]
    public async Task EditChannelAsync_is_refused_in_read_only_mode_with_no_write_request()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadOnly).EditChannelAsync(CancellationToken.None, "5001", name: "renamed");

        Assert.Equal("read_only_mode", ErrorCodeOf(result));
        Assert.Empty(Modifications(_text));
    }

    [Fact]
    public async Task EditChannelAsync_hands_the_callers_token_down_to_the_modify_request()
    {
        using CancellationTokenSource cancel = new();

        await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(cancel.Token, "5001", name: "renamed");

        await _text.Received(1).ModifyAsync(
            Arg.Any<Action<TextChannelProperties>>(),
            Arg.Is<RequestOptions>(options => options.CancelToken == cancel.Token && options.AuditLogReason == "via Discord MCP"));
    }

    // ---- a field the channel's type cannot carry: one test per pair of the table ----

    [Fact]
    public async Task EditChannelAsync_refuses_a_topic_on_a_voice_channel_with_no_write_request()
    {
        IVoiceChannel voice = Nested<IVoiceChannel>(5002, 111, "lounge", null, 0);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "5002", topic: "Topic");

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(Modifications(voice));
    }

    [Fact]
    public async Task EditChannelAsync_refuses_a_slow_mode_on_a_forum_channel_with_no_write_request()
    {
        IForumChannel forum = Nested<IForumChannel>(5005, 111, "ideas", null, 0);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "5005", slowmode_seconds: 10);

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(Modifications(forum));
    }

    [Fact]
    public async Task EditChannelAsync_refuses_a_topic_on_a_category_with_no_write_request()
    {
        ICategoryChannel category = Category(901, guildId: 111);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "901", topic: "Topic");

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(Modifications(category));
    }

    [Fact]
    public async Task EditChannelAsync_refuses_a_parent_on_a_category_with_no_write_request()
    {
        ICategoryChannel category = Category(901, guildId: 111);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "901", parent_id: "900");

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(Modifications(category));
    }

    [Fact]
    public async Task EditChannelAsync_refuses_a_slow_mode_on_a_category_with_no_write_request()
    {
        ICategoryChannel category = Category(901, guildId: 111);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "901", slowmode_seconds: 10);

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(Modifications(category));
    }

    [Fact]
    public async Task EditChannelAsync_refuses_an_nsfw_flag_on_a_category_with_no_write_request()
    {
        ICategoryChannel category = Category(901, guildId: 111);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "901", nsfw: true);

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(Modifications(category));
    }

    // ---- parent_id must be a category of the same guild ----

    [Fact]
    public async Task EditChannelAsync_refuses_a_parent_that_is_not_a_category_with_no_write_request()
    {
        Nested<ITextChannel>(5003, 111, "other", null, 0);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "5001", parent_id: "5003");

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(Modifications(_text));
    }

    [Fact]
    public async Task EditChannelAsync_refuses_a_category_of_another_allowed_guild_as_parent_with_no_write_request()
    {
        Category(902, guildId: 222);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "5001", parent_id: "902");

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(Modifications(_text));
    }

    [Fact]
    public async Task EditChannelAsync_refuses_a_missing_parent_with_no_write_request()
    {
        _api.GetChannelAsync(904, Arg.Any<RequestOptions>()).Returns((IChannel?)null);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "5001", parent_id: "904");

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(Modifications(_text));
    }

    [Fact]
    public async Task EditChannelAsync_moves_a_channel_under_a_category_of_its_own_guild()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "5001", parent_id: "900");

        Assert.False(result.IsError);
        Assert.Single(Modifications(_text));
    }

    // ---- each field given alone reaches the properties ModifyAsync applies ----
    // With a field dropped on its way down, the empty-edit check refuses the call, so the refusal
    // tests above stay green; only a successful call shows the value arrived.

    [Fact]
    public async Task EditChannelAsync_hands_the_given_topic_to_the_modify_properties()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "5001", topic: "Patch notes only");

        Assert.False(result.IsError);
        TextChannelProperties properties = AppliedProperties(_text);
        Assert.Equal((true, "Patch notes only"), (properties.Topic.IsSpecified, properties.Topic.GetValueOrDefault()));
    }

    [Fact]
    public async Task EditChannelAsync_hands_the_given_position_to_the_modify_properties()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "5001", position: 6);

        Assert.False(result.IsError);
        TextChannelProperties properties = AppliedProperties(_text);
        Assert.Equal((true, 6), (properties.Position.IsSpecified, properties.Position.GetValueOrDefault()));
    }

    [Fact]
    public async Task EditChannelAsync_hands_the_given_slow_mode_to_the_modify_properties()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "5001", slowmode_seconds: 45);

        Assert.False(result.IsError);
        TextChannelProperties properties = AppliedProperties(_text);
        Assert.Equal((true, 45), (properties.SlowModeInterval.IsSpecified, properties.SlowModeInterval.GetValueOrDefault()));
    }

    [Fact]
    public async Task EditChannelAsync_hands_the_given_nsfw_flag_to_the_modify_properties()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).EditChannelAsync(CancellationToken.None, "5001", nsfw: true);

        Assert.False(result.IsError);
        TextChannelProperties properties = AppliedProperties(_text);
        Assert.Equal((true, true), (properties.IsNsfw.IsSpecified, properties.IsNsfw.GetValueOrDefault()));
    }

    private EditChannelTool Tool(DiscordMcpMode mode)
    {
        GuildsmithOptions options = new()
        {
            DiscordToken = "FAKE-TOKEN-guildsmith-test-0012",
            AllowedGuildIds = new HashSet<ulong> { 111, 222 },
            Mode = mode,
            AllowDestructive = false,
            AllowAdminGrant = false,
            MaxMessagesPerRead = 50,
            Transport = McpTransport.Stdio,
            LogLevel = LogLevel.Information,
        };
        return new EditChannelTool(new ChannelService(new GuardPipeline(options, new DiscordGateway(_api))));
    }

    private static IEnumerable<NSubstitute.Core.ICall> Modifications(IGuildChannel channel) =>
        channel.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IGuildChannel.ModifyAsync));

    // The properties the single ModifyAsync call on channel would send: its callback, run on a fresh object.
    private static TextChannelProperties AppliedProperties(ITextChannel channel)
    {
        TextChannelProperties properties = new();
        ((Action<TextChannelProperties>)Assert.Single(Modifications(channel)).GetArguments()[0]!)(properties);
        return properties;
    }

    private T Nested<T>(ulong id, ulong guildId, string name, ulong? parentId, int position)
        where T : class, INestedChannel
    {
        T channel = Substitute.For<T>();
        channel.Id.Returns(id);
        channel.GuildId.Returns(guildId);
        channel.Name.Returns(name);
        channel.Position.Returns(position);
        channel.CategoryId.Returns(parentId);
        _api.GetChannelAsync(id, Arg.Any<RequestOptions>()).Returns(channel);
        return channel;
    }

    private ICategoryChannel Category(ulong id, ulong guildId)
    {
        ICategoryChannel category = Substitute.For<ICategoryChannel>();
        category.Id.Returns(id);
        category.GuildId.Returns(guildId);
        category.Name.Returns("category");
        _api.GetChannelAsync(id, Arg.Any<RequestOptions>()).Returns(category);
        return category;
    }

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static string? ErrorCodeOf(CallToolResult result)
    {
        Assert.True(result.IsError);
        using JsonDocument error = JsonDocument.Parse(Text(result));
        return error.RootElement.GetProperty("error").GetString();
    }
}

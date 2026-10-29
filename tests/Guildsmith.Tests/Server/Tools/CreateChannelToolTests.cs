using System.Net;
using System.Text.Json;

using Discord;
using Discord.Net;

using Guildsmith.Core.Configuration;
using Guildsmith.Core.Guards;
using Guildsmith.Core.Services;
using Guildsmith.Discord;
using Guildsmith.Server;
using Guildsmith.Server.Tools;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;

using NSubstitute;

namespace Guildsmith.Tests.Server.Tools;

/// <summary>
/// The tool over every real layer below it (service, <see cref="GuardPipeline"/>, <see cref="DiscordGateway"/>),
/// with only Discord.Net's side, <see cref="IDiscordRestApi"/>, substituted.
/// </summary>
public sealed class CreateChannelToolTests : IDisposable
{
    private const string LogToken = "FAKE-TOKEN-guildsmith-test-0011";

    private readonly IDiscordRestApi _api = Substitute.For<IDiscordRestApi>();
    private readonly IGuild _guild = Substitute.For<IGuild>();
    private readonly List<IHost> _hosts = [];

    public CreateChannelToolTests()
    {
        _guild.Id.Returns(111UL);
        _api.GetGuildAsync(111, Arg.Any<RequestOptions>()).Returns(_guild);
        Category(900, guildId: 111);
    }

    public void Dispose() => _hosts.ForEach(host => host.Dispose());

    [Fact]
    public void Constructor_rejects_a_null_service()
    {
        Assert.Throws<ArgumentNullException>(() => new CreateChannelTool(null!));
    }

    [Fact]
    public async Task CreateChannelAsync_returns_the_exact_json_of_the_created_channel()
    {
        ITextChannel created = Nested<ITextChannel>(5010, 111, "general", 900, 3);
        _guild.CreateTextChannelAsync("general", Arg.Any<Action<TextChannelProperties>>(), Arg.Any<RequestOptions>()).Returns(created);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).CreateChannelAsync(CancellationToken.None, "general", "text", "111", "900", null, 3);

        Assert.False(result.IsError);
        Assert.Equal("""{"id":"5010","name":"general","type":"text","parent_id":"900","position":3}""", Text(result));
    }

    [Theory]
    [InlineData("text", nameof(IGuild.CreateTextChannelAsync))]
    [InlineData("voice", nameof(IGuild.CreateVoiceChannelAsync))]
    [InlineData("category", nameof(IGuild.CreateCategoryAsync))]
    [InlineData("announcement", nameof(IGuild.CreateNewsChannelAsync))]
    [InlineData("forum", nameof(IGuild.CreateForumChannelAsync))]
    public async Task CreateChannelAsync_creates_each_type_through_its_own_discord_net_call(string type, string expectedCall)
    {
        await Tool(DiscordMcpMode.ReadWrite).CreateChannelAsync(CancellationToken.None, "new", type, "111");

        Assert.Equal([expectedCall], CreationCalls());
    }

    [Fact]
    public async Task CreateChannelAsync_refuses_an_unknown_type_with_invalid_argument_listing_the_five_and_no_discord_call()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).CreateChannelAsync(CancellationToken.None, "new", "stage", "111");

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        using JsonDocument error = JsonDocument.Parse(Text(result));
        Assert.Equal(
            ["text", "voice", "category", "announcement", "forum"],
            error.RootElement.GetProperty("valid_values").EnumerateArray().Select(value => value.GetString()));
        Assert.Empty(_api.ReceivedCalls());
    }

    [Fact]
    public async Task CreateChannelAsync_is_refused_in_read_only_mode_with_no_discord_call()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadOnly).CreateChannelAsync(CancellationToken.None, "new", "text", "111");

        Assert.Equal("read_only_mode", ErrorCodeOf(result));
        Assert.Empty(_api.ReceivedCalls());
    }

    // ---- parent_id must be a category of the same guild ----

    [Fact]
    public async Task CreateChannelAsync_refuses_a_parent_that_is_not_a_category_with_no_write_request()
    {
        Nested<ITextChannel>(5001, 111, "general", null, 0);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).CreateChannelAsync(CancellationToken.None, "new", "text", "111", "5001");

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(CreationCalls());
    }

    [Fact]
    public async Task CreateChannelAsync_refuses_a_category_of_another_allowed_guild_as_parent_with_no_write_request()
    {
        Category(902, guildId: 222);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).CreateChannelAsync(CancellationToken.None, "new", "text", "111", "902");

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(CreationCalls());
    }

    [Fact]
    public async Task CreateChannelAsync_refuses_a_missing_parent_with_no_write_request()
    {
        _api.GetChannelAsync(904, Arg.Any<RequestOptions>()).Returns((IChannel?)null);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).CreateChannelAsync(CancellationToken.None, "new", "text", "111", "904");

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(CreationCalls());
    }

    // ---- a field the type cannot carry ----

    [Fact]
    public async Task CreateChannelAsync_refuses_a_topic_on_a_voice_channel_with_no_discord_call()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).CreateChannelAsync(CancellationToken.None, "new", "voice", "111", topic: "Topic");

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(_api.ReceivedCalls());
    }

    [Fact]
    public async Task CreateChannelAsync_refuses_a_topic_on_a_category_with_no_discord_call()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).CreateChannelAsync(CancellationToken.None, "new", "category", "111", topic: "Topic");

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(_api.ReceivedCalls());
    }

    [Fact]
    public async Task CreateChannelAsync_refuses_a_parent_on_a_category_with_no_discord_call()
    {
        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite).CreateChannelAsync(CancellationToken.None, "new", "category", "111", "900");

        Assert.Equal("invalid_argument", ErrorCodeOf(result));
        Assert.Empty(_api.ReceivedCalls());
    }

    // ---- a mapped Discord failure, through the real pipeline, and its audit entry ----

    [Fact]
    public async Task CreateChannelAsync_returns_missing_permission_for_a_discord_403_and_audits_that_code()
    {
        _guild.CreateTextChannelAsync(Arg.Any<string>(), Arg.Any<Action<TextChannelProperties>>(), Arg.Any<RequestOptions>())
            .Returns<Task<ITextChannel>>(_ => throw new HttpException(HttpStatusCode.Forbidden, Substitute.For<IRequest>()));
        using StringWriter log = new();

        CallToolResult result = await AuditedTool(log).CreateChannelAsync(CancellationToken.None, "new", "text", "111");

        Assert.Equal("missing_permission", ErrorCodeOf(result));
        Assert.Equal(["tool=create_channel level=write guild=111 outcome=missing_permission"], AuditEntries(log));
    }

    [Fact]
    public async Task CreateChannelAsync_returns_not_found_for_a_discord_404_and_audits_that_code()
    {
        _guild.CreateTextChannelAsync(Arg.Any<string>(), Arg.Any<Action<TextChannelProperties>>(), Arg.Any<RequestOptions>())
            .Returns<Task<ITextChannel>>(_ => throw new HttpException(HttpStatusCode.NotFound, Substitute.For<IRequest>()));
        using StringWriter log = new();

        CallToolResult result = await AuditedTool(log).CreateChannelAsync(CancellationToken.None, "new", "text", "111");

        Assert.Equal("not_found", ErrorCodeOf(result));
        Assert.Equal(["tool=create_channel level=write guild=111 outcome=not_found"], AuditEntries(log));
    }

    [Fact]
    public async Task CreateChannelAsync_hands_the_callers_token_down_to_the_creation_request()
    {
        using CancellationTokenSource cancel = new();

        await Tool(DiscordMcpMode.ReadWrite).CreateChannelAsync(cancel.Token, "new", "text", "111");

        await _guild.Received(1).CreateTextChannelAsync(
            "new",
            Arg.Any<Action<TextChannelProperties>>(),
            Arg.Is<RequestOptions>(options => options.CancelToken == cancel.Token && options.AuditLogReason == "via Discord MCP"));
    }

    private CreateChannelTool Tool(DiscordMcpMode mode)
    {
        GuildsmithOptions options = Options(mode);
        return new CreateChannelTool(new ChannelService(new GuardPipeline(options, new DiscordGateway(_api))));
    }

    // The tool over a pipeline that audits through the real logging setup, writing to log.
    private CreateChannelTool AuditedTool(StringWriter log)
    {
        GuildsmithOptions options = Options(DiscordMcpMode.ReadWrite);
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        ServerStartup.ConfigureServices(builder, options, log);
        IHost host = builder.Build();
        _hosts.Add(host);
        return new CreateChannelTool(new ChannelService(
            new GuardPipeline(options, new DiscordGateway(_api), host.Services.GetRequiredService<ILoggerFactory>())));
    }

    private static GuildsmithOptions Options(DiscordMcpMode mode) => new()
    {
        DiscordToken = LogToken,
        AllowedGuildIds = new HashSet<ulong> { 111, 222 },
        Mode = mode,
        AllowDestructive = false,
        AllowAdminGrant = false,
        MaxMessagesPerRead = 50,
        Transport = McpTransport.Stdio,
        LogLevel = LogLevel.Information,
    };

    private IEnumerable<string> CreationCalls() =>
        _guild.ReceivedCalls().Select(call => call.GetMethodInfo().Name).Where(name => name.StartsWith("Create", StringComparison.Ordinal));

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

    private void Category(ulong id, ulong guildId)
    {
        ICategoryChannel category = Substitute.For<ICategoryChannel>();
        category.Id.Returns(id);
        category.GuildId.Returns(guildId);
        category.Name.Returns("category");
        _api.GetChannelAsync(id, Arg.Any<RequestOptions>()).Returns(category);
    }

    // The message of every audit line the real logger provider wrote; its header and six-space
    // indent are RedactingLoggerProvider's line format, typed in.
    private static List<string> AuditEntries(StringWriter log)
    {
        string[] lines = log.ToString().Split('\n');
        return lines.Zip(lines.Skip(1))
            .Where(pair => pair.First.StartsWith("Information: Guildsmith.Audit[", StringComparison.Ordinal))
            .Select(pair => pair.Second["      ".Length..])
            .ToList();
    }

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static string? ErrorCodeOf(CallToolResult result)
    {
        Assert.True(result.IsError);
        using JsonDocument error = JsonDocument.Parse(Text(result));
        return error.RootElement.GetProperty("error").GetString();
    }
}

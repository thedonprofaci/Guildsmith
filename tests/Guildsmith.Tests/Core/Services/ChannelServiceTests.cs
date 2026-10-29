using Discord;

using Guildsmith.Core.Configuration;
using Guildsmith.Core.Errors;
using Guildsmith.Core.Gateway;
using Guildsmith.Core.Guards;
using Guildsmith.Core.Models;
using Guildsmith.Core.Services;
using Guildsmith.Discord;

using Microsoft.Extensions.Logging;

using NSubstitute;

using RequestOptions = Discord.RequestOptions;

namespace Guildsmith.Tests.Core.Services;

/// <summary>
/// The service over the real <see cref="GuardPipeline"/>, with the gateway mocked; and the spec
/// section 12 scenario over the real <see cref="DiscordGateway"/>, with only <see cref="IDiscordRestApi"/>
/// substituted. The guard and refusal paths of each tool are pinned end to end in the tool tests.
/// </summary>
public sealed class ChannelServiceTests
{
    private const ulong GuildA = 111;
    private const ulong GuildB = 222;
    private const ulong ChannelInA = 5001;
    private const ulong CategoryInA = 900;

    private readonly IDiscordGateway _gateway = Substitute.For<IDiscordGateway>();
    private readonly CancellationToken _token = new CancellationTokenSource().Token;

    public ChannelServiceTests()
    {
        _gateway.GetChannelGuildIdAsync(ChannelInA, Arg.Any<CancellationToken>()).Returns((ulong?)GuildA);
        _gateway.GetChannelAsync(ChannelInA, Arg.Any<CancellationToken>()).Returns(new ChannelInfo(ChannelInA, GuildA, "general", ChannelKind.Text, null, 0));
        _gateway.GetChannelAsync(CategoryInA, Arg.Any<CancellationToken>()).Returns(new ChannelInfo(CategoryInA, GuildA, "Projektek", ChannelKind.Category, null, 0));
    }

    [Fact]
    public void Constructor_rejects_a_null_pipeline()
    {
        Assert.Throws<ArgumentNullException>(() => new ChannelService(null!));
    }

    // ---- ListChannelsAsync ----

    [Fact]
    public async Task ListChannelsAsync_returns_the_channels_of_the_five_types_in_ascending_numeric_id_order()
    {
        // 10 after 9: a text sort would put "10" first, and the gateway's own order is reversed. The
        // channel with no kind (a stage channel, a thread) is left out.
        Channels(
            new ChannelInfo(10, GuildA, "ten", ChannelKind.Voice, null, 0),
            new ChannelInfo(9, GuildA, "nine", ChannelKind.Text, null, 1),
            new ChannelInfo(8, GuildA, "stage", null, null, 2));

        ToolResult<IReadOnlyList<ChannelInfo>> result = await Service().ListChannelsAsync("list_channels", ToolLevel.Read, "111", null, _token);

        Assert.Equal([new ChannelInfo(9, GuildA, "nine", ChannelKind.Text, null, 1), new ChannelInfo(10, GuildA, "ten", ChannelKind.Voice, null, 0)], result.Value);
    }

    [Fact]
    public async Task ListChannelsAsync_keeps_only_the_channels_of_the_given_type()
    {
        Channels(
            new ChannelInfo(9, GuildA, "nine", ChannelKind.Text, null, 0),
            new ChannelInfo(10, GuildA, "ten", ChannelKind.Voice, null, 1),
            new ChannelInfo(11, GuildA, "eleven", ChannelKind.Text, null, 2));

        ToolResult<IReadOnlyList<ChannelInfo>> result = await Service().ListChannelsAsync("list_channels", ToolLevel.Read, "111", "text", _token);

        Assert.Equal([9UL, 11UL], result.Value!.Select(channel => channel.Id));
    }

    [Fact]
    public async Task ListChannelsAsync_refuses_an_unknown_type_with_invalid_argument_and_no_gateway_call()
    {
        ToolResult<IReadOnlyList<ChannelInfo>> result = await Service().ListChannelsAsync("list_channels", ToolLevel.Read, "111", "stage", _token);

        Assert.Equal(ErrorCode.InvalidArgument, result.Error!.Code);
        Assert.Equal(["text", "voice", "category", "announcement", "forum"], result.Error.ValidValues);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task ListChannelsAsync_hands_the_guild_and_the_callers_token_to_the_gateway()
    {
        Channels();

        await Service().ListChannelsAsync("list_channels", ToolLevel.Read, "222", null, _token);

        await _gateway.Received(1).GetChannelsAsync(GuildB, _token);
    }

    [Fact]
    public async Task ListChannelsAsync_hands_the_tool_level_to_the_pipeline()
    {
        ToolResult<IReadOnlyList<ChannelInfo>> result =
            await Service(DiscordMcpMode.ReadOnly).ListChannelsAsync("list_channels", ToolLevel.Write, "111", null, _token);

        Assert.Equal(ErrorCode.ReadOnlyMode, result.Error!.Code);
    }

    // ---- CreateChannelAsync ----

    [Fact]
    public async Task CreateChannelAsync_hands_the_gateway_the_guild_and_every_given_field()
    {
        ChannelInfo created = new(5010, GuildA, "general", ChannelKind.Text, CategoryInA, 3);
        _gateway.CreateChannelAsync(GuildA, Arg.Any<NewChannel>(), Arg.Any<CancellationToken>()).Returns(created);

        ToolResult<ChannelInfo> result =
            await Service().CreateChannelAsync("create_channel", ToolLevel.Write, "111", "general", "text", "900", "Welcome", 3, _token);

        Assert.Same(created, result.Value);
        await _gateway.Received(1).CreateChannelAsync(GuildA, new NewChannel("general", ChannelKind.Text, CategoryInA, "Welcome", 3), _token);
    }

    [Fact]
    public async Task CreateChannelAsync_hands_the_callers_token_to_the_parent_lookup()
    {
        await Service().CreateChannelAsync("create_channel", ToolLevel.Write, "111", "general", "text", "900", null, null, _token);

        await _gateway.Received(1).GetChannelAsync(CategoryInA, _token);
    }

    [Fact]
    public async Task CreateChannelAsync_makes_no_parent_lookup_when_no_parent_is_given()
    {
        await Service().CreateChannelAsync("create_channel", ToolLevel.Write, "111", "general", "text", null, null, null, _token);

        await _gateway.DidNotReceive().GetChannelAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>());
        await _gateway.Received(1).CreateChannelAsync(GuildA, new NewChannel("general", ChannelKind.Text, null, null, null), _token);
    }

    [Fact]
    public async Task CreateChannelAsync_refuses_a_malformed_parent_id_with_invalid_argument_and_no_gateway_call()
    {
        ToolResult<ChannelInfo> result =
            await Service().CreateChannelAsync("create_channel", ToolLevel.Write, "111", "general", "text", "not-an-id", null, null, _token);

        Assert.Equal(ErrorCode.InvalidArgument, result.Error!.Code);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task CreateChannelAsync_hands_the_tool_level_to_the_pipeline()
    {
        ToolResult<ChannelInfo> result =
            await Service(DiscordMcpMode.ReadOnly).CreateChannelAsync("create_channel", ToolLevel.Write, "111", "general", "text", null, null, null, _token);

        Assert.Equal(ErrorCode.ReadOnlyMode, result.Error!.Code);
    }

    // ---- EditChannelAsync ----

    [Fact]
    public async Task EditChannelAsync_hands_the_gateway_the_channel_and_every_given_field()
    {
        ChannelInfo edited = new(ChannelInA, GuildA, "renamed", ChannelKind.Text, CategoryInA, 2);
        _gateway.EditChannelAsync(ChannelInA, Arg.Any<ChannelEdit>(), Arg.Any<CancellationToken>()).Returns(edited);

        ToolResult<ChannelInfo> result =
            await Service().EditChannelAsync("edit_channel", ToolLevel.Write, "5001", "renamed", "Topic", "900", 2, 30, true, _token);

        Assert.Same(edited, result.Value);
        await _gateway.Received(1).EditChannelAsync(ChannelInA, new ChannelEdit("renamed", "Topic", CategoryInA, 2, 30, true), _token);
    }

    // Each field alone is enough for a call: none of them is mistaken for "no field given".

    [Fact]
    public async Task EditChannelAsync_accepts_a_call_giving_only_the_name()
    {
        await Service().EditChannelAsync("edit_channel", ToolLevel.Write, "5001", "renamed", null, null, null, null, null, _token);

        await _gateway.Received(1).EditChannelAsync(ChannelInA, new ChannelEdit("renamed", null, null, null, null, null), _token);
    }

    [Fact]
    public async Task EditChannelAsync_accepts_a_call_giving_only_the_topic()
    {
        await Service().EditChannelAsync("edit_channel", ToolLevel.Write, "5001", null, "Topic", null, null, null, null, _token);

        await _gateway.Received(1).EditChannelAsync(ChannelInA, new ChannelEdit(null, "Topic", null, null, null, null), _token);
    }

    [Fact]
    public async Task EditChannelAsync_accepts_a_call_giving_only_the_parent()
    {
        await Service().EditChannelAsync("edit_channel", ToolLevel.Write, "5001", null, null, "900", null, null, null, _token);

        await _gateway.Received(1).EditChannelAsync(ChannelInA, new ChannelEdit(null, null, CategoryInA, null, null, null), _token);
    }

    [Fact]
    public async Task EditChannelAsync_accepts_a_call_giving_only_the_position()
    {
        await Service().EditChannelAsync("edit_channel", ToolLevel.Write, "5001", null, null, null, 0, null, null, _token);

        await _gateway.Received(1).EditChannelAsync(ChannelInA, new ChannelEdit(null, null, null, 0, null, null), _token);
    }

    [Fact]
    public async Task EditChannelAsync_accepts_a_call_giving_only_the_slow_mode()
    {
        await Service().EditChannelAsync("edit_channel", ToolLevel.Write, "5001", null, null, null, null, 0, null, _token);

        await _gateway.Received(1).EditChannelAsync(ChannelInA, new ChannelEdit(null, null, null, null, 0, null), _token);
    }

    [Fact]
    public async Task EditChannelAsync_accepts_a_call_giving_only_the_nsfw_flag()
    {
        await Service().EditChannelAsync("edit_channel", ToolLevel.Write, "5001", null, null, null, null, null, false, _token);

        await _gateway.Received(1).EditChannelAsync(ChannelInA, new ChannelEdit(null, null, null, null, null, false), _token);
    }

    [Fact]
    public async Task EditChannelAsync_refuses_a_call_giving_no_field_with_invalid_argument_listing_the_fields_and_no_gateway_call()
    {
        ToolResult<ChannelInfo> result =
            await Service().EditChannelAsync("edit_channel", ToolLevel.Write, "5001", null, null, null, null, null, null, _token);

        Assert.Equal(ErrorCode.InvalidArgument, result.Error!.Code);
        Assert.Equal(["name", "topic", "parent_id", "position", "slowmode_seconds", "nsfw"], result.Error.ValidValues);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task EditChannelAsync_hands_the_callers_token_to_the_channel_lookup()
    {
        await Service().EditChannelAsync("edit_channel", ToolLevel.Write, "5001", "renamed", null, null, null, null, null, _token);

        await _gateway.Received(1).GetChannelAsync(ChannelInA, _token);
    }

    [Fact]
    public async Task EditChannelAsync_hands_the_callers_token_to_the_parent_lookup()
    {
        await Service().EditChannelAsync("edit_channel", ToolLevel.Write, "5001", null, null, "900", null, null, null, _token);

        await _gateway.Received(1).GetChannelAsync(CategoryInA, _token);
    }

    [Fact]
    public async Task EditChannelAsync_returns_not_found_when_the_channel_is_gone_after_the_guards_resolved_it()
    {
        _gateway.GetChannelAsync(ChannelInA, Arg.Any<CancellationToken>()).Returns((ChannelInfo?)null);

        ToolResult<ChannelInfo> result =
            await Service().EditChannelAsync("edit_channel", ToolLevel.Write, "5001", "renamed", null, null, null, null, null, _token);

        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
        await _gateway.DidNotReceive().EditChannelAsync(Arg.Any<ulong>(), Arg.Any<ChannelEdit>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditChannelAsync_refuses_a_channel_of_a_type_outside_the_five_with_invalid_argument_and_no_edit()
    {
        _gateway.GetChannelAsync(ChannelInA, Arg.Any<CancellationToken>()).Returns(new ChannelInfo(ChannelInA, GuildA, "stage", null, null, 0));

        ToolResult<ChannelInfo> result =
            await Service().EditChannelAsync("edit_channel", ToolLevel.Write, "5001", "renamed", null, null, null, null, null, _token);

        Assert.Equal(ErrorCode.InvalidArgument, result.Error!.Code);
        Assert.Equal(["text", "voice", "category", "announcement", "forum"], result.Error.ValidValues);
        await _gateway.DidNotReceive().EditChannelAsync(Arg.Any<ulong>(), Arg.Any<ChannelEdit>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EditChannelAsync_names_the_field_the_type_cannot_carry_in_the_message()
    {
        // The one message-text assertion of this file, on purpose: the criterion is that the error
        // names the field, and the code alone cannot show which field it named.
        _gateway.GetChannelAsync(ChannelInA, Arg.Any<CancellationToken>()).Returns(new ChannelInfo(ChannelInA, GuildA, "lounge", ChannelKind.Voice, null, 0));

        ToolResult<ChannelInfo> result =
            await Service().EditChannelAsync("edit_channel", ToolLevel.Write, "5001", null, "Topic", null, null, null, null, _token);

        Assert.Equal(ErrorCode.InvalidArgument, result.Error!.Code);
        Assert.StartsWith("topic ", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditChannelAsync_refuses_a_malformed_parent_id_with_invalid_argument_and_no_gateway_call()
    {
        ToolResult<ChannelInfo> result =
            await Service().EditChannelAsync("edit_channel", ToolLevel.Write, "5001", null, null, "-1", null, null, null, _token);

        Assert.Equal(ErrorCode.InvalidArgument, result.Error!.Code);
        Assert.Empty(_gateway.ReceivedCalls());
    }

    [Fact]
    public async Task EditChannelAsync_hands_the_tool_level_to_the_pipeline()
    {
        ToolResult<ChannelInfo> result =
            await Service(DiscordMcpMode.ReadOnly).EditChannelAsync("edit_channel", ToolLevel.Write, "5001", "renamed", null, null, null, null, null, _token);

        Assert.Equal(ErrorCode.ReadOnlyMode, result.Error!.Code);
    }

    // ---- DeleteChannelAsync ----

    [Fact]
    public async Task DeleteChannelAsync_deletes_the_channel_with_the_callers_token_and_returns_its_id()
    {
        ToolResult<ulong> result = await Service(allowDestructive: true).DeleteChannelAsync("delete_channel", ToolLevel.Destructive, "5001", _token);

        Assert.Equal(ChannelInA, result.Value);
        await _gateway.Received(1).DeleteChannelAsync(ChannelInA, _token);
    }

    [Fact]
    public async Task DeleteChannelAsync_hands_the_tool_level_to_the_pipeline()
    {
        // Destructive with the gate closed is refused; the same call at Write would pass.
        ToolResult<ulong> result = await Service().DeleteChannelAsync("delete_channel", ToolLevel.Destructive, "5001", _token);

        Assert.Equal(ErrorCode.DestructiveDisabled, result.Error!.Code);
        await _gateway.DidNotReceive().DeleteChannelAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    // ---- spec section 12: a Projektek category with #general and #dev under it ----

    [Fact]
    public async Task The_spec_section_12_scenario_creates_a_Projektek_category_then_general_and_dev_under_it()
    {
        IDiscordRestApi api = Substitute.For<IDiscordRestApi>();
        IGuild guild = Substitute.For<IGuild>();
        guild.Id.Returns(GuildA);
        api.GetGuildAsync(GuildA, Arg.Any<RequestOptions>()).Returns(guild);
        ICategoryChannel projektek = Substitute.For<ICategoryChannel>();
        projektek.Id.Returns(CategoryInA);
        projektek.GuildId.Returns(GuildA);
        projektek.Name.Returns("Projektek");
        guild.CreateCategoryAsync("Projektek", Arg.Any<Action<GuildChannelProperties>>(), Arg.Any<RequestOptions>()).Returns(projektek);
        api.GetChannelAsync(CategoryInA, Arg.Any<RequestOptions>()).Returns(projektek);
        GuildsmithOptions options = Options(DiscordMcpMode.ReadWrite, allowDestructive: false);
        ChannelService service = new(new GuardPipeline(options, new DiscordGateway(api)));

        ToolResult<ChannelInfo> category = await service.CreateChannelAsync("create_channel", ToolLevel.Write, "111", "Projektek", "category", null, null, null, _token);
        string parentId = category.Value!.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ToolResult<ChannelInfo> general = await service.CreateChannelAsync("create_channel", ToolLevel.Write, "111", "general", "text", parentId, null, null, _token);
        ToolResult<ChannelInfo> dev = await service.CreateChannelAsync("create_channel", ToolLevel.Write, "111", "dev", "text", parentId, null, null, _token);

        Assert.True(general.IsSuccess);
        Assert.True(dev.IsSuccess);
        Assert.Equal(
            [
                ("CreateCategoryAsync", "Projektek", (ulong?)null),
                ("CreateTextChannelAsync", "general", (ulong?)900),
                ("CreateTextChannelAsync", "dev", (ulong?)900),
            ],
            guild.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name.StartsWith("Create", StringComparison.Ordinal))
                .Select(call => (call.GetMethodInfo().Name, (string)call.GetArguments()[0]!, ParentOf(call.GetArguments()[1]!))));
    }

    // The category the creation callback sets, read by running it on fresh properties. Action<T> is
    // contravariant, so the category's Action<GuildChannelProperties> casts to this type too.
    private static ulong? ParentOf(object callback)
    {
        TextChannelProperties properties = new();
        ((Action<TextChannelProperties>)callback)(properties);
        return properties.CategoryId.GetValueOrDefault();
    }

    private void Channels(params ChannelInfo[] channels) =>
        _gateway.GetChannelsAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(channels);

    private ChannelService Service(DiscordMcpMode mode = DiscordMcpMode.ReadWrite, bool allowDestructive = false) =>
        new(new GuardPipeline(Options(mode, allowDestructive), _gateway));

    private static GuildsmithOptions Options(DiscordMcpMode mode, bool allowDestructive) => new()
    {
        DiscordToken = "FAKE-TOKEN-guildsmith-test-0009",
        AllowedGuildIds = new HashSet<ulong> { GuildA, GuildB },
        Mode = mode,
        AllowDestructive = allowDestructive,
        AllowAdminGrant = false,
        MaxMessagesPerRead = 50,
        Transport = McpTransport.Stdio,
        LogLevel = LogLevel.Information,
    };
}

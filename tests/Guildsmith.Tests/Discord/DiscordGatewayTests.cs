using System.Net;

using Discord;
using Discord.Net;

using Guildsmith.Core.Errors;
using Guildsmith.Core.Gateway;
using Guildsmith.Core.Models;
using Guildsmith.Discord;

using NSubstitute;
using NSubstitute.Core;

namespace Guildsmith.Tests.Discord;

/// <summary>
/// The gateway over a substituted <see cref="IDiscordRestApi"/> and substituted Discord.Net entities.
/// This is the seam where a request's <see cref="RequestOptions"/> are visible to a test.
/// </summary>
public sealed class DiscordGatewayTests
{
    private readonly IDiscordRestApi _api = Substitute.For<IDiscordRestApi>();
    private readonly CancellationToken _token = new CancellationTokenSource().Token;
    private readonly DiscordGateway _gateway;

    public DiscordGatewayTests()
    {
        _gateway = new DiscordGateway(_api);
    }

    [Fact]
    public void Constructor_rejects_a_null_api()
    {
        Assert.Throws<ArgumentNullException>(() => new DiscordGateway(null!));
    }

    [Fact]
    public async Task GetGuildsAsync_returns_each_joined_guilds_id_and_name_in_discords_order()
    {
        IUserGuild[] joined = [UserGuild(222, "Second"), UserGuild(111, "First")];
        _api.GetGuildSummariesAsync(Arg.Any<RequestOptions>()).Returns(joined);

        IReadOnlyList<GuildSummary> guilds = await _gateway.GetGuildsAsync(_token);

        Assert.Equal([new GuildSummary(222, "Second"), new GuildSummary(111, "First")], guilds);
    }

    [Fact]
    public async Task GetGuildsAsync_sends_the_callers_token_on_the_request_options()
    {
        _api.GetGuildSummariesAsync(Arg.Any<RequestOptions>()).Returns([]);

        await _gateway.GetGuildsAsync(_token);

        await _api.Received(1).GetGuildSummariesAsync(Arg.Is<RequestOptions>(options => options.CancelToken == _token));
    }

    [Fact]
    public async Task GetGuildsAsync_throws_a_mapped_failure_as_a_discord_failure_exception_carrying_the_original()
    {
        HttpException forbidden = Http(HttpStatusCode.Forbidden);
        _api.GetGuildSummariesAsync(Arg.Any<RequestOptions>()).Returns<Task<IReadOnlyCollection<IUserGuild>>>(_ => throw forbidden);

        DiscordFailureException failure = await Assert.ThrowsAsync<DiscordFailureException>(() => _gateway.GetGuildsAsync(_token));

        Assert.Equal(ErrorCode.MissingPermission, failure.Error.Code);
        Assert.Same(forbidden, failure.InnerException);
    }

    [Fact]
    public async Task GetGuildsAsync_lets_an_unmapped_failure_propagate_unchanged()
    {
        HttpException serverError = Http(HttpStatusCode.InternalServerError);
        _api.GetGuildSummariesAsync(Arg.Any<RequestOptions>()).Returns<Task<IReadOnlyCollection<IUserGuild>>>(_ => throw serverError);

        Assert.Same(serverError, await Assert.ThrowsAsync<HttpException>(() => _gateway.GetGuildsAsync(_token)));
    }

    [Fact]
    public async Task GetGuildInfoAsync_returns_the_name_and_the_member_channel_and_role_counts()
    {
        Guild(7, "Seven", memberCount: 42, channelCount: 5, roleCount: 3);

        GuildInfo info = await _gateway.GetGuildInfoAsync(7, _token);

        Assert.Equal(new GuildInfo(7, "Seven", 42, 5, 3), info);
    }

    [Fact]
    public async Task GetGuildInfoAsync_sends_the_callers_token_on_a_fresh_request_options_for_each_request()
    {
        IGuild guild = Guild(7, "Seven", memberCount: 1, channelCount: 1, roleCount: 1);

        await _gateway.GetGuildInfoAsync(7, _token);

        RequestOptions guildOptions = (RequestOptions)_api.ReceivedCalls().Single().GetArguments()[1]!;
        RequestOptions channelOptions = (RequestOptions)guild.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IGuild.GetChannelsAsync)).GetArguments()[1]!;
        Assert.Equal(_token, guildOptions.CancelToken);
        Assert.Equal(_token, channelOptions.CancelToken);
        Assert.NotSame(guildOptions, channelOptions);
    }

    [Fact]
    public async Task GetGuildInfoAsync_downloads_the_channels_rather_than_reading_a_cache()
    {
        IGuild guild = Guild(7, "Seven", memberCount: 1, channelCount: 1, roleCount: 1);

        await _gateway.GetGuildInfoAsync(7, _token);

        await guild.Received(1).GetChannelsAsync(CacheMode.AllowDownload, Arg.Any<RequestOptions>());
    }

    [Fact]
    public async Task GetGuildInfoAsync_throws_not_found_when_discord_returns_no_guild()
    {
        _api.GetGuildAsync(404, Arg.Any<RequestOptions>()).Returns((IGuild?)null);

        DiscordFailureException failure = await Assert.ThrowsAsync<DiscordFailureException>(() => _gateway.GetGuildInfoAsync(404, _token));

        Assert.Equal(ErrorCode.NotFound, failure.Error.Code);
    }

    [Fact]
    public async Task GetGuildInfoAsync_throws_a_mapped_failure_of_the_channel_request_as_a_discord_failure_exception()
    {
        IGuild guild = Guild(7, "Seven", memberCount: 1, channelCount: 1, roleCount: 1);
        guild.GetChannelsAsync(Arg.Any<CacheMode>(), Arg.Any<RequestOptions>())
            .Returns<Task<IReadOnlyCollection<IGuildChannel>>>(_ => throw Http(HttpStatusCode.NotFound));

        DiscordFailureException failure = await Assert.ThrowsAsync<DiscordFailureException>(() => _gateway.GetGuildInfoAsync(7, _token));

        Assert.Equal(ErrorCode.NotFound, failure.Error.Code);
    }

    [Fact]
    public async Task GetGuildInfoAsync_fails_unmapped_when_discord_returns_the_guild_without_a_member_count()
    {
        IGuild guild = Guild(7, "Seven", memberCount: 1, channelCount: 1, roleCount: 1);
        guild.ApproximateMemberCount.Returns((int?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _gateway.GetGuildInfoAsync(7, _token));
    }

    // ---- channel resolution ----

    [Fact]
    public async Task GetChannelGuildIdAsync_returns_the_guild_of_a_guild_channel()
    {
        Nested<ITextChannel>(5001, guildId: 111);

        Assert.Equal(111UL, await _gateway.GetChannelGuildIdAsync(5001, _token));
    }

    [Fact]
    public async Task GetChannelGuildIdAsync_returns_null_for_a_channel_discord_does_not_return()
    {
        _api.GetChannelAsync(5404, Arg.Any<RequestOptions>()).Returns((IChannel?)null);

        Assert.Null(await _gateway.GetChannelGuildIdAsync(5404, _token));
    }

    [Fact]
    public async Task GetChannelGuildIdAsync_returns_null_for_a_dm_channel()
    {
        IDMChannel dm = Substitute.For<IDMChannel>();
        _api.GetChannelAsync(5002, Arg.Any<RequestOptions>()).Returns(dm);

        Assert.Null(await _gateway.GetChannelGuildIdAsync(5002, _token));
    }

    [Fact]
    public async Task GetChannelGuildIdAsync_returns_null_for_a_group_dm_channel()
    {
        IGroupChannel group = Substitute.For<IGroupChannel>();
        _api.GetChannelAsync(5003, Arg.Any<RequestOptions>()).Returns(group);

        Assert.Null(await _gateway.GetChannelGuildIdAsync(5003, _token));
    }

    [Fact]
    public async Task GetChannelGuildIdAsync_hands_the_callers_token_to_the_channel_request()
    {
        Nested<ITextChannel>(5001, guildId: 111);

        await _gateway.GetChannelGuildIdAsync(5001, _token);

        await _api.Received(1).GetChannelAsync(5001, Arg.Is<RequestOptions>(options => options.CancelToken == _token));
    }

    [Fact]
    public async Task GetChannelGuildIdAsync_throws_a_mapped_failure_as_a_discord_failure_exception()
    {
        _api.GetChannelAsync(5001, Arg.Any<RequestOptions>()).Returns<Task<IChannel?>>(_ => throw Http(HttpStatusCode.Forbidden));

        DiscordFailureException failure = await Assert.ThrowsAsync<DiscordFailureException>(() => _gateway.GetChannelGuildIdAsync(5001, _token));

        Assert.Equal(ErrorCode.MissingPermission, failure.Error.Code);
    }

    // ---- GetChannelAsync ----

    [Fact]
    public async Task GetChannelAsync_returns_the_id_guild_name_type_parent_and_position_of_a_guild_channel()
    {
        Nested<ITextChannel>(5001, guildId: 111, name: "general", parentId: 900, position: 4);

        Assert.Equal(new ChannelInfo(5001, 111, "general", ChannelKind.Text, 900, 4), await _gateway.GetChannelAsync(5001, _token));
    }

    [Fact]
    public async Task GetChannelAsync_returns_null_for_a_channel_discord_does_not_return()
    {
        _api.GetChannelAsync(5404, Arg.Any<RequestOptions>()).Returns((IChannel?)null);

        Assert.Null(await _gateway.GetChannelAsync(5404, _token));
    }

    [Fact]
    public async Task GetChannelAsync_returns_null_for_a_dm_channel()
    {
        IDMChannel dm = Substitute.For<IDMChannel>();
        _api.GetChannelAsync(5002, Arg.Any<RequestOptions>()).Returns(dm);

        Assert.Null(await _gateway.GetChannelAsync(5002, _token));
    }

    [Fact]
    public async Task GetChannelAsync_hands_the_callers_token_to_the_channel_request()
    {
        Nested<ITextChannel>(5001, guildId: 111);

        await _gateway.GetChannelAsync(5001, _token);

        await _api.Received(1).GetChannelAsync(5001, Arg.Is<RequestOptions>(options => options.CancelToken == _token));
    }

    [Fact]
    public async Task GetChannelAsync_reads_a_voice_channel_as_voice()
    {
        Nested<IVoiceChannel>(5001, guildId: 111);

        Assert.Equal(ChannelKind.Voice, (await _gateway.GetChannelAsync(5001, _token))!.Kind);
    }

    [Fact]
    public async Task GetChannelAsync_reads_a_category_as_category_with_no_parent()
    {
        Category(900, guildId: 111, name: "Projektek", position: 2);

        Assert.Equal(new ChannelInfo(900, 111, "Projektek", ChannelKind.Category, null, 2), await _gateway.GetChannelAsync(900, _token));
    }

    [Fact]
    public async Task GetChannelAsync_reads_a_news_channel_as_announcement()
    {
        Nested<INewsChannel>(5001, guildId: 111);

        Assert.Equal(ChannelKind.Announcement, (await _gateway.GetChannelAsync(5001, _token))!.Kind);
    }

    [Fact]
    public async Task GetChannelAsync_reads_a_forum_channel_as_forum()
    {
        Nested<IForumChannel>(5001, guildId: 111);

        Assert.Equal(ChannelKind.Forum, (await _gateway.GetChannelAsync(5001, _token))!.Kind);
    }

    [Fact]
    public async Task GetChannelAsync_reads_a_stage_channel_as_no_kind()
    {
        Nested<IStageChannel>(5001, guildId: 111);

        Assert.Null((await _gateway.GetChannelAsync(5001, _token))!.Kind);
    }

    // ---- GetChannelsAsync ----

    [Fact]
    public async Task GetChannelsAsync_returns_every_channel_of_the_guild_in_discords_order()
    {
        IGuild guild = WritableGuild(111);
        IGuildChannel[] channels =
        [
            NestedChannel<ITextChannel>(5002, 111, "general", 900, 1),
            CategoryChannel(900, 111, "Projektek", 0),
            NestedChannel<IStageChannel>(5003, 111, "stage", null, 2),
        ];
        guild.GetChannelsAsync(Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(channels);

        Assert.Equal(
            [
                new ChannelInfo(5002, 111, "general", ChannelKind.Text, 900, 1),
                new ChannelInfo(900, 111, "Projektek", ChannelKind.Category, null, 0),
                new ChannelInfo(5003, 111, "stage", null, null, 2),
            ],
            await _gateway.GetChannelsAsync(111, _token));
    }

    [Fact]
    public async Task GetChannelsAsync_sends_the_callers_token_on_both_requests()
    {
        IGuild guild = WritableGuild(111);
        guild.GetChannelsAsync(Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns([]);

        await _gateway.GetChannelsAsync(111, _token);

        await _api.Received(1).GetGuildAsync(111, Arg.Is<RequestOptions>(options => options.CancelToken == _token));
        await guild.Received(1).GetChannelsAsync(CacheMode.AllowDownload, Arg.Is<RequestOptions>(options => options.CancelToken == _token));
    }

    [Fact]
    public async Task GetChannelsAsync_throws_not_found_when_discord_returns_no_guild()
    {
        _api.GetGuildAsync(404, Arg.Any<RequestOptions>()).Returns((IGuild?)null);

        DiscordFailureException failure = await Assert.ThrowsAsync<DiscordFailureException>(() => _gateway.GetChannelsAsync(404, _token));

        Assert.Equal(ErrorCode.NotFound, failure.Error.Code);
    }

    // ---- CreateChannelAsync ----

    [Fact]
    public async Task CreateChannelAsync_creates_a_text_channel_through_CreateTextChannelAsync_with_the_write_options()
    {
        IGuild guild = WritableGuild(111);

        await _gateway.CreateChannelAsync(111, new NewChannel("general", ChannelKind.Text, null, null, null), _token);

        AssertWriteOptions(SingleCall(guild, nameof(IGuild.CreateTextChannelAsync), "general")[2]);
    }

    [Fact]
    public async Task CreateChannelAsync_creates_a_voice_channel_through_CreateVoiceChannelAsync_with_the_write_options()
    {
        IGuild guild = WritableGuild(111);

        await _gateway.CreateChannelAsync(111, new NewChannel("lounge", ChannelKind.Voice, null, null, null), _token);

        AssertWriteOptions(SingleCall(guild, nameof(IGuild.CreateVoiceChannelAsync), "lounge")[2]);
    }

    [Fact]
    public async Task CreateChannelAsync_creates_a_category_through_CreateCategoryAsync_with_the_write_options()
    {
        IGuild guild = WritableGuild(111);

        await _gateway.CreateChannelAsync(111, new NewChannel("Projektek", ChannelKind.Category, null, null, null), _token);

        AssertWriteOptions(SingleCall(guild, nameof(IGuild.CreateCategoryAsync), "Projektek")[2]);
    }

    [Fact]
    public async Task CreateChannelAsync_creates_an_announcement_channel_through_CreateNewsChannelAsync_with_the_write_options()
    {
        IGuild guild = WritableGuild(111);

        await _gateway.CreateChannelAsync(111, new NewChannel("news", ChannelKind.Announcement, null, null, null), _token);

        AssertWriteOptions(SingleCall(guild, nameof(IGuild.CreateNewsChannelAsync), "news")[2]);
    }

    [Fact]
    public async Task CreateChannelAsync_creates_a_forum_channel_through_CreateForumChannelAsync_with_the_write_options()
    {
        IGuild guild = WritableGuild(111);

        await _gateway.CreateChannelAsync(111, new NewChannel("ideas", ChannelKind.Forum, null, null, null), _token);

        AssertWriteOptions(SingleCall(guild, nameof(IGuild.CreateForumChannelAsync), "ideas")[2]);
    }

    [Fact]
    public async Task CreateChannelAsync_sets_the_given_parent_position_and_topic_on_the_properties()
    {
        IGuild guild = WritableGuild(111);

        await _gateway.CreateChannelAsync(111, new NewChannel("general", ChannelKind.Text, 900, "Welcome", 3), _token);

        TextChannelProperties properties = new();
        ((Action<TextChannelProperties>)SingleCall(guild, nameof(IGuild.CreateTextChannelAsync), "general")[1]!)(properties);
        Assert.Equal((true, (ulong?)900), (properties.CategoryId.IsSpecified, properties.CategoryId.GetValueOrDefault()));
        Assert.Equal((true, 3), (properties.Position.IsSpecified, properties.Position.GetValueOrDefault()));
        Assert.Equal((true, "Welcome"), (properties.Topic.IsSpecified, properties.Topic.GetValueOrDefault()));
    }

    [Fact]
    public async Task CreateChannelAsync_leaves_the_parent_position_and_topic_unspecified_when_not_given()
    {
        IGuild guild = WritableGuild(111);

        await _gateway.CreateChannelAsync(111, new NewChannel("general", ChannelKind.Text, null, null, null), _token);

        TextChannelProperties properties = new();
        ((Action<TextChannelProperties>)SingleCall(guild, nameof(IGuild.CreateTextChannelAsync), "general")[1]!)(properties);
        Assert.False(properties.CategoryId.IsSpecified);
        Assert.False(properties.Position.IsSpecified);
        Assert.False(properties.Topic.IsSpecified);
    }

    [Fact]
    public async Task CreateChannelAsync_returns_the_created_channel()
    {
        IGuild guild = WritableGuild(111);
        ITextChannel created = NestedChannel<ITextChannel>(5010, 111, "general", 900, 3);
        guild.CreateTextChannelAsync("general", Arg.Any<Action<TextChannelProperties>>(), Arg.Any<RequestOptions>()).Returns(created);

        ChannelInfo channel = await _gateway.CreateChannelAsync(111, new NewChannel("general", ChannelKind.Text, 900, null, 3), _token);

        Assert.Equal(new ChannelInfo(5010, 111, "general", ChannelKind.Text, 900, 3), channel);
    }

    [Fact]
    public async Task CreateChannelAsync_hands_the_callers_token_to_the_guild_request()
    {
        WritableGuild(111);

        await _gateway.CreateChannelAsync(111, new NewChannel("general", ChannelKind.Text, null, null, null), _token);

        await _api.Received(1).GetGuildAsync(111, Arg.Is<RequestOptions>(options => options.CancelToken == _token));
    }

    [Fact]
    public async Task CreateChannelAsync_throws_not_found_when_discord_returns_no_guild()
    {
        _api.GetGuildAsync(404, Arg.Any<RequestOptions>()).Returns((IGuild?)null);

        DiscordFailureException failure = await Assert.ThrowsAsync<DiscordFailureException>(
            () => _gateway.CreateChannelAsync(404, new NewChannel("general", ChannelKind.Text, null, null, null), _token));

        Assert.Equal(ErrorCode.NotFound, failure.Error.Code);
    }

    [Fact]
    public async Task CreateChannelAsync_throws_a_mapped_failure_of_the_creation_as_a_discord_failure_exception()
    {
        IGuild guild = WritableGuild(111);
        guild.CreateTextChannelAsync(Arg.Any<string>(), Arg.Any<Action<TextChannelProperties>>(), Arg.Any<RequestOptions>())
            .Returns<Task<ITextChannel>>(_ => throw Http(HttpStatusCode.Forbidden));

        DiscordFailureException failure = await Assert.ThrowsAsync<DiscordFailureException>(
            () => _gateway.CreateChannelAsync(111, new NewChannel("general", ChannelKind.Text, null, null, null), _token));

        Assert.Equal(ErrorCode.MissingPermission, failure.Error.Code);
    }

    // ---- EditChannelAsync ----

    [Fact]
    public async Task EditChannelAsync_sets_only_the_name_when_only_the_name_is_given()
    {
        TextChannelProperties properties = await EditText(new ChannelEdit("renamed", null, null, null, null, null));

        Assert.Equal("renamed", properties.Name.GetValueOrDefault());
        Assert.Equal(["Name"], SpecifiedFields(properties));
    }

    [Fact]
    public async Task EditChannelAsync_sets_only_the_topic_when_only_the_topic_is_given()
    {
        TextChannelProperties properties = await EditText(new ChannelEdit(null, "New topic", null, null, null, null));

        Assert.Equal("New topic", properties.Topic.GetValueOrDefault());
        Assert.Equal(["Topic"], SpecifiedFields(properties));
    }

    [Fact]
    public async Task EditChannelAsync_sets_only_the_category_when_only_the_parent_is_given()
    {
        TextChannelProperties properties = await EditText(new ChannelEdit(null, null, 900, null, null, null));

        Assert.Equal(900UL, properties.CategoryId.GetValueOrDefault());
        Assert.Equal(["CategoryId"], SpecifiedFields(properties));
    }

    [Fact]
    public async Task EditChannelAsync_sets_only_the_position_when_only_the_position_is_given()
    {
        TextChannelProperties properties = await EditText(new ChannelEdit(null, null, null, 7, null, null));

        Assert.Equal(7, properties.Position.GetValueOrDefault());
        Assert.Equal(["Position"], SpecifiedFields(properties));
    }

    [Fact]
    public async Task EditChannelAsync_sets_only_the_slow_mode_when_only_the_slow_mode_is_given()
    {
        TextChannelProperties properties = await EditText(new ChannelEdit(null, null, null, null, 30, null));

        Assert.Equal(30, properties.SlowModeInterval.GetValueOrDefault());
        Assert.Equal(["SlowModeInterval"], SpecifiedFields(properties));
    }

    [Fact]
    public async Task EditChannelAsync_sets_only_the_nsfw_flag_when_only_nsfw_is_given()
    {
        TextChannelProperties properties = await EditText(new ChannelEdit(null, null, null, null, null, true));

        Assert.True(properties.IsNsfw.GetValueOrDefault());
        Assert.Equal(["IsNsfw"], SpecifiedFields(properties));
    }

    [Fact]
    public async Task EditChannelAsync_modifies_a_text_channel_through_ITextChannel_with_the_write_options()
    {
        ITextChannel channel = Nested<ITextChannel>(5001, guildId: 111);

        await _gateway.EditChannelAsync(5001, new ChannelEdit("renamed", null, null, null, null, null), _token);

        AssertModifiedThrough(channel, typeof(ITextChannel));
    }

    [Fact]
    public async Task EditChannelAsync_modifies_an_announcement_channel_through_ITextChannel_with_the_write_options()
    {
        INewsChannel channel = Nested<INewsChannel>(5001, guildId: 111);

        await _gateway.EditChannelAsync(5001, new ChannelEdit("renamed", null, null, null, null, null), _token);

        AssertModifiedThrough(channel, typeof(ITextChannel));
    }

    [Fact]
    public async Task EditChannelAsync_modifies_a_voice_channel_through_IVoiceChannel_with_the_write_options()
    {
        IVoiceChannel channel = Nested<IVoiceChannel>(5001, guildId: 111);

        await _gateway.EditChannelAsync(5001, new ChannelEdit("renamed", null, null, null, null, null), _token);

        AssertModifiedThrough(channel, typeof(IVoiceChannel));
    }

    [Fact]
    public async Task EditChannelAsync_modifies_a_forum_channel_through_IForumChannel_with_the_write_options()
    {
        IForumChannel channel = Nested<IForumChannel>(5001, guildId: 111);

        await _gateway.EditChannelAsync(5001, new ChannelEdit("renamed", null, null, null, null, null), _token);

        AssertModifiedThrough(channel, typeof(IForumChannel));
    }

    [Fact]
    public async Task EditChannelAsync_modifies_a_category_through_IGuildChannel_with_the_write_options()
    {
        ICategoryChannel channel = Category(900, guildId: 111);

        await _gateway.EditChannelAsync(900, new ChannelEdit("renamed", null, null, null, null, null), _token);

        AssertModifiedThrough(channel, typeof(IGuildChannel));
    }

    [Fact]
    public async Task EditChannelAsync_returns_the_channel_as_discord_net_holds_it_after_the_change()
    {
        Nested<ITextChannel>(5001, guildId: 111, name: "general", parentId: 900, position: 4);

        ChannelInfo channel = await _gateway.EditChannelAsync(5001, new ChannelEdit("general", null, null, null, null, null), _token);

        Assert.Equal(new ChannelInfo(5001, 111, "general", ChannelKind.Text, 900, 4), channel);
    }

    [Fact]
    public async Task EditChannelAsync_throws_not_found_when_discord_returns_no_channel()
    {
        _api.GetChannelAsync(5404, Arg.Any<RequestOptions>()).Returns((IChannel?)null);

        DiscordFailureException failure = await Assert.ThrowsAsync<DiscordFailureException>(
            () => _gateway.EditChannelAsync(5404, new ChannelEdit("renamed", null, null, null, null, null), _token));

        Assert.Equal(ErrorCode.NotFound, failure.Error.Code);
    }

    [Fact]
    public async Task EditChannelAsync_hands_the_callers_token_to_the_channel_request()
    {
        Nested<ITextChannel>(5001, guildId: 111);

        await _gateway.EditChannelAsync(5001, new ChannelEdit("renamed", null, null, null, null, null), _token);

        await _api.Received(1).GetChannelAsync(5001, Arg.Is<RequestOptions>(options => options.CancelToken == _token));
    }

    // ---- DeleteChannelAsync ----

    [Fact]
    public async Task DeleteChannelAsync_deletes_the_channel_with_the_write_options()
    {
        ITextChannel channel = Nested<ITextChannel>(5001, guildId: 111);

        await _gateway.DeleteChannelAsync(5001, _token);

        RequestOptions options = (RequestOptions)channel.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IDeletable.DeleteAsync)).GetArguments()[0]!;
        AssertWriteOptions(options);
    }

    [Fact]
    public async Task DeleteChannelAsync_hands_the_callers_token_to_the_channel_request()
    {
        Nested<ITextChannel>(5001, guildId: 111);

        await _gateway.DeleteChannelAsync(5001, _token);

        await _api.Received(1).GetChannelAsync(5001, Arg.Is<RequestOptions>(options => options.CancelToken == _token));
    }

    [Fact]
    public async Task DeleteChannelAsync_throws_not_found_when_discord_returns_no_channel()
    {
        _api.GetChannelAsync(5404, Arg.Any<RequestOptions>()).Returns((IChannel?)null);

        DiscordFailureException failure = await Assert.ThrowsAsync<DiscordFailureException>(() => _gateway.DeleteChannelAsync(5404, _token));

        Assert.Equal(ErrorCode.NotFound, failure.Error.Code);
    }

    [Fact]
    public async Task DeleteChannelAsync_throws_a_mapped_failure_of_the_deletion_as_a_discord_failure_exception()
    {
        ITextChannel channel = Nested<ITextChannel>(5001, guildId: 111);
        channel.DeleteAsync(Arg.Any<RequestOptions>()).Returns<Task>(_ => throw Http(HttpStatusCode.NotFound));

        DiscordFailureException failure = await Assert.ThrowsAsync<DiscordFailureException>(() => _gateway.DeleteChannelAsync(5001, _token));

        Assert.Equal(ErrorCode.NotFound, failure.Error.Code);
    }

    [Fact]
    public async Task GetRoleGuildIdAsync_is_not_supported_before_the_role_tools()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => _gateway.GetRoleGuildIdAsync(1, _token));
        Assert.Empty(_api.ReceivedCalls());
    }

    private IGuild Guild(ulong id, string name, int memberCount, int channelCount, int roleCount)
    {
        IGuild guild = Substitute.For<IGuild>();
        guild.Id.Returns(id);
        guild.Name.Returns(name);
        guild.ApproximateMemberCount.Returns(memberCount);
        guild.Roles.Returns(Enumerable.Range(0, roleCount).Select(_ => Substitute.For<IRole>()).ToArray());
        guild.GetChannelsAsync(Arg.Any<CacheMode>(), Arg.Any<RequestOptions>())
            .Returns(Enumerable.Range(0, channelCount).Select(_ => Substitute.For<IGuildChannel>()).ToArray());
        _api.GetGuildAsync(id, Arg.Any<RequestOptions>()).Returns(guild);
        return guild;
    }

    private static IUserGuild UserGuild(ulong id, string name)
    {
        IUserGuild guild = Substitute.For<IUserGuild>();
        guild.Id.Returns(id);
        guild.Name.Returns(name);
        return guild;
    }

    private static HttpException Http(HttpStatusCode status) => new(status, Substitute.For<IRequest>(), null, null, null);

    // A guild whose channel creation calls are not configured: NSubstitute answers each with a
    // substitute channel, so a test reads the call itself from guild.ReceivedCalls().
    private IGuild WritableGuild(ulong id)
    {
        IGuild guild = Substitute.For<IGuild>();
        guild.Id.Returns(id);
        _api.GetGuildAsync(id, Arg.Any<RequestOptions>()).Returns(guild);
        return guild;
    }

    // A channel of type T that the api returns for its id.
    private T Nested<T>(ulong id, ulong guildId, string name = "channel", ulong? parentId = null, int position = 0)
        where T : class, INestedChannel
    {
        T channel = NestedChannel<T>(id, guildId, name, parentId, position);
        _api.GetChannelAsync(id, Arg.Any<RequestOptions>()).Returns(channel);
        return channel;
    }

    private static T NestedChannel<T>(ulong id, ulong guildId, string name, ulong? parentId, int position)
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

    private ICategoryChannel Category(ulong id, ulong guildId, string name = "category", int position = 0)
    {
        ICategoryChannel category = CategoryChannel(id, guildId, name, position);
        _api.GetChannelAsync(id, Arg.Any<RequestOptions>()).Returns(category);
        return category;
    }

    private static ICategoryChannel CategoryChannel(ulong id, ulong guildId, string name, int position)
    {
        ICategoryChannel category = Substitute.For<ICategoryChannel>();
        category.Id.Returns(id);
        category.GuildId.Returns(guildId);
        category.Name.Returns(name);
        category.Position.Returns(position);
        return category;
    }

    // The arguments of the one call to method the guild received, whose first argument is the name.
    private static object?[] SingleCall(IGuild guild, string method, string name)
    {
        object?[] arguments = guild.ReceivedCalls().Single(call => call.GetMethodInfo().Name == method).GetArguments();
        Assert.Equal(name, arguments[0]);
        return arguments;
    }

    // The two properties every Write or Destructive request carries, as literals.
    private void AssertWriteOptions(object? options)
    {
        RequestOptions requestOptions = Assert.IsType<RequestOptions>(options);
        Assert.Equal("via Discord MCP", requestOptions.AuditLogReason);
        Assert.Equal(_token, requestOptions.CancelToken);
    }

    private void AssertModifiedThrough(IGuildChannel channel, Type declaringType)
    {
        ICall modify = channel.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IGuildChannel.ModifyAsync));
        Assert.Equal(declaringType, modify.GetMethodInfo().DeclaringType);
        AssertWriteOptions(modify.GetArguments()[1]);
    }

    // Edits text channel 5001 with edit, and runs the callback ModifyAsync received on fresh properties.
    private async Task<TextChannelProperties> EditText(ChannelEdit edit)
    {
        ITextChannel channel = Nested<ITextChannel>(5001, guildId: 111);
        await _gateway.EditChannelAsync(5001, edit, _token);

        TextChannelProperties properties = new();
        ((Action<TextChannelProperties>)channel.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(ITextChannel.ModifyAsync)).GetArguments()[0]!)(properties);
        return properties;
    }

    // Every property edit_channel could reach, plus the two it must never set, by whether it is specified.
    private static IEnumerable<string> SpecifiedFields(TextChannelProperties properties) =>
        new (string Name, bool IsSpecified)[]
        {
            ("Name", properties.Name.IsSpecified),
            ("Position", properties.Position.IsSpecified),
            ("CategoryId", properties.CategoryId.IsSpecified),
            ("PermissionOverwrites", properties.PermissionOverwrites.IsSpecified),
            ("Flags", properties.Flags.IsSpecified),
            ("Topic", properties.Topic.IsSpecified),
            ("IsNsfw", properties.IsNsfw.IsSpecified),
            ("SlowModeInterval", properties.SlowModeInterval.IsSpecified),
        }.Where(field => field.IsSpecified).Select(field => field.Name);
}

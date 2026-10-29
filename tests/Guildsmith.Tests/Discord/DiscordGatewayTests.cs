using System.Net;

using Discord;
using Discord.Net;

using Guildsmith.Core.Errors;
using Guildsmith.Core.Gateway;
using Guildsmith.Core.Models;
using Guildsmith.Discord;

using NSubstitute;

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

    [Fact]
    public async Task GetChannelGuildIdAsync_is_not_supported_before_the_channel_tools()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => _gateway.GetChannelGuildIdAsync(1, _token));
        Assert.Empty(_api.ReceivedCalls());
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
}

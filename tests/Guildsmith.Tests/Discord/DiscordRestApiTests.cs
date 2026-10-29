using System.Net;

using Discord;
using Discord.Net;
using Discord.Rest;

using Guildsmith.Discord;
using Guildsmith.Tests.Support;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Guildsmith.Tests.Discord;

/// <summary>
/// The real <see cref="DiscordRestClient"/> under <see cref="DiscordRestApi"/>, with Discord's HTTP
/// layer replaced by <see cref="FakeDiscordRest"/>: no token is checked and no socket is opened.
/// </summary>
public sealed class DiscordRestApiTests
{
    private const string Token = "FAKE-TOKEN-guildsmith-test-0005";
    private const string Me = """{"id":"42","username":"bot","discriminator":"0","bot":true}""";
    private const string Application = """{"id":"42","name":"app","bot_public":true,"bot_require_code_grant":false,"flags":0}""";

    private static readonly string[] s_login = ["GET users/@me", "GET oauth2/applications/@me"];

    private readonly FakeDiscordRest _rest = new();

    public DiscordRestApiTests()
    {
        _rest.Respond("users/@me", HttpStatusCode.OK, Me);
        _rest.Respond("oauth2/applications/@me", HttpStatusCode.OK, Application);
        _rest.Respond("guilds/7?with_counts=true", HttpStatusCode.OK, GuildJson);
        _rest.Respond("guilds/404?with_counts=true", HttpStatusCode.NotFound, """{"message":"Unknown Guild","code":10004}""");
        _rest.Respond("users/@me/guilds?limit=200&after=0&with_counts=true", HttpStatusCode.OK, """[{"id":"7","name":"Seven","permissions":"0","features":[]},{"id":"8","name":"Eight","permissions":"0","features":[]}]""");
    }

    private static string GuildJson =>
        """
        {"id":"7","name":"Seven","owner_id":"1","roles":[{"id":"7","name":"@everyone","permissions":"0","position":0,"color":0,
        "hoist":false,"managed":false,"mentionable":false}],"emojis":[],"features":[],"approximate_member_count":12,
        "verification_level":0,"default_message_notifications":0,"explicit_content_filter":0,"mfa_level":0,"premium_tier":0,
        "nsfw_level":0,"system_channel_flags":0,"preferred_locale":"en-US"}
        """;

    [Fact]
    public void Constructor_makes_no_discord_request()
    {
        using DiscordRestApi api = Api();

        Assert.Empty(_rest.Requests);
    }

    [Fact]
    public async Task GetGuildAsync_logs_in_on_the_first_call_then_asks_for_the_guild_with_its_counts()
    {
        using DiscordRestApi api = Api();

        IGuild? guild = await api.GetGuildAsync(7, new RequestOptions());

        Assert.Equal([.. s_login, "GET guilds/7?with_counts=true"], _rest.Requests);
        Assert.Equal("Seven", guild!.Name);
        Assert.Equal(12, guild.ApproximateMemberCount);
    }

    [Fact]
    public async Task GetGuildAsync_returns_null_for_a_guild_discord_answers_404()
    {
        using DiscordRestApi api = Api();

        Assert.Null(await api.GetGuildAsync(404, new RequestOptions()));
    }

    [Fact]
    public async Task GetGuildSummariesAsync_returns_every_joined_guild_from_users_me_guilds()
    {
        using DiscordRestApi api = Api();

        IReadOnlyCollection<IUserGuild> guilds = await api.GetGuildSummariesAsync(new RequestOptions());

        Assert.Equal([(7UL, "Seven"), (8UL, "Eight")], guilds.Select(guild => (guild.Id, guild.Name)));
        Assert.Equal([.. s_login, "GET users/@me/guilds?limit=200&after=0&with_counts=true"], _rest.Requests);
    }

    [Fact]
    public async Task A_second_call_does_not_log_in_again()
    {
        using DiscordRestApi api = Api();

        await api.GetGuildAsync(7, new RequestOptions());
        await api.GetGuildSummariesAsync(new RequestOptions());

        Assert.Equal([.. s_login, "GET guilds/7?with_counts=true", "GET users/@me/guilds?limit=200&after=0&with_counts=true"], _rest.Requests);
    }

    [Fact]
    public async Task A_failed_login_is_not_kept_and_the_next_call_logs_in_again()
    {
        _rest.Respond("users/@me", HttpStatusCode.Unauthorized, """{"message":"401: Unauthorized","code":0}""");
        using DiscordRestApi api = Api();

        HttpException failure = await Assert.ThrowsAsync<HttpException>(() => api.GetGuildAsync(7, new RequestOptions()));
        _rest.Respond("users/@me", HttpStatusCode.OK, Me);
        IGuild? guild = await api.GetGuildAsync(7, new RequestOptions());

        Assert.Equal(HttpStatusCode.Unauthorized, failure.HttpCode);
        Assert.Equal(["GET users/@me", .. s_login, "GET guilds/7?with_counts=true"], _rest.Requests);
        Assert.Equal("Seven", guild!.Name);
    }

    [Fact]
    public async Task A_failed_login_disposes_the_client_it_created()
    {
        _rest.Respond("users/@me", HttpStatusCode.Unauthorized, """{"message":"401: Unauthorized","code":0}""");
        using DiscordRestApi api = Api();

        await Assert.ThrowsAsync<HttpException>(() => api.GetGuildAsync(7, new RequestOptions()));

        Assert.True(_rest.Disposed);
    }

    [Fact]
    public async Task Dispose_disposes_the_logged_in_client_and_refuses_later_calls()
    {
        DiscordRestApi api = Api();
        await api.GetGuildAsync(7, new RequestOptions());

        api.Dispose();

        Assert.True(_rest.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => api.GetGuildAsync(7, new RequestOptions()));
    }

    [Fact]
    public void Dispose_before_any_call_creates_no_client()
    {
        DiscordRestApi api = Api();

        api.Dispose();

        Assert.False(_rest.Disposed);
        Assert.Empty(_rest.Requests);
    }

    [Fact]
    public async Task The_first_call_logs_the_login_at_information()
    {
        ILogger<DiscordRestApi> logger = Substitute.For<ILogger<DiscordRestApi>>();
        using DiscordRestApi api = new(Token, _rest.Config(), logger);

        await api.GetGuildAsync(7, new RequestOptions());
        await api.GetGuildAsync(7, new RequestOptions());

        // Typed in, not read from DiscordRestApi.LoginMessage: ProgramTests asserts this text is
        // absent from a server's stderr after initialize and tools/list.
        logger.Received(1).Log(LogLevel.Information, Arg.Any<EventId>(),
            Arg.Is<object>(state => state.ToString() == "Logging in to Discord on the first Discord call."),
            null, Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task A_call_waiting_for_the_login_honours_its_cancellation_token()
    {
        using DiscordRestApi api = Api();
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetGuildAsync(7, new RequestOptions { CancelToken = cancel.Token }));
        Assert.Empty(_rest.Requests);
    }

    [Fact]
    public async Task A_summaries_call_waiting_for_the_login_honours_its_cancellation_token()
    {
        using DiscordRestApi api = Api();
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        // Without the token at the login, a cancelled list_guilds still sent GET users/@me and
        // GET oauth2/applications/@me, then ended in a TimeoutException from Discord.Net's request
        // bucket instead of an OperationCanceledException (#33).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetGuildSummariesAsync(new RequestOptions { CancelToken = cancel.Token }));
        Assert.Empty(_rest.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_rejects_a_missing_token(string? token)
    {
        Assert.ThrowsAny<ArgumentException>(() => new DiscordRestApi(token!, _rest.Config(), NullLogger<DiscordRestApi>.Instance));
    }

    [Fact]
    public void Constructor_rejects_a_null_config()
    {
        Assert.Throws<ArgumentNullException>(() => new DiscordRestApi(Token, null!, NullLogger<DiscordRestApi>.Instance));
    }

    [Fact]
    public void Constructor_rejects_a_null_logger()
    {
        Assert.Throws<ArgumentNullException>(() => new DiscordRestApi(Token, _rest.Config(), null!));
    }

    [Fact]
    public async Task GetGuildAsync_rejects_null_options()
    {
        using DiscordRestApi api = Api();

        await Assert.ThrowsAsync<ArgumentNullException>(() => api.GetGuildAsync(7, null!));
    }

    [Fact]
    public async Task GetGuildSummariesAsync_rejects_null_options()
    {
        using DiscordRestApi api = Api();

        await Assert.ThrowsAsync<ArgumentNullException>(() => api.GetGuildSummariesAsync(null!));
    }

    private DiscordRestApi Api() => new(Token, _rest.Config(), NullLogger<DiscordRestApi>.Instance);
}

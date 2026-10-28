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
public sealed class ListGuildsToolTests
{
    private readonly IDiscordRestApi _api = Substitute.For<IDiscordRestApi>();

    [Fact]
    public void Constructor_rejects_a_null_service()
    {
        Assert.Throws<ArgumentNullException>(() => new ListGuildsTool(null!));
    }

    [Fact]
    public async Task ListGuildsAsync_returns_the_exact_json_of_the_guilds_that_are_allowed_and_joined()
    {
        IUserGuild[] joined = [UserGuild(222, "Beta"), UserGuild(999, "Foreign"), UserGuild(111, "Alpha")];
        _api.GetGuildSummariesAsync(Arg.Any<RequestOptions>()).Returns(joined);

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite, 111, 222, 333).ListGuildsAsync(CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("""[{"id":"111","name":"Alpha"},{"id":"222","name":"Beta"}]""", Text(result));
    }

    [Fact]
    public async Task ListGuildsAsync_returns_not_found_for_a_discord_404_rather_than_internal_error()
    {
        _api.GetGuildSummariesAsync(Arg.Any<RequestOptions>())
            .Returns<Task<IReadOnlyCollection<IUserGuild>>>(_ => throw new HttpException(HttpStatusCode.NotFound, Substitute.For<IRequest>()));

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite, 111).ListGuildsAsync(CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("not_found", ErrorCodeOf(result));
    }

    [Fact]
    public async Task ListGuildsAsync_returns_internal_error_for_an_unmapped_discord_401()
    {
        _api.GetGuildSummariesAsync(Arg.Any<RequestOptions>())
            .Returns<Task<IReadOnlyCollection<IUserGuild>>>(_ => throw new HttpException(HttpStatusCode.Unauthorized, Substitute.For<IRequest>()));

        CallToolResult result = await Tool(DiscordMcpMode.ReadWrite, 111).ListGuildsAsync(CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("internal_error", ErrorCodeOf(result));
    }

    [Fact]
    public async Task ListGuildsAsync_succeeds_in_read_only_mode()
    {
        // Pins the level the tool hands the pipeline: Write or Destructive is refused here.
        IUserGuild[] joined = [UserGuild(111, "Alpha")];
        _api.GetGuildSummariesAsync(Arg.Any<RequestOptions>()).Returns(joined);

        CallToolResult result = await Tool(DiscordMcpMode.ReadOnly, 111).ListGuildsAsync(CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("""[{"id":"111","name":"Alpha"}]""", Text(result));
    }

    private ListGuildsTool Tool(DiscordMcpMode mode, params ulong[] allowed)
    {
        GuildsmithOptions options = new()
        {
            DiscordToken = "FAKE-TOKEN-guildsmith-test-0006",
            AllowedGuildIds = allowed.ToHashSet(),
            Mode = mode,
            AllowDestructive = false,
            AllowAdminGrant = false,
            MaxMessagesPerRead = 50,
            Transport = McpTransport.Stdio,
            LogLevel = LogLevel.Information,
        };
        return new ListGuildsTool(new GuildService(new GuardPipeline(options, new DiscordGateway(_api)), options));
    }

    private static IUserGuild UserGuild(ulong id, string name)
    {
        IUserGuild guild = Substitute.For<IUserGuild>();
        guild.Id.Returns(id);
        guild.Name.Returns(name);
        return guild;
    }

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static string? ErrorCodeOf(CallToolResult result)
    {
        using JsonDocument error = JsonDocument.Parse(Text(result));
        return error.RootElement.GetProperty("error").GetString();
    }
}

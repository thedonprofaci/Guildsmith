using System.Net;
using System.Text;

using Discord.Net.Rest;

namespace Guildsmith.Tests.Support;

public sealed class FakeDiscordRestTests
{
    [Fact]
    public async Task SendAsync_answers_an_endpoint_with_the_response_set_for_it_and_records_the_request()
    {
        FakeDiscordRest rest = new();
        rest.Respond("guilds/1", HttpStatusCode.Forbidden, "{\"code\":50001}");

        RestResponse response = await rest.SendAsync("GET", "guilds/1", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("{\"code\":50001}", await new StreamReader(response.Stream, Encoding.UTF8).ReadToEndAsync());
        Assert.Equal(["GET guilds/1"], rest.Requests);
    }

    [Fact]
    public async Task SendAsync_throws_for_an_endpoint_with_no_response_and_still_records_it()
    {
        FakeDiscordRest rest = new();
        rest.Respond("guilds/1", HttpStatusCode.OK, "{}");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => rest.SendAsync("GET", "guilds/2", CancellationToken.None));

        Assert.Contains("GET guilds/2", exception.Message, StringComparison.Ordinal);
        Assert.Equal(["GET guilds/2"], rest.Requests);
    }

    [Fact]
    public async Task SendAsync_with_a_json_body_or_multipart_answers_the_same_way()
    {
        FakeDiscordRest rest = new();
        rest.Respond("channels/3", HttpStatusCode.OK, "{}");

        await rest.SendAsync("POST", "channels/3", "{}", CancellationToken.None);
        await rest.SendAsync("PATCH", "channels/3", new Dictionary<string, object>(), CancellationToken.None);

        Assert.Equal(["POST channels/3", "PATCH channels/3"], rest.Requests);
    }

    [Fact]
    public void Config_sends_a_clients_requests_to_this_fake()
    {
        FakeDiscordRest rest = new();

        Assert.Same(rest, rest.Config().RestClientProvider("https://discord.invalid/"));
    }

    [Fact]
    public void Dispose_is_recorded()
    {
        FakeDiscordRest rest = new();

        rest.Dispose();

        Assert.True(rest.Disposed);
    }
}

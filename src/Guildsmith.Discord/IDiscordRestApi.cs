using Discord;

namespace Guildsmith.Discord;

/// <summary>
/// The <c>DiscordRestClient</c> calls <see cref="DiscordGateway"/> makes, one method per call,
/// returning Discord.Net's own entity interfaces. This is the test seam of the gateway: a test
/// substitutes this interface and the entities it returns (<see cref="IGuild"/>,
/// <see cref="IUserGuild"/>, …), and asserts on the <see cref="RequestOptions"/> each call receives.
/// </summary>
public interface IDiscordRestApi
{
    /// <summary>Every guild the bot has joined, as Discord's partial guild objects (<c>GET /users/@me/guilds</c>).</summary>
    Task<IReadOnlyCollection<IUserGuild>> GetGuildSummariesAsync(RequestOptions options);

    /// <summary>
    /// The guild <paramref name="guildId"/> with its approximate member count
    /// (<c>GET /guilds/{id}?with_counts=true</c>), or <see langword="null"/> when Discord answers 404.
    /// </summary>
    Task<IGuild?> GetGuildAsync(ulong guildId, RequestOptions options);
}

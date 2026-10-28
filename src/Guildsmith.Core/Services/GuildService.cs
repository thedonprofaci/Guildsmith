using Guildsmith.Core.Configuration;
using Guildsmith.Core.Errors;
using Guildsmith.Core.Guards;
using Guildsmith.Core.Models;

namespace Guildsmith.Core.Services;

/// <summary>The server tools of spec section 6.3, each one call through <see cref="GuardPipeline"/>.</summary>
public sealed class GuildService
{
    private readonly GuardPipeline _pipeline;
    private readonly GuildsmithOptions _options;

    /// <summary>A service running every call through <paramref name="pipeline"/>.</summary>
    public GuildService(GuardPipeline pipeline, GuildsmithOptions options)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(options);
        _pipeline = pipeline;
        _options = options;
    }

    /// <summary>
    /// The guilds that are both in <c>ALLOWED_GUILD_IDS</c> and joined by the bot, in ascending
    /// numeric ID order. The call is not scoped to one guild, so the allowlist is applied here, as a
    /// filter on what the gateway returns.
    /// </summary>
    public Task<ToolResult<IReadOnlyList<GuildSummary>>> ListGuildsAsync(string toolName, ToolLevel level, CancellationToken cancellationToken) =>
        _pipeline.RunUnscopedAsync<IReadOnlyList<GuildSummary>>(
            toolName,
            level,
            async (gateway, token) => (await gateway.GetGuildsAsync(token).ConfigureAwait(false))
                .Where(guild => _options.AllowedGuildIds.Contains(guild.Id))
                .OrderBy(guild => guild.Id)
                .ToList(),
            cancellationToken);

    /// <summary>
    /// The overview of the guild <paramref name="guildId"/> names, or of the only allowed guild when
    /// it is <see langword="null"/>; the guild goes through the allowlist guard before the gateway is asked.
    /// </summary>
    public Task<ToolResult<GuildInfo>> GetGuildInfoAsync(string toolName, ToolLevel level, string? guildId, CancellationToken cancellationToken) =>
        _pipeline.RunAsync(
            toolName,
            level,
            ToolTarget.Guild(guildId),
            (gateway, target, token) => gateway.GetGuildInfoAsync(target.GuildId, token),
            cancellationToken);
}

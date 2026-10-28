using System.Net;

using Discord;
using Discord.Net;

using Guildsmith.Core.Errors;
using Guildsmith.Discord;

using NSubstitute;

namespace Guildsmith.Tests.Discord;

/// <summary>The mapping on constructed Discord.Net exceptions: no client, no network.</summary>
public sealed class DiscordFailureMappingTests
{
    [Fact]
    public void Map_gives_not_found_for_an_http_404()
    {
        Assert.Equal(ErrorCode.NotFound, DiscordFailureMapping.Map(Http(HttpStatusCode.NotFound, (DiscordErrorCode)10004))!.Code);
    }

    [Fact]
    public void Map_gives_missing_permission_for_an_http_403()
    {
        // 50001 is Discord's "Missing Access": the 403 alone decides.
        Assert.Equal(ErrorCode.MissingPermission, DiscordFailureMapping.Map(Http(HttpStatusCode.Forbidden, (DiscordErrorCode)50001))!.Code);
    }

    [Fact]
    public void Map_gives_missing_permission_for_discord_code_50013_on_a_status_other_than_403()
    {
        // 50013 is Discord's "Missing Permissions" (Discord.Net: InsufficientPermissions), typed in
        // as a number so a mapping on the wrong enum member does not pass.
        Assert.Equal(ErrorCode.MissingPermission, DiscordFailureMapping.Map(Http(HttpStatusCode.BadRequest, (DiscordErrorCode)50013))!.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public void Map_leaves_an_unmapped_http_failure_unmapped(HttpStatusCode status)
    {
        Assert.Null(DiscordFailureMapping.Map(Http(status, null)));
    }

    [Fact]
    public void Map_leaves_a_rate_limited_exception_unmapped()
    {
        Assert.Null(DiscordFailureMapping.Map(new RateLimitedException(Substitute.For<IRequest>())));
    }

    [Fact]
    public void Map_leaves_an_exception_that_is_not_from_discord_unmapped()
    {
        Assert.Null(DiscordFailureMapping.Map(new InvalidOperationException("x")));
    }

    [Fact]
    public void Map_rejects_a_null_exception()
    {
        Assert.Throws<ArgumentNullException>(() => DiscordFailureMapping.Map(null!));
    }

    private static HttpException Http(HttpStatusCode status, DiscordErrorCode? code) =>
        new(status, Substitute.For<IRequest>(), code, "reason-marker", null);
}

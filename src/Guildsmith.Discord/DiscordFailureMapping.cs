using System.Net;

using Discord;
using Discord.Net;

using Guildsmith.Core.Errors;

namespace Guildsmith.Discord;

/// <summary>
/// The one mapping from Discord.Net failures to the error codes of spec section 8. Anything it does
/// not map stays an exception, which <c>GuardPipeline</c> returns as <c>internal_error</c>.
/// </summary>
public static class DiscordFailureMapping
{
    /// <summary>
    /// The error <paramref name="exception"/> reaches the model as, or <see langword="null"/> when it
    /// is not a mapped failure: an HTTP 404 is <see cref="ErrorCode.NotFound"/>; an HTTP 403, or
    /// Discord's error code 50013 "Missing Permissions" (Discord.Net's
    /// <see cref="DiscordErrorCode.InsufficientPermissions"/>), is <see cref="ErrorCode.MissingPermission"/>.
    /// Discord.Net's exception carries no permission name, so the message names none.
    /// A <see cref="RateLimitedException"/> is not mapped: it carries no retry-after value, and spec
    /// section 7.6 wants one.
    /// </summary>
    public static ToolError? Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is not HttpException http)
        {
            return null;
        }

        if (http.HttpCode == HttpStatusCode.NotFound)
        {
            return new ToolError(ErrorCode.NotFound, "Discord reports that the requested object does not exist.");
        }

        if (http.HttpCode == HttpStatusCode.Forbidden || http.DiscordCode == DiscordErrorCode.InsufficientPermissions)
        {
            return new ToolError(ErrorCode.MissingPermission, "The bot is missing a Discord permission this action needs.");
        }

        return null;
    }
}

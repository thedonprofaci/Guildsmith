using Discord;

using Guildsmith.Core.Audit;

namespace Guildsmith.Discord;

/// <summary>The Discord.Net request options of every Write and Destructive request (spec section 7.8).</summary>
public static class WriteRequestOptions
{
    /// <summary>
    /// A new <see cref="RequestOptions"/> carrying <see cref="AuditReason.Value"/> as its audit-log
    /// reason and <paramref name="cancellationToken"/> as its cancel token. A new instance on every
    /// call: the options are mutable, and one instance shared across calls would carry one request's
    /// changes into the next. There is no overload without the token, so no write can leave it out.
    /// </summary>
    public static RequestOptions Create(CancellationToken cancellationToken) =>
        new() { AuditLogReason = AuditReason.Value, CancelToken = cancellationToken };
}

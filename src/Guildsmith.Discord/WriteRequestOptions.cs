using Discord;

using Guildsmith.Core.Audit;

namespace Guildsmith.Discord;

/// <summary>The Discord.Net request options of every Write and Destructive request (spec section 7.8).</summary>
public static class WriteRequestOptions
{
    /// <summary>
    /// A new <see cref="RequestOptions"/> carrying <see cref="AuditReason.Value"/> as its audit-log
    /// reason. A new instance on every call: the options are mutable, and one instance shared
    /// across calls would carry one request's changes into the next.
    /// </summary>
    public static RequestOptions Create() => new() { AuditLogReason = AuditReason.Value };
}

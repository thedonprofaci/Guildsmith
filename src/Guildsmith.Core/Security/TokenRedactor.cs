namespace Guildsmith.Core.Security;

/// <summary>
/// Removes the configured bot token from text before it leaves the process (spec section 7.1):
/// the tool error response, the log lines on stderr and the startup failure text all pass
/// through one instance of this type.
/// </summary>
/// <remarks>
/// It redacts the configured token only, by ordinal replacement. It does not guess at strings
/// that look like tokens.
/// </remarks>
public sealed class TokenRedactor
{
    /// <summary>The fixed text that takes the token's place.</summary>
    public const string Marker = "[REDACTED]";

    private readonly string _token;

    /// <summary>A redactor for <paramref name="token"/>, trimmed of surrounding whitespace.</summary>
    public TokenRedactor(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        // GuildsmithOptionsReader keeps DISCORD_TOKEN untrimmed: a value set with a trailing space
        // would otherwise never match the bare token where a message echoes it.
        _token = token.Trim();
    }

    /// <summary><paramref name="text"/> with every occurrence of the token replaced by <see cref="Marker"/>.</summary>
    public string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace(_token, Marker, StringComparison.Ordinal);
    }

    /// <summary>
    /// The redacted <see cref="Exception.ToString"/> of <paramref name="exception"/>: its type,
    /// message and stack trace, followed by every inner exception, and every member of an
    /// <see cref="AggregateException"/>.
    /// </summary>
    public string RedactException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Redact(exception.ToString());
    }
}

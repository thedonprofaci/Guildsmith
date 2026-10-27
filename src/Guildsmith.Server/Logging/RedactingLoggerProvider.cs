using System.Text;

using Guildsmith.Core.Security;

using Microsoft.Extensions.Logging;

namespace Guildsmith.Server.Logging;

/// <summary>
/// The only logger provider of the host (spec section 7.1). It renders each log line in full,
/// the formatted message with its arguments and the exception text with its inner exceptions,
/// and passes the whole line through <see cref="TokenRedactor"/> before it writes it to stderr.
/// Scopes are not printed.
/// </summary>
public sealed class RedactingLoggerProvider : ILoggerProvider
{
    private readonly TokenRedactor _redactor;
    private readonly TextWriter _writer;
    private readonly object _gate = new();

    /// <summary>A provider writing redacted lines to <paramref name="writer"/>, which is stderr outside tests.</summary>
    public RedactingLoggerProvider(TokenRedactor redactor, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(redactor);
        ArgumentNullException.ThrowIfNull(writer);
        _redactor = redactor;
        _writer = writer;
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        return new RedactingLogger(this, categoryName);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // The writer belongs to the caller: stderr is not closed with the host.
    }

    // Redacts the assembled line rather than its parts: the exception text is appended after the
    // message, and a redaction applied to the message alone would print the exception as it is.
    private void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        StringBuilder line = new();
        line.Append(level).Append(": ").Append(category).Append('[').Append(eventId.Id).Append("]\n");
        line.Append("      ").Append(message).Append('\n');
        if (exception is not null)
        {
            line.Append(exception).Append('\n');
        }

        string redacted = _redactor.Redact(line.ToString());
        lock (_gate)
        {
            _writer.Write(redacted);
        }
    }

    private sealed class RedactingLogger(RedactingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(category, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}

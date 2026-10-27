using Guildsmith.Core.Security;
using Guildsmith.Server.Logging;

using Microsoft.Extensions.Logging;

namespace Guildsmith.Tests.Server.Logging;

public sealed class RedactingLoggerProviderTests
{
    private const string Token = "FAKE-TOKEN-provider-81d2f0";

    private readonly StringWriter _writer = new();
    private readonly RedactingLoggerProvider _provider;

    public RedactingLoggerProviderTests()
    {
        _provider = new RedactingLoggerProvider(new TokenRedactor(Token), _writer);
    }

    [Fact]
    public void Constructor_rejects_a_null_redactor()
    {
        Assert.Throws<ArgumentNullException>(() => new RedactingLoggerProvider(null!, _writer));
    }

    [Fact]
    public void Constructor_rejects_a_null_writer()
    {
        Assert.Throws<ArgumentNullException>(() => new RedactingLoggerProvider(new TokenRedactor(Token), null!));
    }

    [Fact]
    public void CreateLogger_rejects_a_null_category()
    {
        Assert.Throws<ArgumentNullException>(() => _provider.CreateLogger(null!));
    }

    [Fact]
    public void Log_writes_the_level_the_category_the_event_id_and_the_message()
    {
        _provider.CreateLogger("Some.Category").Log(LogLevel.Warning, new EventId(7), "state", null, (state, _) => $"message {state}");

        Assert.Equal("Warning: Some.Category[7]\n      message state\n", _writer.ToString());
    }

    [Fact]
    public void Log_appends_the_exception_text_after_the_message()
    {
        InvalidOperationException exception = new("exception-marker");

        _provider.CreateLogger("Some.Category").Log(LogLevel.Error, new EventId(0), "state", exception, (_, _) => "message");

        Assert.Equal($"Error: Some.Category[0]\n      message\n{exception}\n", _writer.ToString());
    }

    [Fact]
    public void Log_redacts_the_token_in_the_formatted_message()
    {
        _provider.CreateLogger("Some.Category").Log(LogLevel.Information, new EventId(0), Token, null, (state, _) => $"value {state}");

        string text = _writer.ToString();
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
        Assert.Contains("value [REDACTED]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_redacts_the_token_in_the_exception_text()
    {
        InvalidOperationException exception = new("outer", new HttpRequestException($"inner-marker {Token}"));

        _provider.CreateLogger("Some.Category").Log(LogLevel.Error, new EventId(0), "state", exception, (_, _) => "message");

        string text = _writer.ToString();
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
        Assert.Contains("inner-marker [REDACTED]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_writes_nothing_at_level_none()
    {
        _provider.CreateLogger("Some.Category").Log(LogLevel.None, new EventId(0), "state", null, (_, _) => "message");

        Assert.Equal("", _writer.ToString());
    }

    [Fact]
    public void Log_rejects_a_null_formatter()
    {
        ILogger logger = _provider.CreateLogger("Some.Category");

        Assert.Throws<ArgumentNullException>(() => logger.Log(LogLevel.Information, new EventId(0), "state", null, null!));
    }

    [Theory]
    [InlineData(LogLevel.Trace, true)]
    [InlineData(LogLevel.Critical, true)]
    [InlineData(LogLevel.None, false)]
    public void IsEnabled_is_true_for_every_level_but_none(LogLevel level, bool expected)
    {
        Assert.Equal(expected, _provider.CreateLogger("Some.Category").IsEnabled(level));
    }

    [Fact]
    public void BeginScope_returns_no_scope_because_scopes_are_not_printed()
    {
        Assert.Null(_provider.CreateLogger("Some.Category").BeginScope("scope"));
    }

    [Fact]
    public void Dispose_leaves_the_writer_open()
    {
        _provider.Dispose();

        _writer.Write("still open");
        Assert.Equal("still open", _writer.ToString());
    }
}

using Guildsmith.Core.Security;

namespace Guildsmith.Tests.Core.Security;

public sealed class TokenRedactorTests
{
    private const string Token = "FAKE-TOKEN-redactor-5c1e9a";

    private readonly TokenRedactor _redactor = new(Token);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_a_null_empty_or_blank_token(string? token)
    {
        Assert.ThrowsAny<ArgumentException>(() => new TokenRedactor(token!));
    }

    [Fact]
    public void Redact_replaces_the_bare_token()
    {
        string redacted = _redactor.Redact(Token);

        Assert.DoesNotContain(Token, redacted, StringComparison.Ordinal);
        Assert.Equal(TokenRedactor.Marker, redacted);
    }

    [Fact]
    public void Redact_replaces_the_token_inside_an_authorization_header()
    {
        string redacted = _redactor.Redact($"Authorization: Bot {Token}");

        Assert.DoesNotContain(Token, redacted, StringComparison.Ordinal);
        Assert.Equal($"Authorization: Bot {TokenRedactor.Marker}", redacted);
    }

    [Fact]
    public void Redact_replaces_every_occurrence_of_the_token()
    {
        string redacted = _redactor.Redact($"first {Token} second {Token}");

        Assert.DoesNotContain(Token, redacted, StringComparison.Ordinal);
        Assert.Equal($"first {TokenRedactor.Marker} second {TokenRedactor.Marker}", redacted);
    }

    [Fact]
    public void Redact_replaces_the_token_at_the_very_start_of_the_text()
    {
        string redacted = _redactor.Redact($"{Token} was rejected");

        Assert.DoesNotContain(Token, redacted, StringComparison.Ordinal);
        Assert.Equal($"{TokenRedactor.Marker} was rejected", redacted);
    }

    [Fact]
    public void Redact_replaces_the_token_at_the_very_end_of_the_text()
    {
        string redacted = _redactor.Redact($"rejected: {Token}");

        Assert.DoesNotContain(Token, redacted, StringComparison.Ordinal);
        Assert.Equal($"rejected: {TokenRedactor.Marker}", redacted);
    }

    [Fact]
    public void Redact_returns_text_without_the_token_unchanged()
    {
        const string text = "Guild 111 is not in ALLOWED_GUILD_IDS.\n  FAKE-TOKEN- alone is not the token";

        Assert.Equal(text, _redactor.Redact(text));
    }

    [Fact]
    public void Redact_matches_the_token_case_sensitively()
    {
        string other = Token.ToLowerInvariant();

        Assert.Equal(other, _redactor.Redact(other));
    }

    [Fact]
    public void Redact_replaces_the_bare_token_when_it_was_configured_with_surrounding_whitespace()
    {
        TokenRedactor redactor = new($" {Token} ");

        string redacted = redactor.Redact($"Authorization: Bot {Token}");

        Assert.DoesNotContain(Token, redacted, StringComparison.Ordinal);
        Assert.Equal($"Authorization: Bot {TokenRedactor.Marker}", redacted);
    }

    [Fact]
    public void Redact_rejects_null_text()
    {
        Assert.Throws<ArgumentNullException>(() => _redactor.Redact(null!));
    }

    [Fact]
    public void RedactException_redacts_the_token_in_the_outer_message()
    {
        string redacted = _redactor.RedactException(new InvalidOperationException($"outer-marker {Token}"));

        Assert.DoesNotContain(Token, redacted, StringComparison.Ordinal);
        Assert.Contains($"outer-marker {TokenRedactor.Marker}", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactException_includes_and_redacts_the_inner_exceptions_message()
    {
        InvalidOperationException exception = new("outer", new HttpRequestException($"inner-marker {Token}"));

        string redacted = _redactor.RedactException(exception);

        Assert.DoesNotContain(Token, redacted, StringComparison.Ordinal);
        Assert.Contains($"inner-marker {TokenRedactor.Marker}", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactException_includes_and_redacts_the_second_member_of_an_aggregate_exception()
    {
        AggregateException exception = new(
            new InvalidOperationException("first member"),
            new HttpRequestException($"second-marker {Token}"));

        string redacted = _redactor.RedactException(exception);

        Assert.DoesNotContain(Token, redacted, StringComparison.Ordinal);
        Assert.Contains($"second-marker {TokenRedactor.Marker}", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactException_rejects_a_null_exception()
    {
        Assert.Throws<ArgumentNullException>(() => _redactor.RedactException(null!));
    }
}

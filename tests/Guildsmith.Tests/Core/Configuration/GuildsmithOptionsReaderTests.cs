using Guildsmith.Core.Configuration;

using Microsoft.Extensions.Logging;

namespace Guildsmith.Tests.Core.Configuration;

public sealed class GuildsmithOptionsReaderTests
{
    private const string FakeToken = "FAKE-TOKEN-guildsmith-test-0001";

    // ---- defaults and required values ----

    [Fact]
    public void Read_applies_every_default_when_only_the_two_required_variables_are_set()
    {
        ConfigurationResult result = Read(("DISCORD_TOKEN", FakeToken), ("ALLOWED_GUILD_IDS", "123456789012345678"));

        Assert.Empty(result.Errors);
        GuildsmithOptions options = Assert.IsType<GuildsmithOptions>(result.Options);
        Assert.Equal(FakeToken, options.DiscordToken);
        Assert.Equal(new HashSet<ulong> { 123456789012345678 }, options.AllowedGuildIds);
        Assert.Equal(DiscordMcpMode.ReadWrite, options.Mode);
        Assert.False(options.AllowDestructive);
        Assert.False(options.AllowAdminGrant);
        Assert.Equal(50, options.MaxMessagesPerRead);
        Assert.Equal(McpTransport.Stdio, options.Transport);
        Assert.Equal(LogLevel.Information, options.LogLevel);
    }

    [Fact]
    public void Read_throws_on_a_null_lookup()
    {
        Assert.Throws<ArgumentNullException>(() => GuildsmithOptionsReader.Read(null!));
    }

    // ---- DISCORD_TOKEN ----

    [Fact]
    public void TokenVariable_is_the_name_the_token_is_read_from()
    {
        Assert.Equal("DISCORD_TOKEN", GuildsmithOptionsReader.TokenVariable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_rejects_a_missing_or_blank_token(string? token)
    {
        ConfigurationResult result = Read(("DISCORD_TOKEN", token), ("ALLOWED_GUILD_IDS", "1"));

        AssertOnlyError(result, "DISCORD_TOKEN", ConfigurationErrorKind.Missing);
    }

    [Fact]
    public void Read_keeps_a_non_blank_token_verbatim_without_trimming()
    {
        // The token is passed through untouched: the reader does not repair a secret it cannot see.
        Assert.Equal($"  {FakeToken}\n", Options(("DISCORD_TOKEN", $"  {FakeToken}\n")).DiscordToken);
    }

    // ---- ALLOWED_GUILD_IDS ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Read_rejects_a_missing_or_empty_guild_allowlist(string? ids)
    {
        AssertOnlyError(Valid(("ALLOWED_GUILD_IDS", ids)), "ALLOWED_GUILD_IDS", ConfigurationErrorKind.Missing);
    }

    [Theory]
    [InlineData("1,,2")]
    [InlineData("1,2,")]
    [InlineData(",1")]
    [InlineData("abc")]
    [InlineData("12a")]
    [InlineData("0")]
    [InlineData("1,0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1 2")]
    [InlineData("1;2")]
    [InlineData("x,,y")]
    [InlineData("18446744073709551616")]
    public void Read_rejects_an_unparseable_guild_allowlist(string ids)
    {
        AssertOnlyError(Valid(("ALLOWED_GUILD_IDS", ids)), "ALLOWED_GUILD_IDS", ConfigurationErrorKind.Invalid);
    }

    [Theory]
    [InlineData("1", new ulong[] { 1 })]
    [InlineData(" 1 , 2 ,3", new ulong[] { 1, 2, 3 })]
    [InlineData("5,5", new ulong[] { 5 })]
    [InlineData("18446744073709551615", new ulong[] { 18446744073709551615 })]
    public void Read_parses_a_trimmed_comma_separated_guild_allowlist(string ids, ulong[] expected)
    {
        Assert.Equal(new HashSet<ulong>(expected), Options(("ALLOWED_GUILD_IDS", ids)).AllowedGuildIds);
    }

    // ---- DISCORD_MCP_MODE ----

    [Theory]
    [InlineData("Admin")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("99")]
    [InlineData("")]
    [InlineData("Read Only")]
    [InlineData("ReadOnly,ReadWrite")]
    public void Read_rejects_an_unknown_mode(string mode)
    {
        AssertOnlyError(Valid(("DISCORD_MCP_MODE", mode)), "DISCORD_MCP_MODE", ConfigurationErrorKind.Invalid);
    }

    [Theory]
    [InlineData("ReadOnly", DiscordMcpMode.ReadOnly)]
    [InlineData("readonly", DiscordMcpMode.ReadOnly)]
    [InlineData("ReadWrite", DiscordMcpMode.ReadWrite)]
    [InlineData("READWRITE", DiscordMcpMode.ReadWrite)]
    public void Read_parses_a_mode_name_case_insensitively(string mode, DiscordMcpMode expected)
    {
        Assert.Equal(expected, Options(("DISCORD_MCP_MODE", mode)).Mode);
    }

    // ---- ALLOW_DESTRUCTIVE / ALLOW_ADMIN_GRANT ----

    [Theory]
    [InlineData("ALLOW_DESTRUCTIVE", "1")]
    [InlineData("ALLOW_DESTRUCTIVE", "0")]
    [InlineData("ALLOW_DESTRUCTIVE", "yes")]
    [InlineData("ALLOW_DESTRUCTIVE", "no")]
    [InlineData("ALLOW_DESTRUCTIVE", " true")]
    [InlineData("ALLOW_DESTRUCTIVE", "false ")]
    [InlineData("ALLOW_DESTRUCTIVE", "")]
    [InlineData("ALLOW_ADMIN_GRANT", "1")]
    [InlineData("ALLOW_ADMIN_GRANT", "0")]
    [InlineData("ALLOW_ADMIN_GRANT", "yes")]
    [InlineData("ALLOW_ADMIN_GRANT", "no")]
    [InlineData("ALLOW_ADMIN_GRANT", " true")]
    [InlineData("ALLOW_ADMIN_GRANT", "false ")]
    [InlineData("ALLOW_ADMIN_GRANT", "")]
    public void Read_rejects_a_non_boolean_flag(string variable, string value)
    {
        AssertOnlyError(Valid((variable, value)), variable, ConfigurationErrorKind.Invalid);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    public void Read_parses_allow_destructive_case_insensitively(string value, bool expected)
    {
        GuildsmithOptions options = Options(("ALLOW_DESTRUCTIVE", value));

        Assert.Equal(expected, options.AllowDestructive);
        Assert.False(options.AllowAdminGrant);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    public void Read_parses_allow_admin_grant_case_insensitively(string value, bool expected)
    {
        GuildsmithOptions options = Options(("ALLOW_ADMIN_GRANT", value));

        Assert.Equal(expected, options.AllowAdminGrant);
        Assert.False(options.AllowDestructive);
    }

    // ---- MAX_MESSAGES_PER_READ ----

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1.5")]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData(" 5")]
    [InlineData("5 ")]
    [InlineData("99999999999")]
    [InlineData("1,0")] // in range if read with a thousands separator (as 10), so only NumberStyles.None rejects it
    public void Read_rejects_a_non_numeric_message_limit(string value)
    {
        AssertOnlyError(Valid(("MAX_MESSAGES_PER_READ", value)), "MAX_MESSAGES_PER_READ", ConfigurationErrorKind.Invalid);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("2147483647")]
    public void Read_rejects_an_out_of_range_message_limit_instead_of_clamping(string value)
    {
        AssertOnlyError(Valid(("MAX_MESSAGES_PER_READ", value)), "MAX_MESSAGES_PER_READ", ConfigurationErrorKind.OutOfRange);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("100", 100)]
    [InlineData("50", 50)]
    [InlineData("7", 7)]
    public void Read_accepts_a_message_limit_from_1_to_100(string value, int expected)
    {
        Assert.Equal(expected, Options(("MAX_MESSAGES_PER_READ", value)).MaxMessagesPerRead);
    }

    // ---- MCP_TRANSPORT ----

    [Theory]
    [InlineData("http")]
    [InlineData("HTTP")]
    public void Read_rejects_http_transport_as_not_supported_yet(string value)
    {
        AssertOnlyError(Valid(("MCP_TRANSPORT", value)), "MCP_TRANSPORT", ConfigurationErrorKind.NotSupported);
    }

    [Theory]
    [InlineData("grpc")]
    [InlineData("sse")]
    [InlineData("")]
    [InlineData("0")]
    public void Read_rejects_an_unknown_transport(string value)
    {
        AssertOnlyError(Valid(("MCP_TRANSPORT", value)), "MCP_TRANSPORT", ConfigurationErrorKind.Invalid);
    }

    [Theory]
    [InlineData("stdio")]
    [InlineData("STDIO")]
    public void Read_accepts_stdio_transport_case_insensitively(string value)
    {
        Assert.Equal(McpTransport.Stdio, Options(("MCP_TRANSPORT", value)).Transport);
    }

    // ---- LOG_LEVEL ----

    [Theory]
    [InlineData("Verbose")]
    [InlineData("Info")]
    [InlineData("1")]
    [InlineData("6")]
    [InlineData("")]
    public void Read_rejects_an_unknown_log_level(string value)
    {
        AssertOnlyError(Valid(("LOG_LEVEL", value)), "LOG_LEVEL", ConfigurationErrorKind.Invalid);
    }

    [Theory]
    [InlineData("Trace", LogLevel.Trace)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("WARNING", LogLevel.Warning)]
    [InlineData("Error", LogLevel.Error)]
    [InlineData("critical", LogLevel.Critical)]
    [InlineData("None", LogLevel.None)]
    public void Read_parses_a_log_level_name_case_insensitively(string value, LogLevel expected)
    {
        Assert.Equal(expected, Options(("LOG_LEVEL", value)).LogLevel);
    }

    // ---- several errors, and the token ----

    [Fact]
    public void Read_reports_every_invalid_variable_not_only_the_first()
    {
        ConfigurationResult result = Valid(("ALLOW_DESTRUCTIVE", "yes"), ("MAX_MESSAGES_PER_READ", "0"));

        Assert.Null(result.Options);
        Assert.Equal(
            [("ALLOW_DESTRUCTIVE", ConfigurationErrorKind.Invalid), ("MAX_MESSAGES_PER_READ", ConfigurationErrorKind.OutOfRange)],
            result.Errors.Select(error => (error.Variable, error.Kind)));
    }

    [Fact]
    public void Read_reports_both_required_variables_when_neither_is_set()
    {
        ConfigurationResult result = Read();

        Assert.Null(result.Options);
        Assert.Equal(
            [("DISCORD_TOKEN", ConfigurationErrorKind.Missing), ("ALLOWED_GUILD_IDS", ConfigurationErrorKind.Missing)],
            result.Errors.Select(error => (error.Variable, error.Kind)));
    }

    [Fact]
    public void Read_never_echoes_the_token_when_it_is_pasted_into_every_other_variable()
    {
        // The plausible leak: the token pasted into the wrong variable, whose error message
        // would print the rejected value.
        ConfigurationResult result = Read(
            ("DISCORD_TOKEN", FakeToken),
            ("ALLOWED_GUILD_IDS", FakeToken),
            ("DISCORD_MCP_MODE", FakeToken),
            ("ALLOW_DESTRUCTIVE", FakeToken),
            ("ALLOW_ADMIN_GRANT", FakeToken),
            ("MAX_MESSAGES_PER_READ", FakeToken),
            ("MCP_TRANSPORT", FakeToken),
            ("LOG_LEVEL", FakeToken));

        Assert.Equal(
            ["ALLOWED_GUILD_IDS", "DISCORD_MCP_MODE", "ALLOW_DESTRUCTIVE", "ALLOW_ADMIN_GRANT", "MAX_MESSAGES_PER_READ", "MCP_TRANSPORT", "LOG_LEVEL"],
            result.Errors.Select(error => error.Variable));
        Assert.All(result.Errors, error => Assert.DoesNotContain(FakeToken, error.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void Read_never_echoes_the_token_in_any_error_message()
    {
        ConfigurationResult result = Read(
            ("DISCORD_TOKEN", FakeToken),
            ("DISCORD_MCP_MODE", "x"),
            ("ALLOW_DESTRUCTIVE", "x"),
            ("ALLOW_ADMIN_GRANT", "x"),
            ("MAX_MESSAGES_PER_READ", "0"),
            ("MCP_TRANSPORT", "http"),
            ("LOG_LEVEL", "x"));

        Assert.Equal(7, result.Errors.Count);
        Assert.All(result.Errors, error => Assert.DoesNotContain(FakeToken, error.Message, StringComparison.Ordinal));
    }

    // ---- helpers ----

    private static ConfigurationResult Read(params (string Name, string? Value)[] variables)
    {
        Dictionary<string, string?> values = variables.ToDictionary(v => v.Name, v => v.Value);
        return GuildsmithOptionsReader.Read(name => values.GetValueOrDefault(name));
    }

    /// <summary>A valid configuration with <paramref name="overrides"/> applied on top.</summary>
    private static ConfigurationResult Valid(params (string Name, string? Value)[] overrides)
    {
        Dictionary<string, string?> values = new()
        {
            ["DISCORD_TOKEN"] = FakeToken,
            ["ALLOWED_GUILD_IDS"] = "123456789012345678",
        };
        foreach ((string name, string? value) in overrides)
        {
            values[name] = value;
        }

        return GuildsmithOptionsReader.Read(name => values.GetValueOrDefault(name));
    }

    private static GuildsmithOptions Options(params (string Name, string? Value)[] overrides)
    {
        ConfigurationResult result = Valid(overrides);
        Assert.Empty(result.Errors);
        return Assert.IsType<GuildsmithOptions>(result.Options);
    }

    private static void AssertOnlyError(ConfigurationResult result, string variable, ConfigurationErrorKind kind)
    {
        Assert.Null(result.Options);
        Assert.Equal([(variable, kind)], result.Errors.Select(error => (error.Variable, error.Kind)));
    }
}

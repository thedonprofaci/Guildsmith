using Guildsmith.Core.Configuration;
using Guildsmith.Core.Errors;
using Guildsmith.Core.Gateway;
using Guildsmith.Core.Guards;
using Guildsmith.Core.Services;
using Guildsmith.Discord;
using Guildsmith.Server;
using Guildsmith.Server.Logging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

using NSubstitute;

namespace Guildsmith.Tests.Server;

public sealed class ServerStartupTests
{
    private const string Token = "FAKE-TOKEN-guildsmith-test-0001";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveEnvironmentName_defaults_to_production_when_unset_or_blank(string? value)
    {
        Assert.Equal("Production", ServerStartup.ResolveEnvironmentName(value));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void ResolveEnvironmentName_keeps_an_explicit_name(string value)
    {
        Assert.Equal(value, ServerStartup.ResolveEnvironmentName(value));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void AddConfigurationSources_reads_only_environment_variables_outside_development(string environmentName)
    {
        ConfigurationBuilder configuration = new();

        ServerStartup.AddConfigurationSources(configuration, Environment(environmentName));

        IConfigurationSource source = Assert.Single(configuration.Sources);
        EnvironmentVariablesConfigurationSource variables = Assert.IsType<EnvironmentVariablesConfigurationSource>(source);
        Assert.Null(variables.Prefix);
    }

    [Fact]
    public void AddConfigurationSources_reads_user_secrets_then_environment_variables_in_development()
    {
        ConfigurationBuilder configuration = new();

        ServerStartup.AddConfigurationSources(configuration, Environment("Development"));

        // Order matters: the later source wins, so an environment variable overrides a secret.
        Assert.Collection(
            configuration.Sources,
            secrets =>
            {
                JsonConfigurationSource json = Assert.IsType<JsonConfigurationSource>(secrets);
                Assert.Equal("secrets.json", json.Path);

                // Optional: a machine where `dotnet user-secrets` was never run has no file, and
                // a required one crashes the Development host at startup.
                Assert.True(json.Optional);
            },
            variables => Assert.Null(Assert.IsType<EnvironmentVariablesConfigurationSource>(variables).Prefix));
    }

    [Fact]
    public void ConfigureServices_registers_the_validated_options_instance()
    {
        GuildsmithOptions options = Options(LogLevel.Information);

        using IHost host = BuildHost(options);

        Assert.Same(options, host.Services.GetRequiredService<GuildsmithOptions>());
    }

    [Fact]
    public void ConfigureServices_registers_the_redacting_logger_provider_and_no_other()
    {
        using IHost host = BuildHost(Options(LogLevel.Information));

        Assert.Equal([typeof(RedactingLoggerProvider)], host.Services.GetServices<ILoggerProvider>().Select(p => p.GetType()));
    }

    [Fact]
    public void ConfigureServices_writes_a_trace_line_to_stderr_at_the_lowest_log_level()
    {
        StringWriter stderr = new();
        using IHost host = BuildHost(Options(LogLevel.Trace), stderr);

        host.Services.GetRequiredService<ILogger<ServerStartupTests>>().LogTrace("trace-marker");

        Assert.Contains("trace-marker", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureServices_redacts_the_token_passed_as_a_structured_log_argument()
    {
        StringWriter stderr = new();
        using IHost host = BuildHost(Options(LogLevel.Information), stderr);

        host.Services.GetRequiredService<ILogger<ServerStartupTests>>().LogInformation("value-marker {Value}", Token);

        string text = stderr.ToString();
        Assert.Contains("value-marker [REDACTED]", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureServices_redacts_the_token_in_a_logged_exceptions_message()
    {
        StringWriter stderr = new();
        using IHost host = BuildHost(Options(LogLevel.Information), stderr);

        host.Services.GetRequiredService<ILogger<ServerStartupTests>>()
            .LogError(new InvalidOperationException($"outer-marker {Token}"), "line-marker");

        string text = stderr.ToString();
        Assert.Contains("line-marker", text, StringComparison.Ordinal);
        Assert.Contains("outer-marker [REDACTED]", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureServices_redacts_the_token_in_a_logged_exceptions_inner_exception()
    {
        StringWriter stderr = new();
        using IHost host = BuildHost(Options(LogLevel.Information), stderr);

        host.Services.GetRequiredService<ILogger<ServerStartupTests>>()
            .LogError(new InvalidOperationException("outer", new HttpRequestException($"inner-marker {Token}")), "line-marker");

        string text = stderr.ToString();
        Assert.Contains("line-marker", text, StringComparison.Ordinal);
        Assert.Contains("inner-marker [REDACTED]", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Trace)]
    public void ConfigureServices_takes_the_minimum_log_level_from_the_options(LogLevel level)
    {
        using IHost host = BuildHost(Options(level));

        Assert.Equal(level, host.Services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.MinLevel);
    }

    [Fact]
    public void ConfigureServices_keeps_the_audit_category_at_information_when_the_log_level_is_error()
    {
        using IHost host = BuildHost(Options(LogLevel.Error));
        ILoggerFactory loggers = host.Services.GetRequiredService<ILoggerFactory>();

        // Both categories typed in: the audit one, and the GuardPipeline one that proves the
        // filter is scoped to the audit category rather than lowering the minimum level.
        Assert.True(loggers.CreateLogger("Guildsmith.Audit").IsEnabled(LogLevel.Information));
        Assert.False(loggers.CreateLogger("Guildsmith.Core.Guards.GuardPipeline").IsEnabled(LogLevel.Information));
    }

    [Fact]
    public void ConfigureServices_does_not_keep_a_category_that_shares_only_part_of_the_audit_category_name()
    {
        using IHost host = BuildHost(Options(LogLevel.Error));

        // A filter rule matches a category by prefix, so a rule spelling "Guildsmith.Audi" would
        // keep this one too.
        Assert.False(host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Guildsmith.Audio").IsEnabled(LogLevel.Information));
    }

    [Fact]
    public void ConfigureServices_serves_exactly_the_server_and_channel_tools()
    {
        using IHost host = BuildHost(Options(LogLevel.Information));

        McpServerPrimitiveCollection<McpServerTool>? tools =
            host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection;
        Assert.NotNull(tools);
        Assert.Equal(["create_channel", "delete_channel", "edit_channel", "get_guild_info", "list_channels", "list_guilds"], tools.Select(tool => tool.ProtocolTool.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ConfigureServices_builds_a_pipeline_that_audits_a_write_to_stderr_and_writes_nothing_to_stdout()
    {
        // The two-argument GuardPipeline constructor logs nothing: a registration using it drops
        // this audit entry, and the test goes red.
        StringWriter stderr = new();
        StringWriter stdout = new();
        TextWriter original = Console.Out;
        Console.SetOut(stdout);
        try
        {
            using IHost host = BuildHost(Options(LogLevel.Information), stderr);

            ToolResult<string> result = await host.Services.GetRequiredService<GuardPipeline>().RunAsync(
                "fake_write", ToolLevel.Write, ToolTarget.Guild("1"), (_, _, _) => Task.FromResult("written"));

            Assert.Equal("written", result.Value);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains("Information: Guildsmith.Audit[0]\n      tool=fake_write level=write guild=1 outcome=success\n", stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal("", stdout.ToString());
    }

    [Fact]
    public void ConfigureServices_registers_one_pipeline_instance_over_the_registered_gateway()
    {
        using IHost host = BuildHost(Options(LogLevel.Information));

        Assert.Same(host.Services.GetRequiredService<GuardPipeline>(), host.Services.GetRequiredService<GuardPipeline>());
        Assert.IsType<DiscordGateway>(host.Services.GetRequiredService<IDiscordGateway>());
        Assert.IsType<DiscordRestApi>(host.Services.GetRequiredService<IDiscordRestApi>());
        Assert.NotNull(host.Services.GetRequiredService<GuildService>());
    }

    [Fact]
    public async Task Starting_and_stopping_the_host_makes_no_discord_call()
    {
        IDiscordRestApi api = Substitute.For<IDiscordRestApi>();
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        ServerStartup.ConfigureServices(builder, Options(LogLevel.Information), TextWriter.Null);
        builder.Services.AddSingleton(api);
        using IHost host = builder.Build();

        await host.StartAsync();
        _ = host.Services.GetRequiredService<GuildService>();
        await host.StopAsync();

        Assert.Same(api, host.Services.GetRequiredService<IDiscordRestApi>());
        Assert.Empty(api.ReceivedCalls());
    }

    [Fact]
    public void ConfigureServices_registers_no_discord_net_type()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        ServerStartup.ConfigureServices(builder, Options(LogLevel.Information), TextWriter.Null);

        // Startup must not construct or log in a Discord client (no network at startup).
        Assert.DoesNotContain(builder.Services, descriptor =>
            IsDiscordNet(descriptor.ServiceType) || IsDiscordNet(descriptor.ImplementationType));
    }

    [Fact]
    public void FormatErrors_writes_a_header_and_one_line_per_error()
    {
        ConfigurationError[] errors =
        [
            new("DISCORD_TOKEN", ConfigurationErrorKind.Missing, "is required."),
            new("MAX_MESSAGES_PER_READ", ConfigurationErrorKind.OutOfRange, "must be between 1 and 100."),
        ];

        Assert.Equal(
            "Guildsmith: the configuration is invalid, the server did not start.\n"
            + "  DISCORD_TOKEN: is required.\n"
            + "  MAX_MESSAGES_PER_READ: must be between 1 and 100.\n",
            ServerStartup.FormatErrors(errors));
    }

    [Fact]
    public void FormatRefusal_redacts_the_token_from_an_error_message_that_holds_it()
    {
        ConfigurationError[] errors = [new("DISCORD_TOKEN", ConfigurationErrorKind.Invalid, $"echoes {Token}.")];

        string text = ServerStartup.FormatRefusal(errors, Token);

        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
        Assert.Contains("echoes [REDACTED].", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void FormatRefusal_returns_the_formatted_errors_unchanged_when_no_token_was_read(string? token)
    {
        ConfigurationError[] errors = [new("DISCORD_TOKEN", ConfigurationErrorKind.Missing, "is required.")];

        Assert.Equal(ServerStartup.FormatErrors(errors), ServerStartup.FormatRefusal(errors, token));
    }

    [Fact]
    public async Task RunAsync_returns_zero_and_writes_nothing_when_the_host_runs_to_completion()
    {
        StringWriter stderr = new();

        int exitCode = await ServerStartup.RunAsync(stderr, ValidBuilder(), static _ => Task.CompletedTask);

        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_writes_a_host_failure_redacted_to_stderr_and_returns_the_host_failure_exit_code()
    {
        StringWriter stderr = new();

        int exitCode = await ServerStartup.RunAsync(
            stderr, ValidBuilder(), static _ => throw new InvalidOperationException($"host-marker {Token}"));

        string text = stderr.ToString();
        Assert.Equal(ServerStartup.HostFailureExitCode, exitCode);
        Assert.NotEqual("", text);
        Assert.Contains("host-marker [REDACTED]", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_writes_a_host_build_failure_redacted_to_stderr_and_returns_the_host_failure_exit_code()
    {
        // The test above throws from run, so builder.Build() moved out of the try went unnoticed:
        // a build failure then escaped RunAsync and the runtime printed it, token included.
        StringWriter stderr = new();
        HostApplicationBuilder builder = ValidBuilder();
        builder.ConfigureContainer(new ThrowingServiceProviderFactory(
            new InvalidOperationException("build-outer", new InvalidOperationException($"build-marker {Token}"))));

        bool ran = false;

        int exitCode = await ServerStartup.RunAsync(stderr, builder, _ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        string text = stderr.ToString();
        Assert.False(ran);
        Assert.Equal(ServerStartup.HostFailureExitCode, exitCode);
        Assert.NotEqual("", text);
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
        Assert.Contains("build-marker [REDACTED]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void HostFailureExitCode_is_distinct_from_success_and_from_the_invalid_configuration_exit_code()
    {
        Assert.Equal(2, ServerStartup.HostFailureExitCode);
        Assert.NotEqual(ServerStartup.InvalidConfigurationExitCode, ServerStartup.HostFailureExitCode);
    }

    [Fact]
    public async Task RunAsync_refuses_an_invalid_configuration_without_running_the_host()
    {
        StringWriter stderr = new();
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DISCORD_TOKEN"] = Token });
        bool ran = false;

        int exitCode = await ServerStartup.RunAsync(stderr, builder, _ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        Assert.Equal(ServerStartup.InvalidConfigurationExitCode, exitCode);
        Assert.False(ran);
        Assert.StartsWith("Guildsmith: the configuration is invalid", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Token, stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_redacts_the_refusal_text_with_the_token_that_was_read()
    {
        // No reader message echoes a value, so the token is chosen to equal a variable name the
        // refusal text prints: that is the only way the refused path can be shown to redact.
        StringWriter stderr = new();
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DISCORD_TOKEN"] = "ALLOW_DESTRUCTIVE",
            ["ALLOWED_GUILD_IDS"] = "1",
            ["ALLOW_DESTRUCTIVE"] = "maybe",
        });

        int exitCode = await ServerStartup.RunAsync(stderr, builder, static _ => Task.CompletedTask);

        Assert.Equal(ServerStartup.InvalidConfigurationExitCode, exitCode);
        Assert.Contains("  [REDACTED]: ", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("ALLOW_DESTRUCTIVE", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_rejects_a_null_stderr_writer()
    {
        // An empty configuration is refused before the logger provider would reject the writer.
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        await Assert.ThrowsAsync<ArgumentNullException>(() => ServerStartup.RunAsync(null!, builder, static _ => Task.CompletedTask));
    }

    [Fact]
    public async Task RunAsync_rejects_a_null_builder()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ServerStartup.RunAsync(new StringWriter(), null!, static _ => Task.CompletedTask));
    }

    [Fact]
    public async Task RunAsync_rejects_a_null_run_delegate()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ServerStartup.RunAsync(new StringWriter(), ValidBuilder(), null!));
    }

    /// <summary>A container factory whose service provider cannot be built: it throws <c>exception</c>.</summary>
    private sealed class ThrowingServiceProviderFactory(Exception exception) : IServiceProviderFactory<IServiceCollection>
    {
        public IServiceCollection CreateBuilder(IServiceCollection services) => services;

        public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder) => throw exception;
    }

    private static HostApplicationBuilder ValidBuilder()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DISCORD_TOKEN"] = Token,
            ["ALLOWED_GUILD_IDS"] = "1",
        });
        return builder;
    }

    private static bool IsDiscordNet(Type? type) =>
        type?.Assembly.GetName().Name?.StartsWith("Discord.Net", StringComparison.Ordinal) == true;

    private static IHostEnvironment Environment(string name)
    {
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }

    private static GuildsmithOptions Options(LogLevel level) => new()
    {
        DiscordToken = Token,
        AllowedGuildIds = new HashSet<ulong> { 1 },
        Mode = DiscordMcpMode.ReadWrite,
        AllowDestructive = false,
        AllowAdminGrant = false,
        MaxMessagesPerRead = 50,
        Transport = McpTransport.Stdio,
        LogLevel = level,
    };

    private static IHost BuildHost(GuildsmithOptions options) => BuildHost(options, TextWriter.Null);

    private static IHost BuildHost(GuildsmithOptions options, TextWriter stderr)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        ServerStartup.ConfigureServices(builder, options, stderr);
        return builder.Build();
    }
}

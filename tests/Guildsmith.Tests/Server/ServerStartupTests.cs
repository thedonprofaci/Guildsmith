using Guildsmith.Core.Configuration;
using Guildsmith.Server;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

using NSubstitute;

namespace Guildsmith.Tests.Server;

public sealed class ServerStartupTests
{
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
    public void ConfigureServices_sends_every_log_level_to_stderr_through_the_console_logger_only()
    {
        using IHost host = BuildHost(Options(LogLevel.Information));

        Assert.Equal([typeof(ConsoleLoggerProvider)], host.Services.GetServices<ILoggerProvider>().Select(p => p.GetType()));
        Assert.Equal(
            LogLevel.Trace,
            host.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue.LogToStandardErrorThreshold);
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
    public void ConfigureServices_serves_an_empty_tool_collection()
    {
        using IHost host = BuildHost(Options(LogLevel.Information));

        McpServerPrimitiveCollection<McpServerTool>? tools =
            host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection;
        Assert.NotNull(tools);
        Assert.Empty(tools);
    }

    [Fact]
    public void ConfigureServices_registers_no_discord_net_type()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        ServerStartup.ConfigureServices(builder, Options(LogLevel.Information));

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
        DiscordToken = "FAKE-TOKEN-guildsmith-test-0001",
        AllowedGuildIds = new HashSet<ulong> { 1 },
        Mode = DiscordMcpMode.ReadWrite,
        AllowDestructive = false,
        AllowAdminGrant = false,
        MaxMessagesPerRead = 50,
        Transport = McpTransport.Stdio,
        LogLevel = level,
    };

    private static IHost BuildHost(GuildsmithOptions options)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        ServerStartup.ConfigureServices(builder, options);
        return builder.Build();
    }
}

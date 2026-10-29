using Discord.Rest;

using Guildsmith.Core.Audit;
using Guildsmith.Core.Configuration;
using Guildsmith.Core.Gateway;
using Guildsmith.Core.Guards;
using Guildsmith.Core.Security;
using Guildsmith.Core.Services;
using Guildsmith.Discord;
using Guildsmith.Server.Logging;
using Guildsmith.Server.Tools;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Guildsmith.Server;

/// <summary>
/// Builds and runs the stdio MCP host. In stdio mode stdout belongs to the MCP protocol: every
/// diagnostic, the configuration errors included, goes to stderr.
/// </summary>
public static class ServerStartup
{
    /// <summary>The exit code of a startup refused because the configuration is invalid.</summary>
    public const int InvalidConfigurationExitCode = 1;

    /// <summary>The exit code of a server stopped by an exception from building or running the host.</summary>
    public const int HostFailureExitCode = 2;

    /// <summary>
    /// Validates the configuration, then serves MCP over stdio until the client disconnects.
    /// Startup constructs no Discord client and makes no network call.
    /// </summary>
    public static Task<int> RunAsync(TextWriter stderr)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = ResolveEnvironmentName(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")),
        });
        AddConfigurationSources(builder.Configuration, builder.Environment);

        return RunAsync(stderr, builder, static host => host.RunAsync());
    }

    /// <summary>
    /// <see cref="RunAsync(TextWriter)"/> over a prepared <paramref name="builder"/>, with
    /// <paramref name="run"/> in place of <see cref="HostingAbstractionsHostExtensions.RunAsync"/>.
    /// Everything it writes to <paramref name="stderr"/> is redacted.
    /// </summary>
    public static async Task<int> RunAsync(TextWriter stderr, HostApplicationBuilder builder, Func<IHost, Task> run)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(run);

        ConfigurationResult result = GuildsmithOptionsReader.Read(name => builder.Configuration[name]);
        if (result.Options is null)
        {
            await stderr.WriteAsync(FormatRefusal(result.Errors, builder.Configuration[GuildsmithOptionsReader.TokenVariable]));
            return InvalidConfigurationExitCode;
        }

        ConfigureServices(builder, result.Options, stderr);
        TokenRedactor redactor = new(result.Options.DiscordToken);
        try
        {
            using IHost host = builder.Build();
            await run(host);
        }
        catch (Exception exception)
        {
            // Without this catch the runtime prints the unhandled exception, unredacted, to stderr.
            await stderr.WriteAsync(
                "Guildsmith: the server stopped on an unexpected error.\n" + redactor.RedactException(exception) + "\n");
            return HostFailureExitCode;
        }

        return 0;
    }

    /// <summary>
    /// The host environment name from <c>DOTNET_ENVIRONMENT</c>; <c>Production</c> when it is
    /// unset or blank, so user secrets are only ever read on an explicit opt-in.
    /// </summary>
    public static string ResolveEnvironmentName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Environments.Production : value;

    /// <summary>
    /// Environment variables, plus user secrets only in <c>Development</c>. No JSON settings file
    /// is read, so no committed file can carry a token (spec section 5).
    /// </summary>
    public static void AddConfigurationSources(IConfigurationBuilder configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            configuration.AddUserSecrets(typeof(ServerStartup).Assembly, optional: true, reloadOnChange: false);
        }

        // Added last, so a variable set in the environment wins over a user secret.
        configuration.AddEnvironmentVariables();
    }

    /// <summary>
    /// Registers the validated options, logging to <paramref name="stderr"/> through the
    /// redacting provider only, with the audit category kept at <see cref="LogLevel.Information"/>
    /// whatever the minimum level is, the Discord gateway (which logs in on its first call, not here),
    /// the guard pipeline, the services, and the MCP server with its tools.
    /// </summary>
    public static void ConfigureServices(HostApplicationBuilder builder, GuildsmithOptions options, TextWriter stderr)
    {
        builder.Services.AddSingleton(options);

        // The one provider: every line, from this code and from the MCP SDK alike, is redacted
        // before it is written. A second provider added here would print the token unredacted.
        builder.Logging.AddProvider(new RedactingLoggerProvider(new TokenRedactor(options.DiscordToken), stderr));
        builder.Logging.SetMinimumLevel(options.LogLevel);

        // The audit entries of spec section 7.8 are kept whatever LOG_LEVEL says: an operator who
        // set it to Error to quiet the server must still see every write the server made.
        builder.Logging.AddFilter(AuditEntry.Category, LogLevel.Information);

        builder.Services.AddSingleton<IDiscordRestApi>(services =>
            new DiscordRestApi(options.DiscordToken, new DiscordRestConfig(), services.GetRequiredService<ILogger<DiscordRestApi>>()));
        builder.Services.AddSingleton<IDiscordGateway, DiscordGateway>();

        // The logger factory constructor: the two-argument one logs nothing, so a pipeline built
        // with it would drop every audit entry and every internal_error detail without a sign.
        builder.Services.AddSingleton(services => new GuardPipeline(
            options, services.GetRequiredService<IDiscordGateway>(), services.GetRequiredService<ILoggerFactory>()));
        builder.Services.AddSingleton<GuildService>();
        builder.Services.AddSingleton<ChannelService>();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<ListGuildsTool>()
            .WithTools<GetGuildInfoTool>()
            .WithTools<ListChannelsTool>()
            .WithTools<CreateChannelTool>()
            .WithTools<EditChannelTool>()
            .WithTools<DeleteChannelTool>();
    }

    /// <summary>
    /// <see cref="FormatErrors"/> redacted for <paramref name="token"/>, the raw <c>DISCORD_TOKEN</c>
    /// value, when one was read; unchanged when it is missing or blank.
    /// </summary>
    public static string FormatRefusal(IReadOnlyList<ConfigurationError> errors, string? token)
    {
        string text = FormatErrors(errors);
        return string.IsNullOrWhiteSpace(token) ? text : new TokenRedactor(token).Redact(text);
    }

    /// <summary>The stderr text for a refused startup: one line per rejected variable.</summary>
    public static string FormatErrors(IReadOnlyList<ConfigurationError> errors)
    {
        System.Text.StringBuilder text = new();
        text.Append("Guildsmith: the configuration is invalid, the server did not start.\n");
        foreach (ConfigurationError error in errors)
        {
            text.Append("  ").Append(error.Variable).Append(": ").Append(error.Message).Append('\n');
        }

        return text.ToString();
    }
}

using Guildsmith.Core.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Guildsmith.Server;

/// <summary>
/// Builds and runs the stdio MCP host. In stdio mode stdout belongs to the MCP protocol: every
/// diagnostic, the configuration errors included, goes to stderr.
/// </summary>
public static class ServerStartup
{
    /// <summary>The exit code of a startup refused because the configuration is invalid.</summary>
    public const int InvalidConfigurationExitCode = 1;

    /// <summary>
    /// Validates the configuration, then serves MCP over stdio until the client disconnects.
    /// Startup constructs no Discord client and makes no network call.
    /// </summary>
    public static async Task<int> RunAsync(TextWriter stderr)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = ResolveEnvironmentName(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")),
        });
        AddConfigurationSources(builder.Configuration, builder.Environment);

        ConfigurationResult result = GuildsmithOptionsReader.Read(name => builder.Configuration[name]);
        if (result.Options is null)
        {
            await stderr.WriteAsync(FormatErrors(result.Errors));
            return InvalidConfigurationExitCode;
        }

        ConfigureServices(builder, result.Options);
        using IHost host = builder.Build();
        await host.RunAsync();
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

    /// <summary>Registers the validated options, stderr-only logging and the MCP server with no tools.</summary>
    public static void ConfigureServices(HostApplicationBuilder builder, GuildsmithOptions options)
    {
        builder.Services.AddSingleton(options);

        builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(options.LogLevel);

        // An empty tool collection, not none: without one the server does not advertise the
        // tools capability, and tools/list fails with "Method 'tools/list' is not available".
        builder.Services
            .AddMcpServer(server => server.ToolCollection = [])
            .WithStdioServerTransport();
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

using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

using Guildsmith.Tests.Support;

using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Guildsmith.Tests.Server;

/// <summary>
/// Process-level tests of <c>Program.cs</c> / <c>ServerStartup.RunAsync</c>: the built server is
/// started as a child process with an explicit, non-inherited environment and a fake token.
/// Nothing here has network access to Discord, and nothing needs it.
/// </summary>
public sealed class ProgramTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(60);

    private static readonly Dictionary<string, string> s_validSettings = new()
    {
        ["DISCORD_TOKEN"] = ServerProcess.FakeToken,
        ["ALLOWED_GUILD_IDS"] = "123456789012345678",
    };

    [Fact]
    public async Task RunAsync_lists_no_tools_to_an_sdk_stdio_client()
    {
        using CancellationTokenSource cancel = new(s_timeout);
        List<string> stderr = [];
        StdioClientTransport transport = new(new StdioClientTransportOptions
        {
            Command = ServerProcess.DotnetHost,
            Arguments = [ServerProcess.ServerDll],
            InheritEnvironmentVariables = false,
            EnvironmentVariables = ServerProcess.Environment(s_validSettings),
            StandardErrorLines = line =>
            {
                lock (stderr)
                {
                    stderr.Add(line);
                }
            },
        });

        IList<McpClientTool> tools;
        await using (McpClient client = await McpClient.CreateAsync(transport, new McpClientOptions(), NullLoggerFactory.Instance, cancel.Token))
        {
            tools = await client.ListToolsAsync(new RequestOptions(), cancel.Token);
        }

        Assert.Empty(tools);
        lock (stderr)
        {
            // The server did log (the lifetime and transport lines), so the absence below is
            // measured on real stderr output, not on an empty capture.
            Assert.Contains(stderr, line => line.Contains("Hosting environment: Production", StringComparison.Ordinal));
            Assert.DoesNotContain(stderr, line => line.Contains(ServerProcess.FakeToken, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task RunAsync_writes_only_json_rpc_messages_to_stdout_during_a_session()
    {
        using CancellationTokenSource cancel = new(s_timeout);
        using Process server = Process.Start(ServerProcess.StartInfo(s_validSettings))!;
        Task<string> stderr = server.StandardError.ReadToEndAsync(cancel.Token);

        // The client reads stdout through a pipe while every byte is also kept, so the
        // assertion below sees exactly what the server wrote, not what the client accepted.
        Pipe toClient = new();
        MemoryStream stdout = new();
        Task pump = Task.Run(
            async () =>
            {
                byte[] buffer = new byte[4096];
                int read;
                while ((read = await server.StandardOutput.BaseStream.ReadAsync(buffer, cancel.Token)) > 0)
                {
                    stdout.Write(buffer, 0, read);
                    await toClient.Writer.WriteAsync(buffer.AsMemory(0, read), cancel.Token);
                }

                await toClient.Writer.CompleteAsync();
            },
            cancel.Token);

        StreamClientTransport transport = new(server.StandardInput.BaseStream, toClient.Reader.AsStream(), NullLoggerFactory.Instance);
        await using (McpClient client = await McpClient.CreateAsync(transport, new McpClientOptions(), NullLoggerFactory.Instance, cancel.Token))
        {
            Assert.Empty(await client.ListToolsAsync(new RequestOptions(), cancel.Token));
        }

        server.StandardInput.Close();
        await server.WaitForExitAsync(cancel.Token);
        await pump;
        string output = Encoding.UTF8.GetString(stdout.ToArray());

        // Exactly the two responses the client asked for: initialize and tools/list.
        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line =>
        {
            using JsonDocument message = JsonDocument.Parse(line);
            Assert.Equal("2.0", message.RootElement.GetProperty("jsonrpc").GetString());
        });
        Assert.DoesNotContain(ServerProcess.FakeToken, output, StringComparison.Ordinal);
        Assert.DoesNotContain(ServerProcess.FakeToken, await stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_refuses_an_invalid_configuration_on_stderr_with_nothing_on_stdout()
    {
        using CancellationTokenSource cancel = new(s_timeout);
        Dictionary<string, string> settings = new(s_validSettings) { ["ALLOW_DESTRUCTIVE"] = "yes" };
        using Process server = Process.Start(ServerProcess.StartInfo(settings))!;
        server.StandardInput.Close();

        MemoryStream stdout = new();
        Task copy = server.StandardOutput.BaseStream.CopyToAsync(stdout, cancel.Token);
        string stderr = await server.StandardError.ReadToEndAsync(cancel.Token);
        await copy;
        await server.WaitForExitAsync(cancel.Token);

        Assert.Equal(1, server.ExitCode);
        Assert.Equal(0, stdout.Length);
        Assert.Contains("ALLOW_DESTRUCTIVE", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(ServerProcess.FakeToken, stderr, StringComparison.Ordinal);
    }

    // Review tests (Tester, PR #13). The criterion "user secrets only when the host environment is
    // Development" was held only by a source-list inspection of AddConfigurationSources; nothing
    // ran the built server with DOTNET_ENVIRONMENT=Development, so RunAsync ignoring the variable
    // entirely (environment always Production, secrets never read) left the suite green.
    // APPDATA points the user-secrets lookup at a scratch directory, so the developer's real
    // secrets are never read. ALLOW_DESTRUCTIVE=yes keeps both runs on the refusal path.

    [Fact]
    public async Task RunAsync_reads_user_secrets_when_the_environment_is_development()
    {
        string[] rejected = await RejectedVariablesWithSecretsFileAsync("Development");

        // ALLOWED_GUILD_IDS came from secrets.json, so only the environment's bad flag is left.
        Assert.Equal(["ALLOW_DESTRUCTIVE"], rejected);
    }

    [Fact]
    public async Task RunAsync_ignores_user_secrets_outside_development()
    {
        string[] rejected = await RejectedVariablesWithSecretsFileAsync("Production");

        Assert.Equal(["ALLOWED_GUILD_IDS", "ALLOW_DESTRUCTIVE"], rejected);
    }

    /// <summary>
    /// Runs the built server with a secrets.json holding only <c>ALLOWED_GUILD_IDS</c>, the given
    /// environment name, and an invalid <c>ALLOW_DESTRUCTIVE</c>; returns the variables named on stderr.
    /// </summary>
    private static async Task<string[]> RejectedVariablesWithSecretsFileAsync(string environmentName)
    {
        string appData = Path.Combine(Path.GetTempPath(), "guildsmith-review-" + Guid.NewGuid().ToString("N"));
        string secretsId = typeof(Guildsmith.Server.ServerStartup).Assembly
            .GetCustomAttributes(typeof(Microsoft.Extensions.Configuration.UserSecrets.UserSecretsIdAttribute), false)
            .Cast<Microsoft.Extensions.Configuration.UserSecrets.UserSecretsIdAttribute>()
            .Single().UserSecretsId;
        string secretsDirectory = Path.Combine(appData, "Microsoft", "UserSecrets", secretsId);
        Directory.CreateDirectory(secretsDirectory);
        await File.WriteAllTextAsync(Path.Combine(secretsDirectory, "secrets.json"), "{ \"ALLOWED_GUILD_IDS\": \"123456789012345678\" }");
        try
        {
            using CancellationTokenSource cancel = new(s_timeout);
            Dictionary<string, string> settings = new()
            {
                ["DISCORD_TOKEN"] = ServerProcess.FakeToken,
                ["ALLOW_DESTRUCTIVE"] = "yes",
                ["DOTNET_ENVIRONMENT"] = environmentName,
                ["APPDATA"] = appData,
            };
            using Process server = Process.Start(ServerProcess.StartInfo(settings))!;
            server.StandardInput.Close();
            Task<string> stdout = server.StandardOutput.ReadToEndAsync(cancel.Token);
            string stderr = await server.StandardError.ReadToEndAsync(cancel.Token);
            await server.WaitForExitAsync(cancel.Token);

            Assert.Equal(1, server.ExitCode);
            Assert.Equal(string.Empty, await stdout);
            return stderr.Split('\n')
                .Where(line => line.StartsWith("  ", StringComparison.Ordinal))
                .Select(line => line.Trim().Split(':')[0])
                .ToArray();
        }
        finally
        {
            Directory.Delete(appData, recursive: true);
        }
    }
}

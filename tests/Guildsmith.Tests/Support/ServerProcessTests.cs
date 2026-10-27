using System.Diagnostics;

namespace Guildsmith.Tests.Support;

/// <summary>
/// The process helper is test-side code with no other test of its own; these pin the clauses
/// the process tests depend on (core/clause-inventory-for-new-test-code).
/// </summary>
public sealed class ServerProcessTests
{
    [Fact]
    public void DotnetHost_names_the_dotnet_executable_that_exists_on_disk()
    {
        string expectedName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        Assert.Equal(expectedName, Path.GetFileName(ServerProcess.DotnetHost));
        Assert.True(File.Exists(ServerProcess.DotnetHost), ServerProcess.DotnetHost);
    }

    [Fact]
    public void ServerDll_points_at_the_built_server_next_to_the_tests()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Guildsmith.Server.dll"), ServerProcess.ServerDll);
        Assert.True(File.Exists(ServerProcess.ServerDll), ServerProcess.ServerDll);
    }

    [Fact]
    public void Environment_does_not_forward_a_parent_discord_variable_and_forces_production()
    {
        // Set in this process exactly as a developer shell would have them.
        System.Environment.SetEnvironmentVariable("DISCORD_TOKEN", "PARENT-TOKEN-must-not-leak");
        System.Environment.SetEnvironmentVariable("ALLOWED_GUILD_IDS", "999");
        System.Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        try
        {
            Dictionary<string, string?> environment = ServerProcess.Environment(new Dictionary<string, string>
            {
                ["MCP_TRANSPORT"] = "stdio",
            });

            Assert.False(environment.ContainsKey("DISCORD_TOKEN"));
            Assert.False(environment.ContainsKey("ALLOWED_GUILD_IDS"));
            Assert.Equal("Production", environment["DOTNET_ENVIRONMENT"]);
            Assert.Equal("stdio", environment["MCP_TRANSPORT"]);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("DISCORD_TOKEN", null);
            System.Environment.SetEnvironmentVariable("ALLOWED_GUILD_IDS", null);
            System.Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);
        }
    }

    [Fact]
    public void Environment_lets_a_setting_override_the_production_default()
    {
        Dictionary<string, string?> environment = ServerProcess.Environment(new Dictionary<string, string>
        {
            ["DOTNET_ENVIRONMENT"] = "Staging",
        });

        Assert.Equal("Staging", environment["DOTNET_ENVIRONMENT"]);
    }

    [Fact]
    public void StartInfo_runs_the_server_dll_with_exactly_the_built_environment()
    {
        Dictionary<string, string> settings = new() { ["DISCORD_TOKEN"] = ServerProcess.FakeToken };
        System.Environment.SetEnvironmentVariable("GUILDSMITH_TEST_PARENT_ONLY", "parent");
        try
        {
            ProcessStartInfo start = ServerProcess.StartInfo(settings);

            Assert.Equal(ServerProcess.DotnetHost, start.FileName);
            Assert.Equal([ServerProcess.ServerDll], start.ArgumentList);
            Assert.False(start.UseShellExecute);
            Assert.True(start.RedirectStandardInput);
            Assert.True(start.RedirectStandardOutput);
            Assert.True(start.RedirectStandardError);
            Assert.Equal(
                ServerProcess.Environment(settings).OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase),
                start.Environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase));
            Assert.False(start.Environment.ContainsKey("GUILDSMITH_TEST_PARENT_ONLY"));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("GUILDSMITH_TEST_PARENT_ONLY", null);
        }
    }
}

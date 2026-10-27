using System.Diagnostics;
using System.Runtime.InteropServices;

using ModelContextProtocol.Client;

namespace Guildsmith.Tests.Support;

/// <summary>
/// Starts the built server as a child process with an environment the test spells out in full.
/// The child never inherits the developer's environment: a real <c>DISCORD_TOKEN</c> in the
/// shell or in user secrets must not reach a test run or its output.
/// </summary>
internal static class ServerProcess
{
    /// <summary>Recognisable on sight, and asserted absent from every output.</summary>
    public const string FakeToken = "FAKE-TOKEN-guildsmith-test-0001";

    /// <summary>
    /// The dotnet host that runs this test process: the runtime directory is
    /// <c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;/</c>, three levels below the host.
    /// </summary>
    public static string DotnetHost =>
        Path.GetFullPath(Path.Combine(
            RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));

    /// <summary>The server assembly the test project's build copied next to the tests.</summary>
    public static string ServerDll => Path.Combine(AppContext.BaseDirectory, "Guildsmith.Server.dll");

    /// <summary>
    /// The child's whole environment: the SDK's curated system variables (PATH, SYSTEMROOT, HOME…),
    /// <c>DOTNET_ENVIRONMENT=Production</c> so user secrets are never read, then
    /// <paramref name="settings"/>.
    /// </summary>
    public static Dictionary<string, string?> Environment(IReadOnlyDictionary<string, string> settings)
    {
        Dictionary<string, string?> environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["DOTNET_ENVIRONMENT"] = "Production";
        foreach (KeyValuePair<string, string> setting in settings)
        {
            environment[setting.Key] = setting.Value;
        }

        return environment;
    }

    /// <summary>A start description with every stream redirected and nothing inherited.</summary>
    public static ProcessStartInfo StartInfo(IReadOnlyDictionary<string, string> settings)
    {
        ProcessStartInfo start = new(DotnetHost)
        {
            ArgumentList = { ServerDll },
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment.Clear();
        foreach (KeyValuePair<string, string?> variable in Environment(settings))
        {
            start.Environment[variable.Key] = variable.Value;
        }

        return start;
    }
}

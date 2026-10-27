using System.Diagnostics;

using Guildsmith.Tests.Support;

namespace Guildsmith.Tests.TestCountFloor;

/// <summary>
/// The executable CI runs, as a process: its exit code is the only thing the workflow step reads.
/// </summary>
public sealed class ProgramTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("guildsmith-floor-exe-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private (int ExitCode, string Stdout, string Stderr) RunExecutable(string trxExecuted, string floor)
    {
        string results = Directory.CreateDirectory(Path.Combine(_root, "results")).FullName;
        File.WriteAllText(
            Path.Combine(results, "test-results.trx"),
            $"<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><ResultSummary><Counters total=\"9\" executed=\"{trxExecuted}\" /></ResultSummary></TestRun>");
        string floorFile = Path.Combine(_root, "floor.txt");
        File.WriteAllText(floorFile, floor);

        ProcessStartInfo start = new(ServerProcess.DotnetHost)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Guildsmith.TestCountFloor.dll"));
        start.ArgumentList.Add(results);
        start.ArgumentList.Add(floorFile);

        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)), "the floor check did not exit");
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    [Fact]
    public void Executable_exits_1_below_the_floor()
    {
        (int exitCode, string stdout, string stderr) = RunExecutable(trxExecuted: "7", floor: "8\n");

        Assert.Equal(1, exitCode);
        Assert.Equal("test-count floor: executed 7 is below the floor 8" + Environment.NewLine, stderr);
        Assert.Equal("", stdout);
    }

    [Fact]
    public void Executable_exits_0_at_the_floor()
    {
        (int exitCode, string stdout, string stderr) = RunExecutable(trxExecuted: "8", floor: "8\n");

        Assert.Equal(0, exitCode);
        Assert.Equal("test-count floor: executed 8, floor 8" + Environment.NewLine, stdout);
        Assert.Equal("", stderr);
    }
}

using Guildsmith.TestCountFloor;

namespace Guildsmith.Tests.TestCountFloor;

/// <summary>
/// The floor check is test-side code delivered by a gate round; every fail-closed case has its
/// own test (core/fail-closed-guards, core/clause-inventory-for-new-test-code).
/// </summary>
public sealed class FloorCheckTests : IDisposable
{
    // The shape of a real VSTest .trx (SDK 9.0.318), cut to the elements the check reads, with
    // total, executed and passed deliberately different. Built on one line so the fixture has no
    // line endings to differ between a Windows checkout and the Linux CI.
    private const string RealCounters =
        "<Counters total=\"130\" executed=\"124\" passed=\"120\" failed=\"4\" error=\"0\" timeout=\"0\" aborted=\"0\" inconclusive=\"0\" passedButRunAborted=\"0\" notRunnable=\"0\" notExecuted=\"6\" disconnected=\"0\" warning=\"0\" completed=\"0\" inProgress=\"0\" pending=\"0\" />";

    private readonly string _root = Directory.CreateTempSubdirectory("guildsmith-floor-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Trx(string resultSummaryContent) =>
        $"<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun id=\"b018726e\" name=\"run\" xmlns=\"{FloorCheck.TrxNamespace}\"><Times start=\"2026-10-27T17:34:59\" /><ResultSummary outcome=\"Completed\">{resultSummaryContent}</ResultSummary></TestRun>";

    private string Write(string relativePath, string content)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // --- ReadFloor ---

    [Fact]
    public void ReadFloor_reads_the_integer_and_ignores_the_trailing_newline()
    {
        Assert.Equal(124, FloorCheck.ReadFloor(Write("floor.txt", "124\n")));
    }

    [Fact]
    public void ReadFloor_rejects_a_missing_file()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadFloor(Path.Combine(_root, "absent.txt")));
    }

    [Fact]
    public void ReadFloor_rejects_an_empty_file()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadFloor(Write("floor.txt", "")));
    }

    [Fact]
    public void ReadFloor_rejects_a_whitespace_only_file()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadFloor(Write("floor.txt", " \n")));
    }

    [Fact]
    public void ReadFloor_rejects_a_non_numeric_value()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadFloor(Write("floor.txt", "about 124\n")));
    }

    [Fact]
    public void ReadFloor_rejects_a_signed_value()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadFloor(Write("floor.txt", "+124\n")));
    }

    // Leading whitespace is the half of "surrounding whitespace allowed" that a TrimEnd would
    // drop: a floor file written with a leading space would then fail as unparseable.
    [Fact]
    public void ReadFloor_reads_the_integer_and_ignores_leading_whitespace()
    {
        Assert.Equal(124, FloorCheck.ReadFloor(Write("floor.txt", " 124")));
    }

    // Under AllowThousands "1,000" would read as a floor of 1000 instead of failing.
    [Fact]
    public void ReadFloor_rejects_a_thousands_separated_value()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadFloor(Write("floor.txt", "1,000\n")));
    }

    [Fact]
    public void ReadFloor_rejects_zero_because_a_zero_floor_is_no_floor()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadFloor(Write("floor.txt", "0\n")));
    }

    [Fact]
    public void ReadFloor_accepts_one_the_smallest_floor()
    {
        Assert.Equal(1, FloorCheck.ReadFloor(Write("floor.txt", "1\n")));
    }

    // --- FindSingleTrx ---

    [Fact]
    public void FindSingleTrx_returns_the_one_trx_and_ignores_other_files_and_subdirectories()
    {
        Write("results/test-results.trx", Trx(RealCounters));
        Write("results/coverage.xml", "<x />");
        Write("results/nested/older.trx", Trx(RealCounters));

        Assert.Equal(Path.Combine(_root, "results", "test-results.trx"), FloorCheck.FindSingleTrx(Path.Combine(_root, "results")));
    }

    [Fact]
    public void FindSingleTrx_rejects_a_missing_directory()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.FindSingleTrx(Path.Combine(_root, "absent")));
    }

    [Fact]
    public void FindSingleTrx_rejects_a_directory_with_no_trx()
    {
        Write("results/coverage.xml", "<x />");

        Assert.Throws<FloorCheckException>(() => FloorCheck.FindSingleTrx(Path.Combine(_root, "results")));
    }

    [Fact]
    public void FindSingleTrx_rejects_two_trx_files()
    {
        Write("results/a.trx", Trx(RealCounters));
        Write("results/b.trx", Trx(RealCounters));

        Assert.Throws<FloorCheckException>(() => FloorCheck.FindSingleTrx(Path.Combine(_root, "results")));
    }

    // --- ReadExecuted ---

    [Fact]
    public void ReadExecuted_reads_executed_not_total_or_passed()
    {
        Assert.Equal(124, FloorCheck.ReadExecuted(Write("r.trx", Trx(RealCounters))));
    }

    [Fact]
    public void ReadExecuted_rejects_a_missing_counters_element()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadExecuted(Write("r.trx", Trx(""))));
    }

    [Fact]
    public void ReadExecuted_rejects_two_counters_elements()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadExecuted(Write("r.trx", Trx(RealCounters + RealCounters))));
    }

    [Fact]
    public void ReadExecuted_rejects_counters_outside_the_trx_namespace()
    {
        string noNamespace = "<TestRun><ResultSummary outcome=\"Completed\">" + RealCounters + "</ResultSummary></TestRun>";

        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadExecuted(Write("r.trx", noNamespace)));
    }

    [Fact]
    public void ReadExecuted_rejects_counters_outside_result_summary()
    {
        string misplaced = $"<TestRun xmlns=\"{FloorCheck.TrxNamespace}\">{RealCounters}<ResultSummary outcome=\"Completed\" /></TestRun>";

        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadExecuted(Write("r.trx", misplaced)));
    }

    [Fact]
    public void ReadExecuted_rejects_counters_nested_below_result_summary()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadExecuted(Write("r.trx", Trx($"<RunInfos>{RealCounters}</RunInfos>"))));
    }

    [Fact]
    public void ReadExecuted_rejects_a_missing_executed_attribute()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadExecuted(Write("r.trx", Trx("<Counters total=\"130\" passed=\"120\" />"))));
    }

    [Fact]
    public void ReadExecuted_rejects_a_non_numeric_executed_attribute()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadExecuted(Write("r.trx", Trx("<Counters total=\"130\" executed=\"n/a\" />"))));
    }

    // ReadExecuted does not trim: the executed attribute is written by VSTest as bare digits, so
    // anything else is an input the check does not recognise and must fail, not be read.
    // ReadFloor's `floor < 1` would also catch "-1"; ReadExecuted has no such check, so only
    // NumberStyles.None keeps a negative executed count from reaching the comparison.
    [Fact]
    public void ReadExecuted_rejects_a_negative_executed_attribute()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadExecuted(Write("r.trx", Trx("<Counters total=\"1\" executed=\"-1\" />"))));
    }

    [Fact]
    public void ReadExecuted_rejects_a_thousands_separated_executed_attribute()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadExecuted(Write("r.trx", Trx("<Counters total=\"1000\" executed=\"1,000\" />"))));
    }

    [Fact]
    public void ReadExecuted_rejects_an_executed_attribute_with_leading_whitespace()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadExecuted(Write("r.trx", Trx("<Counters total=\"5\" executed=\" 5\" />"))));
    }

    [Fact]
    public void ReadExecuted_rejects_an_executed_attribute_with_trailing_whitespace()
    {
        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadExecuted(Write("r.trx", Trx("<Counters total=\"5\" executed=\"5 \" />"))));
    }

    [Fact]
    public void ReadExecuted_rejects_malformed_xml()
    {
        string truncated = Trx(RealCounters)[..^5];

        Assert.Throws<FloorCheckException>(() => FloorCheck.ReadExecuted(Write("r.trx", truncated)));
    }

    // --- Run ---

    private (int ExitCode, string Stdout, string Stderr) RunWith(string floor, string? trxContent)
    {
        string results = Path.Combine(_root, "results");
        Directory.CreateDirectory(results);
        if (trxContent is not null)
        {
            Write("results/test-results.trx", trxContent);
        }

        string floorFile = Write("floor.txt", floor);
        StringWriter stdout = new();
        StringWriter stderr = new();
        int exitCode = FloorCheck.Run([results, floorFile], stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Run_fails_below_the_floor_with_one_line_naming_both_counts()
    {
        (int exitCode, string stdout, string stderr) = RunWith("125\n", Trx(RealCounters));

        Assert.Equal(1, exitCode);
        Assert.Equal("test-count floor: executed 124 is below the floor 125" + Environment.NewLine, stderr);
        Assert.Equal("", stdout);
    }

    [Fact]
    public void Run_compares_executed_not_total_against_the_floor()
    {
        // total is 130 in the fixture: a check reading total would pass at a floor of 130.
        (int exitCode, _, string stderr) = RunWith("130\n", Trx(RealCounters));

        Assert.Equal(1, exitCode);
        Assert.Equal("test-count floor: executed 124 is below the floor 130" + Environment.NewLine, stderr);
    }

    [Fact]
    public void Run_passes_at_exactly_the_floor()
    {
        (int exitCode, string stdout, string stderr) = RunWith("124\n", Trx(RealCounters));

        Assert.Equal(0, exitCode);
        Assert.Equal("test-count floor: executed 124, floor 124" + Environment.NewLine, stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    public void Run_passes_above_the_floor_and_names_each_count_in_its_own_slot()
    {
        // Tester, PR #16: every other passing case has executed == floor, so a check failing on
        // executed != floor, or a pass line printing the two numbers swapped, stayed green.
        (int exitCode, string stdout, string stderr) = RunWith("100\n", Trx(RealCounters));

        Assert.Equal(0, exitCode);
        Assert.Equal("test-count floor: executed 124, floor 100" + Environment.NewLine, stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    public void Run_fails_with_one_line_when_the_trx_is_missing()
    {
        (int exitCode, string stdout, string stderr) = RunWith("124\n", trxContent: null);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            $"test-count floor: expected exactly one .trx in {Path.Combine(_root, "results")}, found 0" + Environment.NewLine,
            stderr);
        Assert.Equal("", stdout);
    }

    [Fact]
    public void Run_fails_with_one_line_when_the_floor_is_unreadable()
    {
        (int exitCode, string stdout, string stderr) = RunWith("", Trx(RealCounters));

        Assert.Equal(1, exitCode);
        Assert.Equal(
            $"test-count floor: floor file {Path.Combine(_root, "floor.txt")} must hold one positive integer, found \"\"" + Environment.NewLine,
            stderr);
        Assert.Equal("", stdout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Run_returns_the_usage_code_unless_given_exactly_two_arguments(int count)
    {
        StringWriter stdout = new();
        StringWriter stderr = new();

        int exitCode = FloorCheck.Run(Enumerable.Repeat(_root, count).ToArray(), stdout, stderr);

        Assert.Equal(2, exitCode);
        Assert.Equal(
            "test-count floor: usage: Guildsmith.TestCountFloor <results-directory> <floor-file>" + Environment.NewLine,
            stderr.ToString());
        Assert.Equal("", stdout.ToString());
    }
}

using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Guildsmith.TestCountFloor;

/// <summary>
/// The test-count floor CI runs after <c>dotnet test</c>, which exits 0 when it discovers no
/// tests at all. It fails closed: every input it cannot read is a failure with a one-line reason,
/// never a pass.
/// </summary>
public static class FloorCheck
{
    /// <summary>The XML namespace of a VSTest <c>.trx</c> file.</summary>
    public const string TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>The executed count is at or above the floor.</summary>
    public const int PassExitCode = 0;

    /// <summary>The executed count is below the floor, or an input could not be read.</summary>
    public const int FailExitCode = 1;

    /// <summary>The command line is not <c>&lt;results-directory&gt; &lt;floor-file&gt;</c>.</summary>
    public const int UsageExitCode = 2;

    /// <summary>
    /// Checks the single <c>.trx</c> in <c>args[0]</c> against the floor in <c>args[1]</c>.
    /// Failures go to <paramref name="stderr"/> as one line naming the found count and the floor.
    /// </summary>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Count != 2)
        {
            stderr.WriteLine("test-count floor: usage: Guildsmith.TestCountFloor <results-directory> <floor-file>");
            return UsageExitCode;
        }

        try
        {
            int floor = ReadFloor(args[1]);
            int executed = ReadExecuted(FindSingleTrx(args[0]));
            if (executed < floor)
            {
                stderr.WriteLine($"test-count floor: executed {executed} is below the floor {floor}");
                return FailExitCode;
            }

            stdout.WriteLine($"test-count floor: executed {executed}, floor {floor}");
            return PassExitCode;
        }
        catch (FloorCheckException exception)
        {
            stderr.WriteLine($"test-count floor: {exception.Message}");
            return FailExitCode;
        }
    }

    /// <summary>
    /// The committed minimum: one positive decimal integer, surrounding whitespace allowed. A
    /// missing, empty, non-numeric or zero floor throws — a floor of zero is no floor.
    /// </summary>
    public static int ReadFloor(string path)
    {
        if (!File.Exists(path))
        {
            throw new FloorCheckException($"floor file {path} not found");
        }

        string text = File.ReadAllText(path).Trim();
        if (!TryParseCount(text, out int floor))
        {
            throw new FloorCheckException($"floor file {path} must hold one positive integer, found \"{text}\"");
        }

        if (floor < 1)
        {
            throw new FloorCheckException($"floor file {path} holds {floor}; a floor below 1 is no floor");
        }

        return floor;
    }

    /// <summary>
    /// The one <c>.trx</c> directly inside <paramref name="directory"/>. None, or more than one,
    /// throws: with two files there is no telling which run the count belongs to.
    /// </summary>
    public static string FindSingleTrx(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new FloorCheckException($"results directory {directory} not found");
        }

        string[] files = Directory.GetFiles(directory, "*.trx", SearchOption.TopDirectoryOnly);
        if (files.Length != 1)
        {
            throw new FloorCheckException($"expected exactly one .trx in {directory}, found {files.Length}");
        }

        return files[0];
    }

    /// <summary>
    /// The <c>executed</c> attribute of the one <c>TestRun/ResultSummary/Counters</c> element.
    /// Not <c>total</c> and not <c>passed</c>: <c>total</c> counts tests that were discovered but
    /// never ran, and failures are the test step's own verdict.
    /// </summary>
    public static int ReadExecuted(string trxPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(trxPath);
        }
        catch (XmlException exception)
        {
            throw new FloorCheckException($"{trxPath} is not well-formed XML: {exception.Message}");
        }

        XNamespace ns = TrxNamespace;
        XElement[] counters = document
            .Elements(ns + "TestRun")
            .Elements(ns + "ResultSummary")
            .Elements(ns + "Counters")
            .ToArray();
        if (counters.Length != 1)
        {
            throw new FloorCheckException($"{trxPath} must hold exactly one TestRun/ResultSummary/Counters element, found {counters.Length}");
        }

        string? executed = (string?)counters[0].Attribute("executed");
        if (!TryParseCount(executed, out int count))
        {
            throw new FloorCheckException($"{trxPath} Counters has no numeric executed attribute, found \"{executed}\"");
        }

        return count;
    }

    // NumberStyles.None: digits only, so "-1", "+5", "1,000" and " 5" are rejected rather than read.
    private static bool TryParseCount(string? text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}

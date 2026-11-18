using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

/// <summary>
/// Runs every [Test] method in this project. Add tests as static methods in a file under Tests/
/// (one file per module, for example Tests/MatchTests.cs), so packages never edit the same file.
///
///   dotnet run --project Tests/CoreSim              run everything
///   dotnet run --project Tests/CoreSim -- Match     run tests whose "Class.Method" contains "Match"
///
/// Ends with "ALL CHECKS PASSED" and exit code 0, or lists failures and exits with 1.
/// Longer reports (balance tables, simulations) go behind their own first argument the same way,
/// as a static method registered in <see cref="Reports"/>.
/// </summary>
static class Program
{
    /// <summary>Named reports: dotnet run --project Tests/CoreSim -- report &lt;name&gt;.</summary>
    static readonly Dictionary<string, Func<string[], int>> Reports = new Dictionary<string, Func<string[], int>>
    {
        { "progression", ProgressionSim.Report },
        { "scorerate", ProgressionSim.RateReport },
        { "teamsize", ProgressionSim.TeamSizeReport },
        { "flagrush", FlagSim.Report },
        { "map", MapTests.Report },
        { "kitwaves", WaveHarness.Report },
        { "lanestudy", LaneStudyTests.Report },
        { "waves", LaneTests.Report },
        { "overrun", OverrunSim.Report },
        { "breakwater", BreakwaterTests.Report },
    };

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "report")
        {
            if (args.Length > 1 && Reports.TryGetValue(args[1], out var report)) return report(args.Skip(2).ToArray());
            Console.WriteLine("Reports: " + (Reports.Count == 0 ? "(none yet)" : string.Join(", ", Reports.Keys)));
            return 2;
        }

        string filter = args.Length > 0 ? args[0] : null;
        var tests = Assembly.GetExecutingAssembly().GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
            .Select(m => (name: m.DeclaringType.Name + "." + m.Name, method: m))
            .Where(t => filter == null || t.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(t => t.name, StringComparer.Ordinal)
            .ToList();

        int failedTests = 0;
        foreach (var (name, method) in tests)
        {
            int before = Check.Failures;
            try { method.Invoke(null, null); }
            catch (TargetInvocationException ex)
            {
                Check.Fail("threw " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
            }
            bool ok = Check.Failures == before;
            if (!ok) failedTests++;
            Console.WriteLine((ok ? "  ok    " : "  FAIL  ") + name);
            foreach (var line in Check.TakeMessages()) Console.WriteLine("          " + line);
        }

        Console.WriteLine();
        Console.WriteLine(tests.Count + " tests, " + Check.Count + " checks" + (filter != null ? " (filter: " + filter + ")" : ""));
        if (Check.Failures > 0)
        {
            Console.WriteLine(failedTests + " TESTS FAILED (" + Check.Failures + " failed checks)");
            return 1;
        }
        Console.WriteLine("ALL CHECKS PASSED");
        return 0;
    }
}

/// <summary>Marks a static, parameterless method as a test.</summary>
[AttributeUsage(AttributeTargets.Method)]
sealed class TestAttribute : Attribute { }

/// <summary>Assertions. A failed check is recorded and the test keeps running.</summary>
static class Check
{
    public static int Count { get; private set; }
    public static int Failures { get; private set; }
    static readonly List<string> messages = new List<string>();

    public static void True(bool condition, string what)
    {
        Count++;
        if (!condition) Fail(what);
    }

    public static void False(bool condition, string what) => True(!condition, what);

    public static void Equal<T>(T expected, T actual, string what) =>
        True(EqualityComparer<T>.Default.Equals(expected, actual), what + ": expected " + expected + ", got " + actual);

    public static void Near(float expected, float actual, string what, float epsilon = 1e-4f) =>
        True(Math.Abs(expected - actual) <= epsilon, what + ": expected " + expected + ", got " + actual);

    public static void Fail(string what)
    {
        Failures++;
        messages.Add(what);
    }

    public static List<string> TakeMessages()
    {
        var copy = new List<string>(messages);
        messages.Clear();
        return copy;
    }
}

/// <summary>Paths in the repository, for tests that read source or data files.</summary>
static class Repo
{
    /// <summary>The repository root (the folder holding Assets/), found by walking up from the build output.</summary>
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "DuckAndCover"))) dir = dir.Parent;
            if (dir == null) throw new DirectoryNotFoundException("Could not find the repository root above " + AppContext.BaseDirectory);
            return dir.FullName;
        }
    }

    public static string Scripts => Path.Combine(Root, "Assets", "DuckAndCover", "Scripts");
}

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Template.Performance.Tests;

/// <summary>One measured operation: samples in milliseconds against its NFR budget.</summary>
public sealed record Measurement(string Budget, string Operation, IReadOnlyList<double> SamplesMs, double LimitMs)
{
    public double P50 => Percentile(0.50);
    public double P95 => Percentile(0.95);
    public double Max => SamplesMs.Max();
    public bool Passed => P95 <= LimitMs;

    private double Percentile(double quantile)
    {
        var sorted = SamplesMs.Order().ToArray();
        var rank = (int)Math.Ceiling(quantile * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }
}

/// <summary>
/// Collects every measurement of the run and writes it to artifacts/validation/performance/&lt;RunId&gt;/results.md
/// (git-ignored), so the report cites a file rather than test output.
/// </summary>
public static class PerformanceEvidence
{
    private static readonly List<Measurement> Measurements = [];
    private static readonly List<string> Notes = [];
    private static readonly string RunId = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    public static async Task<Measurement> MeasureAsync(string budget, string operation, double limitMs, int warmup, int samples, Func<int, Task> run)
    {
        for (var index = 0; index < warmup; index++)
            await run(index);
        var values = new List<double>(samples);
        for (var index = 0; index < samples; index++)
        {
            var clock = Stopwatch.StartNew();
            await run(warmup + index);
            values.Add(clock.Elapsed.TotalMilliseconds);
        }

        return Record(new Measurement(budget, operation, values, limitMs));
    }

    public static Measurement Record(Measurement measurement)
    {
        lock (Measurements)
        {
            Measurements.Add(measurement);
            Write();
        }

        return measurement;
    }

    public static void Note(string note)
    {
        lock (Measurements)
        {
            Notes.Add(note);
            Write();
        }
    }

    private static void Write()
    {
        var directory = Path.Combine(RepositoryRoot(), "artifacts", "validation", "performance", RunId);
        Directory.CreateDirectory(directory);
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"# NFR performance run {RunId}");
        builder.AppendLine();
        foreach (var note in Notes)
            builder.AppendLine(CultureInfo.InvariantCulture, $"- {note}");
        builder.AppendLine();
        builder.AppendLine("| Budget | Operation | Samples | p50 ms | p95 ms | max ms | Limit | Result |");
        builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---|");
        foreach (var item in Measurements)
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| {item.Budget} | {item.Operation} | {item.SamplesMs.Count} | {item.P50:F1} | {item.P95:F1} | {item.Max:F1} | {item.LimitMs:F0} | {(item.Passed ? "Passed" : "Failed")} |");
        File.WriteAllText(Path.Combine(directory, "results.md"), builder.ToString());
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TemplateCleanArchitecture.App.slnf")))
                return directory.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}

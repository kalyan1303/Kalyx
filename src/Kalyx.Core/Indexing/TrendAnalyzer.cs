using System.Diagnostics;
using Kalyx.Core.Models;
using Kalyx.Core.Parsing;

namespace Kalyx.Core;

public record FileTrend(string Path, List<(string Bucket, double Score)> Series,
    bool CrossingThreshold, bool MonotonicRiser);

public static class TrendAnalyzer
{
    public record Bucket(DateTime Label, string CommitSha);

    public static List<Bucket> BucketCommits(string repoRoot, int intervalMonths)
    {
        var commits = new List<(DateTime Date, string Sha)>();
        RunGit(repoRoot, "log --first-parent --pretty=format:%H|%cI", line =>
        {
            var parts = line.Split('|', 2);
            if (parts.Length == 2 && DateTime.TryParse(parts[1], out var date))
                commits.Add((date.ToUniversalTime(), parts[0]));
        });
        if (commits.Count == 0) return new();
        var buckets = new List<Bucket>();
        var selected = commits.OrderByDescending(c => c.Date).ToList();
        var latestDate = selected[0].Date;
        for (var date = new DateTime(latestDate.Year, latestDate.Month, 1).AddMonths(-(intervalMonths - 1));
             date >= selected[^1].Date; date = date.AddMonths(-intervalMonths))
        {
            var candidate = selected.FirstOrDefault(c => c.Date <= latestDate && c.Date >= date);
            if (candidate.Sha != null) buckets.Add(new Bucket(date, candidate.Sha));
            if (date.AddMonths(-intervalMonths) < selected[^1].Date) break;
        }
        return buckets.OrderBy(b => b.Label).DistinctBy(b => b.CommitSha).ToList();
    }

    public static List<FileTrend> Compute(KnowledgeGraph current, string repoRoot, int intervalMonths,
        int topN, double threshold)
    {
        var buckets = BucketCommits(repoRoot, intervalMonths);
        var series = new List<(string Label, Dictionary<string, double> Scores)>();
        foreach (var bucket in buckets.Take(12))
        {
            var worktree = Path.Combine(Path.GetTempPath(), $"kalyx-trend-{Guid.NewGuid():N}");
            try
            {
                RunGit(repoRoot, $"worktree add --detach \"{worktree}\" {bucket.CommitSha}", _ => { });
                var graph = CSharpParser.ParseDirectory(worktree);
                var hot = Hotspots.Compute(graph, topN);
                series.Add((bucket.Label.ToString("yyyy-MM"),
                    hot.ToDictionary(h => h.Path, h => h.Score, StringComparer.OrdinalIgnoreCase)));
            }
            catch { }
            finally
            {
                try { RunGit(repoRoot, $"worktree remove --force \"{worktree}\"", _ => { }); } catch { }
            }
        }
        var head = Hotspots.Compute(current, topN);
        series.Add((DateTime.UtcNow.ToString("yyyy-MM"),
            head.ToDictionary(h => h.Path, h => h.Score, StringComparer.OrdinalIgnoreCase)));
        return AssembleSeries(series, topN, threshold);
    }

    public static List<FileTrend> AssembleSeries(
        List<(string Label, Dictionary<string, double> Scores)> buckets, int topN, double threshold)
    {
        var paths = buckets.SelectMany(b => b.Scores.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p).ToList();
        var trends = new List<FileTrend>();
        foreach (var path in paths)
        {
            var values = buckets.Select(b => (b.Label, b.Scores.GetValueOrDefault(path, 0))).ToList();
            var scoreValues = values.Select(v => v.Item2).ToList();
            var crossed = values.Any(v => v.Item2 >= threshold) && scoreValues[^1] >= threshold;
            var riser = scoreValues.Count >= 4;
            if (riser)
            {
                var start = scoreValues.Count - 4;
                for (var index = start + 1; index < scoreValues.Count; index++)
                    if (scoreValues[index] <= scoreValues[index - 1]) { riser = false; break; }
            }
            trends.Add(new FileTrend(path, values, crossed, riser));
        }
        return trends.OrderByDescending(t => t.Series[^1].Item2).Take(topN).ToList();
    }

    private static void RunGit(string repoRoot, string arguments, Action<string> onLine)
    {
        var start = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git.");
        while (process.StandardOutput.ReadLine() is { } line) onLine(line);
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {arguments} failed: {process.StandardError.ReadToEnd()}");
    }
}

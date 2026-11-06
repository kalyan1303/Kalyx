using System.Diagnostics;
using Kalyx.Core.Models;

namespace Kalyx.Core;

public record Hotspot(string Path, int Churn, double Coupling, double Score);

public static class Hotspots
{
    public static List<Hotspot> Compute(KnowledgeGraph graph, string repoRoot, int top = 15)
    {
        var churn = GetChurn(repoRoot);
        var coupling = graph.Nodes.Values.Where(n => n.Kind == "file")
            .ToDictionary(n => Normalize(n.File, repoRoot), n =>
                (double)graph.Edges.Count(e => (e.FromId == n.Id || e.ToId == n.Id)
                    && e.Kind is not "defines" and not "contains"));
        var paths = churn.Keys.Union(coupling.Keys).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct();
        var results = paths.Select(path =>
        {
            var c = churn.GetValueOrDefault(path, 0);
            var k = coupling.GetValueOrDefault(path, 0);
            var score = Math.Min(100, c * 1.0 + k * 10.0);
            return new Hotspot(path, c, k, Math.Round(score, 1));
        }).OrderByDescending(h => h.Score).Take(top).ToList();
        return results;
    }

    public static List<Hotspot> Compute(KnowledgeGraph graph, int top = 15)
    {
        var byFile = graph.Nodes.Values.Where(n => n.Kind == "file").ToList();
        return byFile.Select(n =>
        {
            var degree = graph.Edges.Count(e => e.FromId == n.Id || e.ToId == n.Id);
            return new Hotspot(n.File ?? n.Name, 0, degree, degree);
        }).OrderByDescending(h => h.Score).Take(top).ToList();
    }

    private static Dictionary<string, int> GetChurn(string repoRoot)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(Path.Combine(repoRoot, ".git"))) return result;
        var psi = new ProcessStartInfo("git", "log --name-only --pretty=format:")
        { WorkingDirectory = repoRoot, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        try
        {
            using var process = Process.Start(psi)!;
            var lines = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            foreach (var line in lines.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var path = line.Trim().Replace('\\', '/');
                if (!path.StartsWith('{') && !path.Contains("=>"))
                    result[path] = result.GetValueOrDefault(path) + 1;
            }
        }
        catch (Exception) { }
        return result;
    }

    private static string Normalize(string? file, string repoRoot)
    {
        if (string.IsNullOrWhiteSpace(file)) return "";
        try { return Path.GetRelativePath(repoRoot, file).Replace('\\', '/'); }
        catch { return file.Replace('\\', '/'); }
    }
}

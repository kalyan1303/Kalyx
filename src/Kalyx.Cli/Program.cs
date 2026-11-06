using System.Diagnostics;
using System.Text.Json;
using Kalyx.Core;
using Kalyx.Core.Models;
using Kalyx.Core.Parsing;

return Kalyx.Cli.ProgramEntry.Run(args);

namespace Kalyx.Cli
{
    public static class ProgramEntry
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

        public static int Run(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            {
                Console.WriteLine(Help);
                return 0;
            }
            var command = args[0].ToLowerInvariant();
            var rest = args.Skip(1).ToArray();
            var defaultGraph = GetFlag(rest, "--graph", "graph.json");
            try
            {
                return command switch
                {
                    "parse" => Parse(rest),
                    "parse-multi" => ParseMulti(rest),
                    "query" => RunGraph(rest, defaultGraph, (g, r) => Query(g, r)),
                    "path" => RunGraph(rest, defaultGraph, (g, r) => PathCommand(g, r)),
                    "explain" => RunGraph(rest, defaultGraph, (g, r) => Explain(g, r)),
                    "viz" => Visualization(rest),
                    "mermaid" => Mermaid(rest),
                    "cycles" or "smells" => RunGraph(rest, defaultGraph, (g, r) =>
                    {
                        var report = CodeSmells.Analyze(g);
                        Console.WriteLine($"Cycles: {report.Cycles.Count}");
                        foreach (var cycle in report.Cycles.Take(20))
                            Console.WriteLine("  " + string.Join(" -> ", cycle.Take(12)));
                        Console.WriteLine($"God nodes: {report.GodNodes.Count}");
                        foreach (var node in report.GodNodes.Take(20))
                            Console.WriteLine($"  {node.Name} ({node.Degree} connections)");
                        Console.WriteLine($"Unstable modules: {report.UnstableModules.Count}");
                        foreach (var module in report.UnstableModules.Take(20))
                            Console.WriteLine($"  {module.Name} instability={module.Instability:F2}");
                        return 0;
                    }),
                    "hotspots" => RunGraph(rest, defaultGraph, (g, r) =>
                    {
                        var root = GetFlag(r, "--repo", ".");
                        var top = int.Parse(GetFlag(r, "--top", "15"));
                        var hotspots = Hotspots.Compute(g, root, top);
                        Console.WriteLine("Rank  File                        Churn  Coupling  Score");
                        var rank = 1;
                        foreach (var item in hotspots)
                            Console.WriteLine($"{rank++,4}  {item.Path,-28} {item.Churn,5}  {item.Coupling,8:F0}  {item.Score,5:F1}");
                        return 0;
                    }),
                    "impact" => Impact(rest, defaultGraph),
                    "trend" => Trend(rest, defaultGraph),
                    "diff" => Diff(rest),
                    "changed" => Changed(rest, defaultGraph),
                    "ask" => Ask(rest, defaultGraph),
                    "mcp" => McpServer.Run(defaultGraph),
                    "export" => Export(rest),
                    _ => Unknown(command)
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"kalyx: {ex.Message}");
                return 1;
            }
        }

        private static int Parse(string[] args)
        {
            var directory = args.FirstOrDefault(a => !a.StartsWith("--")) ?? ".";
            var outPath = GetFlag(args, "--out", "graph.json");
            var graph = CSharpParser.ParseDirectory(directory);
            GraphStore.Save(graph, outPath);
            Console.WriteLine($"Indexed {graph.Nodes.Count} nodes, {graph.Edges.Count} edges -> {outPath}");
            return 0;
        }

        private static int ParseMulti(string[] args)
        {
            var root = args.FirstOrDefault(a => !a.StartsWith("--")) ?? ".";
            var outPath = GetFlag(args, "--out", "combined.json");
            var combined = new KnowledgeGraph();
            foreach (var repo in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                var graph = CSharpParser.ParseDirectory(repo);
                var prefix = Path.GetFileName(repo) + "||";
                foreach (var node in graph.Nodes.Values)
                {
                    var id = prefix + node.Id;
                    combined.Nodes[id] = new GraphNode { Id = id, Name = node.Name, Kind = node.Kind,
                        File = node.File, Line = node.Line, Community = node.Community };
                }
                foreach (var edge in graph.Edges)
                    combined.AddEdge(prefix + edge.FromId, prefix + edge.ToId, edge.Kind, edge.Origin, edge.Confidence, edge.Weight);
            }
            EdgeResolver.Prune(combined);
            GraphStore.Save(combined, outPath);
            Console.WriteLine($"Indexed {combined.Nodes.Count} nodes across repositories -> {outPath}");
            return 0;
        }

        private static int RunGraph(string[] args, string graphPath, Func<KnowledgeGraph, string[], int> action)
        {
            return action(GraphStore.Load(graphPath), args);
        }

        private static int Query(KnowledgeGraph graph, string[] args)
        {
            var pattern = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "";
            var kind = GetOptionalFlag(args, "--type");
            var limit = int.Parse(GetFlag(args, "--limit", "25"));
            var matches = GraphQueries.Query(graph, pattern, kind, limit);
            Console.WriteLine($"Found {matches.Count} node(s):");
            foreach (var node in matches)
                Console.WriteLine($"  {node.Name,-40} [{node.Kind,-10}] community={node.Community,-3} degree={node.Neighbors.Count,-4} {node.File ?? ""}");
            return matches.Count == 0 ? 0 : 0;
        }

        private static int PathCommand(KnowledgeGraph graph, string[] args)
        {
            var positionals = args.Where(a => !a.StartsWith("--")).ToList();
            if (positionals.Count >= 2)
                Console.WriteLine(GraphQueries.Path(graph, positionals[0], positionals[1]));
            else
                throw new ArgumentException("Usage: kalyx path <from> <to>");
            return 0;
        }

        private static int Explain(KnowledgeGraph graph, string[] args)
        {
            var target = args.FirstOrDefault(a => !a.StartsWith("--"));
            if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("Usage: kalyx explain <node>");
            Console.WriteLine(GraphQueries.Explain(graph, target));
            return 0;
        }

        private static int Visualization(string[] args)
        {
            var graphPath = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "graph.json";
            var outPath = GetFlag(args, "--out", "graph.html");
            var graph = GraphStore.Load(graphPath);
            var json = JsonSerializer.Serialize(graph, JsonOptions);
            var logoDataUri = LoadLogoDataUri();
            var html = HtmlRenderer.Render(json, logoDataUri);
            File.WriteAllText(outPath, html);
            Console.WriteLine($"Wrote interactive visualization -> {outPath}");
            return 0;
        }

        // The logo is embedded in the executable so `viz` works from any install location
        // (published exe, dotnet tool, or repo checkout). Falls back to ./assets for local edits.
        private static string LoadLogoDataUri()
        {
            var assembly = typeof(ProgramEntry).Assembly;
            using var stream = assembly.GetManifestResourceStream("Kalyx.kalyx-logo.png");
            if (stream != null)
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return $"data:image/png;base64,{Convert.ToBase64String(buffer.ToArray())}";
            }
            var fallback = Path.Combine(Directory.GetCurrentDirectory(), "assets", "kalyx-logo.png");
            return File.Exists(fallback)
                ? $"data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(fallback))}"
                : "";
        }

        private static int Mermaid(string[] args)
        {
            var graphPath = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "graph.json";
            var graph = GraphStore.Load(graphPath);
            var output = MermaidExporter.Render(graph, !args.Contains("--all"));
            var outPath = GetOptionalFlag(args, "--out");
            if (outPath != null) { File.WriteAllText(outPath, output); Console.WriteLine($"Wrote {outPath}"); }
            else Console.WriteLine(output);
            return 0;
        }

        private static int Impact(string[] args, string defaultGraph)
        {
            var graphPath = GetFlag(args, "--graph", defaultGraph);
            var graph = GraphStore.Load(graphPath);
            var target = args.FirstOrDefault(a => !a.StartsWith("--"));
            if (string.IsNullOrWhiteSpace(target) && !args.Contains("--changed"))
                throw new ArgumentException("Usage: kalyx impact <file|symbol> [--depth N] [--min-confidence F] [--format json]");
            if (args.Contains("--changed"))
                return Changed(args, graphPath);
            var depth = int.Parse(GetFlag(args, "--depth", "4"));
            var minConfidence = double.Parse(GetFlag(args, "--min-confidence", "0.5"), System.Globalization.CultureInfo.InvariantCulture);
            var report = BlastRadius.ComputeWithRisk(graph, target!, depth, minConfidence);
            if (GetOptionalFlag(args, "--format") == "json")
            {
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
                return 0;
            }
            Console.WriteLine($"Blast radius of {GraphQueries.ResolveNode(graph, target!)?.Name}: {report.Total} node(s), {report.ByCommunity.Count} community/communities -> risk {report.Risk}/100 {report.Grade}");
            foreach (var community in report.ByCommunity.OrderBy(x => x.Key))
            {
                Console.WriteLine($"  [community {community.Key}] {community.Value.Count} node(s)");
                foreach (var node in community.Value.OrderBy(x => x.Depth))
                    Console.WriteLine($"    d={node.Depth} {node.Name} ({node.Id})");
            }
            return 0;
        }

        private static int Trend(string[] args, string defaultGraph)
        {
            var graphPath = GetFlag(args, "--graph", defaultGraph);
            var graph = GraphStore.Load(graphPath);
            var intervalText = GetFlag(args, "--interval", "3M");
            var interval = int.Parse(intervalText.TrimEnd('M', 'm'));
            var threshold = double.Parse(GetFlag(args, "--threshold", "30"), System.Globalization.CultureInfo.InvariantCulture);
            var top = int.Parse(GetFlag(args, "--top", "15"));
            var root = GetFlag(args, "--repo", ".");
            var trends = TrendAnalyzer.Compute(graph, root, interval, top, threshold);
            Console.WriteLine(TrendRenderer.Render(trends));
            return 0;
        }

        private static int Diff(string[] args)
        {
            var positionals = args.Where(a => !a.StartsWith("--")).ToArray();
            if (positionals.Length != 2) throw new ArgumentException("Usage: kalyx diff <before.json> <after.json>");
            var report = GraphDiff.Compare(GraphStore.Load(positionals[0]), GraphStore.Load(positionals[1]));
            Console.WriteLine($"Added: {report.Added.Count}, removed: {report.Removed.Count}, community changes: {report.CommunityChanges.Count}, edge changes: {report.EdgeChanges.Count}");
            foreach (var name in report.Added.Take(50)) Console.WriteLine($"  + {name}");
            foreach (var name in report.Removed.Take(50)) Console.WriteLine($"  - {name}");
            return 0;
        }

        private static int Changed(string[] args, string defaultGraph)
        {
            var graphPath = GetFlag(args, "--graph", defaultGraph);
            var graph = GraphStore.Load(graphPath);
            var staged = args.Contains("--staged");
            var gitArgs = staged ? "diff --name-only --staged" : "diff --name-only HEAD";
            var repo = GetFlag(args, "--repo", ".");
            var files = RunProcess("git", gitArgs, repo);
            if (string.IsNullOrWhiteSpace(files)) { Console.WriteLine("{\"worstRisk\":0,\"grade\":\"LOW\",\"changedFiles\":[]}"); return 0; }
            var paths = files.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var worst = paths.Select(path => {
                var node = GraphQueries.ResolveNode(graph, path);
                if (node == null) return (Risk: 0.0, Grade: "LOW", Node: path);
                var report = BlastRadius.ComputeWithRisk(graph, node.Id);   // computed once per file
                return (Risk: report.Risk, Grade: report.Grade, Node: node.Name);
            }).OrderByDescending(x => x.Risk).First();
            if (GetOptionalFlag(args, "--format") == "json")
                Console.WriteLine(JsonSerializer.Serialize(new { worstRisk = worst.Risk, grade = worst.Grade, changedFiles = paths }, JsonOptions));
            else
                Console.WriteLine($"Worst changed-file risk: {worst.Risk}/100 ({worst.Grade})");
            return 0;
        }

        private static int Ask(string[] args, string defaultGraph)
        {
            var graph = GraphStore.Load(GetFlag(args, "--graph", defaultGraph));
            var question = string.Join(" ", args.Where(a => !a.StartsWith("--")));
            var words = question.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var node = graph.Nodes.Values
                .Where(n => question.Contains(n.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.Name.Length).FirstOrDefault();
            if (node == null) { Console.WriteLine("I could not identify a code entity in that question."); return 0; }
            var lower = question.ToLowerInvariant();
            // "depends on"/"uses" = what this entity relies on -> explain. Must be checked before the
            // broader "depend"/"calls" match, which means "who is affected" -> blast radius.
            if (lower.Contains("depends on") || lower.Contains("uses"))
                Console.WriteLine(GraphQueries.Explain(graph, node.Id));
            else if (lower.Contains("calls") || lower.Contains("depend"))
                Console.WriteLine($"Blast radius for {node.Name}: " +
                    JsonSerializer.Serialize(BlastRadius.ComputeWithRisk(graph, node.Id), JsonOptions));
            else
                Console.WriteLine($"Where is {node.Name}? {node.File ?? node.Id}, kind={node.Kind}, community={node.Community}");
            return 0;
        }

        private static int Export(string[] args)
        {
            var format = args.FirstOrDefault(a => !a.StartsWith("--"));
            var graphPath = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--")) ?? "graph.json";
            var graph = GraphStore.Load(graphPath);
            var output = GetOptionalFlag(args, "--out");
            if (format is "dot")
            {
                var dot = DotExporter.Export(graph);
                if (output != null) File.WriteAllText(output, dot); else Console.WriteLine(dot);
                return 0;
            }
            if (format is "mermaid")
                return Mermaid(args.Skip(1).ToArray());
            throw new ArgumentException("Supported export formats: dot, mermaid");
        }

        private static int Unknown(string command)
        {
            Console.Error.WriteLine($"Unknown command '{command}'. Run 'kalyx help'.");
            return 1;
        }

        private static string GetFlag(string[] args, string flag, string defaultValue)
        {
            var index = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : defaultValue;
        }

        private static string? GetOptionalFlag(string[] args, string flag)
        {
            var index = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static string RunProcess(string command, string arguments, string workingDirectory)
        {
            var start = new ProcessStartInfo(command, arguments)
            { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }

        private const string Help = """
KALYX - codebase knowledge graph analysis

Usage:
  kalyx parse [dir] [--out graph.json]
  kalyx query <pattern> [--type method] [--limit 25]
  kalyx path <from> <to> [--graph graph.json]
  kalyx explain <node> [--graph graph.json]
  kalyx viz [graph.json] [--out graph.html]
  kalyx mermaid [graph.json] [--all] [--out graph.mmd]
  kalyx cycles [--graph graph.json]
  kalyx smells [--graph graph.json]
  kalyx hotspots [--top 15] [--repo .] [--graph graph.json]
  kalyx impact <file|symbol> [--depth 4] [--min-confidence 0.5] [--format json]
  kalyx trend [--interval 3M] [--threshold 30] [--top 15]
  kalyx diff <before.json> <after.json>
  kalyx changed [--staged] [--format json] [--graph graph.json]
  kalyx ask <question> [--graph graph.json]
  kalyx export dot [graph.json] [--out graph.dot]
  kalyx mcp [--graph graph.json]
""";
    }
}

using Kalyx.Core.Models;

namespace Kalyx.Core;

public record BlastRadiusReport(
    Dictionary<int, List<(string Id, string Name, int Depth)>> ByCommunity,
    int Total,
    double Risk,
    string Grade);

public static class BlastRadius
{
    public static Dictionary<int, List<(string Id, string Name, int Depth)>> Compute(
        KnowledgeGraph graph, string target, int maxDepth = 4, double minConfidence = 0.5)
    {
        var targetNode = GraphQueries.ResolveNode(graph, target)
            ?? throw new KeyNotFoundException($"Unknown graph node: {target}");
        var reverse = graph.Edges.Where(e => e.Kind is not "defines" and not "contains"
                && e.Confidence >= minConfidence && graph.Nodes.ContainsKey(e.FromId) && graph.Nodes.ContainsKey(e.ToId))
            .GroupBy(e => e.ToId).ToDictionary(g => g.Key, g => g.Select(e => e.FromId).Distinct().ToList());
        var distance = new Dictionary<string, int> { [targetNode.Id] = 0 };
        var queue = new Queue<string>(); queue.Enqueue(targetNode.Id);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var depth = distance[current];
            if (depth >= maxDepth || !reverse.TryGetValue(current, out var parents)) continue;
            foreach (var parent in parents)
            {
                if (distance.ContainsKey(parent)) continue;
                distance[parent] = depth + 1; queue.Enqueue(parent);
            }
        }
        return distance.Where(x => x.Value > 0)
            .GroupBy(x => graph.Nodes[x.Key].Community)
            .ToDictionary(g => g.Key, g => g.Select(x => (x.Key, graph.Nodes[x.Key].Name, x.Value)).ToList());
    }

    public static BlastRadiusReport ComputeWithRisk(
        KnowledgeGraph graph, string target, int maxDepth = 4, double minConfidence = 0.5)
    {
        var result = Compute(graph, target, maxDepth, minConfidence);
        var total = result.Values.Sum(v => v.Count);
        var targetNode = GraphQueries.ResolveNode(graph, target)!;
        var volume = Math.Min(100, total * 5.0);
        var spread = result.Count == 0 ? 0 : Math.Min(100,
            result.Count * 20.0 + (result.Keys.Contains(targetNode.Community) ? 0 : 10));
        var closest = result.Values.SelectMany(v => v).Select(v => v.Depth).DefaultIfEmpty(99).Min();
        var proximity = closest >= 99 ? 0 : Math.Max(0, 100 - closest * 20);
        var risk = Math.Round(0.40 * volume + 0.35 * spread + 0.25 * proximity, 1);
        var grade = risk switch { < 25 => "LOW", < 50 => "MODERATE", < 75 => "HIGH", _ => "CRITICAL" };
        return new BlastRadiusReport(result, total, risk, grade);
    }
}

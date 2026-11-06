using System.Security.Cryptography;
using System.Text;
using Kalyx.Core.Models;

namespace Kalyx.Core;

public static class MermaidExporter
{
    public static string Render(KnowledgeGraph graph, bool collapseCommunities = true, int maxEdges = 100)
    {
        var output = new StringBuilder("graph TD\n");
        if (collapseCommunities)
        {
            foreach (var community in graph.Nodes.Values.GroupBy(n => n.Community).OrderBy(g => g.Key))
                output.AppendLine($"    C{community.Key}[\"Community {community.Key} ({community.Count()} nodes)\"]");
            var edges = graph.Edges.Where(e => graph.Nodes.ContainsKey(e.FromId) && graph.Nodes.ContainsKey(e.ToId))
                .GroupBy(e => (graph.Nodes[e.FromId].Community, graph.Nodes[e.ToId].Community))
                .Where(g => g.Key.Item1 != g.Key.Item2)
                .Select(g => (From: g.Key.Item1, To: g.Key.Item2, Weight: g.Sum(e => e.Weight)))
                .OrderByDescending(x => x.Weight).Take(maxEdges);
            foreach (var edge in edges)
                output.AppendLine($"    C{edge.From} -->|{edge.Weight:F1}| C{edge.To}");
            return output.ToString();
        }
        foreach (var node in graph.Nodes.Values.Take(200))
        {
            var label = node.Name.Replace("\"", "'");
            output.AppendLine($"    {Id(node.Id)}[\"{label}\"]");
        }
        foreach (var edge in graph.Edges.Take(maxEdges)
                 .Where(e => graph.Nodes.ContainsKey(e.FromId) && graph.Nodes.ContainsKey(e.ToId)))
            output.AppendLine($"    {Id(edge.FromId)} -->|{edge.Kind}| {Id(edge.ToId)}");
        return output.ToString();
    }

    private static string Id(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return "n" + Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}

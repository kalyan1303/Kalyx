using Kalyx.Core.Models;

namespace Kalyx.Core;

public static class GraphQueries
{
    public static IReadOnlyList<GraphNode> Query(KnowledgeGraph graph, string pattern, string? kind = null, int limit = 25)
    {
        return graph.Nodes.Values
            .Where(n => n.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                     && (kind == null || n.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(n => n.Neighbors.Count).Take(limit).ToList();
    }

    public static string Explain(KnowledgeGraph graph, string idOrName)
    {
        var node = ResolveNode(graph, idOrName);
        if (node == null) return $"No node found matching '{idOrName}'.";
        var incoming = graph.Edges.Where(e => e.ToId == node.Id).Take(20).ToList();
        var outgoing = graph.Edges.Where(e => e.FromId == node.Id).Take(20).ToList();
        var lines = new List<string>
        {
            $"Node: {node.Name}",
            $"  ID: {node.Id}",
            $"  Type: {node.Kind}",
            $"  Source: {node.File ?? "?"}{(node.Line is int line ? $" L{line}" : "")}",
            $"  Community: {node.Community}",
            "",
            "Incoming edges:"
        };
        lines.AddRange(incoming.Select(e => $"  <- [{e.Kind}] {graph.Nodes.GetValueOrDefault(e.FromId)?.Name ?? e.FromId} ({e.Origin}, conf={e.Confidence:F2})"));
        lines.Add("");
        lines.Add("Outgoing edges:");
        lines.AddRange(outgoing.Select(e => $"  -> [{e.Kind}] {graph.Nodes.GetValueOrDefault(e.ToId)?.Name ?? e.ToId} ({e.Origin}, conf={e.Confidence:F2})"));
        return string.Join(Environment.NewLine, lines);
    }

    public static string Path(KnowledgeGraph graph, string from, string to)
    {
        var source = ResolveNode(graph, from);
        var target = ResolveNode(graph, to);
        if (source == null || target == null) return $"Could not resolve both endpoints ('{from}' -> '{to}').";
        var adjacency = graph.Edges.GroupBy(e => e.FromId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ToId).Distinct().ToList());
        var previous = new Dictionary<string, string?> { [source.Id] = null };
        var queue = new Queue<string>();
        queue.Enqueue(source.Id);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == target.Id) break;
            if (!adjacency.TryGetValue(current, out var next)) continue;
            foreach (var id in next)
            {
                if (previous.ContainsKey(id)) continue;
                previous[id] = current; queue.Enqueue(id);
            }
        }
        if (!previous.ContainsKey(target.Id)) return $"No dependency path found from '{source.Name}' to '{target.Name}'.";
        var chain = new List<string>();
        for (string? id = target.Id; id != null; id = previous[id]) chain.Add(id);
        chain.Reverse();
        return $"Path ({chain.Count - 1} hops):" + Environment.NewLine
            + string.Join(Environment.NewLine, chain.Select(id => $"  {graph.Nodes[id].Name} [{graph.Nodes[id].Kind}] {graph.Nodes[id].File ?? "?"}"));
    }

    public static GraphNode? ResolveNode(KnowledgeGraph graph, string idOrName)
    {
        if (graph.Nodes.TryGetValue(idOrName, out var exact)) return exact;
        return graph.Nodes.Values.FirstOrDefault(n =>
            n.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase)
            || n.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase)
            || (n.File?.Replace('\\', '/').EndsWith(idOrName.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) ?? false));
    }
}

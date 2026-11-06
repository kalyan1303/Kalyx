using Kalyx.Core.Models;

namespace Kalyx.Core;

public record SmellReport(
    List<List<string>> Cycles,
    List<(string Id, string Name, int Degree)> GodNodes,
    List<(string Id, string Name, double Instability)> UnstableModules);

public static class CodeSmells
{
    public static SmellReport Analyze(KnowledgeGraph graph, double sigma = 2.0)
    {
        var degree = graph.Nodes.Keys.ToDictionary(id => id, _ => 0);
        foreach (var edge in graph.Edges.Where(e => e.Kind != "defines"
                 && graph.Nodes.ContainsKey(e.FromId) && graph.Nodes.ContainsKey(e.ToId)))
        {
            degree[edge.FromId]++; degree[edge.ToId]++;
        }
        var godNodes = new List<(string Id, string Name, int Degree)>();
        if (degree.Count > 0 && degree.Values.Any(v => v > 0))
        {
            var mean = degree.Values.Average();
            var variance = degree.Values.Sum(v => Math.Pow(v - mean, 2)) / degree.Count;
            var threshold = mean + sigma * Math.Sqrt(variance);
            godNodes = degree.Where(x => x.Value > threshold && x.Value > 4)
                .OrderByDescending(x => x.Value)
                .Select(x => (x.Key, graph.Nodes[x.Key].Name, x.Value)).ToList();
        }

        var modules = graph.Nodes.Values
            .Where(n => n.Kind is "file" or "module" or "class").Select(n => n.Id).ToHashSet();
        var unstable = modules.Select(id =>
        {
            var fanIn = graph.Edges.Count(e => e.ToId == id && modules.Contains(e.FromId));
            var fanOut = graph.Edges.Count(e => e.FromId == id && modules.Contains(e.ToId));
            var instability = fanIn + fanOut == 0 ? 0 : (double)fanOut / (fanIn + fanOut);
            return (id, graph.Nodes[id].Name, instability, fanOut);
        }).Where(x => x.instability > 0.8 && x.fanOut >= 3)
          .OrderByDescending(x => x.instability)
          .Select(x => (x.id, x.Name, x.instability)).ToList();
        return new SmellReport(FindCycles(graph), godNodes, unstable);
    }

    public static List<List<string>> FindCycles(KnowledgeGraph graph)
    {
        var adjacency = graph.Edges.Where(e => e.Kind != "defines" && e.Kind != "contains")
            .Where(e => graph.Nodes.ContainsKey(e.FromId) && graph.Nodes.ContainsKey(e.ToId))
            .GroupBy(e => e.FromId).ToDictionary(g => g.Key, g => g.Select(e => e.ToId).Distinct().ToList());
        var index = new Dictionary<string, int>();
        var low = new Dictionary<string, int>();
        var stack = new Stack<string>();
        var onStack = new HashSet<string>();
        var result = new List<List<string>>();
        var counter = 0;

        foreach (var root in graph.Nodes.Keys)
        {
            if (index.ContainsKey(root)) continue;
            var frames = new Stack<(string Node, IEnumerator<string> Children)>();
            index[root] = low[root] = counter++;
            stack.Push(root); onStack.Add(root);
            frames.Push((root, adjacency.TryGetValue(root, out var rootChildren)
                ? rootChildren.GetEnumerator()
                : Enumerable.Empty<string>().GetEnumerator()));

            while (frames.Count > 0)
            {
                var (node, children) = frames.Peek();
                if (children.MoveNext())
                {
                    var child = children.Current;
                    if (!graph.Nodes.ContainsKey(child)) continue;
                    if (!index.ContainsKey(child))
                    {
                        index[child] = low[child] = counter++;
                        stack.Push(child); onStack.Add(child);
                        frames.Push((child, adjacency.TryGetValue(child, out var childChildren)
                            ? childChildren.GetEnumerator()
                            : Enumerable.Empty<string>().GetEnumerator()));
                    }
                    else if (onStack.Contains(child))
                        low[node] = Math.Min(low[node], index[child]);
                }
                else
                {
                    frames.Pop();
                    if (frames.Count > 0)
                    {
                        var parent = frames.Peek().Node;
                        low[parent] = Math.Min(low[parent], low[node]);
                    }
                    if (low[node] == index[node])
                    {
                        var component = new List<string>();
                        string member;
                        do { member = stack.Pop(); onStack.Remove(member); component.Add(member); }
                        while (member != node);
                        if (component.Count > 1 || (adjacency.TryGetValue(node, out var self) && self.Contains(node)))
                            result.Add(component);
                    }
                }
            }
        }
        return result.OrderByDescending(c => c.Count).ToList();
    }
}

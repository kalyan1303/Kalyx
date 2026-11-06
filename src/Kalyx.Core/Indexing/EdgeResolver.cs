using Kalyx.Core.Models;

namespace Kalyx.Core;

public static class EdgeResolver
{
    public static void Prune(KnowledgeGraph graph)
    {
        graph.Edges.RemoveAll(e => !graph.Nodes.ContainsKey(e.FromId) || !graph.Nodes.ContainsKey(e.ToId));
        foreach (var node in graph.Nodes.Values)
            node.Neighbors.RemoveWhere(id => !graph.Nodes.ContainsKey(id));
    }

    public static void Merge(KnowledgeGraph graph) => Prune(graph);
}

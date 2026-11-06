using System.Text;
using Kalyx.Core.Models;

namespace Kalyx.Core;

public static class DotExporter
{
    public static string Export(KnowledgeGraph graph)
    {
        var output = new StringBuilder("digraph KALYX {\n  rankdir=LR;\n  node [shape=box];\n");
        foreach (var node in graph.Nodes.Values)
            output.AppendLine($"  \"{Escape(node.Id)}\" [label=\"{Escape(node.Name)}\\n{node.Kind}\"];");
        foreach (var edge in graph.Edges)
            output.AppendLine($"  \"{Escape(edge.FromId)}\" -> \"{Escape(edge.ToId)}\" [label=\"{Escape(edge.Kind)}\", weight=\"{edge.Weight}\"];");
        output.AppendLine("}");
        return output.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

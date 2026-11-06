using System.Text.Json.Serialization;

namespace Kalyx.Core.Models;

public enum EdgeOrigin
{
    Extracted,
    Inferred
}

public class GraphNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Label { get => Name; set => Name = value; }
    public string Kind { get; set; } = "";
    public string NodeType { get => Kind; set => Kind = value; }
    public string? File { get; set; }
    public string? SourceFile { get => File; set => File = value; }
    public int? Line { get; set; }
    public int? LineNumber { get => Line; set => Line = value; }
    public int Community { get; set; }
    public HashSet<string> Neighbors { get; set; } = new();
}

public class GraphEdge
{
    public string FromId { get; set; } = "";
    public string ToId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Relationship { get => Kind; set => Kind = value; }
    public double Weight { get; set; } = 1.0;
    public double Confidence { get; set; } = 1.0;
    public EdgeOrigin Origin { get; set; } = EdgeOrigin.Extracted;
}

public class KnowledgeGraph
{
    public int SchemaVersion { get; set; } = 1;
    public string KalyxVersion { get; set; } = "0.2.0";
    public Dictionary<string, GraphNode> Nodes { get; set; } = new(StringComparer.Ordinal);
    public List<GraphEdge> Edges { get; set; } = new();

    public GraphNode GetOrAdd(string id, string name, string kind, string? file = null, int? line = null)
    {
        if (!Nodes.TryGetValue(id, out var node))
        {
            node = new GraphNode { Id = id, Name = name, Kind = kind, File = file, Line = line };
            Nodes[id] = node;
        }
        return node;
    }

    public void AddEdge(string from, string to, string kind, EdgeOrigin origin = EdgeOrigin.Extracted,
        double confidence = 1.0, double weight = 1.0)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || from == to) return;
        var existing = Edges.FirstOrDefault(e => e.FromId == from && e.ToId == to && e.Kind == kind);
        if (existing != null)
        {
            existing.Weight += weight;
            existing.Confidence = Math.Max(existing.Confidence, confidence);
            return;
        }
        Edges.Add(new GraphEdge { FromId = from, ToId = to, Kind = kind, Origin = origin, Confidence = confidence, Weight = weight });
        if (Nodes.TryGetValue(from, out var a)) a.Neighbors.Add(to);
        if (Nodes.TryGetValue(to, out var b)) b.Neighbors.Add(from);
    }
}

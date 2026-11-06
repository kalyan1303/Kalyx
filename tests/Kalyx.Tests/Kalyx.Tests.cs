using Kalyx.Core;
using Kalyx.Core.Models;

namespace Kalyx.Tests;

public class GraphTests
{
    private static KnowledgeGraph TwoNodeGraph()
    {
        var graph = new KnowledgeGraph();
        graph.GetOrAdd("a", "A", "file", "a.cs");
        graph.GetOrAdd("b", "B", "method", "b.cs");
        graph.AddEdge("a", "b", "invokes", confidence: 1);
        return graph;
    }

    [Fact]
    public void StoreRoundTripPreservesGraph()
    {
        var graph = TwoNodeGraph();
        var path = Path.GetTempFileName();
        GraphStore.Save(graph, path);
        var loaded = GraphStore.Load(path);
        Assert.Equal(2, loaded.Nodes.Count);
        Assert.Single(loaded.Edges);
        File.Delete(path);
    }

    [Fact]
    public void CyclesFindSelfLoop()
    {
        var graph = TwoNodeGraph();
        graph.AddEdge("b", "a", "invokes", confidence: 1);
        var cycles = CodeSmells.FindCycles(graph);
        Assert.Single(cycles);
    }

    [Fact]
    public void ImpactExcludesDefinesAndConfidenceLowEdges()
    {
        var graph = new KnowledgeGraph();
        graph.GetOrAdd("core", "Core", "class");
        graph.GetOrAdd("direct", "Direct", "class");
        graph.GetOrAdd("low", "Low", "class");
        graph.AddEdge("direct", "core", "invokes", confidence: 1);
        graph.AddEdge("low", "core", "invokes", confidence: 0.3);
        var report = BlastRadius.ComputeWithRisk(graph, "core", 4, 0.5);
        Assert.Equal(1, report.Total);
    }

    [Fact]
    public void TrendFlagsMonotonicRiser()
    {
        var buckets = new List<(string Label, Dictionary<string, double> Scores)>
        {
            ("2024-06", new() { ["a.cs"] = 5 }),
            ("2024-09", new() { ["a.cs"] = 9 }),
            ("2024-12", new() { ["a.cs"] = 15 }),
            ("2025-03", new() { ["a.cs"] = 22 })
        };
        var result = TrendAnalyzer.AssembleSeries(buckets, 10, 30);
        Assert.True(result[0].MonotonicRiser);
        Assert.False(result[0].CrossingThreshold);
    }

    [Fact]
    public void MermaidRendersInterCommunityEdges()
    {
        var graph = TwoNodeGraph();
        graph.Nodes["a"].Community = 0;
        graph.Nodes["b"].Community = 1;
        var mermaid = MermaidExporter.Render(graph);
        Assert.StartsWith("graph TD", mermaid);
    }
}

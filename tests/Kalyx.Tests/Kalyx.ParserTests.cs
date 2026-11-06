using Kalyx.Core;
using Kalyx.Core.Models;
using Kalyx.Core.Parsing;

namespace Kalyx.Tests;

public class ParserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kalyx-test-" + Guid.NewGuid().ToString("N"));

    public ParserTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "Sample.cs"), """
            namespace Demo
            {
                public interface IGreeter { string Greet(); }

                public class Greeter : IGreeter
                {
                    public string Greet() => Helper();
                    private string Helper() => "hi";
                }

                public class App
                {
                    public void Run() { new Greeter().Greet(); }
                }
            }
            """);
        // Files in build/IDE folders must be ignored.
        foreach (var dir in new[] { "obj", "bin", ".vs" })
        {
            Directory.CreateDirectory(Path.Combine(_root, dir));
            File.WriteAllText(Path.Combine(_root, dir, "Ignored.cs"), "namespace Skipped { public class Ignored { } }");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ParserFindsTypesMethodsAndNamespaces()
    {
        var graph = CSharpParser.ParseDirectory(_root);
        Assert.Contains(graph.Nodes.Values, n => n.Name == "Greeter" && n.Kind == "class");
        Assert.Contains(graph.Nodes.Values, n => n.Name == "IGreeter" && n.Kind == "interface");
        Assert.Contains(graph.Nodes.Values, n => n.Name == "Run" && n.Kind == "method");
        Assert.Contains(graph.Nodes.Values, n => n.Name == "Demo" && n.Kind == "namespace");
    }

    [Fact]
    public void ParserSkipsBuildAndIdeDirectories()
    {
        var graph = CSharpParser.ParseDirectory(_root);
        Assert.DoesNotContain(graph.Nodes.Values, n => n.Name == "Ignored");
    }

    [Fact]
    public void ParserExtractsImplementsAndInvokesEdges()
    {
        var graph = CSharpParser.ParseDirectory(_root);
        Assert.Contains(graph.Edges, e => e.Kind == "implements"
            && e.FromId == "type:Demo.Greeter" && e.ToId == "type:Demo.IGreeter");
        Assert.Contains(graph.Edges, e => e.Kind == "invokes"
            && e.FromId == "method:Demo.App.Run()" && e.ToId == "method:Demo.Greeter.Greet()"
            && e.Confidence >= 0.9);
    }

    [Fact]
    public void PathAndExplainWorkOnParsedGraph()
    {
        var graph = CSharpParser.ParseDirectory(_root);
        var path = GraphQueries.Path(graph, "method:Demo.App.Run()", "method:Demo.Greeter.Greet()");
        Assert.StartsWith("Path (", path);
        var explain = GraphQueries.Explain(graph, "Greeter");
        Assert.Contains("Node: Greeter", explain);
        Assert.Contains("Run", GraphQueries.Query(graph, "Run").Select(n => n.Name));
    }

    [Fact]
    public void ImpactOfCalleeIncludesCaller()
    {
        var graph = CSharpParser.ParseDirectory(_root);
        var report = BlastRadius.ComputeWithRisk(graph, "method:Demo.Greeter.Greet()");
        Assert.True(report.Total >= 1);
        Assert.Contains(report.ByCommunity.Values.SelectMany(v => v), item => item.Name == "Run");
    }

    [Fact]
    public void SavedGraphRoundTripsFromParser()
    {
        var graph = CSharpParser.ParseDirectory(_root);
        var file = Path.Combine(_root, "out", "graph.json");
        GraphStore.Save(graph, file);
        var loaded = GraphStore.Load(file);
        Assert.Equal(graph.Nodes.Count, loaded.Nodes.Count);
        Assert.Equal(graph.Edges.Count, loaded.Edges.Count);
    }
}

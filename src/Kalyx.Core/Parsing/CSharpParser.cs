using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kalyx.Core.Models;

namespace Kalyx.Core.Parsing;

public static class CSharpParser
{
    private static string? RelativeFile(string root, string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        return Path.GetRelativePath(root, Path.GetFullPath(file)).Replace('\\', '/');
    }

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".git", ".vs", ".idea", "node_modules", "packages" };

    private static bool IsExcluded(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .SkipLast(1).Any(ExcludedDirectories.Contains);

    public static KnowledgeGraph ParseDirectory(string rootDirectory)
    {
        var graph = new KnowledgeGraph();
        var root = Path.GetFullPath(rootDirectory);
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(Path.GetRelativePath(root, f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        var trees = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f)).ToList();
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location));
        var compilation = CSharpCompilation.Create("kalyx", trees, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        foreach (var tree in trees)
        {
            var file = tree.FilePath;
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var fileId = $"file:{relative}";
            var fileNode = graph.GetOrAdd(fileId, relative, "file", relative);
            var model = compilation.GetSemanticModel(tree);
            var rootNode = tree.GetCompilationUnitRoot();

            foreach (var type in rootNode.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var symbol = model.GetDeclaredSymbol(type);
                if (symbol == null) continue;
                var id = $"type:{symbol.ToDisplayString()}";
                var line = type.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var kind = type switch
                {
                    InterfaceDeclarationSyntax => "interface",
                    RecordDeclarationSyntax => "record",
                    StructDeclarationSyntax => "struct",
                    _ => "class"
                };
                var node = graph.GetOrAdd(id, symbol.Name, kind, relative, line);
                graph.AddEdge(fileId, node.Id, "contains");

                if (symbol.BaseType != null && symbol.BaseType.SpecialType != SpecialType.System_Object)
                    graph.AddEdge(node.Id, $"type:{symbol.BaseType.ToDisplayString()}", "inherits");
                foreach (var iface in symbol.Interfaces)
                    graph.AddEdge(node.Id, $"type:{iface.ToDisplayString()}", "implements", EdgeOrigin.Inferred, 0.85);

                foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
                {
                    var methodSymbol = model.GetDeclaredSymbol(method);
                    if (methodSymbol == null) continue;
                    var methodId = $"method:{methodSymbol.ToDisplayString()}";
                    var methodLine = method.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var methodNode = graph.GetOrAdd(methodId, methodSymbol.Name, "method", relative, methodLine);
                    graph.AddEdge(node.Id, methodNode.Id, "defines");

                    foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        var invoked = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol
                            ?? model.GetSymbolInfo(invocation).CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                        string toId;
                        double confidence;
                        if (invoked != null)
                        {
                            toId = $"method:{invoked.ToDisplayString()}";
                            confidence = 1.0;
                            graph.GetOrAdd(toId, invoked.Name, "method",
                                RelativeFile(root, invoked.Locations.FirstOrDefault()?.SourceTree?.FilePath), null);
                        }
                        else
                        {
                            var name = invocation.Expression.ToString().Split('.').Last();
                            toId = $"method:*:{name}";
                            confidence = 0.6;
                        }
                        graph.AddEdge(methodId, toId, "invokes", EdgeOrigin.Extracted, confidence);
                    }
                }
            }

            foreach (var ns in rootNode.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            {
                var name = ns.Name.ToString();
                graph.GetOrAdd($"namespace:{name}", name, "namespace", relative);
                graph.AddEdge(fileId, $"namespace:{name}", "contains");
            }
        }

        Finalize(graph, root);
        return graph;
    }

    public static void Finalize(KnowledgeGraph graph, string rootDirectory)
    {
        ResolveUnresolvedMethods(graph);
        ImportResolver.Resolve(graph, rootDirectory);
        EdgeResolver.Prune(graph);
        AssignCommunities(graph);
    }

    public static void ResolveUnresolvedMethods(KnowledgeGraph graph)
    {
        foreach (var edge in graph.Edges.Where(e => e.ToId.StartsWith("method:*:")).ToList())
        {
            var name = edge.ToId[(edge.ToId.LastIndexOf(':') + 1)..];
            var candidates = graph.Nodes.Values.Where(n => n.Kind == "method" && n.Name == name).ToList();
            graph.Edges.Remove(edge);
            if (candidates.Count == 1)
                graph.AddEdge(edge.FromId, candidates[0].Id, edge.Kind, EdgeOrigin.Inferred, 0.85, edge.Weight);
            else if (candidates.Count > 1)
                foreach (var candidate in candidates)
                    graph.AddEdge(edge.FromId, candidate.Id, edge.Kind, EdgeOrigin.Inferred, 0.4, edge.Weight);
        }
        EdgeResolver.Prune(graph);
    }

    public static void AssignCommunities(KnowledgeGraph graph)
    {
        var ids = graph.Nodes.Keys.ToList();
        if (ids.Count == 0) return;
        var label = ids.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var changed = false;
            foreach (var id in ids)
            {
                var counts = graph.Nodes[id].Neighbors
                    .Where(label.ContainsKey)
                    .GroupBy(n => label[n])
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
                if (counts.Count == 0) continue;
                var best = counts[0].Key;
                if (label[id] == best) continue;
                label[id] = best; changed = true;
            }
            if (!changed) break;
        }
        var mapping = new Dictionary<int, int>();
        var next = 0;
        foreach (var id in ids)
        {
            if (!mapping.TryGetValue(label[id], out var community))
                mapping[label[id]] = community = next++;
            graph.Nodes[id].Community = community;
        }
    }
}

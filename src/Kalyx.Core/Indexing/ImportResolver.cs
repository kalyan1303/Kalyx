using Kalyx.Core.Models;

namespace Kalyx.Core;

public static class ImportResolver
{
    public static void Resolve(KnowledgeGraph graph, string rootDirectory)
    {
        var sourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".js", ".jsx", ".ts", ".tsx", ".py", ".go", ".rs", ".java" };
        var files = Directory.Exists(rootDirectory)
            ? Directory.EnumerateFiles(rootDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => sourceExtensions.Contains(Path.GetExtension(f)))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .ToList()
            : new List<string>();

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
            var fileId = $"file:{relative}";
            graph.GetOrAdd(fileId, relative, "file", file);
            var text = File.ReadAllText(file);
            var importPrefixes = new[] { "import ", "require(", "from " };
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim().Trim('"', '\'', ';', '\r');
                var fromIndex = trimmed.IndexOf(" from ", StringComparison.Ordinal);
                if (fromIndex >= 0) AddSpecifier(trimmed[(fromIndex + 6)..].Trim().Trim('"', '\'', ';'));
                else if (trimmed.StartsWith("require(", StringComparison.Ordinal))
                {
                    var start = trimmed.IndexOf('"') >= 0 ? trimmed.IndexOf('"') : trimmed.IndexOf('\'');
                    if (start >= 0)
                    {
                        var endQuote = trimmed.IndexOf(trimmed[start], start + 1);
                        if (endQuote > start) AddSpecifier(trimmed[(start + 1)..endQuote]);
                    }
                }
                else if (trimmed.StartsWith("import ", StringComparison.Ordinal))
                {
                    var firstQuote = trimmed.IndexOf('"');
                    var secondQuote = trimmed.IndexOf('\'');
                    var start = firstQuote >= 0 ? (secondQuote >= 0 ? Math.Min(firstQuote, secondQuote) : firstQuote) : secondQuote;
                    if (start >= 0)
                    {
                        var endQuote = trimmed.IndexOf(trimmed[start], start + 1);
                        if (endQuote > start) AddSpecifier(trimmed[(start + 1)..endQuote]);
                    }
                }
            }

            foreach (var specifier in candidates.Where(s => s.StartsWith(".")))
            {
                var directory = Path.GetDirectoryName(file) ?? rootDirectory;
                var basePath = Path.GetFullPath(Path.Combine(directory, specifier.Replace('/', '\\')));
                var resolved = ResolveModule(basePath);
                if (resolved == null) continue;
                var resolvedRelative = Path.GetRelativePath(rootDirectory, resolved).Replace('\\', '/');
                var targetId = $"file:{resolvedRelative}";
                graph.GetOrAdd(targetId, resolvedRelative, "file", resolved);
                graph.AddEdge(fileId, targetId, "imports", EdgeOrigin.Extracted, 0.9);
            }

            void AddSpecifier(string value)
            {
                value = value.Trim().Trim('"', '\'', '`');
                if (value.StartsWith(".")) candidates.Add(value);
            }
        }
    }

    private static string? ResolveModule(string basePath)
    {
        if (File.Exists(basePath)) return basePath;
        foreach (var extension in new[] { ".ts", ".tsx", ".js", ".jsx", ".py" })
        {
            var candidate = basePath + extension;
            if (File.Exists(candidate)) return candidate;
        }
        foreach (var extension in new[] { ".ts", ".tsx", ".js", ".jsx", ".py" })
        {
            var candidate = Path.Combine(basePath, "index" + extension);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

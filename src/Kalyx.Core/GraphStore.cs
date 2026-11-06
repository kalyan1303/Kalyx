using System.Text.Json;
using System.Text.Json.Serialization;
using Kalyx.Core.Models;

namespace Kalyx.Core;

public static class GraphStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static KnowledgeGraph Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Graph file not found; run 'kalyx parse' first.", path);
        return JsonSerializer.Deserialize<KnowledgeGraph>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"Invalid graph file: {path}");
    }

    public static void Save(KnowledgeGraph graph, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(graph, Options));
    }
}

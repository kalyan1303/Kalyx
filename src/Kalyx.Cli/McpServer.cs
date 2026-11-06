using System.Text.Json;
using System.Text.Json.Nodes;
using Kalyx.Core;
using Kalyx.Core.Models;

namespace Kalyx.Cli;

public static class McpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string ServerVersion =>
        typeof(McpServer).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    public static int Run(string graphPath)
    {
        if (!File.Exists(graphPath))
        {
            Console.Error.WriteLine($"kalyx mcp: graph not found: {graphPath}. Run 'kalyx parse' first.");
            return 1;
        }
        var graph = GraphStore.Load(graphPath);
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject? request;
            try { request = JsonNode.Parse(line) as JsonObject; }
            catch { Emit(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = null, ["error"] = new JsonObject { ["code"] = -32700, ["message"] = "Parse error" } }); continue; }
            if (request == null) continue;
            var id = request["id"]?.DeepClone();
            var method = request["method"]?.GetValue<string>();
            try
            {
                JsonNode? result = method switch
                {
                    "initialize" => new JsonObject
                    {
                        ["protocolVersion"] = "2024-11-05",
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = "KALYX", ["version"] = ServerVersion }
                    },
                    "notifications/initialized" => null,
                    "tools/list" => new JsonObject { ["tools"] = Tools },
                    "tools/call" => CallTool(graph, request["params"] as JsonObject),
                    "ping" => new JsonObject(),
                    _ => throw new McpError(-32601, $"Method not found: {method}")
                };
                if (result != null && id != null)
                    Emit(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
            }
            catch (McpError error)
            {
                if (id != null) Emit(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id,
                    ["error"] = new JsonObject { ["code"] = error.Code, ["message"] = error.Message } });
            }
        }
        return 0;
    }

    private static JsonArray Tools => new(
        Tool("kalyx_query", "Search nodes by name", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["pattern"] = new JsonObject { ["type"] = "string" }, ["kind"] = new JsonObject { ["type"] = "string" } },
            ["required"] = new JsonArray("pattern")
        }),
        Tool("kalyx_path", "Find a dependency path", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["from"] = new JsonObject { ["type"] = "string" }, ["to"] = new JsonObject { ["type"] = "string" } },
            ["required"] = new JsonArray("from", "to")
        }),
        Tool("kalyx_impact", "Compute change blast radius", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["node"] = new JsonObject { ["type"] = "string" }, ["depth"] = new JsonObject { ["type"] = "integer" } },
            ["required"] = new JsonArray("node")
        }),
        Tool("kalyx_smells", "Detect cycles and architectural smells", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
        Tool("kalyx_explain", "Explain a code entity", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["node"] = new JsonObject { ["type"] = "string" } },
            ["required"] = new JsonArray("node")
        }));

    private static JsonObject Tool(string name, string description, JsonObject schema) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = schema
    };

    private static JsonObject CallTool(KnowledgeGraph graph, JsonObject? parameters)
    {
        var name = parameters?["name"]?.GetValue<string>();
        var args = parameters?["arguments"] as JsonObject ?? new JsonObject();
        string text = name switch
        {
            "kalyx_query" => string.Join(Environment.NewLine, GraphQueries.Query(graph,
                args["pattern"]?.GetValue<string>() ?? "", args["kind"]?.GetValue<string>(), 25)
                .Select(n => $"{n.Id} | {n.Name} | {n.Kind} | community {n.Community}")),
            "kalyx_path" => GraphQueries.Path(graph, args["from"]?.GetValue<string>() ?? "", args["to"]?.GetValue<string>() ?? ""),
            "kalyx_impact" => ImpactText(graph, args["node"]?.GetValue<string>() ?? "", args["depth"]?.GetValue<int>() ?? 4),
            "kalyx_smells" => SmellText(graph),
            "kalyx_explain" => GraphQueries.Explain(graph, args["node"]?.GetValue<string>() ?? ""),
            _ => throw new McpError(-32602, $"Unknown tool: {name}")
        };
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text })
        };
    }

    private static string ImpactText(KnowledgeGraph graph, string node, int depth)
    {
        var report = BlastRadius.ComputeWithRisk(graph, node, depth);
        var lines = new List<string> { $"risk={report.Risk}/100 ({report.Grade}), dependents={report.Total}" };
        foreach (var bucket in report.ByCommunity.OrderBy(x => x.Key))
            foreach (var item in bucket.Value.OrderBy(x => x.Depth))
                lines.Add($"community={bucket.Key} depth={item.Depth} {item.Name} ({item.Id})");
        return string.Join(Environment.NewLine, lines);
    }

    private static string SmellText(KnowledgeGraph graph)
    {
        var report = CodeSmells.Analyze(graph);
        return $"cycles={report.Cycles.Count}, godNodes={report.GodNodes.Count}, unstableModules={report.UnstableModules.Count}";
    }

    private static void Emit(JsonObject value) =>
        Console.Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private sealed class McpError(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }
}

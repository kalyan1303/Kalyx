using Kalyx.Core.Models;

namespace Kalyx.Core;

public record GraphDiffReport(
    List<string> Added,
    List<string> Removed,
    List<(string Id, int Before, int After)> CommunityChanges,
    List<(string From, string To, string Kind, double Before, double After)> EdgeChanges);

public static class GraphDiff
{
    public static GraphDiffReport Compare(KnowledgeGraph before, KnowledgeGraph after)
    {
        var added = after.Nodes.Keys.Except(before.Nodes.Keys).Select(id => after.Nodes[id].Name).ToList();
        var removed = before.Nodes.Keys.Except(after.Nodes.Keys).Select(id => before.Nodes[id].Name).ToList();
        var community = before.Nodes.Keys.Intersect(after.Nodes.Keys)
            .Where(id => before.Nodes[id].Community != after.Nodes[id].Community)
            .Select(id => (id, before.Nodes[id].Community, after.Nodes[id].Community)).ToList();
        var pairsBefore = before.Edges.GroupBy(e => (e.FromId, e.ToId, e.Kind))
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Weight));
        var pairsAfter = after.Edges.GroupBy(e => (e.FromId, e.ToId, e.Kind))
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Weight));
        var edgeChanges = pairsBefore.Keys.Union(pairsAfter.Keys)
            .Where(k => pairsBefore.GetValueOrDefault(k) != pairsAfter.GetValueOrDefault(k))
            .Select(k => (k.FromId, k.ToId, k.Kind,
                pairsBefore.GetValueOrDefault(k), pairsAfter.GetValueOrDefault(k))).ToList();
        return new GraphDiffReport(added, removed, community, edgeChanges);
    }
}

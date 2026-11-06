namespace Kalyx.Core;

public static class TrendRenderer
{
    public static string Render(List<FileTrend> trends)
    {
        var labels = trends.SelectMany(t => t.Series.Select(s => s.Bucket)).Distinct().OrderBy(x => x).ToList();
        var output = new List<string> { "| File | " + string.Join(" | ", labels) + " | Flag |", "|---|" + string.Join("|", labels.Select(_ => "---:")) + "|---|" };
        foreach (var trend in trends)
        {
            var values = trend.Series.ToDictionary(x => x.Bucket, x => x.Score);
            var flag = trend.CrossingThreshold ? "crossing threshold" : trend.MonotonicRiser ? "riser" : "";
            output.Add("| " + trend.Path + " | " + string.Join(" | ", labels.Select(l => values.GetValueOrDefault(l).ToString("F1"))) + " | " + flag + " |");
        }
        return string.Join(Environment.NewLine, output);
    }
}

// Finds and executes the shortest / cheapest chain of converters between file formats.

namespace ZestDrop.Engines.Routing;

public sealed record ConversionHop(string From, string To, int Cost, Func<string, string, string> Convert);

public sealed record ConversionRoute(IReadOnlyList<ConversionHop> Hops, int TotalCost);

public sealed class RoutePlanner
{
    public const int MaxHops = 3;

    private readonly List<ConversionHop> _hops = [];
    private readonly Dictionary<string, List<ConversionHop>> _edgesFrom = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string from, string to, int cost, Func<string, string, string> convert)
    {
        from = from.TrimStart('.').ToLowerInvariant();
        to = to.TrimStart('.').ToLowerInvariant();

        if (from == to)
            return;

        var hop = new ConversionHop(from, to, cost, convert);
        _hops.Add(hop);

        if (!_edgesFrom.TryGetValue(from, out var list))
        {
            list = [];
            _edgesFrom[from] = list;
        }
        list.Add(hop);
    }

    public ConversionRoute? Plan(string from, string to)
    {
        from = from.TrimStart('.').ToLowerInvariant();
        to = to.TrimStart('.').ToLowerInvariant();

        if (from == to)
            return null;

        if (!_edgesFrom.ContainsKey(from))
            return null;

        var dist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [from] = 0 };
        var prev = new Dictionary<string, (string Node, ConversionHop Hop)>(StringComparer.OrdinalIgnoreCase);
        var hops = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [from] = 0 };
        var queue = new PriorityQueue<string, int>();
        queue.Enqueue(from, 0);

        while (queue.TryDequeue(out var node, out var cost))
        {
            if (cost > dist[node])
                continue;
            if (string.Equals(node, to, StringComparison.OrdinalIgnoreCase))
                break;
            if (hops[node] >= MaxHops)
                continue;

            if (!_edgesFrom.TryGetValue(node, out var edges))
                continue;

            foreach (var edge in edges)
            {
                var nextCost = cost + edge.Cost;
                if (dist.TryGetValue(edge.To, out var known) && known <= nextCost)
                    continue;

                dist[edge.To] = nextCost;
                hops[edge.To] = hops[node] + 1;
                prev[edge.To] = (node, edge);
                queue.Enqueue(edge.To, nextCost);
            }
        }

        if (!prev.ContainsKey(to))
            return null;

        var resultHops = new List<ConversionHop>();
        for (var curr = to; !string.Equals(curr, from, StringComparison.OrdinalIgnoreCase); curr = prev[curr].Node)
        {
            resultHops.Add(prev[curr].Hop);
        }
        resultHops.Reverse();

        return new ConversionRoute(resultHops, dist[to]);
    }

    public bool CanConvert(string from, string to) => Plan(from, to) is not null;

    public string Execute(string inputPath, string targetFormat, string? outputPath = null)
    {
        var from = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
        var to = targetFormat.TrimStart('.').ToLowerInvariant();

        var route = Plan(from, to)
            ?? throw new NotSupportedException($"No conversion route found from .{from} to .{to}");

        outputPath ??= Path.ChangeExtension(inputPath, "." + to);

        var currentFile = inputPath;
        var tempFiles = new List<string>();

        try
        {
            for (var i = 0; i < route.Hops.Count; i++)
            {
                var hop = route.Hops[i];
                var isFinal = (i == route.Hops.Count - 1);
                var stepOutput = isFinal
                    ? outputPath
                    : Path.Combine(Path.GetTempPath(), $"zestdrop_route_{Guid.NewGuid():N}.{hop.To}");

                if (!isFinal)
                    tempFiles.Add(stepOutput);

                currentFile = hop.Convert(currentFile, stepOutput);
            }
            return outputPath;
        }
        finally
        {
            foreach (var temp in tempFiles)
            {
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch
                {
                }
            }
        }
    }
}

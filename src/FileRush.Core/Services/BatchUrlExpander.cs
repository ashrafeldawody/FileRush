using System.Text.RegularExpressions;

namespace FileRush.Core.Services;

public static partial class BatchUrlExpander
{
    public const int MaxUrls = 100000;

    public static IReadOnlyList<string> Expand(string pattern, string from, string to, bool letters, int zeroPadding)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return Array.Empty<string>();
        var star = pattern.IndexOf('*');
        if (star < 0) return new[] { pattern.Trim() };
        var prefix = pattern[..star];
        var suffix = pattern[(star + 1)..];
        var results = new List<string>();
        if (letters)
        {
            var start = from.Length > 0 ? char.ToLowerInvariant(from[0]) : 'a';
            var end = to.Length > 0 ? char.ToLowerInvariant(to[0]) : 'z';
            if (end < start) (start, end) = (end, start);
            var upper = from.Length > 0 && char.IsUpper(from[0]);
            for (var c = start; c <= end && results.Count < MaxUrls; c++)
            {
                results.Add(prefix + (upper ? char.ToUpperInvariant(c) : c) + suffix);
            }
        }
        else
        {
            if (!long.TryParse(from, out var start)) start = 0;
            if (!long.TryParse(to, out var end)) end = start;
            if (end < start) (start, end) = (end, start);
            var width = Math.Max(zeroPadding, 0);
            for (var i = start; i <= end && results.Count < MaxUrls; i++)
            {
                results.Add(prefix + i.ToString().PadLeft(width, '0') + suffix);
            }
        }
        return results;
    }

    public static IReadOnlyList<string> ExpandBrackets(string pattern)
    {
        var match = BracketRegex().Match(pattern);
        if (!match.Success) return new[] { pattern.Trim() };
        var prefix = pattern[..match.Index];
        var suffix = pattern[(match.Index + match.Length)..];
        var from = match.Groups["from"].Value;
        var to = match.Groups["to"].Value;
        var letters = !char.IsDigit(from[0]);
        var padding = letters ? 0 : (from.Length > 1 && from[0] == '0' ? from.Length : 0);
        var results = new List<string>();
        foreach (var head in Expand(prefix + "*" + suffix, from, to, letters, padding))
        {
            foreach (var expanded in ExpandBrackets(head))
            {
                if (results.Count >= MaxUrls) break;
                results.Add(expanded);
            }
        }
        return results;
    }

    [GeneratedRegex(@"\[(?<from>[0-9]+|[a-zA-Z])-(?<to>[0-9]+|[a-zA-Z])\]")]
    private static partial Regex BracketRegex();
}

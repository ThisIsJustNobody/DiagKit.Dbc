using System.Collections.ObjectModel;

namespace DiagKit.Dbc;

internal static class DbcNameLookup
{
    public static IReadOnlyList<string> CreateAliases(
        string canonicalName,
        string? sourceName = null,
        IReadOnlyList<string>? nameAliases = null)
    {
        var aliases = new List<string>();
        AddAlias(aliases, canonicalName, sourceName);
        if (nameAliases is not null)
        {
            foreach (var alias in nameAliases)
            {
                AddAlias(aliases, canonicalName, alias);
            }
        }

        return aliases.Count == 0
            ? Array.AsReadOnly(Array.Empty<string>())
            : new ReadOnlyCollection<string>(aliases.ToArray());
    }

    public static IEnumerable<string> EnumerateLookupNames(string name, IReadOnlyList<string> aliases)
    {
        yield return name;
        foreach (var alias in aliases)
        {
            yield return alias;
        }
    }

    public static bool Matches(string name, IReadOnlyList<string> aliases, string candidate)
    {
        if (string.Equals(name, candidate, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var alias in aliases)
        {
            if (string.Equals(alias, candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static Dictionary<string, T[]> BuildLookup<T>(
        IEnumerable<T> items,
        Func<T, string> getName,
        Func<T, IReadOnlyList<string>> getAliases)
        where T : class
    {
        var map = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            foreach (var lookupName in EnumerateLookupNames(getName(item), getAliases(item)))
            {
                if (!map.TryGetValue(lookupName, out var matches))
                {
                    matches = [];
                    map.Add(lookupName, matches);
                }

                if (!matches.Contains(item))
                {
                    matches.Add(item);
                }
            }
        }

        return map.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    private static void AddAlias(List<string> aliases, string canonicalName, string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias) ||
            string.Equals(alias, canonicalName, StringComparison.Ordinal) ||
            aliases.Contains(alias, StringComparer.Ordinal))
        {
            return;
        }

        aliases.Add(alias);
    }
}

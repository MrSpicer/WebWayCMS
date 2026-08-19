using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WebWayCMS.Services.ContentSeeding;

/// <summary>
/// Resolves cross-item <c>@seed:&lt;guid&gt;</c> references inside a seed item's field overlay by
/// substituting a referenced seed key with the generated node id it produced. Because substitution is
/// a plain string replace, a token works both as a whole value (<c>"featuredFaqId": "@seed:…"</c>, a
/// bare guid string that deserializes to <c>Guid?</c>) and embedded inside a serialized-JSON string
/// (a page-type or widget <c>configurationJson</c>).
/// </summary>
public static class SeedReferenceResolver
{
    /// <summary>The literal token prefix <c>@seed:</c>.</summary>
    public const string TokenPrefix = "@seed:";

    private static readonly Regex TokenPattern = new(
        @"@seed:([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.Compiled);

    /// <summary>Every distinct seed id referenced by a string value anywhere in the tree.</summary>
    public static IReadOnlyCollection<Guid> CollectReferences(JsonNode? node)
    {
        var ids = new HashSet<Guid>();
        CollectStrings(node, ids);
        return ids;
    }

    /// <summary>
    /// Replaces each token in <paramref name="overlay"/> with its resolved node id, mutating the tree
    /// in place; returns the seed ids that had no mapping (once per occurrence).
    /// </summary>
    public static IReadOnlyList<Guid> Substitute(JsonObject overlay, IReadOnlyDictionary<Guid, Guid> resolved)
    {
        var unresolved = new List<Guid>();
        SubstituteNode(overlay, resolved, unresolved);
        return unresolved;
    }

    private static void CollectStrings(JsonNode? node, HashSet<Guid> ids)
    {
        if (node is JsonObject obj)
        {
            foreach (var prop in obj)
                CollectStrings(prop.Value, ids);
            return;
        }

        if (node is JsonArray array)
        {
            foreach (var element in array)
                CollectStrings(element, ids);
            return;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                foreach (Match match in TokenPattern.Matches(text))
                    ids.Add(Guid.Parse(match.Groups[1].Value));
            }
        }
    }

    private static JsonNode? SubstituteNode(JsonNode? node, IReadOnlyDictionary<Guid, Guid> resolved, List<Guid> unresolved)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToArray())
                obj[key] = SubstituteNode(obj[key], resolved, unresolved);
            return obj;
        }

        if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
                array[i] = SubstituteNode(array[i], resolved, unresolved);
            return array;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
                return SubstituteValue(text, resolved, unresolved);
            return value;
        }

        return node;
    }

    private static JsonValue SubstituteValue(string text, IReadOnlyDictionary<Guid, Guid> resolved, List<Guid> unresolved)
    {
        var replaced = TokenPattern.Replace(text, match =>
        {
            var id = Guid.Parse(match.Groups[1].Value);
            if (resolved.TryGetValue(id, out var nodeId))
                return nodeId.ToString();

            unresolved.Add(id);
            return match.Value;
        });

        return JsonValue.Create(replaced);
    }
}

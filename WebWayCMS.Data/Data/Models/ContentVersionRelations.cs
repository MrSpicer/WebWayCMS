namespace WebWayCMS.Data.Models;

/// <summary>
/// Reads and writes a version's relations. Reads see the version's effective relations: its stored
/// rows with any pending <see cref="ContentVersion.RelationWrites"/> laid over them by name.
/// </summary>
public static class ContentVersionRelations
{
    /// <summary>The single target of relation <paramref name="name"/>, or null when it has none.</summary>
    public static Guid? TargetOf(this ContentVersion version, string name)
        => version.TargetsOf(name) is [var first, ..] ? first : null;

    /// <summary>Every target of relation <paramref name="name"/>, by ordinal.</summary>
    public static IReadOnlyList<Guid> TargetsOf(this ContentVersion version, string name)
        => version.EffectiveRelations()
                  .Where(r => r.Name == name)
                  .OrderBy(r => r.Ordinal)
                  .Select(r => r.TargetNodeId)
                  .ToList();

    /// <summary>The ordinal of relation <paramref name="name"/>'s first target, or 0 when it has none.</summary>
    public static int OrdinalOf(this ContentVersion version, string name)
        => version.EffectiveRelations().Where(r => r.Name == name).OrderBy(r => r.Ordinal).FirstOrDefault()?.Ordinal ?? 0;

    /// <summary>The qualifier of relation <paramref name="name"/>'s first target, or null when it has none.</summary>
    public static string? QualifierOf(this ContentVersion version, string name)
        => version.EffectiveRelations().Where(r => r.Name == name).OrderBy(r => r.Ordinal).FirstOrDefault()?.Qualifier;

    /// <summary>Writes relation <paramref name="name"/> as one target, or as none when it is null or empty.</summary>
    public static void SetRelation(this ContentVersion version, string name, Guid? target, RelationKind kind, string qualifier = "", int ordinal = 0)
        => version.Write(name, target is { } t && t != Guid.Empty
            ? [new RelationWrite(t, ordinal, qualifier, kind)]
            : []);

    /// <summary>Writes relation <paramref name="name"/> as <paramref name="targets"/>, in order (empty ids skipped).</summary>
    public static void SetRelations(this ContentVersion version, string name, IEnumerable<Guid> targets, RelationKind kind)
        => version.Write(name, targets.Where(t => t != Guid.Empty)
                                      .Select((t, i) => new RelationWrite(t, i, string.Empty, kind))
                                      .ToList());

    /// <summary>
    /// Writes every relation name to exactly <paramref name="source"/>'s rows: names in the source are
    /// copied, and names this version has that the source lacks are cleared. Used by page revert/discard.
    /// </summary>
    public static void ReplaceAllRelationsFrom(this ContentVersion version, IEnumerable<ContentRelation> source)
    {
        var bySource = source.ToLookup(r => r.Name, StringComparer.Ordinal);
        var writes = new Dictionary<string, IReadOnlyList<RelationWrite>>(version.RelationWrites);
        foreach (var name in version.EffectiveRelations().Select(r => r.Name).Concat(bySource.Select(g => g.Key)).Distinct())
            writes[name] = bySource[name].Select(r => new RelationWrite(r.TargetNodeId, r.Ordinal, r.Qualifier, r.Kind)).ToList();
        version.RelationWrites = writes;
    }

    /// <summary>The version's stored relations with its pending writes laid over them.</summary>
    public static IReadOnlyList<ContentRelation> EffectiveRelations(this ContentVersion version)
        => Overlay(version.Relations, version.RelationWrites, version.Id);

    /// <summary>
    /// <paramref name="baseline"/>'s rows whose name is not in <paramref name="writes"/>, plus one new row
    /// per write, each with a fresh id owned by <paramref name="sourceVersionId"/>.
    /// </summary>
    public static List<ContentRelation> Overlay(
        IEnumerable<ContentRelation> baseline,
        IReadOnlyDictionary<string, IReadOnlyList<RelationWrite>> writes,
        Guid sourceVersionId)
    {
        var rows = baseline
            .Where(r => !writes.ContainsKey(r.Name))
            .Select(r => r with { Id = Guid.NewGuid(), SourceVersionId = sourceVersionId })
            .ToList();
        foreach (var (name, values) in writes)
        {
            rows.AddRange(values.Select(w => new ContentRelation
            {
                Id = Guid.NewGuid(),
                SourceVersionId = sourceVersionId,
                TargetNodeId = w.TargetNodeId,
                Kind = w.Kind,
                Name = name,
                Ordinal = w.Ordinal,
                Qualifier = w.Qualifier
            }));
        }
        return rows;
    }

    /// <summary>
    /// A content fingerprint of the version's effective relations, for equality and hashing; relation
    /// <paramref name="exceptName"/> (when given) is left out.
    /// </summary>
    public static string RelationFingerprint(this ContentVersion version, string? exceptName = null)
        => string.Join('\u001e', version.EffectiveRelations()
            .Where(r => r.Name != exceptName)
            .Select(r => $"{r.Name}|{r.Ordinal}|{r.TargetNodeId}|{r.Qualifier}|{(int)r.Kind}")
            .OrderBy(l => l, StringComparer.Ordinal));

    private static void Write(this ContentVersion version, string name, IReadOnlyList<RelationWrite> values)
        => version.RelationWrites = new Dictionary<string, IReadOnlyList<RelationWrite>>(version.RelationWrites)
        {
            [name] = values
        };
}

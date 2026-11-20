namespace WebWayCMS.Data.Services.Registrations;

/// <summary>
/// Brings a registration table in line with the registrations the code declares, the one rule every registry
/// seeder (widgets, page types, form components, content types) follows: a declared key with no row gets a new
/// row; a declared key with rows gets its code-owned columns synced on every row, never its admin-edited ones;
/// a row whose key the code no longer declares is reported as an orphan and never deleted.
/// </summary>
public static class RegistrationSynchronizer
{
    public static async Task<RegistrationSyncResult> SyncAsync<T>(
        IRegistrationSyncTarget<T> target,
        IEnumerable<RegistrationSyncItem<T>> items,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(items);

        var declared = items.DistinctBy(i => i.Key, StringComparer.OrdinalIgnoreCase).ToList();
        var rows = await target.LoadRowsAsync(ct);
        var rowsByKey = rows
            .GroupBy(target.KeyOf, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var updated = new List<string>();
        foreach (var item in declared)
        {
            if (!rowsByKey.TryGetValue(item.Key, out var existing))
                continue;

            var changed = false;
            foreach (var row in existing)
                changed |= item.SyncCodeOwned(row);
            if (changed)
                updated.Add(item.Key);
        }
        if (updated.Count > 0)
            await target.SaveCodeOwnedChangesAsync(ct);

        var inserted = new List<string>();
        var failed = new List<RegistrationSyncFailure>();
        foreach (var item in declared.Where(i => !rowsByKey.ContainsKey(i.Key)))
        {
            try
            {
                var (success, error) = await target.InsertAsync(item.Create(), ct);
                if (success)
                    inserted.Add(item.Key);
                else
                    failed.Add(new RegistrationSyncFailure(item.Key, error ?? "The registration could not be saved."));
            }
            catch (Exception ex)
            {
                failed.Add(new RegistrationSyncFailure(item.Key, ex.Message));
            }
        }

        var declaredKeys = declared.Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphaned = rowsByKey.Keys.Where(k => !declaredKeys.Contains(k)).ToList();

        return new RegistrationSyncResult(inserted, updated, failed, orphaned);
    }
}

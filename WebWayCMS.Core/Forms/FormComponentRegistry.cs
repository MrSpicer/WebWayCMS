using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Attributes;
using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;

namespace WebWayCMS.Forms;

/// <summary>
/// Singleton registry of form components, backed by the <c>FormComponentRegistration</c>
/// content type. Uses an immutable snapshot swapped under a lock for thread-safe reads.
/// </summary>
public sealed class FormComponentRegistry : IFormComponentRegistry
{
    private const int RefreshIntervalMinutes = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly object _sync = new();
    private volatile Snapshot _snapshot = Snapshot.Empty;

    private sealed record Snapshot(
        IReadOnlyList<FormComponentInfo> All,
        Dictionary<string, FormComponentInfo> ByName,
        Dictionary<EditorType, FormComponentInfo> ByEditorType,
        Dictionary<Type, FormComponentInfo> DefaultsByType,
        DateTime LoadedAt)
    {
        public static readonly Snapshot Empty = new(
            Array.Empty<FormComponentInfo>(),
            new Dictionary<string, FormComponentInfo>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<EditorType, FormComponentInfo>(),
            new Dictionary<Type, FormComponentInfo>(),
            DateTime.MinValue);
    }

    public FormComponentRegistry(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public IReadOnlyList<FormComponentInfo> GetAll()
    {
        var snapshot = EnsureLoaded();
        return snapshot.All;
    }

    public FormComponentInfo? GetByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        var snapshot = EnsureLoaded();
        snapshot.ByName.TryGetValue(name, out var info);
        return info;
    }

    public FormComponentInfo? GetForEditorType(EditorType editorType)
    {
        var snapshot = EnsureLoaded();
        snapshot.ByEditorType.TryGetValue(editorType, out var info);
        return info;
    }

    public FormComponentInfo? GetDefaultFor(Type clrType)
    {
        if (clrType == null)
            return null;
        var unwrapped = Nullable.GetUnderlyingType(clrType) ?? clrType;
        var snapshot = EnsureLoaded();
        snapshot.DefaultsByType.TryGetValue(unwrapped, out var info);
        return info;
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            _snapshot = Snapshot.Empty;
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private Snapshot EnsureLoaded()
    {
        var current = _snapshot;

        // Lock-free fast path
        if ((DateTime.UtcNow - current.LoadedAt).TotalMinutes < RefreshIntervalMinutes
            && current.All.Count > 0)
            return current;

        lock (_sync)
        {
            current = _snapshot;
            if ((DateTime.UtcNow - current.LoadedAt).TotalMinutes < RefreshIntervalMinutes
                && current.All.Count > 0)
                return current;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IFormComponentRegistrationService>();
                var dtos = service.GetActiveAsync().GetAwaiter().GetResult();
                _snapshot = BuildSnapshot(dtos);
            }
            catch (Exception ex)
            {
                Serilog.Log.ForContext<FormComponentRegistry>()
                    .Warning(ex, "Failed to load form component registrations from database.");
            }

            return _snapshot;
        }
    }

    private static Snapshot BuildSnapshot(List<FormComponentRegistrationDTO> dtos)
    {
        var all = new List<FormComponentInfo>(dtos.Count);
        var byName = new Dictionary<string, FormComponentInfo>(StringComparer.OrdinalIgnoreCase);
        var byEditorType = new Dictionary<EditorType, FormComponentInfo>();
        var defaultCandidates = new Dictionary<Type, List<FormComponentInfo>>();

        foreach (var dto in dtos)
        {
            var info = new FormComponentInfo
            {
                Name = dto.ComponentName,
                ViewComponentName = dto.ViewComponentName,
                DisplayName = dto.DisplayName,
                Description = dto.Description,
                Category = dto.Category,
                IconClass = dto.IconClass,
                Order = dto.Order,
                DataTypeNames = DeserializeDataTypeNames(dto.DataTypeNamesJson),
                EditorTypeAlias = string.IsNullOrEmpty(dto.EditorTypeAlias)
                    ? null
                    : Enum.TryParse<EditorType>(dto.EditorTypeAlias, out var et) ? et : null,
                IsDefaultForType = dto.IsDefaultForType,
                WriteViewName = dto.WriteViewName,
                ReadViewName = dto.ReadViewName
            };

            all.Add(info);
            byName[info.Name] = info;

            if (info.EditorTypeAlias.HasValue)
                byEditorType[info.EditorTypeAlias.Value] = info;

            if (info.IsDefaultForType)
            {
                foreach (var typeName in info.DataTypeNames)
                {
                    var type = ResolveClrType(typeName);
                    if (type != null)
                    {
                        if (!defaultCandidates.TryGetValue(type, out var candidates))
                        {
                            candidates = new List<FormComponentInfo>();
                            defaultCandidates[type] = candidates;
                        }
                        candidates.Add(info);
                    }
                }
            }
        }

        var defaultsByType = new Dictionary<Type, FormComponentInfo>();
        foreach (var (type, candidates) in defaultCandidates)
        {
            candidates.Sort((a, b) =>
            {
                var orderCompare = a.Order.CompareTo(b.Order);
                return orderCompare != 0
                    ? orderCompare
                    : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            defaultsByType[type] = candidates[0];

            if (candidates.Count > 1)
            {
                Serilog.Log.ForContext<FormComponentRegistry>()
                    .Warning("Multiple default form components for type {Type}: {Candidates}. Selected '{Selected}'.",
                        type.Name, string.Join(", ", candidates.Select(c => c.Name)), candidates[0].Name);
            }
        }

        all.Sort((a, b) =>
        {
            var catCompare = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
            if (catCompare != 0) return catCompare;
            var orderCompare = a.Order.CompareTo(b.Order);
            return orderCompare != 0
                ? orderCompare
                : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });

        return new Snapshot(
            all.AsReadOnly(),
            byName,
            byEditorType,
            defaultsByType,
            DateTime.UtcNow);
    }

    private static List<string> DeserializeDataTypeNames(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static Type? ResolveClrType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        try
        {
            var type = Type.GetType(typeName, throwOnError: false);
            if (type != null)
                return type;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}

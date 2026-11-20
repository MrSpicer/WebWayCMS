using WebWayCMS.Data.Models;

namespace WebWayCMS.Data.Services;

/// <summary>The listing order and category grouping shared by the component registration services.</summary>
public static class RegistrationQueryExtensions
{
    /// <summary>Registrations by category, then order, then display name.</summary>
    public static IOrderedQueryable<T> InDisplayOrder<T>(this IQueryable<T> q) where T : class, IRegistrationDescriptor
        => q.OrderBy(r => r.Category).ThenBy(r => r.Order).ThenBy(r => r.DisplayName);

    /// <summary>Registrations grouped by category (case-insensitively), each group in the given order.</summary>
    public static Dictionary<string, List<T>> GroupByCategory<T>(this IEnumerable<T> registrations) where T : IRegistrationDescriptor
        => registrations
            .GroupBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
}

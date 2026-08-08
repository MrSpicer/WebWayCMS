using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;

namespace WebWayCMS.ContentZones;

public class WidgetRegistry : IWidgetRegistry
{
    private const int RefreshIntervalMinutes = 5;

    private readonly IServiceScopeFactory _scopeFactory;

    private List<WidgetRegistrationInfo> _components = new();
    private Dictionary<string, WidgetRegistrationInfo> _componentsByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<WidgetRegistrationInfo>> _componentsByCategory = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastRefresh = DateTime.MinValue;

    public WidgetRegistry(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public IReadOnlyList<WidgetRegistrationInfo> GetAllComponents()
    {
        EnsureLoaded();
        return _components.AsReadOnly();
    }

    public WidgetRegistrationInfo? GetByName(string componentName)
    {
        if (string.IsNullOrEmpty(componentName))
            return null;
        EnsureLoaded();
        _componentsByName.TryGetValue(componentName, out var info);
        return info;
    }

    public IReadOnlyList<string> GetCategories()
    {
        EnsureLoaded();
        return _componentsByCategory.Keys.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
    }

    public IReadOnlyList<WidgetRegistrationInfo> GetByCategory(string category)
    {
        if (string.IsNullOrEmpty(category))
            return Array.Empty<WidgetRegistrationInfo>();
        EnsureLoaded();
        return _componentsByCategory.TryGetValue(category, out var list)
            ? list.AsReadOnly()
            : Array.Empty<WidgetRegistrationInfo>();
    }

    public IReadOnlyDictionary<string, IReadOnlyList<WidgetRegistrationInfo>> GetComponentsByCategory()
    {
        EnsureLoaded();
        return _componentsByCategory.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<WidgetRegistrationInfo>)kvp.Value.AsReadOnly(),
            StringComparer.OrdinalIgnoreCase);
    }

    public object? CreateDefaultConfiguration(string componentName)
    {
        var info = GetByName(componentName);
        if (info == null || string.IsNullOrEmpty(info.ConfigurationTypeName))
            return null;

        try
        {
            var type = ResolveType(info.ConfigurationTypeName);
            if (type == null)
                return null;
            return Activator.CreateInstance(type);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create default configuration for component '{componentName}': {ex.Message}");
            return null;
        }
    }

    public IReadOnlyList<string> ValidateConfiguration(string componentName, object configuration)
    {
        var errors = new List<string>();
        var info = GetByName(componentName);

        if (info == null)
        {
            errors.Add($"Unknown component: {componentName}");
            return errors;
        }

        if (string.IsNullOrEmpty(info.ConfigurationTypeName))
            return errors;

        var configType = ResolveType(info.ConfigurationTypeName);
        if (configType == null)
            return errors;

        object? configObj = configuration;
        if (configuration is string jsonString)
        {
            try
            {
                configObj = JsonSerializer.Deserialize(jsonString, configType, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                errors.Add($"Invalid JSON: {ex.Message}");
                return errors;
            }
        }

        if (configObj == null)
        {
            errors.Add("Configuration is required.");
            return errors;
        }

        foreach (var propInfo in info.Properties)
        {
            var prop = configType.GetProperty(propInfo.Name);
            if (prop == null)
                continue;

            var value = prop.GetValue(configObj);

            if (propInfo.IsRequired)
            {
                if (value == null || (value is string s && string.IsNullOrWhiteSpace(s)) ||
                    (value is Guid g && g == Guid.Empty))
                {
                    errors.Add($"{propInfo.Label} is required.");
                }
            }

            if (value != null && (propInfo.Min.HasValue || propInfo.Max.HasValue))
            {
                if (double.TryParse(value.ToString(), out var numValue))
                {
                    if (propInfo.Min.HasValue && numValue < propInfo.Min.Value)
                        errors.Add($"{propInfo.Label} must be at least {propInfo.Min.Value}.");
                    if (propInfo.Max.HasValue && numValue > propInfo.Max.Value)
                        errors.Add($"{propInfo.Label} must be at most {propInfo.Max.Value}.");
                }
            }

            if (value is string strValue && propInfo.MaxLength.HasValue && strValue.Length > propInfo.MaxLength.Value)
            {
                errors.Add($"{propInfo.Label} must not exceed {propInfo.MaxLength.Value} characters.");
            }

            if (value is string patternValue && !string.IsNullOrEmpty(propInfo.Pattern))
            {
                if (!Regex.IsMatch(patternValue, propInfo.Pattern))
                {
                    errors.Add(!string.IsNullOrEmpty(propInfo.PatternErrorMessage)
                        ? propInfo.PatternErrorMessage
                        : $"{propInfo.Label} has an invalid format.");
                }
            }
        }

        return errors;
    }

    public void Invalidate()
    {
        _lastRefresh = DateTime.MinValue;
    }

    private void EnsureLoaded()
    {
        if ((DateTime.UtcNow - _lastRefresh).TotalMinutes < RefreshIntervalMinutes
            && _components.Count > 0)
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IWidgetRegistrationService>();
            var dtos = service.GetActiveAsync().GetAwaiter().GetResult();
            BuildFromDtos(dtos);
            _lastRefresh = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load widget registrations from database: {ex.Message}");
        }
    }

    private void BuildFromDtos(List<WidgetRegistrationDTO> dtos)
    {
        _components = new List<WidgetRegistrationInfo>(dtos.Count);
        _componentsByName = new Dictionary<string, WidgetRegistrationInfo>(StringComparer.OrdinalIgnoreCase);
        _componentsByCategory = new Dictionary<string, List<WidgetRegistrationInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var dto in dtos)
        {
            var info = new WidgetRegistrationInfo
            {
                Name = dto.ComponentName,
                DisplayName = dto.DisplayName,
                Description = dto.Description,
                Category = dto.Category,
                IconClass = dto.IconClass,
                Order = dto.Order,
                ConfigurationTypeName = dto.ConfigurationTypeName,
                Properties = DeserializePropertyDefinitions(dto.PropertyDefinitionsJson)
            };

            _components.Add(info);
            _componentsByName[info.Name] = info;

            if (!_componentsByCategory.TryGetValue(info.Category, out var categoryList))
            {
                categoryList = new List<WidgetRegistrationInfo>();
                _componentsByCategory[info.Category] = categoryList;
            }
            categoryList.Add(info);
        }

        _components.Sort((a, b) =>
        {
            var catCompare = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
            if (catCompare != 0) return catCompare;
            var orderCompare = a.Order.CompareTo(b.Order);
            return orderCompare != 0 ? orderCompare : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static List<FormPropertyInfo> DeserializePropertyDefinitions(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return new List<FormPropertyInfo>();

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var list = JsonSerializer.Deserialize<List<FormPropertyInfo>>(json, options);
            return list ?? new List<FormPropertyInfo>();
        }
        catch
        {
            return new List<FormPropertyInfo>();
        }
    }

    internal static Type? ResolveType(string typeName)
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

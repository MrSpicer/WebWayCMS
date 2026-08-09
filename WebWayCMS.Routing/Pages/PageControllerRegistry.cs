using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

using WebWayCMS.Data.Models;
using WebWayCMS.Data.Services;
using WebWayCMS.Forms;

namespace WebWayCMS.Pages;

public class PageControllerRegistry : IPageControllerRegistry
{
    private const int RefreshIntervalMinutes = 5;

    private readonly IServiceScopeFactory _scopeFactory;

    private List<PageControllerInfo> _controllers = new();
    private Dictionary<string, PageControllerInfo> _controllersByName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<PageControllerInfo>> _controllersByCategory = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastRefresh = DateTime.MinValue;

    public PageControllerRegistry(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public IReadOnlyList<PageControllerInfo> GetAllControllers()
    {
        EnsureLoaded();
        return _controllers.AsReadOnly();
    }

    public PageControllerInfo? GetByName(string controllerName)
    {
        if (string.IsNullOrEmpty(controllerName))
            return null;
        EnsureLoaded();
        _controllersByName.TryGetValue(controllerName, out var info);
        return info;
    }

    public IReadOnlyList<string> GetCategories()
    {
        EnsureLoaded();
        return _controllersByCategory.Keys.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
    }

    public IReadOnlyList<PageControllerInfo> GetByCategory(string category)
    {
        if (string.IsNullOrEmpty(category))
            return Array.Empty<PageControllerInfo>();
        EnsureLoaded();
        return _controllersByCategory.TryGetValue(category, out var list)
            ? list.AsReadOnly()
            : Array.Empty<PageControllerInfo>();
    }

    public object? CreateDefaultConfiguration(string controllerName)
    {
        var info = GetByName(controllerName);
        if (info?.ConfigurationType == null)
            return null;

        try
        {
            return Activator.CreateInstance(info.ConfigurationType);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<string> ValidateConfiguration(string controllerName, object configuration)
    {
        var errors = new List<string>();
        var info = GetByName(controllerName);

        if (info == null)
        {
            errors.Add($"Unknown controller: {controllerName}");
            return errors;
        }

        if (info.ConfigurationType == null)
            return errors;

        object? configObj = configuration;
        if (configuration is string jsonString)
        {
            try
            {
                configObj = JsonSerializer.Deserialize(jsonString, info.ConfigurationType, new JsonSerializerOptions
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
            var prop = info.ConfigurationType.GetProperty(propInfo.Name);
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
            && _controllers.Count > 0)
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IPageControllerRegistrationService>();
            var dtos = service.GetActiveAsync().GetAwaiter().GetResult();
            BuildFromDtos(dtos);
            _lastRefresh = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load page controller registrations from database: {ex.Message}");
        }
    }

    private void BuildFromDtos(List<PageControllerRegistrationDTO> dtos)
    {
        _controllers = new List<PageControllerInfo>(dtos.Count);
        _controllersByName = new Dictionary<string, PageControllerInfo>(StringComparer.OrdinalIgnoreCase);
        _controllersByCategory = new Dictionary<string, List<PageControllerInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var dto in dtos)
        {
            var controllerType = ResolveType(dto.ControllerTypeName) ?? typeof(object);
            Type? configurationType = null;
            if (!string.IsNullOrWhiteSpace(dto.ConfigurationTypeName))
                configurationType = ResolveType(dto.ConfigurationTypeName);

            var info = new PageControllerInfo
            {
                Name = dto.ControllerName,
                DisplayName = dto.DisplayName,
                Description = dto.Description,
                Category = dto.Category,
                IconClass = dto.IconClass,
                Order = dto.Order,
                ControllerType = controllerType,
                ConfigurationType = configurationType,
                Properties = DeserializePropertyDefinitions(dto.PropertyDefinitionsJson)
            };

            _controllers.Add(info);
            _controllersByName[info.Name] = info;

            if (!_controllersByCategory.TryGetValue(info.Category, out var categoryList))
            {
                categoryList = new List<PageControllerInfo>();
                _controllersByCategory[info.Category] = categoryList;
            }
            categoryList.Add(info);
        }

        _controllers.Sort((a, b) =>
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

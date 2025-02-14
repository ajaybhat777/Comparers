using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class ModelChange<T>
{
    public T OldValue { get; set; }
    public T NewValue { get; set; }
}

public class ComparisonConfig
{
    public HashSet<string> IgnoreProperties { get; } = new HashSet<string>();
    public HashSet<string> AlwaysIncludeProperties { get; } = new HashSet<string>();
    public bool IncludeUnchangedAlwaysIncluded { get; set; } = true;
}

public static class ModelComparer
{
    public static ModelChange<T> Compare<T>(T oldObj, T newObj, ComparisonConfig config = null)
    {
        config ??= new ComparisonConfig();
        var hasChanges = HasChanges(oldObj, newObj, typeof(T), config, "");
        
        return hasChanges ? new ModelChange<T> { OldValue = oldObj, NewValue = newObj } : null;
    }

    private static bool HasChanges(object oldObj, object newObj, Type type, ComparisonConfig config, string path)
    {
        // Handle null cases
        if (oldObj == null && newObj == null) return false;
        if (oldObj == null || newObj == null) return true;

        // Check for always included properties at this level
        var currentAlwaysIncluded = config.AlwaysIncludeProperties
            .Where(p => p.StartsWith(path) && p.Count(c => c == '.') == path.Count(c => c == '.'))
            .Any();

        if (currentAlwaysIncluded && config.IncludeUnchangedAlwaysIncluded)
        {
            return true;
        }

        // Check all properties
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propertyPath = string.IsNullOrEmpty(path) 
                ? property.Name 
                : $"{path}.{property.Name}";

            // Check ignore list
            if (config.IgnoreProperties.Contains(propertyPath) ||
                config.IgnoreProperties.Contains(property.Name)) continue;

            var oldValue = property.GetValue(oldObj);
            var newValue = property.GetValue(newObj);

            // Check if property is always included
            if (config.AlwaysIncludeProperties.Contains(propertyPath) ||
                config.AlwaysIncludeProperties.Contains(property.Name))
            {
                return true;
            }

            // Handle nested objects
            if (IsComplexType(property.PropertyType))
            {
                if (HasChanges(oldValue, newValue, property.PropertyType, config, propertyPath))
                {
                    return true;
                }
            }
            // Handle collections
            else if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType) && 
                     property.PropertyType != typeof(string))
            {
                if (CheckCollectionChanges(oldValue, newValue, config, propertyPath))
                {
                    return true;
                }
            }
            // Simple value comparison
            else if (!Equals(oldValue, newValue))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CheckCollectionChanges(object oldColl, object newColl, ComparisonConfig config, string path)
    {
        var oldEnumerable = oldColl as IEnumerable ?? Enumerable.Empty<object>();
        var newEnumerable = newColl as IEnumerable ?? Enumerable.Empty<object>();

        var oldList = oldEnumerable.Cast<object>().ToList();
        var newList = newEnumerable.Cast<object>().ToList();

        // Check collection count difference
        if (oldList.Count != newList.Count) return true;

        // Check items
        for (int i = 0; i < oldList.Count; i++)
        {
            var itemPath = $"{path}[{i}]";
            if (HasChanges(oldList[i], newList[i], oldList[i].GetType(), config, itemPath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsComplexType(Type type)
    {
        return !type.IsPrimitive &&
               type != typeof(string) &&
               !type.IsValueType &&
               !typeof(IEnumerable).IsAssignableFrom(type);
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

public class ModelChange
{
    public ExpandoObject OldValue { get; set; }
    public ExpandoObject NewValue { get; set; }
}

public class ModelChangeSet
{
    public List<ExpandoObject> OldValues { get; set; } = new List<ExpandoObject>();
    public List<ExpandoObject> NewValues { get; set; } = new List<ExpandoObject>();
}

public class ComparisonConfig
{
    public HashSet<string> IgnoreProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AlwaysIncludeProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public Func<object, object> KeySelector { get; set; }
}

public static class ModelComparer
{
    private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
    {
        Converters = { new StringEnumConverter() },
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore
    };

    public static ModelChange CompareObjects<T>(T oldObj, T newObj, ComparisonConfig config = null)
    {
        config ??= new ComparisonConfig();
        var differences = GetDifferences(oldObj, newObj, config);
        return differences.Count > 0 ? CreateChange(oldObj, newObj, differences) : null;
    }

    public static ModelChangeSet CompareLists<T>(IEnumerable<T> oldList, IEnumerable<T> newList, ComparisonConfig config = null)
    {
        config ??= new ComparisonConfig();
        var changeSet = new ModelChangeSet();
        
        var oldDict = config.KeySelector != null 
            ? (oldList ?? Enumerable.Empty<T>()).ToDictionary(config.KeySelector) 
            : new Dictionary<object, T>();
        
        var newDict = config.KeySelector != null 
            ? (newList ?? Enumerable.Empty<T>()).ToDictionary(config.KeySelector) 
            : new Dictionary<object, T>();

        var allKeys = new HashSet<object>(oldDict.Keys.Concat(newDict.Keys));

        foreach (var key in allKeys)
        {
            oldDict.TryGetValue(key, out T oldItem);
            newDict.TryGetValue(key, out T newItem);

            var differences = GetDifferences(oldItem, newItem, config);
            if (differences.Count > 0)
            {
                changeSet.OldValues.Add(CreatePartialObject(oldItem, differences));
                changeSet.NewValues.Add(CreatePartialObject(newItem, differences));
            }
        }

        return changeSet;
    }

    private static List<PropertyDifference> GetDifferences(object oldObj, object newObj, ComparisonConfig config)
    {
        var differences = new List<PropertyDifference>();
        CompareInternal(oldObj, newObj, differences, config, string.Empty);
        return differences;
    }

    private static void CompareInternal(object oldObj, object newObj, List<PropertyDifference> differences, 
        ComparisonConfig config, string currentPath)
    {
        if (oldObj == null && newObj == null) return;
        if (oldObj == null || newObj == null)
        {
            differences.Add(new PropertyDifference(currentPath, oldObj, newObj));
            return;
        }

        var type = oldObj.GetType();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propertyPath = string.IsNullOrEmpty(currentPath) 
                ? property.Name 
                : $"{currentPath}.{property.Name}";

            if (ShouldIgnore(propertyPath, config)) continue;

            var oldValue = property.GetValue(oldObj);
            var newValue = property.GetValue(newObj);

            if (IsAlwaysIncluded(propertyPath, config) && differences.Count > 0)
            {
                differences.Add(new PropertyDifference(propertyPath, oldValue, newValue));
                continue;
            }

            if (IsComplexType(property.PropertyType))
            {
                CompareInternal(oldValue, newValue, differences, config, propertyPath);
            }
            else if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType) && 
                    property.PropertyType != typeof(string))
            {
                CompareCollections(oldValue, newValue, differences, config, propertyPath);
            }
            else if (!AreEqual(oldValue, newValue))
            {
                differences.Add(new PropertyDifference(propertyPath, oldValue, newValue));
            }
        }
    }

    private static bool ShouldIgnore(string path, ComparisonConfig config)
    {
        return config.IgnoreProperties.Contains(path) || 
               config.IgnoreProperties.Any(p => IsWildcardMatch(path, p)));
    }

    private static bool IsAlwaysIncluded(string path, ComparisonConfig config)
    {
        return config.AlwaysIncludeProperties.Contains(path) || 
               config.AlwaysIncludeProperties.Any(p => IsWildcardMatch(path, p)));
    }

    private static bool IsWildcardMatch(string path, string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\[\\]", "\\[\\d+\\]") + "$";
        return Regex.IsMatch(path, regexPattern);
    }

    private static void CompareCollections(object oldColl, object newColl, 
        List<PropertyDifference> differences, ComparisonConfig config, string path)
    {
        var oldList = (oldColl as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
        var newList = (newColl as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();

        for (int i = 0; i < Math.Max(oldList.Count, newList.Count); i++)
        {
            var itemPath = $"{path}[{i}]";
            var oldItem = i < oldList.Count ? oldList[i] : null;
            var newItem = i < newList.Count ? newList[i] : null;

            CompareInternal(oldItem, newItem, differences, config, itemPath);
        }
    }

    private static ModelChange CreateChange(object oldObj, object newObj, List<PropertyDifference> differences)
    {
        return new ModelChange
        {
            OldValue = CreatePartialObject(oldObj, differences),
            NewValue = CreatePartialObject(newObj, differences)
        };
    }

    private static ExpandoObject CreatePartialObject(object obj, List<PropertyDifference> differences)
    {
        if (obj == null) return null;
        
        var result = new ExpandoObject();
        var dict = result as IDictionary<string, object>;

        foreach (var diff in differences)
        {
            var value = GetValueByPath(obj, diff.Path);
            SetValueByPath(dict, diff.Path, value);
        }

        return result;
    }

    // Helper methods for value path resolution (same as previous)
    // ...
}

internal class PropertyDifference
{
    public string Path { get; }
    public object OldValue { get; }
    public object NewValue { get; }

    public PropertyDifference(string path, object oldValue, object newValue)
    {
        Path = path;
        OldValue = oldValue;
        NewValue = newValue;
    }
}

____________

using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

public class ModelChange
{
    public ExpandoObject OldValue { get; set; }
    public ExpandoObject NewValue { get; set; }
}

public class ModelChangeSet
{
    public List<ExpandoObject> OldValues { get; set; } = new List<ExpandoObject>();
    public List<ExpandoObject> NewValues { get; set; } = new List<ExpandoObject>();
}

public class ComparisonConfig
{
    public HashSet<string> IgnoreProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AlwaysIncludeProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public Func<object, object> KeySelector { get; set; }
}

public static class ModelComparer
{
    private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
    {
        Converters = { new StringEnumConverter() },
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore
    };

    public static ModelChange CompareObjects<T>(T oldObj, T newObj, ComparisonConfig config = null)
    {
        config ??= new ComparisonConfig();
        var (hasChanges, differences) = CompareInternal(oldObj, newObj, config, string.Empty);
        return hasChanges ? CreateChange(oldObj, newObj, differences) : null;
    }

    public static ModelChangeSet CompareLists<T>(IEnumerable<T> oldList, IEnumerable<T> newList, ComparisonConfig config = null)
    {
        config ??= new ComparisonConfig();
        var changeSet = new ModelChangeSet();
        
        var oldDict = config.KeySelector != null 
            ? (oldList ?? Enumerable.Empty<T>()).ToDictionary(config.KeySelector) 
            : new Dictionary<object, T>();
        
        var newDict = config.KeySelector != null 
            ? (newList ?? Enumerable.Empty<T>()).ToDictionary(config.KeySelector) 
            : new Dictionary<object, T>();

        var allKeys = new HashSet<object>(oldDict.Keys.Concat(newDict.Keys));

        foreach (var key in allKeys)
        {
            oldDict.TryGetValue(key, out T oldItem);
            newDict.TryGetValue(key, out T newItem);

            var (hasChanges, differences) = CompareInternal(oldItem, newItem, config, string.Empty);
            if (hasChanges)
            {
                changeSet.OldValues.Add(CreatePartialObject(oldItem, differences));
                changeSet.NewValues.Add(CreatePartialObject(newItem, differences));
            }
        }

        return changeSet;
    }

    private static (bool hasChanges, List<PropertyDifference> differences) CompareInternal(
        object oldObj, object newObj, ComparisonConfig config, string currentPath)
    {
        var differences = new List<PropertyDifference>();
        var alwaysIncludeDiffs = new List<PropertyDifference>();
        bool hasRealChanges = false;

        if (oldObj == null && newObj == null) return (false, differences);
        if (oldObj == null || newObj == null)
        {
            differences.Add(new PropertyDifference(currentPath, oldObj, newObj));
            return (true, differences);
        }

        var type = oldObj.GetType();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propertyPath = string.IsNullOrEmpty(currentPath) 
                ? property.Name 
                : $"{currentPath}.{property.Name}";

            if (ShouldIgnore(propertyPath, config)) continue;

            var oldValue = property.GetValue(oldObj);
            var newValue = property.GetValue(newObj);

            // Track always-included properties separately
            if (IsAlwaysIncluded(propertyPath, config))
            {
                if (!AreEqual(oldValue, newValue))
                {
                    alwaysIncludeDiffs.Add(new PropertyDifference(propertyPath, oldValue, newValue));
                }
                continue;
            }

            // Check for real changes
            if (IsComplexType(property.PropertyType))
            {
                var (childHasChanges, childDiffs) = CompareInternal(oldValue, newValue, config, propertyPath);
                if (childHasChanges)
                {
                    hasRealChanges = true;
                    differences.AddRange(childDiffs);
                }
            }
            else if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType) && 
                    property.PropertyType != typeof(string))
            {
                var (collHasChanges, collDiffs) = CompareCollections(oldValue, newValue, config, propertyPath);
                if (collHasChanges)
                {
                    hasRealChanges = true;
                    differences.AddRange(collDiffs);
                }
            }
            else if (!AreEqual(oldValue, newValue))
            {
                hasRealChanges = true;
                differences.Add(new PropertyDifference(propertyPath, oldValue, newValue));
            }
        }

        // Merge always-included diffs ONLY if real changes exist
        if (hasRealChanges)
        {
            differences.AddRange(alwaysIncludeDiffs);
            return (true, differences);
        }

        return (false, new List<PropertyDifference>());
    }

    private static (bool hasChanges, List<PropertyDifference> differences) CompareCollections(
        object oldColl, object newColl, ComparisonConfig config, string path)
    {
        var differences = new List<PropertyDifference>();
        bool hasChanges = false;

        var oldList = (oldColl as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
        var newList = (newColl as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();

        for (int i = 0; i < Math.Max(oldList.Count, newList.Count); i++)
        {
            var itemPath = $"{path}[{i}]";
            var oldItem = i < oldList.Count ? oldList[i] : null;
            var newItem = i < newList.Count ? newList[i] : null;

            var (itemHasChanges, itemDiffs) = CompareInternal(oldItem, newItem, config, itemPath);
            if (itemHasChanges)
            {
                hasChanges = true;
                differences.AddRange(itemDiffs);
            }
        }

        return (hasChanges, differences);
    }

    // Helper methods for path matching, serialization, etc. remain the same
    // ...
}

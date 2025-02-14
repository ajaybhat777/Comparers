using System;
using System.Collections.Generic;
using System.Linq;

public class ModelChangeSet<T>
{
    public List<T> OldValues { get; set; } = new List<T>();
    public List<T> NewValues { get; set; } = new List<T>();
}

public static class ModelComparer
{
    public static ModelChangeSet<T> CompareModels<T>(
        List<T> oldList,
        List<T> newList,
        Func<T, object> keySelector,
        IEnumerable<string> propertiesToIgnore = null,
        IEnumerable<string> propertiesToAlwaysInclude = null)
    {
        propertiesToIgnore ??= Enumerable.Empty<string>();
        propertiesToAlwaysInclude ??= Enumerable.Empty<string>();

        var ignoreSet = new HashSet<string>(propertiesToIgnore);
        var alwaysIncludeSet = new HashSet<string>(propertiesToAlwaysInclude);

        var oldDict = oldList.ToDictionary(keySelector);
        var newDict = newList.ToDictionary(keySelector);
        var changeSet = new ModelChangeSet<T>();

        var allKeys = oldDict.Keys.Union(newDict.Keys).Distinct();

        foreach (var key in allKeys)
        {
            oldDict.TryGetValue(key, out T oldItem);
            newDict.TryGetValue(key, out T newItem);

            if (oldItem == null)
            {
                // Added item
                changeSet.NewValues.Add(newItem);
                continue;
            }

            if (newItem == null)
            {
                // Removed item
                changeSet.OldValues.Add(oldItem);
                continue;
            }

            // Check if any non-ignored properties have changed or if properties to always include exist
            if (HasChanges(oldItem, newItem, ignoreSet, alwaysIncludeSet))
            {
                changeSet.OldValues.Add(oldItem);
                changeSet.NewValues.Add(newItem);
            }
        }

        return changeSet;
    }

    private static bool HasChanges<T>(T oldItem, T newItem, HashSet<string> ignoreSet, HashSet<string> alwaysIncludeSet)
    {
        var properties = typeof(T).GetProperties()
            .Where(p => !ignoreSet.Contains(p.Name) || alwaysIncludeSet.Contains(p.Name));

        foreach (var prop in properties)
        {
            var oldValue = prop.GetValue(oldItem);
            var newValue = prop.GetValue(newItem);

            // If the property is in the alwaysIncludeSet, include it regardless of changes
            if (alwaysIncludeSet.Contains(prop.Name))
            {
                return true;
            }

            // Otherwise, check for changes
            if (!AreEqual(oldValue, newValue))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AreEqual(object a, object b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.Equals(b);
    }
}

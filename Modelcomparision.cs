using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

public class ModelChange<T>
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public T OldValue { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public T NewValue { get; set; }
}

public class ModelChangeSet<T>
{
    public List<T> OldValues { get; set; } = new List<T>();
    public List<T> NewValues { get; set; } = new List<T>();
}

public class ComparisonConfig
{
    public HashSet<string> IgnoreProperties { get; } = new HashSet<string>();
    public HashSet<string> AlwaysIncludeProperties { get; } = new HashSet<string>();
    public bool IncludeUnchangedAlwaysIncluded { get; set; } = true;
}

public static class ModelComparer
{
    private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
    {
        Converters = { new StringEnumConverter() },
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore
    };

    public static ModelChange<T> CompareObjects<T>(T oldObj, T newObj, ComparisonConfig config = null)
    {
        config ??= new ComparisonConfig();
        return HasChanges(oldObj, newObj, typeof(T), config, "")
            ? new ModelChange<T> 
            { 
                OldValue = CloneWithEnumHandling(oldObj), 
                NewValue = CloneWithEnumHandling(newObj) 
            }
            : null;
    }

    public static ModelChangeSet<T> CompareLists<T>(IEnumerable<T> oldList, IEnumerable<T> newList, 
        ComparisonConfig config = null)
    {
        config ??= new ComparisonConfig();
        var changes = new ModelChangeSet<T>();
        
        var oldArr = oldList?.ToArray() ?? Array.Empty<T>();
        var newArr = newList?.ToArray() ?? Array.Empty<T>();
        var maxLength = Math.Max(oldArr.Length, newArr.Length);

        for (var i = 0; i < maxLength; i++)
        {
            var oldItem = i < oldArr.Length ? oldArr[i] : default;
            var newItem = i < newArr.Length ? newArr[i] : default;

            if (HasChanges(oldItem, newItem, typeof(T), config, ""))
            {
                changes.OldValues.Add(CloneWithEnumHandling(oldItem));
                changes.NewValues.Add(CloneWithEnumHandling(newItem));
            }
        }

        return changes;
    }

    private static bool HasChanges(object oldObj, object newObj, Type type, ComparisonConfig config, string path)
    {
        if (oldObj == null && newObj == null) return false;
        if (oldObj == null || newObj == null) return true;

        if (config.AlwaysIncludeProperties.Contains(path) && config.IncludeUnchangedAlwaysIncluded)
            return true;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propertyPath = string.IsNullOrEmpty(path) 
                ? property.Name 
                : $"{path}.{property.Name}";

            if (config.IgnoreProperties.Contains(propertyPath)) continue;

            var oldValue = property.GetValue(oldObj);
            var newValue = property.GetValue(newObj);

            if (config.AlwaysIncludeProperties.Contains(propertyPath))
                return true;

            if (IsComplexType(property.PropertyType))
            {
                if (HasChanges(oldValue, newValue, property.PropertyType, config, propertyPath))
                    return true;
            }
            else if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType) && 
                    property.PropertyType != typeof(string))
            {
                if (CheckCollectionChanges(oldValue, newValue, config, propertyPath))
                    return true;
            }
            else if (!Equals(oldValue, newValue))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CheckCollectionChanges(object oldColl, object newColl, ComparisonConfig config, string path)
    {
        var oldList = (oldColl as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
        var newList = (newColl as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();

        if (oldList.Count != newList.Count) return true;

        for (int i = 0; i < oldList.Count; i++)
        {
            var itemPath = $"{path}[{i}]";
            if (HasChanges(oldList[i], newList[i], oldList[i].GetType(), config, itemPath))
                return true;
        }

        return false;
    }

    private static T CloneWithEnumHandling<T>(T obj)
    {
        if (obj == null) return default;
        return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(obj, SerializerSettings));
    }

    private static bool IsComplexType(Type type)
    {
        return !type.IsPrimitive &&
               type != typeof(string) &&
               !type.IsValueType &&
               !typeof(IEnumerable).IsAssignableFrom(type);
    }
}

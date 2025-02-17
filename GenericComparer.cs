using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
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
            ? oldList?.ToDictionary(config.KeySelector) 
            : new Dictionary<object, T>();
        
        var newDict = config.KeySelector != null 
            ? newList?.ToDictionary(config.KeySelector) 
            : new Dictionary<object, T>();

        var allKeys = new HashSet<object>(
            (oldDict?.Keys ?? Enumerable.Empty<object>())
            .Concat(newDict?.Keys ?? Enumerable.Empty<object>()));

        foreach (var key in allKeys)
        {
            oldDict?.TryGetValue(key, out T oldItem);
            newDict?.TryGetValue(key, out T newItem);

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
            var propertyPath = GetPropertyPath(currentPath, property.Name);

            if (ShouldIgnore(propertyPath, config)) continue;

            var oldValue = property.GetValue(oldObj);
            var newValue = property.GetValue(newObj);

            if (ShouldAlwaysInclude(propertyPath, config))
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

    private static object GetValueByPath(object obj, string path)
    {
        var current = obj;
        foreach (var part in path.Split('.'))
        {
            if (current == null) return null;
            var prop = current.GetType().GetProperty(part);
            current = prop?.GetValue(current);
        }
        return current;
    }

    private static void SetValueByPath(IDictionary<string, object> dict, string path, object value)
    {
        var parts = path.Split('.');
        var current = dict;

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (i == parts.Length - 1)
            {
                current[part] = value;
            }
            else
            {
                if (!current.TryGetValue(part, out object next))
                {
                    next = new ExpandoObject();
                    current[part] = next;
                }
                current = next as IDictionary<string, object>;
            }
        }
    }

    private static bool ShouldIgnore(string path, ComparisonConfig config)
    {
        var patternPath = Regex.Replace(path, @"\[\d+\]", "[]");
        return config.IgnoreProperties.Contains(patternPath) || 
               config.IgnoreProperties.Contains(path);
    }

    private static bool ShouldAlwaysInclude(string path, ComparisonConfig config)
    {
        var patternPath = Regex.Replace(path, @"\[\d+\]", "[]");
        return config.AlwaysIncludeProperties.Contains(patternPath) || 
               config.AlwaysIncludeProperties.Contains(path);
    }

    private static bool AreEqual(object a, object b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Equals(b)) return true;
        return JsonConvert.SerializeObject(a, SerializerSettings) == 
               JsonConvert.SerializeObject(b, SerializerSettings);
    }

    private static string GetPropertyPath(string currentPath, string propertyName)
    {
        return string.IsNullOrEmpty(currentPath) ? propertyName : $"{currentPath}.{propertyName}";
    }

    private static bool IsComplexType(Type type)
    {
        return !type.IsPrimitive &&
               type != typeof(string) &&
               !type.IsValueType &&
               !typeof(IEnumerable).IsAssignableFrom(type);
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

    private class PropertyDifference
    {
        public string Path { get; }
        public object OldValue { get; }
        public object NewValue { get; }

        public PropertyDifference(string path, object oldVal, object newVal)
        {
            Path = path;
            OldValue = oldVal;
            NewValue = newVal;
        }
    }
}

__________________
public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public UserRole Role { get; set; }
    public Address Address { get; set; }
}

var config = new ComparisonConfig
{
    IgnoreProperties = { "Id" },
    AlwaysIncludeProperties = { "Role" }
};

var user1 = new User { Id = 1, Name = "Alice", Role = UserRole.Admin, 
                      Address = new Address { Street = "Main St" } };
var user2 = new User { Id = 1, Name = "Alicia", Role = UserRole.Admin, 
                      Address = new Address { Street = "Oak St" } };

var change = ModelComparer.CompareObjects(user1, user2, config);

// Serialize result
var json = JsonConvert.SerializeObject(change, Formatting.Indented, 
    new JsonConverter[] { new StringEnumConverter() });
_______________
var config = new ComparisonConfig
{
    KeySelector = u => ((User)u).Id,
    IgnoreProperties = { "Address.City" },
    AlwaysIncludeProperties = { "Role" }
};

var oldUsers = new List<User> { /* ... */ };
var newUsers = new List<User> { /* ... */ };

var changes = ModelComparer.CompareLists(oldUsers, newUsers, config);

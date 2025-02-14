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
        return HasChanges(oldObj, newObj, typeof(T), config, "")
            ? new ModelChange<T> { OldValue = CloneWithEnumStrings(oldObj), NewValue = CloneWithEnumStrings(newObj) }
            : null;
    }

    private static bool HasChanges(object oldObj, object newObj, Type type, ComparisonConfig config, string path)
    {
        if (oldObj == null && newObj == null) return false;
        if (oldObj == null || newObj == null) return true;

        // Check always included properties at current level
        var currentAlwaysIncluded = config.AlwaysIncludeProperties
            .Any(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));

        if (currentAlwaysIncluded && config.IncludeUnchangedAlwaysIncluded)
            return true;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propertyPath = string.IsNullOrEmpty(path) 
                ? property.Name 
                : $"{path}.{property.Name}";

            if (ShouldIgnoreProperty(config, property, propertyPath)) 
                continue;

            var oldValue = property.GetValue(oldObj);
            var newValue = property.GetValue(newObj);

            if (IsAlwaysIncluded(config, property, propertyPath))
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
            else if (!AreEqual(oldValue, newValue))
            {
                return true;
            }
        }

        return false;
    }

    private static T CloneWithEnumStrings<T>(T obj)
    {
        if (obj == null) return default;
        
        var settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };
        
        return JsonConvert.DeserializeObject<T>(
            JsonConvert.SerializeObject(obj, settings), 
            settings
        );
    }

    // (Other helper methods from previous implementation remain the same)
}

public enum UserStatus
{
    Active,
    Inactive,
    Suspended
}

public class Address
{
    public string Street { get; set; }
    public string City { get; set; }
    public string Country { get; set; }
}

public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public UserStatus Status { get; set; }
    public Address Address { get; set; }
    public List<string> PhoneNumbers { get; set; }
}

// Usage example
var config = new ComparisonConfig
{
    IgnoreProperties = { "Id" },
    AlwaysIncludeProperties = { "Status" }
};

var oldUser = new User
{
    Id = Guid.NewGuid(),
    Name = "Alice",
    Status = UserStatus.Active,
    Address = new Address
    {
        Street = "123 Main St",
        City = "New York",
        Country = "USA"
    },
    PhoneNumbers = new List<string> { "555-1234" }
};

var newUser = new User
{
    Id = Guid.NewGuid(),
    Name = "Alice Smith",
    Status = UserStatus.Active,  // Same status but always included
    Address = new Address
    {
        Street = "456 Oak Ave",
        City = "Los Angeles",
        Country = "USA"
    },
    PhoneNumbers = new List<string> { "555-5678", "555-9012" }
};

var change = ModelComparer.Compare(oldUser, newUser, config);

// Serialize with enum handling
var settings = new JsonSerializerSettings
{
    Formatting = Formatting.Indented,
    Converters = { new StringEnumConverter() },
    NullValueHandling = NullValueHandling.Ignore
};

var json = JsonConvert.SerializeObject(change, settings);
Console.WriteLine(json);

---------

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

using System.Reflection;

namespace LazyFateAutomation.Helpers.Extensions;

public static class ObjectExtensions {
    public static object? GetFieldOrProperty(this object obj, string name) {
        var type = obj.GetType();
        while (type != null) {
            if (type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) is { } fieldInfo) {
                return fieldInfo.GetValue(obj);
            }
            if (type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) is { } propertyInfo) {
                return propertyInfo.GetValue(obj);
            }
            type = type.BaseType;
        }
        return null;
    }

    public static T? GetFieldOrProperty<T>(this object obj, string name) => (T?)GetFieldOrProperty(obj, name);

    public static object? CallMethod(this object obj, string name, object[] @params, bool matchExactArgumentTypes = false) {
        var info = !matchExactArgumentTypes
            ? obj.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            : obj.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, [.. @params.Select(x => x.GetType())]);
        return info?.Invoke(obj, @params);
    }

    public static T? CallMethod<T>(this object obj, string name, object[] @params, bool matchExactArgumentTypes = false) => (T?)CallMethod(obj, name, @params, matchExactArgumentTypes);
}

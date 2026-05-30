namespace LazyFateAutomation.Helpers.Extensions;

public static class EnumExtensions {
    extension<T>(T @enum) where T : struct, Enum {
        public dynamic Value => Convert.ChangeType(@enum, Enum.GetUnderlyingType(typeof(T)));
        public static T[] Values => Enum.GetValues<T>();
        public T[] Flags => [.. Enum.GetValues<T>().Where(f => @enum.HasFlag(f))];
    }

    /// <summary>
    /// Checks if the flag contains any of the passed flags
    /// </summary>
    public static bool HasAny<T>(this T @enum, params T[] flags) where T : struct, Enum {
        if (flags == null || flags.Length == 0) {
            return false;
        }

        var enumValue = Convert.ToUInt64(@enum);
        foreach (var flag in flags) {
            var flagValue = Convert.ToUInt64(flag);
            if ((enumValue & flagValue) != 0) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if the flag contains all passed flags
    /// </summary>
    public static bool Has<T>(this T @enum, params T[] flags) where T : struct, Enum {
        if (flags == null || flags.Length == 0) {
            return false;
        }

        var enumValue = Convert.ToUInt64(@enum);
        foreach (var flag in flags) {
            var flagValue = Convert.ToUInt64(flag);
            if ((enumValue & flagValue) == flagValue) {
                return true;
            }
        }
        return false;
    }
}

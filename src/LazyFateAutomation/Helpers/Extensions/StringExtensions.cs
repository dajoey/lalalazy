using System;

namespace clib.Extensions;

public static class StringExtensions {
    public static string FromBase64(this string base64) =>
        System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
}

using System;
using System.Windows;

namespace VideoEditor.Services;

internal static class Theming
{
    public static string Resolve()
    {
        var t = AppSettings.Theme;
        if (t == "light" || t == "dark") return t;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            if (v is int n && n == 1) return "light";
        }
        catch { }
        return "dark";
    }

    public static bool IsLight() => Resolve() == "light";
}

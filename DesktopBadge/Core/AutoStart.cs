using Microsoft.Win32;

namespace DesktopBadge.Core;

/// <summary>Toggles the HKCU Run entry used to launch the badge at sign-in.</summary>
public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "DesktopBadge";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string s && s.Length > 0;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key is null) return;

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        key.SetValue(ValueName, $"\"{exe}\"");
    }

    /// <summary>Keeps the Run entry pointing at the current exe after it is moved.</summary>
    public static void RefreshPath()
    {
        if (!IsEnabled()) return;
        Set(true);
    }
}

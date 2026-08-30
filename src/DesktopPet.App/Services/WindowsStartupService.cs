using System.IO;
using Microsoft.Win32;

namespace DesktopPet.App.Services;

/// <summary>Manages a current-user Run entry; no elevation is required.</summary>
public static class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopPet";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value &&
                   !string.IsNullOrWhiteSpace(value);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Removes a stale entry when this portable executable has moved or been deleted.</summary>
    public static bool RemoveStaleEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not string value || string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            var storedExecutable = ExtractQuotedExecutable(value);
            if (storedExecutable is not null && File.Exists(storedExecutable))
            {
                return true;
            }

            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception exception) when (exception is
            UnauthorizedAccessException or
            System.Security.SecurityException or
            IOException)
        {
            return false;
        }
    }

    private static string? ExtractQuotedExecutable(string command)
    {
        var trimmed = command.Trim();
        if (!trimmed.StartsWith('"'))
        {
            var separator = trimmed.IndexOf(' ');
            return separator < 0 ? trimmed : trimmed[..separator];
        }

        var closingQuote = trimmed.IndexOf('"', 1);
        return closingQuote <= 1 ? null : trimmed[1..closingQuote];
    }

    public static bool TrySetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                {
                    return false;
                }

                key.SetValue(ValueName, $"\"{executable}\" --autostart", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}

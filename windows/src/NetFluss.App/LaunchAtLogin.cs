// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.IO;
using Microsoft.Win32;

namespace NetFluss.App;

/// <summary>
/// Start-with-Windows, via the per-user Run key.
///
/// <para>The macOS app uses <c>SMAppService.mainApp.register()</c>, which needs no
/// privileges and is revocable from System Settings. The unpackaged Windows equivalent is
/// <c>HKCU\...\CurrentVersion\Run</c>: also per-user, also no elevation, and it shows up
/// under Task Manager's Startup tab where a user expects to find and disable it. The port
/// plan reserves an MSIX/Startup-task approach for a packaged build, which this is not.</para>
///
/// <para>Windows owns this state, not the settings file. If a user removes the entry from
/// Task Manager, the truth is the registry and the toggle has to reflect that — so this
/// always reads back rather than trusting what was last written.</para>
/// </summary>
internal static class LaunchAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NetFluss";

    internal static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string command && command.Contains("NetFluss", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null)
            {
                return;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return;
            }

            if (ExecutablePath() is { } path)
            {
                // Quoted: the default install path contains a space, and an unquoted Run
                // entry would have Windows try to launch "C:\Program".
                key.SetValue(ValueName, $"\"{path}\"", RegistryValueKind.String);
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // A locked-down profile can forbid this. The preference simply will not stick,
            // and the toggle re-reads to show that rather than claiming success.
        }
    }

    private static string? ExecutablePath()
    {
        var path = Environment.ProcessPath;

        // Under `dotnet run` this is the SDK host, not NetFluss — registering that would
        // start the wrong program at login.
        return path is not null && Path.GetFileNameWithoutExtension(path)
            .Equals("NetFluss", StringComparison.OrdinalIgnoreCase)
            ? path
            : null;
    }
}

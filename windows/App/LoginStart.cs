using System;
using Microsoft.Win32;

namespace Hookjump.App;

/// <summary>The values of one registry key, so what is written there can be tested without the registry.</summary>
public interface IRegistryValues
{
    object? Get(string name);

    void Set(string name, string value);

    void Delete(string name);
}

/// <summary>A key of the listener's own (HKEY_CURRENT_USER): nothing for other users, no administrator asked.</summary>
public sealed class UserRegistryValues(string path) : IRegistryValues
{
    public object? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(name);
    }

    public void Set(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>
/// "Open at Login", as on the Mac: hookjump in the list of apps Windows
/// starts when the listener signs in, which Settings › Apps › Startup shows
/// and can turn off too.
/// </summary>
/// <param name="run">The listener's Run key, which Windows reads at sign-in.</param>
/// <param name="approved">Where Settings › Apps › Startup keeps what was turned off there.</param>
/// <param name="exe">This hookjump.exe.</param>
public sealed class LoginStart(IRegistryValues run, IRegistryValues approved, string exe)
{
    public const string Name = "hookjump";
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>What Windows runs: this exe, quoted, as its path may hold spaces.</summary>
    public string Command => $"\"{exe}\"";

    /// <summary>
    /// On only when Windows would start this very exe: not one moved away
    /// from, and not when turned off in Settings.
    /// </summary>
    public bool On =>
        run.Get(Name) is string command && string.Equals(command, Command, StringComparison.OrdinalIgnoreCase)
        && !TurnedOffInSettings();

    public void Set(bool on)
    {
        // Settings' own mark goes either way: on here means on there too.
        approved.Delete(Name);
        if (on) run.Set(Name, Command);
        else run.Delete(Name);
    }

    // Settings writes 12 bytes; an odd first byte means turned off.
    private bool TurnedOffInSettings() => approved.Get(Name) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
}

using System;
using System.Collections.Generic;
using Microsoft.Win32;
using Xunit;

namespace Hookjump.App.Tests;

/// <summary>"Open at Login": what is written where Windows looks at sign-in, and when it counts as on.</summary>
public class LoginStartTest
{
    private const string Exe = @"C:\Users\someone\Apps\hookjump\hookjump.exe";
    private readonly FakeValues _run = new(), _approved = new();

    private LoginStart Login(string exe = Exe) => new(_run, _approved, exe);

    [Fact]
    public void ItIsOffUntilTurnedOn()
    {
        Assert.False(Login().On);
        Login().Set(true);
        Assert.True(Login().On);
        Assert.Equal($"\"{Exe}\"", _run.Values["hookjump"]); // quoted: the path may hold spaces
    }

    [Fact]
    public void TurnedOffItLeavesNothingBehind()
    {
        Login().Set(true);
        _approved.Values["hookjump"] = new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Login().Set(false);
        Assert.False(Login().On);
        Assert.Empty(_run.Values);
        Assert.Empty(_approved.Values);
    }

    [Fact]
    public void TurnedOffInSettingsItIsOffHereToo()
    {
        Login().Set(true);
        _approved.Values["hookjump"] = new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; // Settings › Apps › Startup: off
        Assert.False(Login().On);
        _approved.Values["hookjump"] = new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; // and on again
        Assert.True(Login().On);
    }

    [Fact]
    public void TurningItOnHereTurnsItOnInSettingsToo()
    {
        _approved.Values["hookjump"] = new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Login().Set(true);
        Assert.True(Login().On);
        Assert.Empty(_approved.Values);
    }

    [Fact]
    public void AnEntryForAnExeMovedAwayFromIsNotThisOne()
    {
        Login(@"C:\Old\hookjump.exe").Set(true);
        Assert.False(Login().On); // Windows would start a file that is not there: turn it on again here
        Assert.True(Login(@"c:\old\HOOKJUMP.EXE").On); // Windows paths do not mind case
    }

    [Fact]
    public void TheListenersOwnRegistryKeyIsReadAndWritten()
    {
        // A key of its own under the listener's, never the real Run key, and gone after.
        var path = $@"Software\hookjump-test-{Guid.NewGuid():N}";
        try
        {
            var values = new UserRegistryValues(path);
            Assert.Null(values.Get("hookjump"));
            values.Delete("hookjump"); // no key yet: nothing happens
            values.Set("other", "kept");
            values.Delete("hookjump"); // a key without the value: nothing happens either
            Assert.Equal("kept", values.Get("other"));
            values.Set("hookjump", "\"C:\\hookjump.exe\"");
            Assert.Equal("\"C:\\hookjump.exe\"", values.Get("hookjump"));
            values.Delete("hookjump");
            Assert.Null(values.Get("hookjump"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    private sealed class FakeValues : IRegistryValues
    {
        public readonly Dictionary<string, object> Values = [];

        public object? Get(string name) => Values.GetValueOrDefault(name);

        public void Set(string name, string value) => Values[name] = value;

        public void Delete(string name) => Values.Remove(name);
    }
}

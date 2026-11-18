using System;
using System.IO;
using System.Threading;
using Hookjump.Storage;
using static Hookjump.App.Native;

namespace Hookjump.App;

public static unsafe class Program
{
    [STAThread]
    public static int Main()
    {
        // Only what was tried is promised: Windows 11. Say so instead of failing later.
        if (Environment.OSVersion.Version.Build < 22000)
        {
            MessageBox(0, "hookjump needs Windows 11.", "hookjump", MbIconInformation);
            return 1;
        }
        using var single = new Mutex(true, @"Local\hookjump", out var first);
        if (!first)
        {
            MessageBox(0, "hookjump is already running: its hook is in the notification area.", "hookjump", MbIconInformation);
            return 1;
        }

        var input = new GdiplusStartupInput { Version = 1 };
        GdiplusStartup(out _, &input, 0);

        using var store = new Store(DataPath());
        using var turntable = SmtcTurntable.OpenAsync().GetAwaiter().GetResult();
        var booth = new Booth(store, turntable, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        using var app = new TrayApp(booth, store, turntable);
        return app.Run();
    }

    /// <summary>
    /// %LOCALAPPDATA%\hookjump\hookjump.db (spec/STORAGE.md), or the file
    /// HOOKJUMP_DB names, for trying things out without touching what is kept.
    /// </summary>
    public static string DataPath() =>
        Environment.GetEnvironmentVariable("HOOKJUMP_DB") is { Length: > 0 } path
            ? path
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hookjump", "hookjump.db");
}

using System;
using System.Runtime.InteropServices;

namespace Hookjump.App;

/// <summary>
/// The Windows functions hookjump calls directly: there is no UI framework in
/// between. Structs are fixed-size and calls source-generated, so the app can
/// be trimmed.
/// </summary>
internal static unsafe partial class Native
{
    public const uint WmDestroy = 0x0002, WmSettingChange = 0x001A, WmContextMenu = 0x007B, WmTimer = 0x0113,
        WmLButtonUp = 0x0202, WmApp = 0x8000;
    public const uint NinSelect = 0x0400, NinKeySelect = 0x0401;
    public const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4, NotifyIconVersion4 = 4;
    public const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4, NifInfo = 0x10, NifShowTip = 0x80;
    public const uint NiifInfo = 0x1, NiifNoSound = 0x10, NiifRespectQuietTime = 0x80;
    public const uint MfString = 0, MfGrayed = 0x1, MfChecked = 0x8, MfPopup = 0x10, MfSeparator = 0x800;
    public const uint TpmRightButton = 0x2, TpmNoNotify = 0x80, TpmReturnCmd = 0x100;
    public const uint MbOkCancel = 0x1, MbIconWarning = 0x30, MbIconInformation = 0x40, MbDefButton2 = 0x100;
    public const int IdOk = 1, SmCxSmIcon = 49;

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowClass
    {
        public uint Size, Style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> Proc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public char* MenuName, ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Message
    {
        public nint Window;
        public uint Id;
        public nint W, L;
        public uint Time;
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, CallbackMessage;
        public nint Icon;
        public fixed char Tip[128];
        public uint State, StateMask;
        public fixed char Info[256];
        public uint Version;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    /// <summary>Copies <paramref name="text"/> into a fixed buffer, cut to fit and ended by a zero.</summary>
    public static void Copy(string text, char* buffer, int length)
    {
        var n = Math.Min(text.Length, length - 1);
        for (var i = 0; i < n; i++) buffer[i] = text[i];
        buffer[n] = '\0';
    }

    // user32 and kernel32 ---------------------------------------------------

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static partial nint GetModuleHandle(nint name);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")]
    public static partial ushort RegisterClassEx(WindowClass* windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(
        uint exStyle, string className, string name, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint window, uint message, nint w, nint l);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    public static partial int GetMessage(Message* message, nint window, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(Message* message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(Message* message);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint window, uint message, nint w, nint l);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int code);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string name);

    [LibraryImport("user32.dll")]
    public static partial nint SetTimer(nint window, nint id, uint ms, nint callback);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint window, nint id);

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenu(nint menu, uint flags, nint id, string? text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(Point* point);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBox(nint window, string text, string caption, uint type);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShellNotifyIcon(uint message, NotifyIconData* data);

    // GDI+, for drawing ------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct GdiplusStartupInput
    {
        public uint Version;
        public nint Callback;
        public int SuppressBackgroundThread, SuppressExternalCodecs;
    }

    public const int SmoothingAntiAlias = 4, LineCapRound = 2, LineJoinRound = 2, PixelFormat32bppArgb = 0x26200A;

    [LibraryImport("gdiplus.dll")]
    public static partial int GdiplusStartup(out nint token, GdiplusStartupInput* input, nint output);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, nint scan0, out nint bitmap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGetImageGraphicsContext(nint image, out nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDisposeImage(nint image);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateHICONFromBitmap(nint bitmap, out nint icon);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteGraphics(nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetSmoothingMode(nint graphics, int mode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipScaleWorldTransform(nint graphics, float sx, float sy, int order);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePen1(uint argb, float width, int unit, out nint pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenStartCap(nint pen, int cap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenEndCap(nint pen, int cap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenLineJoin(nint pen, int join);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePen(nint pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateSolidFill(uint argb, out nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteBrush(nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawEllipse(nint graphics, nint pen, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillEllipse(nint graphics, nint brush, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePath(int fillMode, out nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePath(nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathLine(nint path, float x1, float y1, float x2, float y2);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathArc(nint path, float x, float y, float width, float height, float start, float sweep);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawPath(nint graphics, nint pen, nint path);
}

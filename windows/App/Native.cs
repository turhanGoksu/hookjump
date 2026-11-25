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
        WmLButtonUp = 0x0202, WmHotKey = 0x0312, WmApp = 0x8000;
    public const uint ModNoRepeat = 0x4000;
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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint window, int id);

    [LibraryImport("user32.dll")]
    public static partial int GetKeyboardLayoutList(int count, nint* layouts);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    public static partial uint MapVirtualKeyEx(uint code, uint mapType, nint layout);

    [LibraryImport("user32.dll")]
    public static partial int ToUnicodeEx(uint virtualKey, uint scanCode, byte* keyState, char* buffer, int size, uint flags, nint layout);

    // The strip: a layered window, drawn with per-pixel alpha ---------------

    public const uint WsPopup = 0x80000000;
    public const uint WsExTransparent = 0x20, WsExTopmost = 0x8, WsExToolWindow = 0x80, WsExLayered = 0x80000,
        WsExNoActivate = 0x08000000;
    public const int SwHide = 0, SwShowNoActivate = 4;
    public const uint UlwAlpha = 2, SpiGetWorkArea = 0x30;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Size
    {
        public int Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BlendFunction
    {
        public byte Op, Flags, SourceConstantAlpha, AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateLayeredWindow(
        nint window, nint destination, Point* at, Size* size, nint source, Point* from, uint key, BlendFunction* blend,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint action, uint param, Rect* value, uint winIni);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateDIBSection(nint dc, BitmapInfoHeader* info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint dc, nint thing);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint thing);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint dc);

    /// <summary>Whether the listener is busy: a full-screen game, film or presentation.</summary>
    [LibraryImport("shell32.dll")]
    public static partial int SHQueryUserNotificationState(out int state);

    public const int QunsBusy = 2, QunsRunningD3DFullScreen = 3, QunsPresentationMode = 4;

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShellNotifyIcon(uint message, NotifyIconData* data);

    // A dialog of Windows' own, built from a template in memory -----------------

    public const uint WsCaption = 0x00C00000, WsSysMenu = 0x00080000, WsChild = 0x40000000, WsVisible = 0x10000000,
        WsTabStop = 0x00010000, WsBorder = 0x00800000;
    public const uint DsSetFont = 0x40, DsModalFrame = 0x80, DsCenter = 0x0800;
    public const uint EsAutoHScroll = 0x80, BsPushButton = 0, BsDefPushButton = 1, SsLeft = 0;
    public const ushort ButtonAtom = 0x80, EditAtom = 0x81, StaticAtom = 0x82;
    public const uint WmInitDialog = 0x0110, WmCommand = 0x0111, WmNextDlgCtl = 0x0028;
    public const uint EmSetSel = 0x00B1, EmLimitText = 0x00C5, EmSetCueBanner = 0x1501;
    public const int IdCancel = 2;

    [LibraryImport("user32.dll", EntryPoint = "DialogBoxIndirectParamW")]
    public static partial nint DialogBoxIndirectParam(
        nint instance, void* template, nint owner, delegate* unmanaged<nint, uint, nint, nint, nint> proc, nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "CreateDialogIndirectParamW")]
    public static partial nint CreateDialogIndirectParam(
        nint instance, void* template, nint owner, delegate* unmanaged<nint, uint, nint, nint, nint> proc, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EndDialog(nint dialog, nint result);

    [LibraryImport("user32.dll")]
    public static partial nint GetDlgItem(nint dialog, int id);

    [LibraryImport("user32.dll", EntryPoint = "SetDlgItemTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetDlgItemText(nint dialog, int id, string text);

    [LibraryImport("user32.dll", EntryPoint = "GetDlgItemTextW")]
    public static partial uint GetDlgItemText(nint dialog, int id, char* text, int size);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    public static partial int GetWindowText(nint window, char* text, int size);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint window, uint message, nint w, nint l);

    // GDI+, for drawing ------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct GdiplusStartupInput
    {
        public uint Version;
        public nint Callback;
        public int SuppressBackgroundThread, SuppressExternalCodecs;
    }

    public const int SmoothingAntiAlias = 4, LineCapRound = 2, LineJoinRound = 2, PixelFormat32bppArgb = 0x26200A,
        PixelFormat32bppPArgb = 0xE200B, TextAntiAlias = 4, UnitPixel = 2, StringNoWrap = 0x1000, TrimEllipsis = 3;

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGraphicsClear(nint graphics, uint argb);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetTextRenderingHint(nint graphics, int hint);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipResetWorldTransform(nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillPath(nint graphics, nint brush, nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipClosePathFigure(nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawLine(nint graphics, nint pen, float x1, float y1, float x2, float y2);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillRectangle(nint graphics, nint brush, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenDashArray(nint pen, float* dashes, int count);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipNewPrivateFontCollection(out nint collection);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipPrivateAddMemoryFont(nint collection, void* memory, int length);

    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipCreateFontFamilyFromName(string name, nint collection, out nint family);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateFont(nint family, float size, int style, int unit, out nint font);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteFont(nint font);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateStringFormat(int flags, ushort language, out nint format);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetStringFormatTrimming(nint format, int trimming);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteStringFormat(nint format);

    [StructLayout(LayoutKind.Sequential)]
    public struct RectF(float x, float y, float width, float height)
    {
        public float X = x, Y = y, Width = width, Height = height;
    }

    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipDrawString(nint graphics, string text, int length, nint font, RectF* layout, nint format, nint brush);

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

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathBezier(
        nint path, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathBeziers(nint path, PointF* points, int count);

    [StructLayout(LayoutKind.Sequential)]
    public struct PointF(float x, float y)
    {
        public float X = x, Y = y;
    }

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteFontFamily(nint family);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGetFamily(nint font, out nint family);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGetFamilyName(nint family, char* name, ushort language);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGetFontStyle(nint font, out int style);

    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipMeasureString(
        nint graphics, string text, int length, nint font, RectF* layout, nint format, RectF* bounds, int* codepoints, int* lines);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetStringFormatAlign(nint format, int align);

    public const int StringAlignNear = 0, StringAlignCenter = 1, StringAlignFar = 2;

    // The sea panel: a window that takes clicks and keys ----------------------

    public const uint WmActivate = 0x0006, WmPaint = 0x000F, WmEraseBkgnd = 0x0014, WmSetCursor = 0x0020,
        WmKeyDown = 0x0100, WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmMouseLeave = 0x02A3;
    public const uint CsDropShadow = 0x00020000, SwpShowWindow = 0x40, SwpNoActivate = 0x10, TmeLeave = 2;
    public const int VkTab = 0x09, VkReturn = 0x0D, VkShift = 0x10, VkEscape = 0x1B, VkSpace = 0x20;
    public const int IdcArrow = 32512, IdcHand = 32649, HtClient = 1;
    public const uint SrcCopy = 0x00CC0020;
    public const uint DwmWindowCornerPreference = 33, DwmBorderColor = 34, DwmRound = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct PaintStruct
    {
        public nint Dc;
        public int Erase;
        public Rect Paint;
        public int Restore, IncUpdate;
        public fixed byte Reserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TrackMouse
    {
        public uint Size, Flags;
        public nint Window;
        public uint HoverTime;
    }

    [LibraryImport("user32.dll")]
    public static partial nint BeginPaint(nint window, PaintStruct* paint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EndPaint(nint window, PaintStruct* paint);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(nint to, int x, int y, int width, int height, nint from, int fromX, int fromY, uint op);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InvalidateRect(nint window, Rect* area, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TrackMouseEvent(TrackMouse* track);

    [LibraryImport("user32.dll")]
    public static partial nint SetCursor(nint cursor);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    public static partial nint LoadCursor(nint instance, nint name);

    [LibraryImport("user32.dll")]
    public static partial short GetKeyState(int key);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint window);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint window, uint attribute, void* value, uint size);

    // Motion: Windows' "Animation effects" setting, and turning what is drawn --

    public const uint SpiGetClientAreaAnimation = 0x1042;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint action, uint param, int* value, uint winIni);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSaveGraphics(nint graphics, out uint state);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipRestoreGraphics(nint graphics, uint state);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipTranslateWorldTransform(nint graphics, float dx, float dy, int order);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipRotateWorldTransform(nint graphics, float angle, int order);
}

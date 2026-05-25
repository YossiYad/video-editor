using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VideoEditor.Services;

/// <summary>
/// Enumerates the monitors attached to the system via Win32 EnumDisplayMonitors.
/// Used by the Screen Recorder so the user can pick which monitor to capture
/// (or the whole virtual desktop).
/// </summary>
public static class MonitorInfo
{
    public sealed class Display
    {
        public required int Index { get; init; }
        public required string DeviceName { get; init; }  // e.g. "\\.\DISPLAY1"
        public required int X { get; init; }              // virtual-screen coords (can be negative)
        public required int Y { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required bool IsPrimary { get; init; }
        /// <summary>Real physical pixel width from GetDeviceCaps (DESKTOPHORZRES).
        /// On HiDPI displays this is greater than <see cref="Width"/>; ffmpeg's
        /// gdigrab needs the physical dimensions to actually capture the whole
        /// monitor.</summary>
        public required int PhysicalWidth { get; init; }
        public required int PhysicalHeight { get; init; }

        public bool HasDpiScaling =>
            PhysicalWidth > Width || PhysicalHeight > Height;

        public string FriendlyName =>
            HasDpiScaling
                ? $"Monitor {Index + 1} — {PhysicalWidth}×{PhysicalHeight}" +
                  $" (scaled to {Width}×{Height})" +
                  (IsPrimary ? " · primary" : "")
                : $"Monitor {Index + 1} — {Width}×{Height}" +
                  (IsPrimary ? " (primary)" : "");
    }

    public static List<Display> EnumerateAll()
    {
        var list = new List<Display>();
        int i = 0;
        bool ok = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMon, IntPtr hdc, ref RECT _, IntPtr __) =>
            {
                var info = new MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(hMon, ref info))
                {
                    int physW = info.rcMonitor.right - info.rcMonitor.left;
                    int physH = info.rcMonitor.bottom - info.rcMonitor.top;
                    // Try CreateDC + GetDeviceCaps(DESKTOPHORZRES/VERTRES) to find
                    // the actual pixel resolution behind DPI scaling. If anything
                    // fails, fall back to the rcMonitor size.
                    IntPtr hdcMon = CreateDC(info.szDevice, info.szDevice, null, IntPtr.Zero);
                    if (hdcMon != IntPtr.Zero)
                    {
                        int dw = GetDeviceCaps(hdcMon, DESKTOPHORZRES);
                        int dh = GetDeviceCaps(hdcMon, DESKTOPVERTRES);
                        if (dw > 0 && dh > 0) { physW = dw; physH = dh; }
                        DeleteDC(hdcMon);
                    }
                    list.Add(new Display
                    {
                        Index = i++,
                        DeviceName = info.szDevice,
                        X = info.rcMonitor.left,
                        Y = info.rcMonitor.top,
                        Width = info.rcMonitor.right - info.rcMonitor.left,
                        Height = info.rcMonitor.bottom - info.rcMonitor.top,
                        PhysicalWidth = physW,
                        PhysicalHeight = physH,
                        IsPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0
                    });
                }
                return true;
            }, IntPtr.Zero);
        return ok ? list : new List<Display>();
    }

    // ---- Win32 interop ----

    private const int MONITORINFOF_PRIMARY = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    // ---- GDI for physical-pixel dimensions ----

    private const int DESKTOPHORZRES = 118;
    private const int DESKTOPVERTRES = 117;

    [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr CreateDC(string lpDriver, string lpDevice, string? lpOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);
}

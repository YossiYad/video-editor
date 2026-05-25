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

        public string FriendlyName =>
            $"Monitor {Index + 1} — {Width}×{Height}" + (IsPrimary ? " (primary)" : "");
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
                    list.Add(new Display
                    {
                        Index = i++,
                        DeviceName = info.szDevice,
                        X = info.rcMonitor.left,
                        Y = info.rcMonitor.top,
                        Width = info.rcMonitor.right - info.rcMonitor.left,
                        Height = info.rcMonitor.bottom - info.rcMonitor.top,
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
}

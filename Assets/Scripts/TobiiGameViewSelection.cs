using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Coordinate-independent policy, shared by the Win32 locator and standalone tests.
public static class TobiiGameViewSelection
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Bounds
    {
        public int Left, Top, Right, Bottom;
        public bool IsValid { get { return Right > Left && Bottom > Top; } }
        public bool SameAs(Bounds other)
        {
            return Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
        }
    }

    public sealed class Window
    {
        public IntPtr Handle;
        public string Monitor;
        public Bounds MonitorBounds;
        public bool Focused;
    }

    public static IntPtr Choose(string monitor, Bounds bounds, IList<Window> windows, IntPtr previous)
    {
        if (string.IsNullOrEmpty(monitor) || !bounds.IsValid || windows == null) return IntPtr.Zero;
        IntPtr focused = IntPtr.Zero, retained = IntPtr.Zero, first = IntPtr.Zero;
        bool retainedFocused = false;
        foreach (Window window in windows)
        {
            if (window == null || window.Handle == IntPtr.Zero ||
                !string.Equals(window.Monitor, monitor, StringComparison.OrdinalIgnoreCase) ||
                !window.MonitorBounds.SameAs(bounds)) continue;
            if (first == IntPtr.Zero || window.Handle.ToInt64() < first.ToInt64()) first = window.Handle;
            if (window.Handle == previous) { retained = previous; retainedFocused = window.Focused; }
            if (window.Focused && (focused == IntPtr.Zero || window.Handle.ToInt64() < focused.ToInt64()))
                focused = window.Handle;
        }
        return retainedFocused ? retained : focused != IntPtr.Zero ? focused : retained != IntPtr.Zero ? retained : first;
    }

    public static bool CanRecord(bool targetAvailable, bool connected, bool valid, int frame, int changedFrame, double age)
    {
        // A previous window's valid Last value must not survive a rebind or a stopped stream.
        return targetAvailable && connected && valid && frame > changedFrame &&
            !double.IsNaN(age) && !double.IsInfinity(age) && age >= 0 && age < 0.5;
    }
}

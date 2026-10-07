using System;
using System.Reflection;
using UnityEngine;
using Tobii.Gaming;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
using System.Text;
using TobiiGameIntegrationApi = Tobii.GameIntegration.Net.TobiiGameIntegrationApi;
using TrackerInfo = Tobii.GameIntegration.Net.TrackerInfo;
#endif

public class TobiiDebugCheck : MonoBehaviour
{
    private float logTimer = 0f;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private bool trackerEnumerationPending;
    private float trackerEnumerationStartedAt;
    private float nextTrackerEnumerationAt;
    private string trackerEnumerationResult = "TrackerCount = Unknown";
#endif

    private void Update()
    {
        // Diagnostics must continue while the experiment is paused (timeScale == 0).
        logTimer += Time.unscaledDeltaTime;
        if (logTimer < 1f) return;
        logTimer = 0f;

        try
        {
            // Let the SDK tick before reading native connection/initialization status.
            GazePoint gazePoint = TobiiTrackedGameView.GetGazePoint();
            Debug.Log(
                "Tobii IsConnected = " + TobiiAPI.IsConnected +
                ", Gaze IsValid = " + gazePoint.IsValid +
                ", Screen = " + gazePoint.Screen +
                ", Viewport = " + gazePoint.Viewport +
                ", AppFocused = " + Application.isFocused +
                ", ExperimentPaused = " + ExperimentPauseManager.IsPaused +
                ", GameScreen = " + Screen.width + "x" + Screen.height +
                ", GameViewSelection = " + TobiiTrackedGameView.SelectionStatus +
                ", " + ReadHostDiagnostics() + ReadNativeDiagnostics()
            );
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Tobii diagnostic failed: " + exception.GetType().Name + ": " + exception.Message);
        }
    }

    private static string ReadHostDiagnostics()
    {
        // SDK 5.0 keeps these types internal. Inspect them without changing the SDK
        // or registering a different window, which could affect gaze coordinates.
        try
        {
            PropertyInfo property = typeof(TobiiAPI).GetProperty("Host", BindingFlags.Static | BindingFlags.NonPublic);
            object host = property == null ? null : property.GetValue(null, null);
            if (host == null) return "Host = Unavailable, HostInitialized = Unknown, HWND = Unknown";
            Type type = host.GetType();
            PropertyInfo initialized = type.GetProperty("IsInitialized", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo providerField = type.GetField("_gameViewBoundsProvider", BindingFlags.Instance | BindingFlags.NonPublic);
            object provider = providerField == null ? null : providerField.GetValue(host);
            object handle = null;
            if (provider != null)
            {
                for (Type providerType = provider.GetType(); providerType != null; providerType = providerType.BaseType)
                {
                    FieldInfo field = providerType.GetField("_hwnd", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (field == null) continue;
                    handle = field.GetValue(provider);
                    break;
                }
            }
            string hwnd = handle is IntPtr ? "0x" + ((IntPtr)handle).ToInt64().ToString("X") : "Unknown";
            // HostInitialized means TrackWindow was called, not that it succeeded.
            return "Host = " + type.FullName + ", HostInitialized = " +
                (initialized == null ? "Unknown" : Convert.ToString(initialized.GetValue(host, null))) +
                ", HWND = " + hwnd + ReadWindowMonitorDiagnostics(handle);
        }
        catch (Exception exception)
        {
            return "HostDiagnostics = " + exception.GetType().Name;
        }
    }

    private string ReadNativeDiagnostics()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        try
        {
            bool initialized = TobiiGameIntegrationApi.IsApiInitialized();
            string status = ", ApiInitialized = " + initialized +
                ", TrackerEnabled = " + TobiiGameIntegrationApi.IsTrackerEnabled() +
                ", NativeDll = " + TobiiGameIntegrationApi.LoadedDll;
            if (!initialized) return status + ", TrackerEnumeration = NotInitialized";
            return status + ", SelectedTracker = " + FormatTracker(TobiiGameIntegrationApi.GetTrackerInfo()) +
                ReadTrackerEnumeration();
        }
        catch (Exception exception)
        {
            return ", NativeDiagnostics = " + exception.GetType().Name + ": " + exception.Message;
        }
#else
        return ", NativeDiagnostics = WindowsOnly";
#endif
    }

    private static string ReadWindowMonitorDiagnostics(object handle)
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        try
        {
            if (!(handle is IntPtr) || (IntPtr)handle == IntPtr.Zero)
                return ", WindowMonitor = Unavailable";
            IntPtr monitor = MonitorFromWindow((IntPtr)handle, 2); // MONITOR_DEFAULTTONEAREST
            MonitorInfo info = new MonitorInfo();
            info.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
                return ", WindowMonitor = Unavailable";
            return ", WindowMonitor = " + info.Device + ", WindowMonitorRect = " +
                FormatRectangle(info.Bounds.Left, info.Bounds.Top, info.Bounds.Right, info.Bounds.Bottom);
        }
        catch (Exception exception)
        {
            return ", WindowMonitorDiagnostics = " + exception.GetType().Name;
        }
#else
        return ", WindowMonitor = WindowsOnly";
#endif
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private string ReadTrackerEnumeration()
    {
        float now = Time.realtimeSinceStartup;
        // A null list means the asynchronous request is still pending, not zero devices.
        // Poll the existing request before asking for another one.
        if (trackerEnumerationPending)
        {
            var trackers = TobiiGameIntegrationApi.GetTrackerInfos();
            if (trackers != null)
            {
                trackerEnumerationPending = false;
                nextTrackerEnumerationAt = now + 5f;
                StringBuilder report = new StringBuilder("TrackerCount = " + trackers.Count + ", TrackerList = [");
                for (int i = 0; i < trackers.Count && i < 8; i++)
                {
                    if (i > 0) report.Append("; ");
                    report.Append(FormatTracker(trackers[i]));
                }
                if (trackers.Count > 8) report.Append("; ...");
                report.Append("]");
                trackerEnumerationResult = report.ToString();
            }
        }
        if (!trackerEnumerationPending && now >= nextTrackerEnumerationAt)
        {
            TobiiGameIntegrationApi.UpdateTrackerInfos();
            trackerEnumerationStartedAt = now;
            trackerEnumerationPending = true;
        }
        string state = trackerEnumerationPending
            ? "Pending(" + (now - trackerEnumerationStartedAt).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s)"
            : "Complete";
        return ", TrackerEnumeration = " + state + ", " + trackerEnumerationResult;
    }

    private static string FormatTracker(TrackerInfo tracker)
    {
        if (tracker == null) return "None";
        var rect = tracker.DisplayRectInOSCoordinates;
        // Model/monitor metadata is sufficient; do not log device URLs or serial numbers.
        return "{Model=" + tracker.ModelName + ", Type=" + tracker.Type +
            ", Attached=" + tracker.IsAttached + ", Capabilities=" + tracker.Capabilities +
            ", Monitor=" + tracker.MonitorNameInOS + ", DisplayRect=" +
            FormatRectangle(rect.Left, rect.Top, rect.Right, rect.Bottom) + "}";
    }

    private static string FormatRectangle(int left, int top, int right, int bottom)
    {
        return "(" + left + "," + top + "," + right + "," + bottom + ")";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRectangle Bounds, WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
#endif
}

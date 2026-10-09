using System;
using UnityEngine;
using Tobii.Gaming;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using NativeApi = Tobii.GameIntegration.Net.TobiiGameIntegrationApi;
using TrackerInfo = Tobii.GameIntegration.Net.TrackerInfo;
using TrackerType = Tobii.GameIntegration.Net.TrackerType;
using CapabilityFlags = Tobii.GameIntegration.Net.CapabilityFlags;
using Bounds = TobiiGameViewSelection.Bounds;
#endif

// SDK 5.0.0.3 has no public hook for its window locator. Keep its bounds/gaze
// conversion, but pin its cached HWND to a Game view on the configured monitor.
[DefaultExecutionOrder(-1000)]
public class TobiiTrackedGameView : MonoBehaviour
{
    private static TobiiTrackedGameView instance;
    public static string SelectionStatus { get { return instance == null ? "NotRunning" : instance.status; } }
    private string status = "WaitingForTrackerDisplay";
#if CRANE_GAZE_TESTS
    internal static Func<GazePoint> TestRead;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (instance != null) return;
        if (FindObjectOfType<TobiiDebugCheck>(true) == null && FindObjectOfType<TobiiGazeCsvLogger>(true) == null &&
            FindObjectOfType<TaskSwitchExperimentCsvLogger>(true) == null) return;
        GameObject root = new GameObject("TobiiTrackedGameView");
        DontDestroyOnLoad(root);
        root.AddComponent<TobiiTrackedGameView>();
#endif
    }

    private void OnEnable()
    {
        if (instance != null && instance != this) { enabled = false; return; }
        instance = this;
    }

    private void Update()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        RefreshSafely();
#endif
    }

    private void OnDisable()
    {
        if (instance != this) return;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        RestoreBinding();
        ready = false;
#endif
        instance = null;
    }

    public static GazePoint GetGazePoint()
    {
#if CRANE_GAZE_TESTS
        if (TestRead != null) return TestRead();
#endif
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (instance == null) return GazePoint.Invalid;
        instance.RefreshSafely();
        if (!instance.ready) return GazePoint.Invalid;
        try
        {
            GazePoint point = TobiiAPI.GetGazePoint();
            bool finite = point.IsValid && !float.IsInfinity(point.Viewport.x) && !float.IsInfinity(point.Viewport.y);
            return TobiiGameViewSelection.CanRecord(instance.ready, TobiiAPI.IsConnected, finite,
                Time.frameCount, instance.changedFrame, Time.unscaledTime - point.Timestamp) ? point : GazePoint.Invalid;
        }
        catch (Exception exception)
        {
            instance.ready = false;
            instance.SetStatus("GazeReadFailed:" + exception.GetType().Name, true);
            return GazePoint.Invalid;
        }
#else
        return TobiiAPI.GetGazePoint();
#endif
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    // ready means a matching Game view is bound; each sample still needs a connected, fresh SDK point.
    private bool ready, enumerationPending;
    private bool trackWindowAccepted;
    private float nextSearch, nextEnumeration;
    private List<TrackerInfo> enumeratedTrackers;
    private IntPtr selected;
    private string selectedMonitor;
    private Bounds selectedBounds;
    private int changedFrame = -1;
    private object boundProvider;
    private FieldInfo hwndField, timerField;
    private IntPtr originalHwnd;
    private object originalTimer;

    private void SetStatus(string value, bool warning)
    {
        if (status == value) return;
        status = value;
        string message = "Tobii GameView selection = " + value;
        if (warning) UnityEngine.Debug.LogWarning(message);
        else UnityEngine.Debug.Log(message);
    }

    private void RefreshSafely()
    {
        try { Refresh(); }
        catch (Exception exception)
        {
            ready = false;
            RestoreBinding();
            SetStatus("SelectionFailed:" + exception.GetType().Name + ":" + exception.Message, true);
        }
    }

    private void Refresh()
    {
        // Revalidate the selected window every frame, even while timeScale is zero.
        if (ready && !WindowMatches(selected, selectedMonitor, selectedBounds))
        {
            ready = false;
            nextSearch = 0;
            RestoreBinding();
            SetStatus("SelectedGameViewUnavailable", true);
        }
        if (Time.realtimeSinceStartup < nextSearch) return;
        nextSearch = Time.realtimeSinceStartup + 0.5f;

        PropertyInfo hostProperty = typeof(TobiiAPI).GetProperty("Host", BindingFlags.Static | BindingFlags.NonPublic);
        object host = hostProperty == null ? null : hostProperty.GetValue(null, null);
        if (host == null || host.GetType().FullName != "Tobii.Gaming.Internal.TobiiHost")
        { Reject("SdkHostUnavailable"); return; }

        // Reading Host creates the SDK's host if necessary; no duplicate Start/settings.
        if (!NativeApi.IsApiInitialized()) { Reject("NativeApiNotInitialized"); return; }
        TrackerInfo tracker = ReadConfiguredTracker();
        if (tracker == null) { Reject("WaitingForTrackerDisplay"); return; }
        Bounds bounds = ToBounds(tracker);
        string monitor = tracker.MonitorNameInOS;
        List<TobiiGameViewSelection.Window> candidates = EnumerateWindows();
        IntPtr chosen = TobiiGameViewSelection.Choose(monitor, bounds, candidates, selected);
        if (chosen == IntPtr.Zero) { Reject("NoGameViewOnTrackedMonitor:" + monitor); return; }

        FieldInfo providerField = host.GetType().GetField("_gameViewBoundsProvider", BindingFlags.Instance | BindingFlags.NonPublic);
        object provider = providerField == null ? null : providerField.GetValue(host);
        if (provider == null) { Reject("SdkBoundsProviderUnavailable"); return; }
        bool changed = !ready || chosen != selected || monitor != selectedMonitor ||
            !bounds.SameAs(selectedBounds) || !ReferenceEquals(provider, boundProvider);
        if (!ReferenceEquals(provider, boundProvider))
        {
            RestoreBinding();
            hwndField = FindField(provider.GetType(), "_hwnd");
            timerField = FindField(provider.GetType(), "_newHandleTimer");
            if (hwndField == null || hwndField.FieldType != typeof(IntPtr))
            { Reject("UnsupportedSdkWindowProvider"); return; }
#if UNITY_EDITOR_WIN
            if (timerField == null || timerField.FieldType != typeof(float))
            { Reject("UnsupportedSdkEditorWindowRefresh"); return; }
#endif
            boundProvider = provider;
            originalHwnd = (IntPtr)hwndField.GetValue(provider);
            originalTimer = timerField == null ? null : timerField.GetValue(provider);
        }
        // Disable the SDK's monitor-unaware two-second search while this selector owns it.
        hwndField.SetValue(boundProvider, chosen);
        if (timerField != null) timerField.SetValue(boundProvider, float.NegativeInfinity);
        if (changed)
        {
            ready = false;
            // Keep the verified window binding even if the native call returns false.
            // The SDK also calls TrackWindow each frame. Restoring its old HWND here
            // makes this selector rebind on every search and can interrupt stream recovery.
            // Recording still requires an actual fresh point after this cache reset.
            trackWindowAccepted = NativeApi.TrackWindow(chosen);
            NativeApi.GetGazePoints(); // Discard queued coordinates from the previous window.
            ClearLastGaze(host);
            changedFrame = Time.frameCount;
        }
        selected = chosen;
        selectedMonitor = monitor;
        selectedBounds = bounds;
        ready = true;
        if (changed)
            SetStatus((trackWindowAccepted ? "Matched:" : "Bound:") + monitor +
                ":0x" + chosen.ToInt64().ToString("X") +
                (trackWindowAccepted ? "" : ":TrackWindowReturnedFalse"), !trackWindowAccepted);
    }

    private TrackerInfo ReadConfiguredTracker()
    {
        // The selected tracker can report Attached=false even during valid streaming.
        // Do not reject that flag; require PC/Gaze/configured monitor geometry instead.
        TrackerInfo current = NativeApi.GetTrackerInfo();
        if (Configured(current)) return current;
        if (enumerationPending)
        {
            var result = NativeApi.GetTrackerInfos();
            if (result != null)
            {
                enumeratedTrackers = result;
                enumerationPending = false;
                nextEnumeration = Time.realtimeSinceStartup + 5f;
            }
        }
        if (!enumerationPending && Time.realtimeSinceStartup >= nextEnumeration)
        {
            NativeApi.UpdateTrackerInfos();
            enumerationPending = true;
        }
        TrackerInfo only = null;
        if (enumeratedTrackers != null) foreach (TrackerInfo tracker in enumeratedTrackers)
        {
            if (tracker == null || !tracker.IsAttached || !Configured(tracker)) continue;
            if (only != null) return null; // Do not silently choose between multiple trackers.
            only = tracker;
        }
        return only;
    }

    private static bool Configured(TrackerInfo tracker)
    {
        return tracker != null && tracker.Type == TrackerType.PC &&
            (tracker.Capabilities & CapabilityFlags.Gaze) != 0 &&
            !string.IsNullOrEmpty(tracker.MonitorNameInOS) && ToBounds(tracker).IsValid;
    }

    private static Bounds ToBounds(TrackerInfo tracker)
    {
        var rect = tracker.DisplayRectInOSCoordinates;
        return new Bounds { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom };
    }

    private void Reject(string reason)
    {
        ready = false;
        RestoreBinding();
        SetStatus(reason, true);
    }

    private void RestoreBinding()
    {
        if (boundProvider == null) return;
        try
        {
            hwndField.SetValue(boundProvider, originalHwnd);
            if (timerField != null) timerField.SetValue(boundProvider, originalTimer);
        }
        catch (Exception) { /* SDK may already have been destroyed on Play exit. */ }
        boundProvider = null;
        hwndField = timerField = null;
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (; type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        return null;
    }

    private static void ClearLastGaze(object host)
    {
        FieldInfo field = FindField(host.GetType(), "_gazePointDataProvider");
        object provider = field == null ? null : field.GetValue(host);
        FieldInfo last = provider == null ? null : FindField(provider.GetType(), "_last");
        if (last == null) throw new InvalidOperationException("SDK gaze cache could not be invalidated");
        last.SetValue(provider, GazePoint.Invalid);
        FieldInfo history = FindField(provider.GetType(), "_lastDataPoints");
        IList points = history == null ? null : history.GetValue(provider) as IList;
        if (points != null) points.Clear();
    }

    private static bool WindowMatches(IntPtr handle, string monitor, Bounds bounds)
    {
        if (handle == IntPtr.Zero || !IsWindowVisible(handle)) return false;
        uint process;
        GetWindowThreadProcessId(handle, out process);
        if (process != (uint)Process.GetCurrentProcess().Id) return false;
#if UNITY_EDITOR_WIN
        StringBuilder caption = new StringBuilder(256);
        GetWindowText(handle, caption, caption.Capacity);
        if (caption.ToString() != "UnityEditor.GameView") return false;
#endif
        MonitorInfo info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
        IntPtr display = MonitorFromWindow(handle, 0); // MONITOR_DEFAULTTONULL
        return display != IntPtr.Zero && GetMonitorInfo(display, ref info) &&
            string.Equals(info.Device, monitor, StringComparison.OrdinalIgnoreCase) && info.Bounds.SameAs(bounds);
    }

    private static List<TobiiGameViewSelection.Window> enumeratingWindows;
    private static uint enumeratingProcess;
    private static IntPtr enumeratingForeground;

    private static List<TobiiGameViewSelection.Window> EnumerateWindows()
    {
        var windows = new List<TobiiGameViewSelection.Window>();
        enumeratingWindows = windows;
        enumeratingProcess = (uint)Process.GetCurrentProcess().Id;
        enumeratingForeground = GetForegroundWindow();
        try { EnumWindows(EnumerateRoot, IntPtr.Zero); }
        finally { enumeratingWindows = null; }
        return windows;
    }

    [AOT.MonoPInvokeCallback(typeof(EnumWindowCallback))]
    private static bool EnumerateRoot(IntPtr root, IntPtr unused)
    {
        uint owner;
        GetWindowThreadProcessId(root, out owner);
        if (owner != enumeratingProcess || !IsWindowVisible(root)) return true;
#if UNITY_EDITOR_WIN
        StringBuilder name = new StringBuilder(256);
        GetClassName(root, name, name.Capacity);
        if (name.ToString() != "UnityContainerWndClass") return true;
        EnumChildWindows(root, EnumerateChild, root);
#else
        if (GetWindow(root, 4) == IntPtr.Zero) AddWindow(enumeratingWindows, root, root == enumeratingForeground); // GW_OWNER
#endif
        return true;
    }

    [AOT.MonoPInvokeCallback(typeof(EnumWindowCallback))]
    private static bool EnumerateChild(IntPtr child, IntPtr parent)
    {
        StringBuilder caption = new StringBuilder(256);
        GetWindowText(child, caption, caption.Capacity);
        if (caption.ToString() == "UnityEditor.GameView") AddWindow(enumeratingWindows, child, parent == enumeratingForeground);
        return true;
    }

    private static void AddWindow(List<TobiiGameViewSelection.Window> windows, IntPtr handle, bool focused)
    {
        if (!IsWindowVisible(handle)) return;
        Bounds client;
        if (!GetClientRect(handle, out client) || !client.IsValid) return;
        MonitorInfo info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
        IntPtr monitor = MonitorFromWindow(handle, 0);
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;
        windows.Add(new TobiiGameViewSelection.Window { Handle = handle, Monitor = info.Device,
            MonitorBounds = info.Bounds, Focused = focused });
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Bounds Bounds, WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Bounds bounds);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetWindowText(IntPtr window, StringBuilder name, int size);
#endif
}

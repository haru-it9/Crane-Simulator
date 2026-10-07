using System;
using System.Reflection;
using UnityEngine;
using Tobii.Gaming;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using TobiiGameIntegrationApi = Tobii.GameIntegration.Net.TobiiGameIntegrationApi;
#endif

public class TobiiDebugCheck : MonoBehaviour
{
    private float logTimer = 0f;

    private void Update()
    {
        // Diagnostics must continue while the experiment is paused (timeScale == 0).
        logTimer += Time.unscaledDeltaTime;
        if (logTimer < 1f) return;
        logTimer = 0f;

        try
        {
            // Let the SDK tick before reading native connection/initialization status.
            GazePoint gazePoint = TobiiAPI.GetGazePoint();
            Debug.Log(
                "Tobii IsConnected = " + TobiiAPI.IsConnected +
                ", Gaze IsValid = " + gazePoint.IsValid +
                ", Screen = " + gazePoint.Screen +
                ", Viewport = " + gazePoint.Viewport +
                ", AppFocused = " + Application.isFocused +
                ", ExperimentPaused = " + ExperimentPauseManager.IsPaused +
                ", GameScreen = " + Screen.width + "x" + Screen.height +
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
                ", HWND = " + hwnd;
        }
        catch (Exception exception)
        {
            return "HostDiagnostics = " + exception.GetType().Name;
        }
    }

    private static string ReadNativeDiagnostics()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        try
        {
            return ", ApiInitialized = " + TobiiGameIntegrationApi.IsApiInitialized() +
                ", TrackerEnabled = " + TobiiGameIntegrationApi.IsTrackerEnabled() +
                ", NativeDll = " + TobiiGameIntegrationApi.LoadedDll;
        }
        catch (Exception exception)
        {
            return ", NativeDiagnostics = " + exception.GetType().Name + ": " + exception.Message;
        }
#else
        return ", NativeDiagnostics = WindowsOnly";
#endif
    }
}

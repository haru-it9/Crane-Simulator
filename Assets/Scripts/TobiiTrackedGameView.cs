using System;
using UnityEngine;
using Tobii.Gaming;

// Use the same SDK acquisition and sample checks as EyeTrack_2022ver/yuba1st.
// Keep this component/API for existing scenes and both CSV loggers. Window
// selection, native registration and gaze caches are owned entirely by the SDK.
[DefaultExecutionOrder(-1000)]
public class TobiiTrackedGameView : MonoBehaviour
{
    public static string SelectionStatus { get { return "SdkDirect"; } }
    private static float nextReadErrorTime;

#if CRANE_GAZE_TESTS
    internal static Func<GazePoint> TestRead;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (FindObjectOfType<TobiiTrackedGameView>(true) != null) return;
        if (FindObjectOfType<TobiiDebugCheck>(true) == null && FindObjectOfType<TobiiGazeCsvLogger>(true) == null &&
            FindObjectOfType<TaskSwitchExperimentCsvLogger>(true) == null) return;
        GameObject root = new GameObject("TobiiTrackedGameView");
        DontDestroyOnLoad(root);
        root.AddComponent<TobiiTrackedGameView>();
#endif
    }

    private void Update()
    {
        // As in EyeTrack, poll before recording starts and while timeScale is zero.
        // The SDK updates its providers at most once per Unity frame.
        GetGazePoint();
    }

    public static GazePoint GetGazePoint()
    {
#if CRANE_GAZE_TESTS
        if (TestRead != null) return TestRead();
#endif
        try
        {
            GazePoint point = TobiiAPI.GetGazePoint();
            Vector2 screen = point.Screen;
            return point.IsValid && point.IsRecent() && IsFinite(screen.x) && IsFinite(screen.y)
                ? point : GazePoint.Invalid;
        }
        catch (Exception exception)
        {
            if (Time.realtimeSinceStartup >= nextReadErrorTime)
            {
                nextReadErrorTime = Time.realtimeSinceStartup + 1f;
                Debug.LogWarning("Tobii SDK gaze acquisition failed: " + exception.GetType().Name + ": " + exception.Message);
            }
            return GazePoint.Invalid;
        }
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

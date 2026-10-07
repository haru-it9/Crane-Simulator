using UnityEngine;
using Tobii.Gaming;

public class TobiiDebugCheck : MonoBehaviour
{
    private float logTimer = 0f;

    private void Update()
    {
        // Diagnostics must continue while the experiment is paused (timeScale == 0).
        logTimer += Time.unscaledDeltaTime;
        if (logTimer < 1f) return;
        logTimer = 0f;

        GazePoint gazePoint = TobiiAPI.GetGazePoint();

        Debug.Log(
            "Tobii IsConnected = " + TobiiAPI.IsConnected +
            ", Gaze IsValid = " + gazePoint.IsValid +
            ", Screen = " + gazePoint.Screen +
            ", Viewport = " + gazePoint.Viewport +
            ", AppFocused = " + Application.isFocused +
            ", ExperimentPaused = " + ExperimentPauseManager.IsPaused +
            ", GameScreen = " + Screen.width + "x" + Screen.height
        );
    }
}

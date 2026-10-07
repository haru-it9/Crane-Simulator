using System;
using UnityEngine;

public partial class TaskSwitchExperimentCsvLogger
{
    private ExperimentCsvFile pauseFile;
    private int pauseIntervalIndex, pauseStartRun, pauseStartSwitch, pauseStartCrane;
    private double pauseIntervalStart = double.NaN;
    private string pauseStartState, pauseStartPhase, pauseStartStep;
    private bool pauseStartObserved;

    private void OpenPauseFile()
    {
        pauseFile = Open("pause_intervals", IdentityHeader + ",pause_interval_index,start_s,end_s,duration_s,start_observed,end_observed,outcome,start_switch_index,end_switch_index,start_crane_index,end_crane_index,start_state,end_state,start_phase,start_step,end_phase,end_step");
        pauseIntervalIndex = 0; pauseIntervalStart = double.NaN;
    }
    private void SubscribePauseLogging()
    {
        ExperimentPauseManager.PauseStateChanged += HandleGlobalPauseChanged;
        if (ExperimentPauseManager.IsPaused) BeginPauseInterval(false);
    }
    private void HandleGlobalPauseChanged(bool paused)
    {
        if (!isLogging) return;
        if (paused) BeginPauseInterval(true);
        else EndPauseInterval(true, "Resumed");
        // Force a wall-clock sample at each boundary; ongoing paused samples continue normally.
        nextSampleTime = Math.Min(nextSampleTime, RealSeconds);
    }
    private void BeginPauseInterval(bool observed)
    {
        if (!double.IsNaN(pauseIntervalStart)) return;
        pauseIntervalIndex++;
        pauseIntervalStart = RealSeconds;
        pauseStartRun = experimentRunIndex;
        pauseStartSwitch = taskSwitchExperimentManager.CurrentSwitchIndex;
        pauseStartCrane = GetSelectedCraneIndex();
        pauseStartState = taskSwitchExperimentManager.CurrentState.ToString();
        CraneWorkPhaseTracker tracker = GetWorkPhaseTracker(pauseStartCrane);
        pauseStartPhase = tracker != null ? tracker.CurrentMajorPhase.ToString() : "";
        pauseStartStep = tracker != null ? tracker.CurrentStepId : "";
        pauseStartObserved = observed;
        WriteEvent("Pause", observed ? "GlobalPauseStarted" : "GlobalPauseAtLoggingStart", pauseStartCrane,
            null, pauseStartStep, "PauseInterval=" + pauseIntervalIndex);
        writer.Flush();
    }
    private void EndPauseInterval(bool observed, string outcome)
    {
        if (double.IsNaN(pauseIntervalStart) || pauseFile == null) return;
        double end = RealSeconds;
        int crane = GetSelectedCraneIndex();
        CraneWorkPhaseTracker tracker = GetWorkPhaseTracker(crane);
        string phase = tracker != null ? tracker.CurrentMajorPhase.ToString() : "";
        string step = tracker != null ? tracker.CurrentStepId : "";
        pauseFile.Write(Join(Identity(pauseStartRun), pauseIntervalIndex, pauseIntervalStart, end,
            Math.Max(0, end - pauseIntervalStart), pauseStartObserved, observed, outcome,
            pauseStartSwitch, taskSwitchExperimentManager.CurrentSwitchIndex, pauseStartCrane, crane,
            pauseStartState, taskSwitchExperimentManager.CurrentState, pauseStartPhase, pauseStartStep, phase, step));
        pauseFile.Flush();
        if (observed)
        {
            WriteEvent("Pause", "GlobalPauseEnded", crane, null, step,
                "PauseInterval=" + pauseIntervalIndex + ";DurationSeconds=" + ExperimentCsvFile.Encode(end - pauseIntervalStart));
            writer.Flush();
        }
        pauseIntervalStart = double.NaN;
    }
    private int? ActivePauseInterval => double.IsNaN(pauseIntervalStart) ? (int?)null : pauseIntervalIndex;
}

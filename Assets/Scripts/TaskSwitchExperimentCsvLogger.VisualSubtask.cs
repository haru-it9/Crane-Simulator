using System;
using UnityEngine;

public partial class TaskSwitchExperimentCsvLogger
{
    private ExperimentCsvFile visualEventFile, visualTrialFile;
    private int visualOnsetTrial;
    private string visualOnsetState = "", visualOnsetPhase = "", visualOnsetStep = "";
    private int? visualOnsetCrane, visualOnsetSwitch;
    private const string VisualHeader = IdentityHeader + ",activation_index,event_type,secondary_task_mode,utc_timestamp,real_elapsed_s,simulation_elapsed_s,frame,switch_index,active_crane_index,state,input_locked,global_paused,operation_enabled,trial_index,stimulus_side,expected_sign,response_sign,outcome,correct,reaction_time_s,presented,held_at_onset,raw_pedal,pedal_axis,invert_axis,press_threshold,release_threshold,response_timeout_s,minimum_valid_reaction_s,minimum_interval_s,maximum_interval_s,random_seed,active_clock_s,scheduled_onset_active_clock_s,actual_onset_active_clock_s,scheduled_onset_estimated_real_s,onset_observed_real_s,onset_frame_delay_s,response_observed_real_s,left_red,right_red,onset_switch_index,onset_crane_index,onset_state,onset_phase,onset_step,current_phase,current_step,app_focused,configuration_json,detail,pause_count,paused_duration_s,wall_reaction_time_s";

    private void OpenVisualFiles()
    {
        visualEventFile = Open("visual_subtask", VisualHeader);
        visualTrialFile = Open("visual_trials", VisualHeader);
        visualOnsetTrial = 0;
        visualOnsetState = visualOnsetPhase = visualOnsetStep = "";
        visualOnsetCrane = visualOnsetSwitch = null;
    }
    private void HandleVisualEvent(TaskSwitchAuditoryEvent e)
    {
        if (!isLogging || visualEventFile == null) return;
        TaskSwitchVisualSettings settings = taskSwitchExperimentManager.VisualRunSettings ?? new TaskSwitchVisualSettings();
        int crane = GetSelectedCraneIndex();
        CraneWorkPhaseTracker tracker = GetWorkPhaseTracker(crane);
        if (e.EventType == "StimulusScheduled" || e.EventType == "SubtaskStarted")
        {
            visualOnsetTrial = 0;
            visualOnsetCrane = visualOnsetSwitch = null;
            visualOnsetState = visualOnsetPhase = visualOnsetStep = "";
        }
        if (e.EventType == "StimulusOnsetObserved")
        {
            visualOnsetTrial = e.TrialIndex;
            visualOnsetCrane = crane;
            visualOnsetSwitch = taskSwitchExperimentManager.CurrentSwitchIndex;
            visualOnsetState = taskSwitchExperimentManager.CurrentState.ToString();
            visualOnsetPhase = tracker != null ? tracker.CurrentMajorPhase.ToString() : "";
            visualOnsetStep = tracker != null ? tracker.CurrentStepId : "";
        }
        bool associated = e.TrialIndex > 0;
        bool onsetKnown = associated && e.TrialIndex == visualOnsetTrial;
        object[] row = Join(Identity(), taskSwitchExperimentManager.VisualActivationIndex, e.EventType, TaskSwitchSecondaryTaskMode.Visual,
            DateTime.UtcNow.ToString("O"), Math.Max(0, e.RealTime - sessionRealOrigin), SimulationSeconds, Time.frameCount,
            taskSwitchExperimentManager.CurrentSwitchIndex, crane, taskSwitchExperimentManager.CurrentState, InputLocked,
            ExperimentPauseManager.IsPaused, SimulatorStartManager.IsOperationEnabled,
            associated ? (object)e.TrialIndex : null, e.Tone,
            e.ExpectedSign == 0 ? null : (object)e.ExpectedSign, e.ResponseSign == 0 ? null : (object)e.ResponseSign,
            e.Outcome, e.Correct.HasValue ? (object)e.Correct.Value : null, e.ReactionSeconds, e.Presented, e.HeldAtOnset,
            e.RawPedal, settings.pedalAxisName, settings.invertPedalAxis, settings.pressThreshold, settings.releaseThreshold,
            settings.responseTimeoutSeconds, settings.minimumValidReactionSeconds,
            settings.minimumIntervalSeconds, settings.maximumIntervalSeconds, settings.randomSeed,
            e.DspTime, associated ? (object)e.PlannedOnsetClock : null,
            associated && e.Presented ? (object)e.ScheduledDsp : null,
            associated ? (object)(e.PlannedOnsetReal - sessionRealOrigin) : null,
            e.ObservedOnsetReal - sessionRealOrigin, e.ObservedOnsetReal - e.PlannedOnsetReal, e.ResponseReal - sessionRealOrigin,
            e.VisualLeftRed, e.VisualRightRed,
            onsetKnown ? (object)visualOnsetSwitch : null, onsetKnown ? (object)visualOnsetCrane : null,
            onsetKnown ? visualOnsetState : "", onsetKnown ? visualOnsetPhase : "", onsetKnown ? visualOnsetStep : "",
            tracker != null ? tracker.CurrentMajorPhase.ToString() : "", tracker != null ? tracker.CurrentStepId : "",
            Application.isFocused, taskSwitchExperimentManager.VisualRunConfigurationJson, e.Detail,
            e.PauseCount, e.PausedSeconds, e.WallReactionSeconds);
        visualEventFile.Write(row);
        if (e.EventType == "TrialFinished") { visualTrialFile.Write(row); visualTrialFile.Flush(); }
        visualEventFile.Flush();
        if (e.EventType == "SubtaskStarted" || e.EventType == "SubtaskStopped" || e.EventType == "SubtaskError")
            WriteSessionMetadata("Visual" + e.EventType);
    }
}

using System;
using UnityEngine;

public partial class TaskSwitchExperimentCsvLogger
{
    private ExperimentCsvFile auditoryEventFile, auditoryTrialFile;
    private int auditoryOnsetTrial;
    private string auditoryOnsetState = "", auditoryOnsetPhase = "", auditoryOnsetStep = "";
    private int? auditoryOnsetCrane, auditoryOnsetSwitch;
    private const string AuditoryHeader = IdentityHeader + ",activation_index,event_type,utc_timestamp,real_elapsed_s,simulation_elapsed_s,frame,switch_index,active_crane_index,state,input_locked,global_paused,operation_enabled,trial_index,tone,frequency_hz,expected_sign,response_sign,outcome,correct,reaction_time_s,presented,held_at_onset,raw_pedal,pedal_axis,invert_axis,high_uses_positive,press_threshold,release_threshold,response_timeout_s,minimum_valid_reaction_s,tone_duration_s,volume,random_seed,dsp_time_s,scheduled_onset_dsp_s,onset_estimated_real_s,onset_observed_real_s,onset_observation_lag_s,response_observed_real_s,onset_switch_index,onset_crane_index,onset_state,onset_phase,onset_step,current_phase,current_step,app_focused,configuration_json,detail";
    private void OpenAuditoryFiles()
    {
        auditoryEventFile = Open("auditory_subtask", AuditoryHeader);
        auditoryTrialFile = Open("auditory_trials", AuditoryHeader);
        auditoryOnsetTrial = 0;
        auditoryOnsetState = auditoryOnsetPhase = auditoryOnsetStep = "";
        auditoryOnsetCrane = auditoryOnsetSwitch = null;
    }
    private void HandleAuditoryEvent(TaskSwitchAuditoryEvent e)
    {
        if (!isLogging || auditoryEventFile == null) return;
        TaskSwitchAuditorySettings settings = taskSwitchExperimentManager.AuditoryRunSettings ?? new TaskSwitchAuditorySettings();
        int crane = GetSelectedCraneIndex();
        CraneWorkPhaseTracker tracker = GetWorkPhaseTracker(crane);
        if (e.EventType == "StimulusScheduled" || e.EventType == "SubtaskStarted")
        {
            auditoryOnsetTrial = 0;
            auditoryOnsetCrane = auditoryOnsetSwitch = null;
            auditoryOnsetState = auditoryOnsetPhase = auditoryOnsetStep = "";
        }
        if (e.EventType == "StimulusOnsetObserved")
        {
            auditoryOnsetTrial = e.TrialIndex;
            auditoryOnsetCrane = crane;
            auditoryOnsetSwitch = taskSwitchExperimentManager.CurrentSwitchIndex;
            auditoryOnsetState = taskSwitchExperimentManager.CurrentState.ToString();
            auditoryOnsetPhase = tracker != null ? tracker.CurrentMajorPhase.ToString() : "";
            auditoryOnsetStep = tracker != null ? tracker.CurrentStepId : "";
        }
        bool onsetKnown = e.TrialIndex > 0 && e.TrialIndex == auditoryOnsetTrial;
        object[] row = Join(Identity(), taskSwitchExperimentManager.AuditoryActivationIndex, e.EventType,
            DateTime.UtcNow.ToString("O"), Math.Max(0, e.RealTime - sessionRealOrigin), SimulationSeconds, Time.frameCount,
            taskSwitchExperimentManager.CurrentSwitchIndex, crane, taskSwitchExperimentManager.CurrentState, InputLocked,
            ExperimentPauseManager.IsPaused, SimulatorStartManager.IsOperationEnabled,
            e.TrialIndex > 0 ? (object)e.TrialIndex : null, e.Tone,
            e.Tone == "High" ? (object)settings.highToneHz : e.Tone == "Low" ? (object)settings.lowToneHz : null,
            e.ExpectedSign == 0 ? null : (object)e.ExpectedSign, e.ResponseSign == 0 ? null : (object)e.ResponseSign,
            e.Outcome, e.Correct.HasValue ? (object)e.Correct.Value : null, e.ReactionSeconds, e.Presented, e.HeldAtOnset,
            e.RawPedal, settings.pedalAxisName, settings.invertPedalAxis, settings.highToneUsesPositivePedal,
            settings.pressThreshold, settings.releaseThreshold, settings.responseTimeoutSeconds, settings.minimumValidReactionSeconds,
            settings.toneDurationSeconds, settings.volume, settings.randomSeed, e.DspTime, e.ScheduledDsp,
            e.EstimatedOnsetReal - sessionRealOrigin, e.ObservedOnsetReal - sessionRealOrigin,
            e.ObservedOnsetReal - e.EstimatedOnsetReal, e.ResponseReal - sessionRealOrigin,
            onsetKnown ? (object)auditoryOnsetSwitch : null, onsetKnown ? (object)auditoryOnsetCrane : null,
            onsetKnown ? auditoryOnsetState : "", onsetKnown ? auditoryOnsetPhase : "", onsetKnown ? auditoryOnsetStep : "",
            tracker != null ? tracker.CurrentMajorPhase.ToString() : "", tracker != null ? tracker.CurrentStepId : "",
            Application.isFocused, taskSwitchExperimentManager.AuditoryRunConfigurationJson, e.Detail);
        auditoryEventFile.Write(row);
        if (e.EventType == "TrialFinished") { auditoryTrialFile.Write(row); auditoryTrialFile.Flush(); }
        auditoryEventFile.Flush();
    }
}

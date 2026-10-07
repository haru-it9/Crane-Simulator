using System;
using UnityEngine;
using UnityEngine.UI;

public partial class TaskSwitchExperimentManager
{
    [Header("Secondary Task / サブタスク選択")]
    [Tooltip("None＝なし / Auditory＝高音・低音 / Visual＝左右の青→赤。変更は実行中にも反映。")]
    [SerializeField] private TaskSwitchSecondaryTaskMode secondaryTaskMode = TaskSwitchSecondaryTaskMode.None;
    [SerializeField, HideInInspector] private bool secondaryTaskSelectionMigrated;

    [Header("Visual Settings / 色サブタスク設定")]
    [SerializeField] private TaskSwitchVisualSettings visualSubtask = new TaskSwitchVisualSettings();
    [Tooltip("作業情報パネル左側のImage。Visual以外では非表示。Raycastを無効にします。")]
    [SerializeField] private Image visualLeftIndicator;
    [Tooltip("作業情報パネル右側のImage。Visual以外では非表示。Raycastを無効にします。")]
    [SerializeField] private Image visualRightIndicator;

    private TaskSwitchVisualSettings visualRunSettings;
    private TaskSwitchAuditoryTrial visualTrial;
    private System.Random visualRandom;
    private bool visualRunning, visualPaused, visualSuppressed, visualWaitingForOnset, visualScheduledRight;
    private bool visualLeftRed, visualRightRed;
    private int visualLastTrialIndex, visualActivationIndex;
    private double visualScheduledClock = double.NaN, visualScheduledReal = double.NaN, visualPauseStartedReal;
    private float visualRawPedal;

    public TaskSwitchSecondaryTaskMode SecondaryTaskMode => !secondaryTaskSelectionMigrated &&
        enableAuditorySubtask && secondaryTaskMode == TaskSwitchSecondaryTaskMode.None
        ? TaskSwitchSecondaryTaskMode.Auditory : secondaryTaskMode;
    public bool VisualSubtaskEnabled => SecondaryTaskMode == TaskSwitchSecondaryTaskMode.Visual;
    public bool VisualSubtaskRunning => visualRunning;
    public bool VisualLeftRed => visualLeftRed;
    public bool VisualRightRed => visualRightRed;
    public int VisualActivationIndex => visualActivationIndex;
    public TaskSwitchVisualSettings VisualRunSettings => visualRunSettings ?? visualSubtask;
    public string VisualConfigurationJson => JsonUtility.ToJson(visualSubtask);
    public string VisualRunConfigurationJson => visualRunSettings == null ? "" : JsonUtility.ToJson(visualRunSettings);
    public bool SecondarySubtaskRunning => auditoryRunning || visualRunning;
    public string SecondaryPedalAxis => visualRunning || VisualSubtaskEnabled
        ? (VisualRunSettings != null ? VisualRunSettings.pedalAxisName : "") : AuditoryPedalAxis;
    public float SecondaryRawPedal => visualRunning ? visualRawPedal : auditoryRawPedal;
    public bool SecondaryPedalArmed => visualRunning ? visualTrial.PedalArmed : auditoryRunning && AuditoryPedalArmed;
    public event Action<TaskSwitchAuditoryEvent> VisualSubtaskEventOccurred;

    private void MigrateSecondaryTaskSelection()
    {
        if (secondaryTaskSelectionMigrated) return;
        secondaryTaskMode = SecondaryTaskMode;
        enableAuditorySubtask = false;
        secondaryTaskSelectionMigrated = true;
    }
    private void InitializeSecondaryTaskUi()
    {
        MigrateSecondaryTaskSelection();
        SetVisualColors(false, false);
        SetVisualVisibility(false);
    }
    private void SubscribeSecondaryPauseEvents()
    {
        SubscribeAuditoryPauseEvents();
        ExperimentPauseManager.PauseStateChanged -= HandleVisualPause;
        ExperimentPauseManager.PauseStateChanged += HandleVisualPause;
    }
    private void UnsubscribeSecondaryPauseEvents()
    {
        UnsubscribeAuditoryPauseEvents();
        ExperimentPauseManager.PauseStateChanged -= HandleVisualPause;
    }
    private void ResetSecondaryExperiment()
    {
        ResetAuditoryExperiment();
        visualLastTrialIndex = visualActivationIndex = 0;
        visualSuppressed = false;
    }
    public void SuspendSecondaryForLoggingStop()
    {
        auditorySuppressed = visualSuppressed = true;
        StopSecondarySubtask("LoggingStopped");
    }
    public void ResumeSecondaryForLoggingStart()
    {
        auditorySuppressed = visualSuppressed = false;
        StartSecondarySubtask();
    }
    private void StartSecondarySubtask()
    {
        MigrateSecondaryTaskSelection();
        StartAuditorySubtask();
        StartVisualSubtask();
    }
    private void UpdateSecondarySubtask()
    {
        MigrateSecondaryTaskSelection();
        // Stop the old modality before starting the new one; the same pedal cannot answer both.
        if (!AuditorySubtaskEnabled && auditoryRunning) StopAuditorySubtask("InspectorDisabled");
        if (!VisualSubtaskEnabled && visualRunning) StopVisualSubtask("InspectorDisabled");
        UpdateAuditorySubtask();
        UpdateVisualSubtask();
    }
    private void StopSecondarySubtask(string reason)
    {
        StopAuditorySubtask(reason);
        StopVisualSubtask(reason);
    }
    private void DisposeSecondarySubtask()
    {
        StopVisualSubtask("ManagerDestroyed");
        UnsubscribeSecondaryPauseEvents();
        DisposeAuditorySubtask();
    }

    private void StartVisualSubtask()
    {
        if (!VisualSubtaskEnabled || visualRunning || visualSuppressed || !AuditoryExperimentActive || !SimulatorStartManager.IsOperationEnabled) return;
        try
        {
            if (visualSubtask == null) throw new InvalidOperationException("Visual settings are missing");
            if (visualLeftIndicator == null || visualRightIndicator == null || visualLeftIndicator == visualRightIndicator)
                throw new InvalidOperationException("Assign separate left and right UI Images");
            visualRunSettings = JsonUtility.FromJson<TaskSwitchVisualSettings>(JsonUtility.ToJson(visualSubtask));
            string error = visualRunSettings.Validate();
            if (error != null) throw new InvalidOperationException(error);
            if (visualRunSettings.randomSeed == 0) visualRunSettings.randomSeed = Guid.NewGuid().GetHashCode();
            visualRandom = new System.Random(visualRunSettings.randomSeed);
            visualRawPedal = Input.GetAxisRaw(visualRunSettings.pedalAxisName);
            visualTrial = new TaskSwitchAuditoryTrial(visualRunSettings.PedalSettings(), visualLastTrialIndex, true);
            visualTrial.EventOccurred += HandleVisualTrialEvent;
            SetVisualColors(false, false);
            SetVisualVisibility(true);
            ValidateVisualVisibility();
            visualScheduledClock = visualScheduledReal = double.NaN;
            visualRunning = true;
            visualPaused = ExperimentPauseManager.IsPaused;
            visualWaitingForOnset = false;
            visualActivationIndex++;
            EmitVisualLifecycle("SubtaskStarted", VisualRunConfigurationJson);
            if (!visualPaused) ScheduleVisualTrial();
        }
        catch (Exception exception) { FailVisualSubtask(exception.Message); }
    }
    private void ScheduleVisualTrial()
    {
        double clock = ExperimentPauseManager.ActiveRealtime, real = Time.realtimeSinceStartupAsDouble;
        double interval = visualRunSettings.minimumIntervalSeconds + visualRandom.NextDouble() *
            (visualRunSettings.maximumIntervalSeconds - visualRunSettings.minimumIntervalSeconds);
        // Called on activation or the frame that a correct response returns the marker to blue.
        visualScheduledClock = clock + interval;
        visualScheduledReal = real + visualScheduledClock - clock;
        visualScheduledRight = visualRandom.Next(2) == 1;
        visualWaitingForOnset = true;
        visualTrial.ScheduleStimulus(visualScheduledRight ? "Right" : "Left", visualScheduledRight ? 1 : -1,
            visualScheduledClock, visualScheduledReal, clock, real, visualRawPedal, true);
    }
    private void UpdateVisualSubtask()
    {
        if (!VisualSubtaskEnabled || !AuditoryExperimentActive)
        {
            if (visualRunning) StopVisualSubtask(VisualSubtaskEnabled ? "ExperimentInactive" : "InspectorDisabled");
            return;
        }
        if (!visualRunning) StartVisualSubtask();
        if (!visualRunning) return;
        HandleVisualPause(ExperimentPauseManager.IsPaused);
        if (!visualRunning || visualPaused) return;
        try
        {
            ValidateVisualVisibility();
            visualRawPedal = Input.GetAxisRaw(visualRunSettings.pedalAxisName);
            double clock = ExperimentPauseManager.ActiveRealtime, real = Time.realtimeSinceStartupAsDouble;
            if (visualWaitingForOnset && clock >= visualScheduledClock)
            {
                visualWaitingForOnset = false;
                SetVisualColors(!visualScheduledRight, visualScheduledRight);
                visualTrial.Present(clock, real, visualRawPedal);
            }
            visualTrial.Tick(clock, real, visualRawPedal);
            if (!visualTrial.HasPendingTrial) ScheduleVisualTrial();
        }
        catch (Exception exception) { FailVisualSubtask(exception.Message); }
    }
    private void HandleVisualPause(bool paused)
    {
        if (!SimulatorStartManager.IsOperationEnabled && visualRunning) { StopVisualSubtask("SimulatorStopped"); return; }
        if (!visualRunning || visualPaused == paused) return;
        visualPaused = paused;
        double clock = ExperimentPauseManager.ActiveRealtime, real = Time.realtimeSinceStartupAsDouble;
        if (paused)
        {
            visualPauseStartedReal = real;
            visualTrial.Pause(clock, real, visualRawPedal);
            EmitVisualLifecycle("SubtaskPaused", "GlobalPause");
        }
        else
        {
            if (visualWaitingForOnset) visualScheduledReal += Math.Max(0, real - visualPauseStartedReal);
            visualTrial.Resume(clock, real, visualRawPedal);
            EmitVisualLifecycle("SubtaskResumed", "");
            if (double.IsNaN(visualScheduledClock)) ScheduleVisualTrial();
        }
    }
    private void HandleVisualTrialEvent(TaskSwitchAuditoryEvent data)
    {
        visualLastTrialIndex = visualTrial.LastTrialIndex;
        if (data.EventType == "TrialFinished") SetVisualColors(false, false);
        PublishVisualEvent(data);
    }
    private void PublishVisualEvent(TaskSwitchAuditoryEvent data)
    {
        data.PlannedOnsetClock = visualScheduledClock; data.PlannedOnsetReal = visualScheduledReal;
        data.VisualLeftRed = visualLeftRed; data.VisualRightRed = visualRightRed;
        VisualSubtaskEventOccurred?.Invoke(data);
    }
    private void EmitVisualLifecycle(string type, string detail)
    {
        PublishVisualEvent(new TaskSwitchAuditoryEvent { EventType = type, Detail = detail,
            RealTime = Time.realtimeSinceStartupAsDouble, DspTime = ExperimentPauseManager.ActiveRealtime, RawPedal = visualRawPedal });
    }
    private void SetVisualColors(bool leftRed, bool rightRed)
    {
        visualLeftRed = leftRed; visualRightRed = rightRed;
        if (visualLeftIndicator != null) visualLeftIndicator.color = leftRed ? Color.red : Color.blue;
        if (visualRightIndicator != null) visualRightIndicator.color = rightRed ? Color.red : Color.blue;
    }
    private void SetVisualVisibility(bool visible)
    {
        foreach (Image image in new[] { visualLeftIndicator, visualRightIndicator })
            if (image != null) { image.raycastTarget = false; image.enabled = true; image.gameObject.SetActive(visible); }
    }
    private void ValidateVisualVisibility()
    {
        if (visualLeftIndicator == null || visualRightIndicator == null ||
            !visualLeftIndicator.gameObject.activeInHierarchy || !visualRightIndicator.gameObject.activeInHierarchy ||
            !visualLeftIndicator.enabled || !visualRightIndicator.enabled)
            throw new InvalidOperationException("Visual indicators are not active in the task-information UI");
    }
    private void StopVisualSubtask(string reason)
    {
        if (visualRunning)
        {
            visualTrial.Cancel(reason, ExperimentPauseManager.ActiveRealtime, Time.realtimeSinceStartupAsDouble, visualRawPedal);
            visualTrial.EventOccurred -= HandleVisualTrialEvent;
            EmitVisualLifecycle("SubtaskStopped", reason);
        }
        visualRunning = visualWaitingForOnset = false;
        SetVisualColors(false, false);
        SetVisualVisibility(false);
    }
    private void FailVisualSubtask(string reason)
    {
        StopVisualSubtask("ConfigurationError");
        visualSuppressed = true;
        Debug.LogError("Visual subtask disabled: " + reason);
        EmitVisualLifecycle("SubtaskError", reason);
    }
}

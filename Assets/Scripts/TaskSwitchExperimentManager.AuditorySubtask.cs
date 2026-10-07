using System;
using UnityEngine;

public partial class TaskSwitchExperimentManager
{
    // Retained only to migrate older scenes that used the auditory checkbox.
    [SerializeField, HideInInspector] private bool enableAuditorySubtask = false;
    [Header("Auditory Settings / 音サブタスク設定")]
    [SerializeField] private TaskSwitchAuditorySettings auditorySubtask = new TaskSwitchAuditorySettings();

    private TaskSwitchAuditorySettings auditoryRunSettings;
    private TaskSwitchAuditoryTrial auditoryTrial;
    private AudioSource auditoryAudio;
    private AudioClip auditoryLowClip, auditoryHighClip;
    private System.Random auditoryRandom;
    private bool auditoryRunning, auditoryPaused, auditorySuppressed;
    private int auditoryLastTrialIndex, auditoryActivationIndex;
    private double auditoryLastScheduledDsp = double.NaN;
    private float auditoryRawPedal;

    public bool AuditorySubtaskEnabled => SecondaryTaskMode == TaskSwitchSecondaryTaskMode.Auditory;
    public bool AuditorySubtaskRunning => auditoryRunning;
    public bool AuditoryPedalArmed => auditoryTrial != null && auditoryTrial.PedalArmed;
    public float AuditoryRawPedal => auditoryRawPedal;
    public string AuditoryPedalAxis => auditoryRunSettings != null ? auditoryRunSettings.pedalAxisName : auditorySubtask != null ? auditorySubtask.pedalAxisName : "";
    public int AuditoryActivationIndex => auditoryActivationIndex;
    public string AuditoryConfigurationJson => JsonUtility.ToJson(auditorySubtask);
    public string AuditoryRunConfigurationJson => auditoryRunSettings == null ? "" : JsonUtility.ToJson(auditoryRunSettings);
    public TaskSwitchAuditorySettings AuditoryRunSettings => auditoryRunSettings ?? auditorySubtask;
    public event Action<TaskSwitchAuditoryEvent> AuditorySubtaskEventOccurred;

    private bool AuditoryExperimentActive => CurrentState != TaskSwitchExperimentState.Idle && CurrentState != TaskSwitchExperimentState.Completed;
    private void SubscribeAuditoryPauseEvents() { ExperimentPauseManager.PauseStateChanged -= HandleAuditoryPause; ExperimentPauseManager.PauseStateChanged += HandleAuditoryPause; }
    private void UnsubscribeAuditoryPauseEvents() { ExperimentPauseManager.PauseStateChanged -= HandleAuditoryPause; }
    private void ResetAuditoryExperiment()
    {
        auditoryLastTrialIndex = auditoryActivationIndex = 0;
        auditorySuppressed = false;
    }
    // Logging can stop independently of the experiment; do not keep issuing unrecorded trials after Stop.
    public void SuspendAuditoryForLoggingStop() { SuspendSecondaryForLoggingStop(); }
    public void ResumeAuditoryForLoggingStart() { ResumeSecondaryForLoggingStart(); }
    private void StartAuditorySubtask()
    {
        if (!AuditorySubtaskEnabled || auditoryRunning || auditorySuppressed || !AuditoryExperimentActive || !SimulatorStartManager.IsOperationEnabled) return;
        // Freeze this activation's settings; changing parameters requires OFF -> ON or another experiment.
        try
        {
            if (auditorySubtask == null) throw new InvalidOperationException("Auditory settings are missing");
            auditoryRunSettings = JsonUtility.FromJson<TaskSwitchAuditorySettings>(JsonUtility.ToJson(auditorySubtask));
            int sampleRate = AudioSettings.outputSampleRate;
            string error = sampleRate <= 0 ? "Audio output is unavailable" : auditoryRunSettings.Validate(sampleRate);
            if (error != null) throw new InvalidOperationException(error);
            if (auditoryRunSettings.randomSeed == 0) auditoryRunSettings.randomSeed = Guid.NewGuid().GetHashCode();
            auditoryRandom = new System.Random(auditoryRunSettings.randomSeed);
            auditoryRawPedal = Input.GetAxisRaw(auditoryRunSettings.pedalAxisName);
            DisposeAuditoryClips();
            auditoryLowClip = CreateAuditoryTone("TaskSwitchLow", auditoryRunSettings.lowToneHz, sampleRate);
            auditoryHighClip = CreateAuditoryTone("TaskSwitchHigh", auditoryRunSettings.highToneHz, sampleRate);
            if (auditoryAudio == null) auditoryAudio = gameObject.AddComponent<AudioSource>();
            auditoryAudio.playOnAwake = false; auditoryAudio.loop = false; auditoryAudio.spatialBlend = 0f;
            auditoryAudio.volume = auditoryRunSettings.volume; auditoryAudio.pitch = 1f;
            auditoryAudio.priority = 0;
            auditoryAudio.ignoreListenerPause = false;
            auditoryTrial = new TaskSwitchAuditoryTrial(auditoryRunSettings, auditoryLastTrialIndex);
            auditoryTrial.EventOccurred += HandleAuditoryTrialEvent;
            auditoryLastScheduledDsp = double.NaN;
            auditoryRunning = true; auditoryPaused = ExperimentPauseManager.IsPaused || !SimulatorStartManager.IsOperationEnabled;
            auditoryActivationIndex++;
            EmitAuditoryLifecycle("SubtaskStarted", AuditoryRunConfigurationJson);
            if (!auditoryPaused) ScheduleAuditoryTrial();
        }
        catch (Exception exception) { FailAuditorySubtask(exception.Message); }
    }
    private AudioClip CreateAuditoryTone(string name, float hz, int sampleRate)
    {
        int count = Math.Max(1, (int)Math.Round(sampleRate * auditoryRunSettings.toneDurationSeconds));
        float[] samples = new float[count];
        int fade = Math.Max(1, (int)(sampleRate * 0.005));
        for (int i = 0; i < count; i++)
        {
            double envelope = Math.Min(1, Math.Min((double)i / fade, (double)(count - 1 - i) / fade));
            samples[i] = (float)(Math.Sin(2 * Math.PI * hz * i / sampleRate) * envelope);
        }
        AudioClip clip = AudioClip.Create(name, count, 1, sampleRate, false);
        if (!clip.SetData(samples, 0)) { Destroy(clip); throw new InvalidOperationException("Cannot initialize tone samples"); }
        return clip;
    }
    private void ScheduleAuditoryTrial()
    {
        double dsp = AudioSettings.dspTime, real = Time.realtimeSinceStartupAsDouble;
        double interval = auditoryRunSettings.minimumIntervalSeconds + auditoryRandom.NextDouble() *
            (auditoryRunSettings.maximumIntervalSeconds - auditoryRunSettings.minimumIntervalSeconds);
        double onset = double.IsNaN(auditoryLastScheduledDsp) ? dsp + interval : Math.Max(dsp + 0.1, auditoryLastScheduledDsp + interval);
        bool high = auditoryRandom.Next(2) == 1;
        auditoryAudio.clip = high ? auditoryHighClip : auditoryLowClip;
        auditoryAudio.PlayScheduled(onset);
        auditoryLastScheduledDsp = onset;
        auditoryTrial.Schedule(high, onset, real + onset - dsp, dsp, real, auditoryRawPedal);
    }
    private void UpdateAuditorySubtask()
    {
        if (!AuditorySubtaskEnabled || !AuditoryExperimentActive)
        {
            if (auditoryRunning) StopAuditorySubtask(AuditorySubtaskEnabled ? "ExperimentInactive" : "InspectorDisabled");
            return;
        }
        if (!auditoryRunning) StartAuditorySubtask();
        if (!auditoryRunning) return;
        bool paused = ExperimentPauseManager.IsPaused || !SimulatorStartManager.IsOperationEnabled;
        HandleAuditoryPause(paused);
        if (!auditoryRunning || auditoryPaused) return;
        try
        {
            auditoryRawPedal = Input.GetAxisRaw(auditoryRunSettings.pedalAxisName);
            auditoryTrial.Tick(AudioSettings.dspTime, Time.realtimeSinceStartupAsDouble, auditoryRawPedal);
            // Changing AudioSource.clip before the current tone ends would truncate an early-response tone.
            if (!auditoryTrial.HasPendingTrial && AudioSettings.dspTime >= auditoryLastScheduledDsp + auditoryRunSettings.toneDurationSeconds + 0.05)
                ScheduleAuditoryTrial();
        }
        catch (Exception exception) { FailAuditorySubtask(exception.Message); }
    }
    private void HandleAuditoryPause(bool paused)
    {
        if (!SimulatorStartManager.IsOperationEnabled && auditoryRunning)
        {
            StopAuditorySubtask("SimulatorStopped");
            return;
        }
        if (!auditoryRunning || auditoryPaused == paused) return;
        auditoryPaused = paused;
        if (paused)
        {
            auditoryTrial.Pause(AudioSettings.dspTime, Time.realtimeSinceStartupAsDouble, auditoryRawPedal);
            EmitAuditoryLifecycle("SubtaskPaused", "GlobalPause");
        }
        else
        {
            auditoryTrial.Resume(AudioSettings.dspTime, Time.realtimeSinceStartupAsDouble, auditoryRawPedal);
            EmitAuditoryLifecycle("SubtaskResumed", "");
            if (double.IsNaN(auditoryLastScheduledDsp)) ScheduleAuditoryTrial();
        }
    }
    private void HandleAuditoryTrialEvent(TaskSwitchAuditoryEvent data)
    {
        auditoryLastTrialIndex = auditoryTrial.LastTrialIndex;
        AuditorySubtaskEventOccurred?.Invoke(data);
    }
    private void EmitAuditoryLifecycle(string type, string detail)
    {
        AuditorySubtaskEventOccurred?.Invoke(new TaskSwitchAuditoryEvent { EventType = type, Detail = detail,
            RealTime = Time.realtimeSinceStartupAsDouble, DspTime = AudioSettings.dspTime, RawPedal = auditoryRawPedal });
    }
    private void StopAuditorySubtask(string reason)
    {
        if (auditoryAudio != null) auditoryAudio.Stop();
        if (!auditoryRunning) return;
        auditoryTrial.Cancel(reason, AudioSettings.dspTime, Time.realtimeSinceStartupAsDouble, auditoryRawPedal);
        auditoryTrial.EventOccurred -= HandleAuditoryTrialEvent;
        EmitAuditoryLifecycle("SubtaskStopped", reason);
        auditoryRunning = false;
    }
    private void FailAuditorySubtask(string reason)
    {
        StopAuditorySubtask("ConfigurationError");
        auditorySuppressed = true;
        Debug.LogError("Auditory subtask disabled: " + reason);
        EmitAuditoryLifecycle("SubtaskError", reason);
    }
    private void DisposeAuditoryClips()
    {
        if (auditoryLowClip != null) Destroy(auditoryLowClip);
        if (auditoryHighClip != null) Destroy(auditoryHighClip);
        auditoryLowClip = auditoryHighClip = null;
    }
    private void DisposeAuditorySubtask()
    {
        StopAuditorySubtask("ManagerDestroyed");
        UnsubscribeAuditoryPauseEvents();
        DisposeAuditoryClips();
        if (auditoryAudio != null) Destroy(auditoryAudio);
    }
}

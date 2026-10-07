using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Tobii.Gaming;

public partial class TaskSwitchExperimentCsvLogger
{
    [Serializable]
    private class GazeArea
    {
        public string label = "";
        public RectTransform rectangle = null;
        public Camera canvasCamera = null;
    }
    [Header("Task Switchセッション")]
    [SerializeField] private string participantId = "";
    [SerializeField] private string blockId = "";
    [SerializeField, Min(0.01f)] private float sampleIntervalSeconds = 0.05f;
    [Tooltip("上から優先。未設定・視線無効・領域外はUnknown。画面座標はTobii Viewport基準。")]
    [SerializeField] private GazeArea[] gazeAreas = new GazeArea[0];

    private const string IdentityHeader = "session_id,participant_id,block_id,experiment_run_index";
    private const string SampleHeader = IdentityHeader + ",sample_index,utc_timestamp,real_elapsed_s,simulation_elapsed_s,frame,switch_index,active_crane_index,state,input_locked,global_paused,display_mode,operation_enabled,pause_interval_index";
    private string sessionId;
    private string sessionDirectory;
    private string operatorFileLabel, sessionParticipantId, sessionBlockId;
    private int experimentRunIndex;
    private bool experimentHasStarted;
    private double sessionRealOrigin, sessionSimulationOrigin, nextSampleTime, lastSampleTime;
    private int sampleIndex, cycleInstanceIndex;
    private DisplayLayoutManager displayLayout;
    private ExperimentCsvFile sessionFile, stateFile, inputFile, gazeFile, switchFile, cycleFile;
    private readonly List<CraneWorkCycleController> subscribedCycles = new List<CraneWorkCycleController>();
    private readonly Dictionary<CraneWorkCycleController, CycleRecord> cycles = new Dictionary<CraneWorkCycleController, CycleRecord>();
    private TaskSwitchTimingSummary switchTiming;
    private int summarySwitchIndex;
    private string summarySwitchMethod, interruptionPhase, interruptionStep;
    private int interruptionCycle, interruptionBoardCount;
    private float interruptionStepElapsed;
    private bool interruptionLatched, targetHeldAtUnlock, sourceHeldAtUnlock;
    private int switchInvalidations, switchCurrentDrops;
    private Vector3 lastLoggedCommand;
    private float lastLoggedSpread;
    private int lastCommandCrane = -1;

    private class CycleRecord
    {
        public int instance, cycle, crane, switchIndex, run;
        public double started, monitoring, controlAvailable, globalPause, sampledUntil;
        public string firstPhase;
        public int steps, invalidations, currentDrops, rollbacks;
    }

    public string SessionDirectory => sessionDirectory;
    private double RealSeconds => Math.Max(0, Time.realtimeSinceStartupAsDouble - sessionRealOrigin);
    private double SimulationSeconds => Math.Max(0, Time.timeAsDouble - sessionSimulationOrigin);
    private bool InputLocked => craneOperationManager == null || craneOperationManager.IsOperationInputLocked;
    private string DisplayMode => displayLayout != null ? displayLayout.CurrentMode.ToString() : "Unknown";

    private string StartSession(string safeName, string label)
    {
        sessionId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + Guid.NewGuid().ToString("N");
        sessionDirectory = Path.Combine(saveFolderPath, safeName + "_" + sessionId);
        Directory.CreateDirectory(sessionDirectory);
        operatorFileLabel = label ?? "";
        sessionParticipantId = participantId;
        sessionBlockId = blockId;
        experimentRunIndex = 1;
        experimentHasStarted = false;
        sampleIndex = cycleInstanceIndex = 0;
        cycles.Clear();
        switchTiming = null;
        lastCommandCrane = -1;
        lastLoggedCommand = Vector3.zero;
        lastLoggedSpread = 0;
        sessionRealOrigin = Time.realtimeSinceStartupAsDouble;
        sessionSimulationOrigin = Time.timeAsDouble;
        nextSampleTime = lastSampleTime = 0;
        displayLayout = FindObjectOfType<DisplayLayoutManager>(true);
        sessionFile = Open("session", IdentityHeader + ",record_type,utc_timestamp,real_elapsed_s,operator_file_label,schema_version,unity_version,application_version,source_revision,scene,switch_method,source_crane_index,target_crane_index,target_runs_full_cycle,sample_interval_s,active_crane_count,movement_dead_zone,manager_configuration_json,operation_configuration_json,aoi_configuration_json,source_total_cycle_count,countdown_s,switch_schedule_csv,auditory_subtask_enabled,auditory_configuration_json,secondary_task_mode,visual_subtask_enabled,visual_configuration_json");
        stateFile = Open("crane_state", SampleHeader + ",crane_index,role,cycle_instance_id,cycle_number,phase,step,monitoring,cycle_running,cycle_paused,boundary_waiting,step_elapsed_s,condition_stable_s,position_condition_latched,position_exit_hysteresis,world_x,world_y,world_z,target_world_x,target_world_z,signed_error_x,signed_error_z,abs_error_x,abs_error_z,display_x,display_z,attached_board_count,attached_weight_kg,pickup_target_weight_kg,placement_remaining_target_kg,pickup_weight_error_kg,electric_current_a,required_current_a,lift_capacity_kg,safe_current_hold_active,maximum_current_a,safe_hold_release_threshold_a,step_configuration_json,display_weight_ton,x_highlighted,z_highlighted,weight_highlighted,achievement_frame,display_align_tolerance,x_within_display_tolerance,z_within_display_tolerance");
        inputFile = Open("input", SampleHeader + ",input_mode,movement_dead_zone,joystick2_horizontal,joystick2_vertical,joystick3_vertical,joystick3_slider,slider_axis,raw_current_slider,requested_current_a,requested_move_x,requested_move_y,requested_move_z,requested_spread,last_accepted_move_x,last_accepted_move_y,last_accepted_move_z,last_accepted_spread,accepted_command_frame,command_accepted_this_frame,applied_current_a,safe_current_hold_active,confirmation_button,detach_button,auditory_subtask_enabled,auditory_subtask_running,pedal_axis,raw_pedal,pedal_armed,secondary_task_mode,visual_subtask_enabled,visual_subtask_running,visual_left_red,visual_right_red,pedal_axis_raw,pedal_positive_key,pedal_negative_key,pedal_input_source,pedal_input_conflict,plus_minus_keys_enabled");
        gazeFile = Open("gaze", SampleHeader + ",is_connected,app_focused,is_valid,game_screen_x,game_screen_y,clamped_game_screen_x,clamped_game_screen_y,viewport_x,viewport_y,raw_screen_x,raw_screen_y,screen_width,screen_height,aoi,aoi_layout_json");
        switchFile = Open("switch_summary", IdentityHeader + ",switch_index,switch_method,outcome,logical_completed,source_input_observed,source_phase_at_suspend,source_step_at_suspend,source_cycle_at_suspend,step_elapsed_at_suspend_s,position_latched_at_suspend,board_count_at_suspend,target_input_held_at_unlock,source_input_held_at_unlock,weight_invalidations,current_drop_events," + TaskSwitchTimingSummary.Header);
        cycleFile = Open("cycle_summary", IdentityHeader + ",cycle_instance_id,crane_index,cycle_number,switch_index_at_start,first_phase,outcome,start_s,end_s,elapsed_s,monitoring_s,control_available_s,global_pause_s,steps_completed,weight_invalidations,current_drop_events,rollbacks,duration_sampled_until_s");
        OpenAuditoryFiles();
        OpenVisualFiles();
        OpenPauseFile();
        WriteSessionMetadata("LoggingStarted");
        return Path.Combine(sessionDirectory, "events.csv");
    }

    private ExperimentCsvFile Open(string name, string header)
    {
        return new ExperimentCsvFile(Path.Combine(sessionDirectory, name + ".csv"), header);
    }
    private object[] Identity(int? run = null)
    {
        return new object[] { sessionId, sessionParticipantId, sessionBlockId, run ?? experimentRunIndex };
    }
    private object[] Join(object[] prefix, params object[] cells)
    {
        object[] row = new object[prefix.Length + cells.Length];
        Array.Copy(prefix, row, prefix.Length);
        Array.Copy(cells, 0, row, prefix.Length, cells.Length);
        return row;
    }
    private string sampleUtc;
    private double sampleReal, sampleSimulation;
    private object[] SampleContext(double sampleTime)
    {
        sampleUtc = DateTime.UtcNow.ToString("O");
        sampleReal = sampleTime;
        sampleSimulation = SimulationSeconds;
        return Join(Identity(), sampleIndex, sampleUtc, sampleReal, sampleSimulation,
            Time.frameCount, taskSwitchExperimentManager.CurrentSwitchIndex, GetSelectedCraneIndex(),
            taskSwitchExperimentManager.CurrentState, InputLocked, ExperimentPauseManager.IsPaused, DisplayMode, SimulatorStartManager.IsOperationEnabled, ActivePauseInterval);
    }
    private void WriteSessionMetadata(string type)
    {
        if (sessionFile == null) return;
        sessionFile.Write(Join(Identity(), type, DateTime.UtcNow.ToString("O"), RealSeconds, operatorFileLabel,
            7, Application.unityVersion, Application.version, BuildRevision,
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            taskSwitchExperimentManager.SwitchMethod, taskSwitchExperimentManager.SourceCondition.craneIndex,
            taskSwitchExperimentManager.TargetCondition.craneIndex, taskSwitchExperimentManager.TargetRunsFullCycle,
            sampleIntervalSeconds, craneRegistry != null ? craneRegistry.ActiveCraneCount : 0,
            craneOperationManager != null ? (object)craneOperationManager.MovementDeadZone : null,
            JsonUtility.ToJson(taskSwitchExperimentManager), craneOperationManager != null ? JsonUtility.ToJson(craneOperationManager) : "",
            JsonUtility.ToJson(new GazeAreaConfiguration { areas = gazeAreas }), taskSwitchExperimentManager.SourceTotalCycleCount,
            taskSwitchExperimentManager.CountdownSeconds, taskSwitchExperimentManager.SwitchScheduleCsvText,
            taskSwitchExperimentManager.AuditorySubtaskEnabled, taskSwitchExperimentManager.AuditoryConfigurationJson,
            taskSwitchExperimentManager.SecondaryTaskMode, taskSwitchExperimentManager.VisualSubtaskEnabled, taskSwitchExperimentManager.VisualConfigurationJson));
        sessionFile.Flush();
    }
    // Set at build time if a release embeds its Git revision; empty means unavailable.
    [SerializeField] private string sourceRevision = "";
    private string BuildRevision => sourceRevision;
    [Serializable] private class GazeAreaConfiguration { public GazeArea[] areas; }

    private CraneWorkCycleController GetCycleController(int index)
    {
        CraneInstance crane = craneRegistry != null ? craneRegistry.GetCraneByRuntimeIndex(index) : null;
        return crane != null ? crane.GetComponentInChildren<CraneWorkCycleController>(true) : null;
    }
    private int CycleNumber(int index)
    {
        CraneWorkCycleController c = GetCycleController(index);
        CycleRecord r;
        return c != null ? (cycles.TryGetValue(c, out r) ? r.cycle : c.CurrentCycleNumber) : 0;
    }
    private int GetCraneIndex(CraneWorkCycleController controller)
    {
        if (craneRegistry != null)
            for (int i = 0; i < craneRegistry.ActiveCraneCount; i++)
                if (GetCycleController(i) == controller) return i;
        return -1;
    }

    private void SubscribeSessionEvents()
    {
        SubscribePauseLogging();
        taskSwitchExperimentManager.AuditorySubtaskEventOccurred += HandleAuditoryEvent;
        taskSwitchExperimentManager.VisualSubtaskEventOccurred += HandleVisualEvent;
        if (craneOperationManager != null) craneOperationManager.MovementInputAccepted += HandleMovementAccepted;
        foreach (CraneWorkPhaseTracker t in subscribedTrackers) t.PickupWeightInvalidated += HandleWeightInvalidated;
        foreach (LifMagSystem l in subscribedLifMagSystems)
        {
            l.ElectricCurrentInputAccepted += HandleCurrentAccepted;
            l.BoardAttachmentChanged += HandleBoardChanged;
        }
        if (craneRegistry == null) return;
        for (int i = 0; i < craneRegistry.ActiveCraneCount; i++)
        {
            CraneWorkCycleController c = GetCycleController(i);
            if (c == null || subscribedCycles.Contains(c)) continue;
            c.PhaseStarted += HandleCyclePhaseStarted;
            c.PhaseCompleted += HandleCyclePhaseCompleted;
            c.CycleCompleted += HandleCycleCompleted;
            c.PickupWeightRollback += HandleRollback;
            subscribedCycles.Add(c);
            if (c.IsRunning) BeginCycle(c, c.CurrentPhase, c.CurrentCycleNumber);
        }
    }
    private void UnsubscribeSessionEvents()
    {
        ExperimentPauseManager.PauseStateChanged -= HandleGlobalPauseChanged;
        if (taskSwitchExperimentManager != null) taskSwitchExperimentManager.AuditorySubtaskEventOccurred -= HandleAuditoryEvent;
        if (taskSwitchExperimentManager != null) taskSwitchExperimentManager.VisualSubtaskEventOccurred -= HandleVisualEvent;
        if (craneOperationManager != null) craneOperationManager.MovementInputAccepted -= HandleMovementAccepted;
        foreach (CraneWorkPhaseTracker t in subscribedTrackers) if (t != null) t.PickupWeightInvalidated -= HandleWeightInvalidated;
        foreach (LifMagSystem l in subscribedLifMagSystems) if (l != null)
        {
            l.ElectricCurrentInputAccepted -= HandleCurrentAccepted;
            l.BoardAttachmentChanged -= HandleBoardChanged;
        }
        foreach (CraneWorkCycleController c in subscribedCycles) if (c != null)
        {
            c.PhaseStarted -= HandleCyclePhaseStarted;
            c.PhaseCompleted -= HandleCyclePhaseCompleted;
            c.CycleCompleted -= HandleCycleCompleted;
            c.PickupWeightRollback -= HandleRollback;
        }
        subscribedCycles.Clear();
    }
    private void StopSession()
    {
        EndPauseInterval(false, "LoggingStoppedWhilePaused");
        if (taskSwitchExperimentManager != null) taskSwitchExperimentManager.SuspendSecondaryForLoggingStop();
        WriteSessionMetadata("LoggingStopped");
        FinishSwitch("LoggingStopped");
        foreach (var pair in cycles) WriteCycle(pair.Value, "Incomplete");
        cycles.Clear();
        DisposeSessionFiles();
    }
    private void DisposeSessionFiles()
    {
        foreach (ExperimentCsvFile f in new[] { sessionFile, stateFile, inputFile, gazeFile, switchFile, cycleFile, auditoryEventFile, auditoryTrialFile, visualEventFile, visualTrialFile, pauseFile })
            if (f != null) f.Dispose();
        sessionFile = stateFile = inputFile = gazeFile = switchFile = cycleFile = null;
        auditoryEventFile = auditoryTrialFile = null;
        visualEventFile = visualTrialFile = null;
        pauseFile = null;
    }
    private void FlushSessionSamples()
    {
        foreach (ExperimentCsvFile f in new[] { stateFile, inputFile, gazeFile }) if (f != null) f.Flush();
    }

    private void ObserveEvent(string name, int craneIndex, double eventTime)
    {
        if (name == "ExperimentPreparing")
        {
            if (experimentHasStarted)
            {
                FinishSwitch("ExperimentRestarted");
                foreach (var pair in cycles) WriteCycle(pair.Value, "Incomplete");
                cycles.Clear();
                experimentRunIndex++;
            }
            experimentHasStarted = true;
        }
        if (name == "ExperimentStarted")
        {
            // StartExperiment initializes trackers synchronously before emitting ExperimentStarted.
            foreach (CraneWorkCycleController c in subscribedCycles)
                if (c != null && c.IsRunning && !cycles.ContainsKey(c)) BeginCycle(c, c.CurrentPhase, c.CurrentCycleNumber);
            WriteSessionMetadata(name);
        }
        if (name == "SwitchRequested")
        {
            FinishSwitch("NextSwitchRequested");
            switchTiming = new TaskSwitchTimingSummary();
            summarySwitchIndex = taskSwitchExperimentManager.CurrentSwitchIndex;
            summarySwitchMethod = taskSwitchExperimentManager.SwitchMethod.ToString();
            interruptionPhase = interruptionStep = "";
            interruptionCycle = interruptionBoardCount = 0;
            interruptionStepElapsed = float.NaN;
            interruptionLatched = targetHeldAtUnlock = sourceHeldAtUnlock = false;
            switchInvalidations = switchCurrentDrops = 0;
        }
        if (switchTiming != null)
        {
            switchTiming.Mark(name, eventTime);
            if (name == "SourceWorkSuspended")
            {
                int source = taskSwitchExperimentManager.SourceCondition.craneIndex;
                CraneWorkPhaseTracker tracker = GetWorkPhaseTracker(source);
                interruptionPhase = tracker != null ? tracker.CurrentMajorPhase.ToString() : "";
                interruptionStep = tracker != null ? tracker.CurrentStepId : "";
                interruptionStepElapsed = tracker != null ? tracker.StepElapsedSeconds : float.NaN;
                interruptionLatched = tracker != null && tracker.PositionConditionLatched;
                interruptionCycle = CycleNumber(source);
                CraneInstance crane = craneRegistry != null ? craneRegistry.GetCraneByRuntimeIndex(source) : null;
                interruptionBoardCount = crane != null && crane.LifMagSystem != null ? crane.LifMagSystem.AttachedBoards.Count : 0;
            }
            if (name == "TargetInputUnlocked") targetHeldAtUnlock = IsInputHeldAtUnlock();
            if (name == "SourceInputUnlocked") sourceHeldAtUnlock = IsInputHeldAtUnlock();
            if (name == "PickupWeightInvalidated") switchInvalidations++;
            if (name == "BoardDetachedInsufficientCurrent") switchCurrentDrops++;
        }
        CraneWorkCycleController controller = GetCycleController(craneIndex);
        CycleRecord record;
        if (controller != null && cycles.TryGetValue(controller, out record))
        {
            if (name == "CycleStarted") { record.started = eventTime; record.sampledUntil = eventTime; }
            if (name == "StepCompleted") record.steps++;
            if (name == "PickupWeightInvalidated") record.invalidations++;
            if (name == "BoardDetachedInsufficientCurrent") record.currentDrops++;
            if (name == "PickupWeightRollback") record.rollbacks++;
        }
    }
    private bool IsInputHeldAtUnlock()
    {
        Vector3 command = craneOperationManager != null ? craneOperationManager.ReadMovementCommand() : Vector3.zero;
        CraneInstance crane = craneOperationManager != null ? craneOperationManager.CurrentCraneInstance : null;
        LifMagSystem l = crane != null ? crane.LifMagSystem : null;
        return command.sqrMagnitude > 0 || (craneOperationManager != null && Mathf.Abs(craneOperationManager.ReadSpreadCommand()) > 0.001f) ||
            (l != null && l.ReadSliderCurrentAmpere() > 0.01f);
    }
    private void FinishSwitch(string reason)
    {
        if (switchTiming == null || switchFile == null) return;
        string outcome = switchTiming.LogicalCompletion
            ? (switchTiming.SourceInput ? "Completed" : "CompletedWithoutSourceInput") : "Incomplete:" + reason;
        object[] metadata = Join(Identity(), summarySwitchIndex, summarySwitchMethod, outcome, switchTiming.LogicalCompletion,
            switchTiming.SourceInput, interruptionPhase, interruptionStep, interruptionCycle, interruptionStepElapsed,
            interruptionLatched, interruptionBoardCount, targetHeldAtUnlock, sourceHeldAtUnlock, switchInvalidations, switchCurrentDrops);
        switchFile.Write(Join(metadata, switchTiming.Cells()));
        switchFile.Flush();
        switchTiming = null;
    }
    private void HandleMovementAccepted(CraneOperationManager manager, Vector3 command, float spread)
    {
        string detail = "x=" + ExperimentCsvFile.Encode(command.x) + ";y=" + ExperimentCsvFile.Encode(command.y) +
            ";z=" + ExperimentCsvFile.Encode(command.z) + ";spread=" + ExperimentCsvFile.Encode(spread);
        if (isLogging && (lastCommandCrane != manager.CurrentCraneIndex ||
            (command - lastLoggedCommand).sqrMagnitude >= 0.000001f || Mathf.Abs(spread - lastLoggedSpread) >= 0.001f))
        {
            WriteEvent("Input", "MovementCommandChanged", manager.CurrentCraneIndex, null, "", detail);
            lastCommandCrane = manager.CurrentCraneIndex;
            lastLoggedCommand = command;
            lastLoggedSpread = spread;
        }
        if (command.sqrMagnitude > 0 || Mathf.Abs(spread) > 0.001f)
            MarkEffectiveInput(manager.CurrentCraneIndex, "Movement", detail);
    }
    private void HandleCurrentAccepted(LifMagSystem l, float current)
    {
        int index = GetCraneIndex(l);
        string detail = "current_a=" + ExperimentCsvFile.Encode(current);
        WriteEvent("Input", "ElectricCurrentInputAccepted", index, null, "", detail);
        MarkEffectiveInput(index, "ElectricCurrent", detail);
    }
    private void MarkEffectiveInput(int index, string kind, string detail)
    {
        if (!isLogging || InputLocked || ExperimentPauseManager.IsPaused || !SimulatorStartManager.IsOperationEnabled || switchTiming == null) return;
        string marker = null;
        if (index == taskSwitchExperimentManager.TargetCondition.craneIndex && !double.IsNaN(switchTiming.At("TargetInputUnlocked")))
            marker = "FirstEffectiveTargetInput";
        if (index == taskSwitchExperimentManager.SourceCondition.craneIndex && !double.IsNaN(switchTiming.At("SourceInputUnlocked")))
            marker = "FirstEffectiveSourceInput";
        if (marker == null || !double.IsNaN(switchTiming.At(marker))) return;
        CraneWorkPhaseTracker t = GetWorkPhaseTracker(index);
        WriteEvent("Input", marker, index, t != null ? (CraneStatusManager.WorkPhase?)t.CurrentMajorPhase : null,
            t != null ? t.CurrentStepId : "", "kind=" + kind + ";" + detail);
        if (marker == "FirstEffectiveSourceInput" && switchTiming.LogicalCompletion) FinishSwitch("SourceInput");
    }
    private void HandleBoardChanged(LifMagSystem l, string name, string board)
    {
        int index = GetCraneIndex(l);
        CraneWorkPhaseTracker t = GetWorkPhaseTracker(index);
        WriteEvent("Attachment", name, index, t != null ? (CraneStatusManager.WorkPhase?)t.CurrentMajorPhase : null,
            t != null ? t.CurrentStepId : "", board);
        if (name == "BoardAttached" || name == "BoardsDetached") MarkEffectiveInput(index, "Attachment", name);
    }
    private void HandleWeightInvalidated(CraneWorkPhaseTracker t, CraneStatusManager.WorkPhase phase, string step)
    {
        WriteEvent("WorkPhase", "PickupWeightInvalidated", GetCraneIndex(t), phase, step,
            "weight_error_kg=" + ExperimentCsvFile.Encode(t.LastInvalidationWeightErrorKg));
    }
    private void HandleRollback(CraneWorkCycleController c, string detail)
    {
        WriteEvent("Cycle", "PickupWeightRollback", GetCraneIndex(c), c.CurrentPhase, "", detail);
    }
    private void BeginCycle(CraneWorkCycleController c, CraneStatusManager.WorkPhase phase, int number)
    {
        CycleRecord old;
        if (cycles.TryGetValue(c, out old)) WriteCycle(old, "Incomplete:Restarted");
        cycles[c] = new CycleRecord { instance = ++cycleInstanceIndex, cycle = number, crane = GetCraneIndex(c),
            run = experimentRunIndex, switchIndex = taskSwitchExperimentManager.CurrentSwitchIndex, started = RealSeconds, sampledUntil = RealSeconds, firstPhase = phase.ToString() };
        WriteEvent("Cycle", "CycleStarted", GetCraneIndex(c), phase, "", "cycle_number=" + number);
    }
    private void HandleCyclePhaseStarted(CraneWorkCycleController c, CraneStatusManager.WorkPhase phase, int number)
    {
        CycleRecord record;
        if (!cycles.TryGetValue(c, out record) || record.cycle != number) BeginCycle(c, phase, number);
        WriteEvent("Cycle", "CyclePhaseStarted", GetCraneIndex(c), phase, "", "cycle_number=" + number);
    }
    private void HandleCyclePhaseCompleted(CraneWorkCycleController c, CraneStatusManager.WorkPhase phase, int number)
    {
        WriteEvent("Cycle", "CyclePhaseCompleted", GetCraneIndex(c), phase, "", "cycle_number=" + number);
    }
    private void HandleCycleCompleted(CraneWorkCycleController c, int number)
    {
        CycleRecord record;
        WriteEvent("Cycle", "CycleCompleted", GetCraneIndex(c), c.CurrentPhase, "", "completed_cycle_number=" + number);
        if (cycles.TryGetValue(c, out record)) { WriteCycle(record, "Completed"); cycles.Remove(c); }
    }
    private void WriteCycle(CycleRecord r, string outcome)
    {
        if (cycleFile == null) return;
        WriteEvent("Cycle", "CycleSummaryClosed", r.crane, null, "", outcome);
        double end = lastWrittenEventRealSeconds;
        cycleFile.Write(Join(Identity(r.run), r.instance, r.crane, r.cycle, r.switchIndex, r.firstPhase, outcome,
            r.started, end, end - r.started, r.monitoring, r.controlAvailable, r.globalPause,
            r.steps, r.invalidations, r.currentDrops, r.rollbacks, r.sampledUntil));
        cycleFile.Flush();
    }

    private void LateUpdate()
    {
        if (!isLogging || stateFile == null || craneRegistry == null) return;
        try { SampleSession(); }
        catch (Exception exception)
        {
            Debug.LogError("Task Switch CSVの記録中にエラーが発生しました: " + exception.Message);
            StopLogging();
        }
    }
    private void SampleSession()
    {
        double now = RealSeconds;
        if (now < nextSampleTime) return;
        nextSampleTime = now + Math.Max(0.01f, sampleIntervalSeconds);
        // Use the same right-endpoint samples exported below, so durations can be rebuilt from crane_state.csv.
        foreach (var pair in cycles)
        {
            CraneWorkCycleController c = pair.Key;
            CraneWorkPhaseTracker t = GetWorkPhaseTracker(pair.Value.crane);
            double delta = Math.Max(0, now - Math.Max(lastSampleTime, pair.Value.started));
            if (ExperimentPauseManager.IsPaused) pair.Value.globalPause += delta;
            else if (c != null && c.IsRunning && !c.IsPaused && !c.IsWaitingAtBoundary && t != null && t.IsMonitoring)
            {
                pair.Value.monitoring += delta;
                if (!InputLocked && SimulatorStartManager.IsOperationEnabled && pair.Value.crane == GetSelectedCraneIndex()) pair.Value.controlAvailable += delta;
            }
            pair.Value.sampledUntil = now;
        }
        lastSampleTime = now;
        object[] context = SampleContext(now);
        for (int i = 0; i < craneRegistry.ActiveCraneCount; i++) WriteCraneState(context, i);
        WriteInput(context);
        WriteGaze(context);
        sampleIndex++;
        if (sampleIndex % 20 == 0) FlushSessionSamples();
    }
    private void WriteCraneState(object[] context, int index)
    {
        CraneInstance c = craneRegistry.GetCraneByRuntimeIndex(index);
        CraneWorkPhaseTracker t = GetWorkPhaseTracker(index);
        CraneWorkCycleController cycle = GetCycleController(index);
        CycleRecord r = null;
        if (cycle != null) cycles.TryGetValue(cycle, out r);
        LifMagSystem l = c != null ? c.LifMagSystem : null;
        CraneWorkLoadPlanManager plan = c != null ? c.GetComponentInChildren<CraneWorkLoadPlanManager>(true) : null;
        Vector3? p = c != null && c.InformationTarget != null ? (Vector3?)c.InformationTarget.position : null;
        CraneInformationDisplay display = index == GetSelectedCraneIndex() && craneOperationManager != null
            ? craneOperationManager.CurrentInformationDisplay : null;
        float tx = 0, tz = 0;
        bool target = t != null && t.TryGetTargetPosition(out tx, out tz);
        stateFile.Write(Join(context, index, GetCraneRole(index, taskSwitchExperimentManager.SourceCondition.craneIndex, taskSwitchExperimentManager.TargetCondition.craneIndex),
            r != null ? (object)r.instance : null, cycle != null ? (object)cycle.CurrentCycleNumber : null,
            t != null ? t.CurrentMajorPhase.ToString() : "", t != null ? t.CurrentStepId : "", t != null && t.IsMonitoring,
            cycle != null && cycle.IsRunning, cycle != null && cycle.IsPaused, cycle != null && cycle.IsWaitingAtBoundary,
            t != null ? (object)t.StepElapsedSeconds : null, t != null ? (object)t.ConditionStableSeconds : null,
            t != null && t.PositionConditionLatched, t != null ? (object)t.PositionExitHysteresis : null,
            p.HasValue ? (object)p.Value.x : null, p.HasValue ? (object)p.Value.y : null, p.HasValue ? (object)p.Value.z : null,
            target ? (object)tx : null, target ? (object)tz : null,
            target && p.HasValue ? (object)(p.Value.x - tx) : null, target && p.HasValue ? (object)(p.Value.z - tz) : null,
            target && p.HasValue ? (object)Mathf.Abs(p.Value.x - tx) : null, target && p.HasValue ? (object)Mathf.Abs(p.Value.z - tz) : null,
            p.HasValue ? (object)(p.Value.x + 20f) : null, p.HasValue ? (object)(250f - p.Value.z) : null,
            l != null ? (object)l.AttachedBoards.Count : null, l != null ? (object)l.GetAttachedTotalWeightKgForDisplay() : null,
            plan != null && plan.HasPickupTarget ? (object)plan.PickupTargetWeightKg : null,
            plan != null && plan.HasPlacementPlan ? (object)plan.TargetRemainingWeightKg : null,
            plan != null && plan.HasPickupTarget && l != null ? (object)(l.GetAttachedTotalWeightKgForDisplay() - plan.PickupTargetWeightKg) : null,
            l != null ? (object)l.CurrentElectricCurrentA : null,
            l != null ? (object)l.CurrentRequiredCurrentA : null, l != null ? (object)l.CurrentLiftCapacityKg : null,
            l != null && l.IsTaskSwitchSafeCurrentHoldActive, l != null ? (object)l.MaximumCurrentAmpere : null,
            l != null ? (object)l.SafeHoldReleaseCurrentAmpere : null, t != null ? t.CurrentStepConfigurationJson : "",
            display != null ? (object)display.CurrentDisplayWeightTon : null, display != null ? (object)display.CurrentXHighlighted : null,
            display != null ? (object)display.CurrentZHighlighted : null, display != null ? (object)display.CurrentWeightHighlighted : null,
            display != null ? (object)display.AchievementFrame : null, display != null ? (object)display.FineAlignTolerance : null,
            display != null && target && p.HasValue ? (object)(Mathf.Abs(p.Value.x - tx) <= display.FineAlignTolerance) : null,
            display != null && target && p.HasValue ? (object)(Mathf.Abs(p.Value.z - tz) <= display.FineAlignTolerance) : null));
    }
    private void WriteInput(object[] context)
    {
        CraneInstance c = craneOperationManager != null ? craneOperationManager.CurrentCraneInstance : null;
        LifMagSystem l = c != null ? c.LifMagSystem : null;
        Vector3 command = craneOperationManager != null ? craneOperationManager.ReadMovementCommand() : Vector3.zero;
        Vector3 accepted = craneOperationManager != null ? craneOperationManager.LastAcceptedMovement : Vector3.zero;
        int acceptedFrame = craneOperationManager != null ? craneOperationManager.LastAcceptedInputFrame : -1;
        bool hasPedalSample = taskSwitchExperimentManager.SecondarySubtaskRunning && !ExperimentPauseManager.IsPaused && SimulatorStartManager.IsOperationEnabled;
        TaskSwitchPedalSample pedalSample = taskSwitchExperimentManager.SecondaryPedalSample;
        inputFile.Write(Join(context, craneOperationManager != null ? craneOperationManager.MovementInputMode : "Unknown",
            craneOperationManager != null ? (object)craneOperationManager.MovementDeadZone : null,
            Input.GetAxis("JoyStick2Horizontal"), Input.GetAxis("JoyStick2Vertical"), Input.GetAxis("JoyStick3Vertical"), Input.GetAxis("JoyStick3Slider"),
            l != null ? l.CurrentSliderAxis : "", l != null ? (object)l.ReadRawSliderInput() : null, l != null ? (object)l.ReadSliderCurrentAmpere() : null,
            command.x, command.y, command.z, craneOperationManager != null ? (object)craneOperationManager.ReadSpreadCommand() : null,
            accepted.x, accepted.y, accepted.z, craneOperationManager != null ? (object)craneOperationManager.LastAcceptedSpread : null,
            acceptedFrame, acceptedFrame == Time.frameCount, l != null ? (object)l.CurrentElectricCurrentA : null,
            l != null && l.IsTaskSwitchSafeCurrentHoldActive, Input.GetButton("JoyStick2RedButton"), Input.GetButton("JoyStick2BlackButton"),
            taskSwitchExperimentManager.AuditorySubtaskEnabled, taskSwitchExperimentManager.AuditorySubtaskRunning,
            taskSwitchExperimentManager.SecondaryPedalAxis,
            taskSwitchExperimentManager.SecondarySubtaskRunning && !ExperimentPauseManager.IsPaused && SimulatorStartManager.IsOperationEnabled ? (object)taskSwitchExperimentManager.SecondaryRawPedal : null,
            taskSwitchExperimentManager.SecondarySubtaskRunning && taskSwitchExperimentManager.SecondaryPedalArmed,
            taskSwitchExperimentManager.SecondaryTaskMode, taskSwitchExperimentManager.VisualSubtaskEnabled,
            taskSwitchExperimentManager.VisualSubtaskRunning, taskSwitchExperimentManager.VisualLeftRed, taskSwitchExperimentManager.VisualRightRed,
            hasPedalSample ? (object)pedalSample.AxisRaw : null, hasPedalSample ? (object)pedalSample.PositiveKey : null,
            hasPedalSample ? (object)pedalSample.NegativeKey : null, hasPedalSample ? pedalSample.Source : "",
            hasPedalSample ? (object)pedalSample.ConflictingKeys : null, taskSwitchExperimentManager.SecondaryPlusMinusKeysEnabled));
    }
    [Serializable] private class GazeLayout { public List<GazeRectangle> areas = new List<GazeRectangle>(); }
    [Serializable] private class GazeRectangle { public string label; public bool active; public Vector2[] screenCorners; }
    private string CaptureGazeLayout()
    {
        GazeLayout layout = new GazeLayout();
        if (gazeAreas != null) foreach (GazeArea area in gazeAreas)
        {
            if (area == null || area.rectangle == null) continue;
            Vector3[] world = new Vector3[4];
            area.rectangle.GetWorldCorners(world);
            Vector2[] screen = new Vector2[4];
            for (int i = 0; i < 4; i++) screen[i] = RectTransformUtility.WorldToScreenPoint(area.canvasCamera, world[i]);
            layout.areas.Add(new GazeRectangle { label = area.label, active = area.rectangle.gameObject.activeInHierarchy, screenCorners = screen });
        }
        return JsonUtility.ToJson(layout);
    }
    private void WriteGaze(object[] context)
    {
        GazePoint g = TobiiTrackedGameView.GetGazePoint();
        Vector2 v = g.IsValid ? g.Viewport : Vector2.zero;
        Vector2 raw = g.IsValid ? g.Screen : Vector2.zero;
        Vector2 screen = new Vector2(v.x * Screen.width, v.y * Screen.height);
        string aoi = "Unknown";
        if (g.IsValid && gazeAreas != null) foreach (GazeArea area in gazeAreas)
            if (area != null && area.rectangle != null && area.rectangle.gameObject.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(area.rectangle, screen, area.canvasCamera))
            { aoi = string.IsNullOrEmpty(area.label) ? "Unknown" : area.label; break; }
        gazeFile.Write(Join(context, TobiiAPI.IsConnected, Application.isFocused, g.IsValid,
            g.IsValid ? (object)screen.x : null, g.IsValid ? (object)screen.y : null,
            g.IsValid ? (object)Mathf.Clamp(screen.x, 0, Screen.width) : null, g.IsValid ? (object)Mathf.Clamp(screen.y, 0, Screen.height) : null,
            g.IsValid ? (object)v.x : null, g.IsValid ? (object)v.y : null, g.IsValid ? (object)raw.x : null, g.IsValid ? (object)raw.y : null,
            Screen.width, Screen.height, aoi, CaptureGazeLayout()));
    }
}

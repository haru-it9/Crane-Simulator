using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Task Switch実験の状態遷移、実作業ステップ、安全電流保持を
/// 1つのイベントCSVへ時系列で記録します。
/// </summary>
[DisallowMultipleComponent]
public class TaskSwitchExperimentCsvLogger : MonoBehaviour
{
    private struct CraneSnapshot
    {
        public bool hasCrane;
        public bool hasPosition;
        public float currentX;
        public float currentZ;
        public bool hasTarget;
        public float targetX;
        public float targetZ;
        public bool hasLifMag;
        public bool hasAttachedBoard;
        public float attachedWeightKg;
        public float electricCurrentA;
        public float requiredCurrentA;
        public bool safeCurrentHoldActive;
    }

    [Header("保存先フォルダ")]
    [SerializeField]
    private string saveFolderPath =
        @"C:\Users\harui\Git\Crane-Simulator\Assets\ExperimentData";

    [Header("Manager参照（未設定時は自動検索）")]
    [SerializeField]
    private TaskSwitchExperimentManager taskSwitchExperimentManager;

    [SerializeField]
    private CraneOperationManager craneOperationManager;

    [SerializeField]
    private CraneRegistry craneRegistry;

    [Header("書き込み設定")]
    [Tooltip("Task Switchイベントは重要度が高いため、既定では1行ごとにFlushします。")]
    [SerializeField]
    [Min(1)]
    private int flushEveryLines = 1;

    private readonly List<CraneWorkPhaseTracker> subscribedTrackers =
        new List<CraneWorkPhaseTracker>();

    private readonly List<LifMagSystem> subscribedLifMagSystems =
        new List<LifMagSystem>();

    private StreamWriter writer;
    private bool isLogging;
    private int eventIndex;
    private int linesSinceFlush;
    private float realStartTime;
    private float simulationStartTime;

    private float switchRequestedRealTime = float.NaN;
    private float targetOperationStartedRealTime = float.NaN;
    private float targetPhaseCompletedRealTime = float.NaN;
    private float sourceOperationResumedRealTime = float.NaN;

    public bool IsLogging => isLogging;

    private void Awake()
    {
        FindReferences();
    }

    public void StartLogging(string inputFileName)
    {
        if (isLogging || writer != null)
        {
            StopLogging();
        }

        FindReferences();

        if (taskSwitchExperimentManager == null)
        {
            Debug.LogError(
                "TaskSwitchExperimentCsvLogger: " +
                "TaskSwitchExperimentManagerが見つかりません。"
            );
            return;
        }

        if (!Directory.Exists(saveFolderPath))
        {
            Directory.CreateDirectory(saveFolderPath);
        }

        string safeName = SanitizeFileName(inputFileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "Experiment";
        }

        string path = Path.Combine(
            saveFolderPath,
            safeName + "_TaskSwitchEvents.csv"
        );

        writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(true)
        );

        WriteHeader();

        eventIndex = 0;
        linesSinceFlush = 0;
        realStartTime = Time.realtimeSinceStartup;
        simulationStartTime = Time.time;
        ResetIntervalMarkers();
        isLogging = true;

        SubscribeToEvents();

        WriteEvent(
            "Logger",
            "LoggingStarted",
            GetSelectedCraneIndex(),
            null,
            "",
            "Task Switch event logging started"
        );

        writer.Flush();
        linesSinceFlush = 0;

        Debug.Log("Task Switch CSV記録開始: " + path);
    }

    public void StopLogging()
    {
        if (!isLogging && writer == null)
        {
            return;
        }

        if (isLogging)
        {
            WriteEvent(
                "Logger",
                "LoggingStopped",
                GetSelectedCraneIndex(),
                null,
                "",
                "Task Switch event logging stopped"
            );
        }

        isLogging = false;
        UnsubscribeFromEvents();

        if (writer != null)
        {
            writer.Flush();
            writer.Close();
            writer = null;
        }

        Debug.Log("Task Switch CSV記録を終了しました。");
    }

    private void SubscribeToEvents()
    {
        UnsubscribeFromEvents();

        if (taskSwitchExperimentManager != null)
        {
            taskSwitchExperimentManager.ExperimentEventOccurred +=
                HandleTaskSwitchEvent;
        }

        CraneWorkPhaseTracker[] trackers =
            FindObjectsOfType<CraneWorkPhaseTracker>(true);

        foreach (CraneWorkPhaseTracker tracker in trackers)
        {
            if (tracker == null || subscribedTrackers.Contains(tracker))
            {
                continue;
            }

            tracker.StepStarted += HandleStepStarted;
            tracker.StepCompleted += HandleStepCompleted;
            tracker.StepResumed += HandleStepResumed;
            tracker.MajorPhaseCompleted += HandleMajorPhaseCompleted;
            subscribedTrackers.Add(tracker);
        }

        LifMagSystem[] lifMagSystems =
            FindObjectsOfType<LifMagSystem>(true);

        foreach (LifMagSystem lifMagSystem in lifMagSystems)
        {
            if (lifMagSystem == null ||
                subscribedLifMagSystems.Contains(lifMagSystem))
            {
                continue;
            }

            lifMagSystem.TaskSwitchSafeCurrentHoldStarted +=
                HandleSafeCurrentHoldStarted;
            lifMagSystem.TaskSwitchSafeCurrentHoldReleased +=
                HandleSafeCurrentHoldReleased;
            subscribedLifMagSystems.Add(lifMagSystem);
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (taskSwitchExperimentManager != null)
        {
            taskSwitchExperimentManager.ExperimentEventOccurred -=
                HandleTaskSwitchEvent;
        }

        foreach (CraneWorkPhaseTracker tracker in subscribedTrackers)
        {
            if (tracker == null) continue;

            tracker.StepStarted -= HandleStepStarted;
            tracker.StepCompleted -= HandleStepCompleted;
            tracker.StepResumed -= HandleStepResumed;
            tracker.MajorPhaseCompleted -= HandleMajorPhaseCompleted;
        }

        subscribedTrackers.Clear();

        foreach (LifMagSystem lifMagSystem in subscribedLifMagSystems)
        {
            if (lifMagSystem == null) continue;

            lifMagSystem.TaskSwitchSafeCurrentHoldStarted -=
                HandleSafeCurrentHoldStarted;
            lifMagSystem.TaskSwitchSafeCurrentHoldReleased -=
                HandleSafeCurrentHoldReleased;
        }

        subscribedLifMagSystems.Clear();
    }

    private void HandleTaskSwitchEvent(TaskSwitchEventData eventData)
    {
        if (!isLogging)
        {
            return;
        }

        float now = Time.realtimeSinceStartup;

        switch (eventData.eventName)
        {
            case "ExperimentStarted":
                ResetIntervalMarkers();
                break;

            case "SwitchRequested":
                switchRequestedRealTime = now;
                targetOperationStartedRealTime = float.NaN;
                targetPhaseCompletedRealTime = float.NaN;
                sourceOperationResumedRealTime = float.NaN;
                break;

            case "TargetOperationStarted":
                targetOperationStartedRealTime = now;
                break;

            case "TargetMajorPhaseCompleted":
                targetPhaseCompletedRealTime = now;
                break;

            case "SourceOperationResumed":
                sourceOperationResumedRealTime = now;
                break;
        }

        int eventCraneIndex = GetSelectedCraneIndex();
        CraneWorkPhaseTracker tracker =
            GetWorkPhaseTracker(eventCraneIndex);

        WriteEvent(
            "TaskSwitch",
            eventData.eventName,
            eventCraneIndex,
            tracker != null
                ? tracker.CurrentMajorPhase
                : (CraneStatusManager.WorkPhase?)null,
            tracker != null ? tracker.CurrentStepId : "",
            eventData.detail,
            eventData.realtimeSinceExperimentStart,
            eventData
        );
    }

    private void HandleStepStarted(
        CraneWorkPhaseTracker tracker,
        CraneStatusManager.WorkPhase phase,
        string stepId
    )
    {
        WriteTrackerEvent("StepStarted", tracker, phase, stepId);
    }

    private void HandleStepCompleted(
        CraneWorkPhaseTracker tracker,
        CraneStatusManager.WorkPhase phase,
        string stepId
    )
    {
        WriteTrackerEvent("StepCompleted", tracker, phase, stepId);
    }

    private void HandleStepResumed(
        CraneWorkPhaseTracker tracker,
        CraneStatusManager.WorkPhase phase,
        string stepId
    )
    {
        WriteTrackerEvent("StepResumed", tracker, phase, stepId);
    }

    private void HandleMajorPhaseCompleted(
        CraneWorkPhaseTracker tracker,
        CraneStatusManager.WorkPhase phase
    )
    {
        WriteTrackerEvent(
            "MajorPhaseCompleted",
            tracker,
            phase,
            tracker != null ? tracker.CurrentStepId : ""
        );
    }

    private void WriteTrackerEvent(
        string eventType,
        CraneWorkPhaseTracker tracker,
        CraneStatusManager.WorkPhase phase,
        string stepId
    )
    {
        if (!isLogging || tracker == null)
        {
            return;
        }

        int craneIndex = GetCraneIndex(tracker);

        WriteEvent(
            "WorkPhase",
            eventType,
            craneIndex,
            phase,
            stepId,
            ""
        );
    }

    private void HandleSafeCurrentHoldStarted(
        LifMagSystem lifMagSystem,
        float currentAmpere
    )
    {
        WriteSafeCurrentEvent(
            "SafeCurrentHoldStarted",
            lifMagSystem,
            currentAmpere
        );
    }

    private void HandleSafeCurrentHoldReleased(
        LifMagSystem lifMagSystem,
        float currentAmpere
    )
    {
        WriteSafeCurrentEvent(
            "SafeCurrentHoldReleased",
            lifMagSystem,
            currentAmpere
        );
    }

    private void WriteSafeCurrentEvent(
        string eventType,
        LifMagSystem lifMagSystem,
        float currentAmpere
    )
    {
        if (!isLogging || lifMagSystem == null)
        {
            return;
        }

        int craneIndex = GetCraneIndex(lifMagSystem);
        CraneWorkPhaseTracker tracker =
            GetWorkPhaseTracker(craneIndex);

        WriteEvent(
            "SafetyCurrent",
            eventType,
            craneIndex,
            tracker != null
                ? tracker.CurrentMajorPhase
                : (CraneStatusManager.WorkPhase?)null,
            tracker != null ? tracker.CurrentStepId : "",
            "current=" + F(currentAmpere, "F2") + " A"
        );
    }

    private void WriteEvent(
        string eventSource,
        string eventType,
        int eventCraneIndex,
        CraneStatusManager.WorkPhase? eventPhase,
        string eventStep,
        string detail,
        float? taskExperimentElapsed = null,
        TaskSwitchEventData? taskEventData = null
    )
    {
        if (writer == null || taskSwitchExperimentManager == null)
        {
            return;
        }

        TaskSwitchMethod method = taskEventData.HasValue
            ? taskEventData.Value.switchMethod
            : taskSwitchExperimentManager.SwitchMethod;

        TaskSwitchExperimentState state = taskEventData.HasValue
            ? taskEventData.Value.state
            : taskSwitchExperimentManager.CurrentState;

        TaskSwitchCraneCondition sourceCondition =
            taskSwitchExperimentManager.SourceCondition;
        TaskSwitchCraneCondition targetCondition =
            taskSwitchExperimentManager.TargetCondition;

        int sourceCraneIndex = taskEventData.HasValue
            ? taskEventData.Value.sourceCraneIndex
            : sourceCondition.craneIndex;
        int targetCraneIndex = taskEventData.HasValue
            ? taskEventData.Value.targetCraneIndex
            : targetCondition.craneIndex;

        CraneWorkPhaseTracker sourceTracker =
            GetWorkPhaseTracker(sourceCraneIndex);
        CraneWorkPhaseTracker targetTracker =
            GetWorkPhaseTracker(targetCraneIndex);

        CraneStatusManager.WorkPhase sourcePhase = taskEventData.HasValue
            ? taskEventData.Value.sourcePhase
            : sourceTracker != null
                ? sourceTracker.CurrentMajorPhase
                : sourceCondition.workPhase;

        CraneStatusManager.WorkPhase targetPhase = taskEventData.HasValue
            ? taskEventData.Value.targetPhase
            : targetTracker != null
                ? targetTracker.CurrentMajorPhase
                : targetCondition.workPhase;

        CraneStatusManager.ErrorType sourceError = taskEventData.HasValue
            ? taskEventData.Value.sourceErrorType
            : sourceCondition.errorType;

        CraneStatusManager.ErrorType targetError = taskEventData.HasValue
            ? taskEventData.Value.targetErrorType
            : targetCondition.errorType;

        CraneSnapshot snapshot = CaptureSnapshot(eventCraneIndex);

        string[] columns =
        {
            eventIndex.ToString(CultureInfo.InvariantCulture),
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            F(GetRealElapsedTime(), "F4"),
            F(GetSimulationElapsedTime(), "F4"),
            taskExperimentElapsed.HasValue
                ? F(taskExperimentElapsed.Value, "F4")
                : "",
            Csv(eventSource),
            Csv(eventType),
            method.ToString(),
            state.ToString(),
            IndexOrEmpty(GetSelectedCraneIndex()),
            IndexOrEmpty(sourceCraneIndex),
            sourcePhase.ToString(),
            Csv(sourceTracker != null ? sourceTracker.CurrentStepId : ""),
            sourceError.ToString(),
            IndexOrEmpty(targetCraneIndex),
            targetPhase.ToString(),
            Csv(targetTracker != null ? targetTracker.CurrentStepId : ""),
            targetError.ToString(),
            IndexOrEmpty(eventCraneIndex),
            GetCraneRole(eventCraneIndex, sourceCraneIndex, targetCraneIndex),
            eventPhase.HasValue ? eventPhase.Value.ToString() : "",
            Csv(eventStep),
            snapshot.hasPosition ? F(snapshot.currentX, "F4") : "",
            snapshot.hasPosition ? F(snapshot.currentZ, "F4") : "",
            snapshot.hasTarget ? F(snapshot.targetX, "F4") : "",
            snapshot.hasTarget ? F(snapshot.targetZ, "F4") : "",
            snapshot.hasLifMag ? B(snapshot.hasAttachedBoard) : "",
            snapshot.hasLifMag ? F(snapshot.attachedWeightKg, "F3") : "",
            snapshot.hasLifMag ? F(snapshot.electricCurrentA, "F3") : "",
            snapshot.hasLifMag ? F(snapshot.requiredCurrentA, "F3") : "",
            snapshot.hasLifMag
                ? B(snapshot.safeCurrentHoldActive)
                : "",
            ElapsedFrom(switchRequestedRealTime),
            ElapsedFrom(targetOperationStartedRealTime),
            ElapsedFrom(targetPhaseCompletedRealTime),
            ElapsedFrom(sourceOperationResumedRealTime),
            Csv(detail)
        };

        writer.WriteLine(string.Join(",", columns));
        eventIndex++;
        linesSinceFlush++;

        if (linesSinceFlush >= flushEveryLines)
        {
            writer.Flush();
            linesSinceFlush = 0;
        }
    }

    private CraneSnapshot CaptureSnapshot(int craneIndex)
    {
        CraneSnapshot snapshot = new CraneSnapshot();

        if (craneRegistry == null || craneIndex < 0)
        {
            return snapshot;
        }

        CraneInstance crane =
            craneRegistry.GetCraneByRuntimeIndex(craneIndex);

        if (crane == null)
        {
            return snapshot;
        }

        snapshot.hasCrane = true;

        if (crane.InformationTarget != null)
        {
            Vector3 position = crane.InformationTarget.position;
            snapshot.hasPosition = true;
            snapshot.currentX = position.x;
            snapshot.currentZ = position.z;
        }

        CraneWorkTargetManager targetManager =
            crane.GetComponent<CraneWorkTargetManager>();

        if (targetManager == null)
        {
            targetManager =
                crane.GetComponentInChildren<CraneWorkTargetManager>(true);
        }

        if (targetManager != null &&
            targetManager.TryGetTarget(
                out float targetX,
                out float targetZ
            ))
        {
            snapshot.hasTarget = true;
            snapshot.targetX = targetX;
            snapshot.targetZ = targetZ;
        }

        LifMagSystem lifMagSystem = crane.LifMagSystem;
        if (lifMagSystem != null)
        {
            snapshot.hasLifMag = true;
            snapshot.hasAttachedBoard = lifMagSystem.HasAttachedBoard;
            snapshot.attachedWeightKg =
                lifMagSystem.GetAttachedTotalWeightKgForDisplay();
            snapshot.electricCurrentA =
                lifMagSystem.CurrentElectricCurrentA;
            snapshot.requiredCurrentA =
                lifMagSystem.CurrentRequiredCurrentA;
            snapshot.safeCurrentHoldActive =
                lifMagSystem.IsTaskSwitchSafeCurrentHoldActive;
        }

        return snapshot;
    }

    private CraneWorkPhaseTracker GetWorkPhaseTracker(int craneIndex)
    {
        if (craneRegistry == null || craneIndex < 0)
        {
            return null;
        }

        CraneInstance crane =
            craneRegistry.GetCraneByRuntimeIndex(craneIndex);

        if (crane == null)
        {
            return null;
        }

        CraneWorkPhaseTracker tracker =
            crane.GetComponent<CraneWorkPhaseTracker>();

        if (tracker == null)
        {
            tracker =
                crane.GetComponentInChildren<CraneWorkPhaseTracker>(true);
        }

        return tracker;
    }

    private int GetCraneIndex(CraneWorkPhaseTracker tracker)
    {
        if (tracker == null || craneRegistry == null)
        {
            return -1;
        }

        for (int i = 0; i < craneRegistry.ActiveCraneCount; i++)
        {
            if (GetWorkPhaseTracker(i) == tracker)
            {
                return i;
            }
        }

        return -1;
    }

    private int GetCraneIndex(LifMagSystem lifMagSystem)
    {
        if (lifMagSystem == null || craneRegistry == null)
        {
            return -1;
        }

        for (int i = 0; i < craneRegistry.ActiveCraneCount; i++)
        {
            CraneInstance crane =
                craneRegistry.GetCraneByRuntimeIndex(i);

            if (crane != null && crane.LifMagSystem == lifMagSystem)
            {
                return i;
            }
        }

        return -1;
    }

    private int GetSelectedCraneIndex()
    {
        return craneOperationManager != null
            ? craneOperationManager.CurrentCraneIndex
            : -1;
    }

    private string GetCraneRole(
        int craneIndex,
        int sourceCraneIndex,
        int targetCraneIndex
    )
    {
        if (craneIndex == sourceCraneIndex) return "Source";
        if (craneIndex == targetCraneIndex) return "Target";
        return craneIndex >= 0 ? "Other" : "";
    }

    private void FindReferences()
    {
        if (taskSwitchExperimentManager == null)
        {
            taskSwitchExperimentManager =
                FindObjectOfType<TaskSwitchExperimentManager>(true);
        }

        if (craneOperationManager == null)
        {
            craneOperationManager =
                FindObjectOfType<CraneOperationManager>(true);
        }

        if (craneRegistry == null)
        {
            craneRegistry = FindObjectOfType<CraneRegistry>(true);
        }
    }

    private void ResetIntervalMarkers()
    {
        switchRequestedRealTime = float.NaN;
        targetOperationStartedRealTime = float.NaN;
        targetPhaseCompletedRealTime = float.NaN;
        sourceOperationResumedRealTime = float.NaN;
    }

    private float GetRealElapsedTime()
    {
        return Mathf.Max(
            0f,
            Time.realtimeSinceStartup - realStartTime
        );
    }

    private float GetSimulationElapsedTime()
    {
        return Mathf.Max(0f, Time.time - simulationStartTime);
    }

    private string ElapsedFrom(float startTime)
    {
        return float.IsNaN(startTime)
            ? ""
            : F(
                Mathf.Max(0f, Time.realtimeSinceStartup - startTime),
                "F4"
            );
    }

    private string IndexOrEmpty(int index)
    {
        return index >= 0
            ? index.ToString(CultureInfo.InvariantCulture)
            : "";
    }

    private string F(float value, string format)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return "";
        }

        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private string B(bool value)
    {
        return value ? "1" : "0";
    }

    private string Csv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        bool needsQuotes =
            value.Contains(",") ||
            value.Contains("\"") ||
            value.Contains("\n") ||
            value.Contains("\r");

        string escaped = value.Replace("\"", "\"\"");
        return needsQuotes ? "\"" + escaped + "\"" : escaped;
    }

    private string SanitizeFileName(string inputFileName)
    {
        string safeName = inputFileName ?? "";

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(c, '_');
        }

        return safeName.Trim();
    }

    private void WriteHeader()
    {
        writer.WriteLine(
            "event_index,utc_timestamp,real_elapsed_s," +
            "simulation_elapsed_s,task_experiment_elapsed_s," +
            "event_source,event_type,switch_method,state," +
            "active_crane_index,source_crane_index,source_phase," +
            "source_step,source_error_type,target_crane_index," +
            "target_phase,target_step,target_error_type," +
            "event_crane_index,event_crane_role,event_phase,event_step," +
            "current_x,current_z,target_x,target_z,has_attached_board," +
            "attached_weight_kg,electric_current_a,required_current_a," +
            "safe_current_hold_active,since_switch_request_s," +
            "since_target_operation_start_s," +
            "since_target_phase_completed_s," +
            "since_source_operation_resumed_s,detail"
        );
    }

    private void OnApplicationQuit()
    {
        StopLogging();
    }

    private void OnDestroy()
    {
        StopLogging();
    }

    private void OnValidate()
    {
        flushEveryLines = Mathf.Max(1, flushEveryLines);
    }
}

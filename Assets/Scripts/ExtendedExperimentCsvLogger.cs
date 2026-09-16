using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 追加した実験要素を、イベント系2ファイルと位置時系列1ファイルへ記録します。
/// 既存の視線・操作入力・作業情報Loggerとは併用できます。
/// </summary>
[DisallowMultipleComponent]
public class ExtendedExperimentCsvLogger : MonoBehaviour
{
    private sealed class CraneStatusSnapshot
    {
        public CraneStatusManager.WorkPhase phase;
        public CraneStatusManager.ErrorType errorType;
        public bool hasError;
        public bool isStopped;
        public bool isPausedBySelection;
    }

    [Header("保存先フォルダ")]
    [SerializeField]
    private string saveFolderPath =
        @"C:\Users\harui\Git\Crane-Simulator\Assets\ExperimentData";

    [Header("Manager参照（未設定時は自動検索）")]
    [SerializeField]
    private CraneRegistry craneRegistry;

    [SerializeField]
    private CraneStatusManager craneStatusManager;

    [SerializeField]
    private CraneStockManager craneStockManager;

    [SerializeField]
    private CraneOperationManager craneOperationManager;

    [Header("自動クレーン位置ログ")]
    [Tooltip("位置ログの記録周期です。0.1秒なら10 Hzです。")]
    [SerializeField]
    [Min(0.01f)]
    private float positionLogInterval = 0.1f;

    [Tooltip("ONの場合、実験全体のPause中にも位置行を書き込みます。")]
    [SerializeField]
    private bool logPositionsDuringGlobalPause = false;

    [Header("書き込み設定")]
    [SerializeField]
    [Min(1)]
    private int flushEveryLines = 100;

    private StreamWriter craneEventWriter;
    private StreamWriter stockEventWriter;
    private StreamWriter positionWriter;

    private bool isLogging;
    private float simulationStartTime;
    private float realStartTime;
    private float positionTimer;

    private int craneEventIndex;
    private int stockEventIndex;
    private int positionSampleIndex;
    private int craneEventLinesSinceFlush;
    private int stockEventLinesSinceFlush;
    private int positionLinesSinceFlush;

    private bool previousGlobalPauseState;
    private int previousSelectedCraneIndex = -1;
    private float selectionStartSimulationTime;
    private float selectionStartRealTime;
    private bool stockInitialSnapshotWritten;

    private readonly List<CraneStatusSnapshot> statusSnapshots =
        new List<CraneStatusSnapshot>();

    private CraneSchematicDisplay[] schematicDisplays =
        new CraneSchematicDisplay[0];

    public bool IsLogging => isLogging;

    private void Awake()
    {
        FindReferences();
    }

    public void StartLogging(string inputFileName)
    {
        if (isLogging ||
            craneEventWriter != null ||
            stockEventWriter != null ||
            positionWriter != null)
        {
            StopLogging();
        }

        FindReferences();

        if (!Directory.Exists(saveFolderPath))
        {
            Directory.CreateDirectory(saveFolderPath);
        }

        string safeName = SanitizeFileName(inputFileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "Experiment";
        }

        string craneEventPath = Path.Combine(
            saveFolderPath,
            safeName + "_CraneEvents.csv"
        );
        string stockEventPath = Path.Combine(
            saveFolderPath,
            safeName + "_StockEvents.csv"
        );
        string positionPath = Path.Combine(
            saveFolderPath,
            safeName + "_AutoCranePositions.csv"
        );

        UTF8Encoding utf8WithBom = new UTF8Encoding(true);
        craneEventWriter =
            new StreamWriter(craneEventPath, false, utf8WithBom);
        stockEventWriter =
            new StreamWriter(stockEventPath, false, utf8WithBom);
        positionWriter =
            new StreamWriter(positionPath, false, utf8WithBom);

        WriteHeaders();

        simulationStartTime = Time.time;
        realStartTime = Time.realtimeSinceStartup;
        positionTimer = 0f;
        craneEventIndex = 0;
        stockEventIndex = 0;
        positionSampleIndex = 0;
        craneEventLinesSinceFlush = 0;
        stockEventLinesSinceFlush = 0;
        positionLinesSinceFlush = 0;
        statusSnapshots.Clear();
        stockInitialSnapshotWritten = false;

        previousGlobalPauseState = ExperimentPauseManager.IsPaused;
        previousSelectedCraneIndex = GetSelectedCraneIndex();

        if (previousSelectedCraneIndex >= 0)
        {
            selectionStartSimulationTime = Time.time;
            selectionStartRealTime = Time.realtimeSinceStartup;
        }

        RefreshSchematicDisplays();
        SubscribeToStockEvents();
        isLogging = true;

        WriteCraneEvent(
            "LoggingStarted",
            -1,
            -1,
            null,
            null,
            "Extended experiment logging started"
        );
        WriteCraneEvent(
            "InitialPauseState",
            -1,
            -1,
            null,
            null,
            previousGlobalPauseState ? "Paused" : "Running"
        );

        if (previousSelectedCraneIndex >= 0)
        {
            WriteCraneEvent(
                "SelectionStarted",
                previousSelectedCraneIndex,
                -1,
                null,
                null,
                "Initial selected crane"
            );
        }

        DetectCraneStatusChanges();
        WriteInitialStockSnapshotIfReady();
        WritePositionSample();
        FlushAllWriters();

        Debug.Log(
            "追加実験CSV記録開始:\n" +
            craneEventPath + "\n" +
            stockEventPath + "\n" +
            positionPath
        );
    }

    public void StopLogging()
    {
        if (!isLogging &&
            craneEventWriter == null &&
            stockEventWriter == null &&
            positionWriter == null)
        {
            return;
        }

        if (isLogging && previousSelectedCraneIndex >= 0)
        {
            WriteSelectionEnded(
                previousSelectedCraneIndex,
                -1,
                "LoggingStopped"
            );
        }

        if (isLogging)
        {
            WriteCraneEvent(
                "LoggingStopped",
                -1,
                -1,
                null,
                null,
                "Extended experiment logging stopped"
            );
        }

        isLogging = false;
        UnsubscribeFromStockEvents();
        CloseAllWriters();

        Debug.Log("追加実験CSV記録を終了しました。");
    }

    private void Update()
    {
        if (!isLogging)
        {
            return;
        }

        DetectGlobalPauseChange();
        DetectCraneSelectionChange();
        DetectCraneStatusChanges();
        WriteInitialStockSnapshotIfReady();

        if (logPositionsDuringGlobalPause ||
            !ExperimentPauseManager.IsPaused)
        {
            positionTimer += logPositionsDuringGlobalPause
                ? Time.unscaledDeltaTime
                : Time.deltaTime;

            if (positionTimer >= positionLogInterval)
            {
                positionTimer -= positionLogInterval;
                WritePositionSample();
            }
        }
    }

    private void DetectGlobalPauseChange()
    {
        bool currentPauseState = ExperimentPauseManager.IsPaused;
        if (currentPauseState == previousGlobalPauseState)
        {
            return;
        }

        previousGlobalPauseState = currentPauseState;

        WriteCraneEvent(
            currentPauseState ? "Pause" : "Resume",
            -1,
            -1,
            null,
            null,
            currentPauseState
                ? "Experiment globally paused"
                : "Experiment globally resumed"
        );
    }

    private void DetectCraneSelectionChange()
    {
        int selectedCraneIndex = GetSelectedCraneIndex();
        if (selectedCraneIndex == previousSelectedCraneIndex)
        {
            return;
        }

        int previousIndex = previousSelectedCraneIndex;

        if (previousIndex >= 0)
        {
            WriteSelectionEnded(
                previousIndex,
                selectedCraneIndex,
                selectedCraneIndex >= 0
                    ? "Selection switched"
                    : "Selection cleared"
            );
        }

        previousSelectedCraneIndex = selectedCraneIndex;

        if (selectedCraneIndex >= 0)
        {
            selectionStartSimulationTime = Time.time;
            selectionStartRealTime = Time.realtimeSinceStartup;

            WriteCraneEvent(
                "SelectionStarted",
                selectedCraneIndex,
                previousIndex,
                null,
                null,
                "Crane selected"
            );
        }
    }

    private void WriteSelectionEnded(
        int craneIndex,
        int nextCraneIndex,
        string details
    )
    {
        float realDuration = Mathf.Max(
            0f,
            Time.realtimeSinceStartup - selectionStartRealTime
        );
        float simulationDuration = Mathf.Max(
            0f,
            Time.time - selectionStartSimulationTime
        );

        WriteCraneEvent(
            "SelectionEnded",
            craneIndex,
            nextCraneIndex,
            realDuration,
            simulationDuration,
            details
        );
    }

    private void DetectCraneStatusChanges()
    {
        if (craneStatusManager == null)
        {
            return;
        }

        int craneCount = craneStatusManager.ActiveCraneCount;

        while (statusSnapshots.Count < craneCount)
        {
            statusSnapshots.Add(null);
        }

        for (int craneIndex = 0;
             craneIndex < craneCount;
             craneIndex++)
        {
            CraneStatusManager.CraneState state =
                craneStatusManager.GetCraneState(craneIndex);

            if (state == null)
            {
                continue;
            }

            CraneStatusSnapshot previous =
                statusSnapshots[craneIndex];

            if (previous == null)
            {
                statusSnapshots[craneIndex] = CaptureStatus(state);
                WriteCraneEvent(
                    "InitialCraneStatus",
                    craneIndex,
                    -1,
                    null,
                    null,
                    "Initial status snapshot"
                );
                continue;
            }

            if (previous.phase != state.currentPhase)
            {
                WriteCraneEvent(
                    "PhaseChanged",
                    craneIndex,
                    -1,
                    null,
                    null,
                    previous.phase + " -> " + state.currentPhase
                );
            }

            if (previous.errorType != state.currentErrorType ||
                previous.hasError != state.hasError ||
                previous.isStopped != state.isStopped)
            {
                WriteCraneEvent(
                    "StatusChanged",
                    craneIndex,
                    -1,
                    null,
                    null,
                    "Error/stop state changed"
                );
            }

            if (previous.isPausedBySelection !=
                state.isPausedBySelection)
            {
                WriteCraneEvent(
                    "SelectionPauseChanged",
                    craneIndex,
                    -1,
                    null,
                    null,
                    state.isPausedBySelection
                        ? "Automatic progress paused by selection"
                        : "Automatic progress resumed after selection"
                );
            }

            statusSnapshots[craneIndex] = CaptureStatus(state);
        }
    }

    private CraneStatusSnapshot CaptureStatus(
        CraneStatusManager.CraneState state
    )
    {
        return new CraneStatusSnapshot
        {
            phase = state.currentPhase,
            errorType = state.currentErrorType,
            hasError = state.hasError,
            isStopped = state.isStopped,
            isPausedBySelection = state.isPausedBySelection
        };
    }

    private void SubscribeToStockEvents()
    {
        if (craneStockManager == null)
        {
            return;
        }

        craneStockManager.DetailedStockChanged -=
            HandleDetailedStockChanged;
        craneStockManager.DetailedStockChanged +=
            HandleDetailedStockChanged;
    }

    private void UnsubscribeFromStockEvents()
    {
        if (craneStockManager != null)
        {
            craneStockManager.DetailedStockChanged -=
                HandleDetailedStockChanged;
        }
    }

    private void HandleDetailedStockChanged(
        CraneStockManager.StockChangeEventData eventData
    )
    {
        if (!isLogging || eventData == null)
        {
            return;
        }

        WriteInitialStockSnapshotIfReady();

        string eventType = eventData.reason ==
                           CraneStockManager.StockChangeReason.Arrival
            ? "BoardGenerated"
            : "StockDecreased";

        WriteStockEvent(
            eventType,
            eventData.reason.ToString(),
            eventData.craneIndex,
            eventData.previousStockCount,
            eventData.newStockCount,
            eventData.delta,
            eventData.remainingUntilNextStock
        );
    }

    private void WriteInitialStockSnapshotIfReady()
    {
        if (stockInitialSnapshotWritten ||
            craneStockManager == null ||
            craneStockManager.ManagedCraneCount <= 0)
        {
            return;
        }

        stockInitialSnapshotWritten = true;

        for (int craneIndex = 0;
             craneIndex < craneStockManager.ManagedCraneCount;
             craneIndex++)
        {
            int stockCount =
                craneStockManager.GetStockCount(craneIndex);

            WriteStockEvent(
                "InitialStock",
                "InitialState",
                craneIndex,
                stockCount,
                stockCount,
                0,
                craneStockManager.GetRemainingUntilNextStock(
                    craneIndex
                )
            );
        }
    }

    private void WritePositionSample()
    {
        if (positionWriter == null || craneStatusManager == null)
        {
            return;
        }

        int craneCount = craneStatusManager.ActiveCraneCount;

        if (schematicDisplays == null ||
            schematicDisplays.Length == 0)
        {
            RefreshSchematicDisplays();
        }

        for (int craneIndex = 0;
             craneIndex < craneCount;
             craneIndex++)
        {
            WriteOneCranePosition(craneIndex);
        }
    }

    private void WriteOneCranePosition(int craneIndex)
    {
        CraneStatusManager.CraneState state =
            craneStatusManager.GetCraneState(craneIndex);

        if (state == null)
        {
            return;
        }

        CraneSchematicDisplay display =
            FindBestSchematicDisplay(craneIndex);

        Vector2 schematicPosition = Vector2.zero;
        float logicalZ = 0f;
        int startPointIndex = -1;
        int endPointIndex = -1;
        float progress = 0f;

        bool hasPosition = display != null &&
            display.TryGetAutomaticMovementSnapshot(
                out schematicPosition,
                out logicalZ,
                out startPointIndex,
                out endPointIndex,
                out progress
            );

        if (!hasPosition)
        {
            schematicPosition = Vector2.zero;
            logicalZ = 0f;
            startPointIndex = -1;
            endPointIndex = -1;
            progress = state.phaseDuration > 0f
                ? Mathf.Clamp01(
                    1f -
                    state.remainingTime /
                    state.phaseDuration
                )
                : 0f;
        }

        int selectedCraneIndex = GetSelectedCraneIndex();
        bool isSelected = selectedCraneIndex == craneIndex;
        string operationState = GetOperationState(
            state,
            isSelected
        );

        GetCraneIdentity(
            craneIndex,
            out int craneId,
            out string craneName
        );

        string[] columns =
        {
            positionSampleIndex.ToString(CultureInfo.InvariantCulture),
            GetUtcTimestamp(),
            F(GetRealElapsedTime(), "F4"),
            F(GetSimulationElapsedTime(), "F4"),
            craneIndex.ToString(CultureInfo.InvariantCulture),
            craneId.ToString(CultureInfo.InvariantCulture),
            Csv(craneName),
            operationState,
            state.currentPhase.ToString(),
            state.currentErrorType.ToString(),
            B(state.isStopped),
            B(isSelected),
            B(ExperimentPauseManager.IsPaused),
            F(state.phaseDuration, "F4"),
            F(Mathf.Max(0f, state.remainingTime), "F4"),
            F(progress, "F6"),
            startPointIndex >= 0
                ? startPointIndex.ToString(CultureInfo.InvariantCulture)
                : "",
            endPointIndex >= 0
                ? endPointIndex.ToString(CultureInfo.InvariantCulture)
                : "",
            hasPosition ? F(schematicPosition.x, "F4") : "",
            hasPosition ? F(schematicPosition.y, "F4") : "",
            hasPosition ? F(logicalZ, "F4") : "",
            display != null ? F(display.CurrentTargetX, "F4") : "",
            display != null ? F(display.CurrentTargetZ, "F4") : ""
        };

        positionWriter.WriteLine(string.Join(",", columns));
        positionSampleIndex++;
        positionLinesSinceFlush++;
        FlushIfNeeded(positionWriter, ref positionLinesSinceFlush);
    }

    private string GetOperationState(
        CraneStatusManager.CraneState state,
        bool isSelected
    )
    {
        if (ExperimentPauseManager.IsPaused)
        {
            return "GlobalPaused";
        }

        if (isSelected)
        {
            return "SelectedIntervention";
        }

        if (state.isStopped)
        {
            return "ErrorStopped";
        }

        return "Automatic";
    }

    private void WriteCraneEvent(
        string eventType,
        int craneIndex,
        int previousCraneIndex,
        float? selectionDurationReal,
        float? selectionDurationSimulation,
        string details
    )
    {
        if (craneEventWriter == null)
        {
            return;
        }

        CraneStatusManager.CraneState state =
            craneIndex >= 0 && craneStatusManager != null
                ? craneStatusManager.GetCraneState(craneIndex)
                : null;

        GetCraneIdentity(
            craneIndex,
            out int craneId,
            out string craneName
        );

        string[] columns =
        {
            craneEventIndex.ToString(CultureInfo.InvariantCulture),
            GetUtcTimestamp(),
            F(GetRealElapsedTime(), "F4"),
            F(GetSimulationElapsedTime(), "F4"),
            eventType,
            craneIndex >= 0
                ? craneIndex.ToString(CultureInfo.InvariantCulture)
                : "",
            craneIndex >= 0
                ? craneId.ToString(CultureInfo.InvariantCulture)
                : "",
            craneIndex >= 0 ? Csv(craneName) : "",
            previousCraneIndex >= 0
                ? previousCraneIndex.ToString(
                    CultureInfo.InvariantCulture
                )
                : "",
            state != null ? state.currentPhase.ToString() : "",
            state != null ? state.currentErrorType.ToString() : "",
            state != null ? B(state.hasError) : "",
            state != null ? B(state.isStopped) : "",
            state != null ? B(state.isPausedBySelection) : "",
            B(ExperimentPauseManager.IsPaused),
            selectionDurationReal.HasValue
                ? F(selectionDurationReal.Value, "F4")
                : "",
            selectionDurationSimulation.HasValue
                ? F(selectionDurationSimulation.Value, "F4")
                : "",
            Csv(details)
        };

        craneEventWriter.WriteLine(string.Join(",", columns));
        craneEventIndex++;
        craneEventLinesSinceFlush++;
        FlushIfNeeded(
            craneEventWriter,
            ref craneEventLinesSinceFlush
        );
    }

    private void WriteStockEvent(
        string eventType,
        string reason,
        int craneIndex,
        int previousStockCount,
        int newStockCount,
        int delta,
        float remainingUntilNextStock
    )
    {
        if (stockEventWriter == null)
        {
            return;
        }

        GetCraneIdentity(
            craneIndex,
            out int craneId,
            out string craneName
        );

        string[] columns =
        {
            stockEventIndex.ToString(CultureInfo.InvariantCulture),
            GetUtcTimestamp(),
            F(GetRealElapsedTime(), "F4"),
            F(GetSimulationElapsedTime(), "F4"),
            eventType,
            reason,
            craneIndex.ToString(CultureInfo.InvariantCulture),
            craneId.ToString(CultureInfo.InvariantCulture),
            Csv(craneName),
            previousStockCount.ToString(CultureInfo.InvariantCulture),
            newStockCount.ToString(CultureInfo.InvariantCulture),
            delta.ToString(CultureInfo.InvariantCulture),
            F(remainingUntilNextStock, "F4"),
            B(GetSelectedCraneIndex() == craneIndex),
            B(ExperimentPauseManager.IsPaused)
        };

        stockEventWriter.WriteLine(string.Join(",", columns));
        stockEventIndex++;
        stockEventLinesSinceFlush++;
        FlushIfNeeded(
            stockEventWriter,
            ref stockEventLinesSinceFlush
        );
    }

    private void WriteHeaders()
    {
        craneEventWriter.WriteLine(
            "event_index,utc_timestamp,real_elapsed_s," +
            "simulation_elapsed_s,event_type,crane_index,crane_id," +
            "crane_name,previous_or_next_crane_index,phase,error_type," +
            "has_error,is_stopped,is_paused_by_selection," +
            "is_global_paused,selection_duration_real_s," +
            "selection_duration_simulation_s,details"
        );

        stockEventWriter.WriteLine(
            "event_index,utc_timestamp,real_elapsed_s," +
            "simulation_elapsed_s,event_type,reason,crane_index," +
            "crane_id,crane_name,previous_stock_count,new_stock_count," +
            "delta,remaining_until_next_stock_s,is_selected," +
            "is_global_paused"
        );

        positionWriter.WriteLine(
            "sample_index,utc_timestamp,real_elapsed_s," +
            "simulation_elapsed_s,crane_index,crane_id,crane_name," +
            "operation_state,phase,error_type,is_stopped,is_selected," +
            "is_global_paused,phase_duration_s,remaining_time_s," +
            "phase_progress,start_point_index,end_point_index," +
            "schematic_x,schematic_y,logical_z,target_x,target_z"
        );
    }

    private void FindReferences()
    {
        if (craneRegistry == null)
        {
            craneRegistry = FindObjectOfType<CraneRegistry>(true);
        }

        if (craneStatusManager == null)
        {
            craneStatusManager =
                FindObjectOfType<CraneStatusManager>(true);
        }

        if (craneStockManager == null)
        {
            craneStockManager =
                FindObjectOfType<CraneStockManager>(true);
        }

        if (craneOperationManager == null)
        {
            craneOperationManager =
                FindObjectOfType<CraneOperationManager>(true);
        }
    }

    private void RefreshSchematicDisplays()
    {
        schematicDisplays =
            FindObjectsOfType<CraneSchematicDisplay>(true);
    }

    private CraneSchematicDisplay FindBestSchematicDisplay(
        int craneIndex
    )
    {
        CraneSchematicDisplay fallback = null;

        if (schematicDisplays == null)
        {
            return null;
        }

        foreach (CraneSchematicDisplay display in schematicDisplays)
        {
            if (display == null ||
                display.CraneIndex != craneIndex)
            {
                continue;
            }

            if (fallback == null)
            {
                fallback = display;
            }

            if (display.isActiveAndEnabled &&
                display.gameObject.activeInHierarchy)
            {
                return display;
            }
        }

        return fallback;
    }

    private int GetSelectedCraneIndex()
    {
        return craneOperationManager != null
            ? craneOperationManager.CurrentCraneIndex
            : -1;
    }

    private void GetCraneIdentity(
        int craneIndex,
        out int craneId,
        out string craneName
    )
    {
        craneId = craneIndex >= 0 ? craneIndex + 1 : -1;
        craneName = craneIndex >= 0
            ? "Crane " + (craneIndex + 1)
            : "";

        if (craneRegistry == null || craneIndex < 0)
        {
            return;
        }

        CraneInstance crane =
            craneRegistry.GetCraneByRuntimeIndex(craneIndex);

        if (crane == null)
        {
            return;
        }

        craneId = crane.CraneId;
        craneName = crane.DisplayName;
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

    private string GetUtcTimestamp()
    {
        return DateTime.UtcNow.ToString(
            "O",
            CultureInfo.InvariantCulture
        );
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

    private void FlushIfNeeded(
        StreamWriter writer,
        ref int linesSinceFlush
    )
    {
        if (writer == null || linesSinceFlush < flushEveryLines)
        {
            return;
        }

        writer.Flush();
        linesSinceFlush = 0;
    }

    private void FlushAllWriters()
    {
        craneEventWriter?.Flush();
        stockEventWriter?.Flush();
        positionWriter?.Flush();
        craneEventLinesSinceFlush = 0;
        stockEventLinesSinceFlush = 0;
        positionLinesSinceFlush = 0;
    }

    private void CloseAllWriters()
    {
        CloseWriter(ref craneEventWriter);
        CloseWriter(ref stockEventWriter);
        CloseWriter(ref positionWriter);
    }

    private void CloseWriter(ref StreamWriter writer)
    {
        if (writer == null)
        {
            return;
        }

        writer.Flush();
        writer.Close();
        writer = null;
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
        positionLogInterval = Mathf.Max(0.01f, positionLogInterval);
        flushEveryLines = Mathf.Max(1, flushEveryLines);
    }
}

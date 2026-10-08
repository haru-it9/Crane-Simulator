using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 2基間の切替タイミングだけを管理します。
/// 作業開始状態は既存のCraneInterventionScenarioManagerへ委譲します。
/// </summary>
[DisallowMultipleComponent]
public partial class TaskSwitchExperimentManager : MonoBehaviour
{
    [Serializable]
    private class ScheduledSwitchEntry
    {
        public int switchIndex;
        public int sourceCycle;
        public CraneStatusManager.WorkPhase sourcePhase;
        public float minimumDelaySeconds;
        public float maximumDelaySeconds;
        public bool triggered;
        public TaskSwitchTargetTaskPattern targetTaskPattern;
    }
    [Header("既存Manager参照")]
    [SerializeField] private CraneOperationManager craneOperationManager;
    [SerializeField] private CraneRegistry craneRegistry;
    [SerializeField]
    private CraneInterventionScenarioManager interventionScenarioManager;

    [Header("実験条件")]
    [SerializeField]
    private TaskSwitchMethod switchMethod =
        TaskSwitchMethod.ConfirmAfterDisplaySwitch;

    [SerializeField]
    private TaskSwitchCraneCondition sourceCondition =
        new TaskSwitchCraneCondition
        {
            craneIndex = 0,
            taskName = "Source Task",
            workPhase = CraneStatusManager.WorkPhase.Move1,
            errorType = CraneStatusManager.ErrorType.None
        };

    [SerializeField]
    private TaskSwitchCraneCondition targetCondition =
        new TaskSwitchCraneCondition
        {
            craneIndex = 1,
            taskName = "Target Task",
            workPhase = CraneStatusManager.WorkPhase.Place,
            errorType = CraneStatusManager.ErrorType.ErrorC
        };

    [Tooltip("カウントダウン式で使用する秒数です。")]
    [SerializeField]
    [Min(0f)]
    private float countdownSeconds = 5f;

    [Header("Source終了条件")]
    [Tooltip(
        "Sourceがこのサイクル数を累積完了したら実験を終了します。" +
        "Targetへの切替を挟んでも完了数はリセットしません。"
    )]
    [SerializeField]
    [Min(1)]
    private int sourceTotalCycleCount = 5;

    [Header("CSV切替スケジュール")]
    [SerializeField]
    private bool useSwitchScheduleCsv = true;

    [Tooltip(
        "列: switchIndex,sourceCycle,sourcePhase," +
        "minimumDelaySeconds,maximumDelaySeconds,targetTaskPattern（Move1ToLiftUp / Move2ToPlace）"
    )]
    [SerializeField]
    private TextAsset switchScheduleCsv;

    [Header("フェーズ境界通知")]
    [SerializeField] private TaskSwitchPhaseTracker sourcePhaseTracker;
    [SerializeField] private TaskSwitchPhaseTracker targetPhaseTracker;

    [Header("実作業フェーズ境界")]
    [Tooltip(
        "Sourceクレーンの実位置・吸着状態から大フェーズ完了を判定するTrackerです。" +
        "未設定時はSource Crane Indexに対応するクレーンから自動取得します。"
    )]
    [SerializeField]
    private CraneWorkPhaseTracker sourceWorkPhaseTracker;

    [Tooltip(
        "Targetクレーンの実作業完了を判定するTrackerです。" +
        "未設定時はTarget Crane Indexに対応するクレーンから自動取得します。"
    )]
    [SerializeField]
    private CraneWorkPhaseTracker targetWorkPhaseTracker;

    [Header("作業サイクル連携")]
    [Tooltip(
        "Sourceのサイクル進行を管理するControllerです。" +
        "未設定時はSource Crane Indexから自動取得します。"
    )]
    [SerializeField]
    private CraneWorkCycleController sourceCycleController;

    [Tooltip(
        "TargetのMove1→LiftUp / Move2→Placeの進行を管理するControllerです。" +
        "未設定時はTarget Crane Indexから自動取得します。"
    )]
    [SerializeField]
    private CraneWorkCycleController targetCycleController;

    [Tooltip(
        "ONの場合、Task Switch開始時にSourceの実作業監視を自動開始し、" +
        "MajorPhaseCompletedをPhase Boundary切替へ使用します。"
    )]
    [SerializeField]
    private bool useRealWorkPhaseBoundary = true;

    [Tooltip(
        "ONの場合、従来のTaskSwitchPhaseTrackerと" +
        "NotifySourcePhaseBoundary()も確認用フォールバックとして残します。"
    )]
    [SerializeField]
    private bool allowLegacyPhaseBoundaryFallback = true;

    [Header("切替UI")]
    [SerializeField] private GameObject confirmationPanel;
    [SerializeField] private Button confirmationButton;
    [SerializeField] private Text confirmationButtonText;
    [SerializeField] private GameObject countdownPanel;
    [SerializeField] private Text countdownText;
    [SerializeField] private Text experimentStateText;

    [Header("確認コントローラ入力")]
    [SerializeField]
    private string confirmationInputButton = "JoyStick2RedButton";

    [SerializeField]
    private string targetConfirmationReadyText =
        "赤ボタンで作業開始";

    [SerializeField]
    private string sourceConfirmationReadyText =
        "赤ボタンで作業再開";

    [SerializeField]
    private string confirmationStandbyText =
        "確認待機中";

    [SerializeField]
    private string confirmationAcceptedText =
        "確認入力を受け付けました";

    [SerializeField]
    private Color confirmationReadyColor =
        new Color(0.65f, 1f, 0.10f, 1f);

    [SerializeField]
    private Color confirmationStandbyColor =
        new Color(0.35f, 0.35f, 0.35f, 1f);

    [SerializeField]
    private Color confirmationAcceptedColor =
        new Color(0.20f, 0.85f, 1f, 1f);

    [SerializeField]
    [Min(0f)]
    private float confirmationAcceptedDisplaySeconds = 0.5f;

    [Header("このモード中に非表示にする自動操業UI")]
    [SerializeField]
    private GameObject[] objectsHiddenDuringExperiment =
        new GameObject[0];

    [Header("開始・終了設定")]
    [SerializeField]
    private bool initializeScenariosOnStartExperiment = true;

    [SerializeField]
    private bool clearScenariosOnExit = true;

    private readonly Dictionary<GameObject, bool> previousActiveStates =
        new Dictionary<GameObject, bool>();

    private TaskSwitchExperimentState currentState =
        TaskSwitchExperimentState.Idle;
    private float countdownRemaining;
    private float experimentStartRealtime;
    private bool realSourceBoundarySubscribed;
    private bool legacySourceBoundarySubscribed;
    private bool targetWorkPhaseSubscribed;
    private bool sourceSchedulePhaseSubscribed;
    private bool targetCycleSubscribed;
    private bool sourceCycleSubscribed;
    private bool sourceMajorPhaseCompleted;
    private string pendingTargetConfirmationDetail = "Confirmation";
    private bool pendingScheduledSwitch;
    private string pendingScheduledSwitchDetail = string.Empty;
    private bool scheduledSwitchCountdownActive;
    private bool scheduledSwitchAwaitingCarryoverPhase;
    private float scheduledSwitchDueRealtime;
    private float scheduledSwitchDelaySeconds;
    private int scheduledSwitchOriginCycle;
    private CraneStatusManager.WorkPhase scheduledSwitchOriginPhase;
    private readonly List<ScheduledSwitchEntry> scheduledSwitches =
        new List<ScheduledSwitchEntry>();
    private int activeCraneCountBeforeExperiment = -1;
    private bool pendingOperationInputUnlock;
    private float confirmationAcceptedUntilRealtime;

    [Header("複数回切替（実行時確認用）")]
    [SerializeField]
    [Min(0)]
    private int currentSwitchIndex;

    [SerializeField]
    [Min(0)]
    private int completedSwitchCount;

    public string SwitchScheduleCsvText => switchScheduleCsv != null ? switchScheduleCsv.text : "";
    public int SourceTotalCycleCount => sourceTotalCycleCount;
    public float CountdownSeconds => countdownSeconds;
    public TaskSwitchMethod SwitchMethod => switchMethod;
    public TaskSwitchExperimentState CurrentState => currentState;
    public float CountdownRemaining => countdownRemaining;
    public TaskSwitchCraneCondition SourceCondition => sourceCondition;
    public TaskSwitchCraneCondition TargetCondition => targetCondition;
    public int CurrentSwitchIndex => currentSwitchIndex;
    public int CompletedSwitchCount => completedSwitchCount;
    public bool TargetRunsFullCycle => false;
    public bool CanRequestSwitch =>
        !ExperimentPauseManager.IsPaused &&
        (currentState == TaskSwitchExperimentState.OperatingSource ||
         currentState == TaskSwitchExperimentState.OperatingReturnedSource);

    public event Action<TaskSwitchEventData> ExperimentEventOccurred;

    private void Awake()
    {
        InitializeSecondaryTaskUi();
        FindReferences();
        ResolveConfirmationButtonReferences();
        HideTransitionPanels();
        UpdateStateText();
        UpdateConfirmationButtonVisual();
    }

    private void OnEnable()
    {
        SubscribeSecondaryPauseEvents();
        ResolveWorkPhaseTrackers();
        ResolveCycleControllers();
        SubscribeToWorkPhaseEvents();
        SubscribeToCycleEvents();
    }

    private void OnDisable()
    {
        StopSecondarySubtask("ManagerDisabled");
        UnsubscribeSecondaryPauseEvents();
        UnsubscribeFromWorkPhaseEvents();
        UnsubscribeFromCycleEvents();
    }

    private void OnDestroy()
    {
        DisposeSecondarySubtask();
        UnsubscribeFromCycleEvents();

        if (craneOperationManager != null &&
            craneOperationManager.IsTaskSwitchExperimentMode)
        {
            craneOperationManager.EndTaskSwitchExperimentMode();
        }

        RestoreActiveCraneCount();
        RestoreAutomaticOperationObjects();
        ReleasePreparedTargets();
        StopSourceWorkPhaseMonitoring();
        StopTargetWorkPhaseMonitoring();
    }

    private void Update()
    {
        UpdateSecondarySubtask();
        if (ExperimentPauseManager.IsPaused) return;
        UpdatePendingOperationInputUnlock();
        UpdateConfirmationButtonVisual();

        if (CanAcceptConfirmationInput() &&
            Input.GetButtonDown(confirmationInputButton))
        {
            EmitEvent(
                "ConfirmationControllerButtonPressed",
                confirmationInputButton
            );
            ConfirmTargetTask();
            return;
        }

        if (scheduledSwitchCountdownActive &&
            !scheduledSwitchAwaitingCarryoverPhase &&
            CanRequestSwitch &&
            (float)ExperimentPauseManager.ActiveRealtime >= scheduledSwitchDueRealtime)
        {
            scheduledSwitchCountdownActive = false;
            pendingScheduledSwitch = true;

            int actualCycle =
                sourceCycleController != null &&
                sourceCycleController.IsRunning
                    ? sourceCycleController.CurrentCycleNumber
                    : scheduledSwitchOriginCycle;

            CraneStatusManager.WorkPhase actualPhase =
                sourceCycleController != null &&
                sourceCycleController.IsRunning
                    ? sourceCycleController.CurrentPhase
                    : sourceWorkPhaseTracker != null
                        ? sourceWorkPhaseTracker.CurrentMajorPhase
                        : scheduledSwitchOriginPhase;

            pendingScheduledSwitchDetail +=
                $";ActualCycle={actualCycle}" +
                $";ActualPhase={actualPhase}" +
                $";CarriedOver=" +
                $"{(actualCycle != scheduledSwitchOriginCycle || actualPhase != scheduledSwitchOriginPhase)}";

            EmitEvent(
                "SwitchRandomDelayCompleted",
                pendingScheduledSwitchDetail
            );
        }

        if (pendingScheduledSwitch && CanRequestSwitch)
        {
            pendingScheduledSwitch = false;
            EmitEvent(
                "ScheduledSwitchRequested",
                pendingScheduledSwitchDetail
            );
            RequestSwitch();
            return;
        }

        if (currentState == TaskSwitchExperimentState.OperatingTarget && targetCycleController != null &&
            !targetCycleController.IsRunning && targetCycleController.CompletedCycleCount >= targetCycleController.TotalCycleCount)
        {
            EmitEvent("TargetWorkCycleCompletionFallback", $"Pattern={ActiveTargetTaskPattern};CompletedSegments={targetCycleController.CompletedCycleCount}");
            ReturnControlToSource(targetCycleController.CurrentPhase);
            return;
        }

        if (currentState != TaskSwitchExperimentState.CountingDown)
        {
            return;
        }

        countdownRemaining = Mathf.Max(
            0f,
            countdownRemaining - Time.deltaTime
        );
        UpdateCountdownText();

        if (countdownRemaining <= 0f)
        {
            EmitEvent("CountdownCompleted");
            SwitchControlToTarget("Countdown");
        }
    }

    /// <summary>
    /// 既存の介入開始状態を2基分生成し、切替元の操作を開始します。
    /// </summary>
    public void StartExperiment()
    {
        if (ExperimentPauseManager.IsPaused) return;
        FindReferences();
        ResolveWorkPhaseTrackers();
        ResolveCycleControllers();
        SubscribeToWorkPhaseEvents();
        SubscribeToCycleEvents();

        if (!LoadExperimentConditions()) return;
        ApplyTargetCycleStartCondition();

        if (sourceCycleController != null)
        {
            sourceCycleController.SetTotalCycleCount(
                sourceTotalCycleCount
            );
        }

        if (!ValidateConfiguration())
        {
            return;
        }

        if (sourceCycleController != null && sourceCycleController.IsRunning) sourceCycleController.StopCycleAndTracker();
        StopTargetWorkPhaseMonitoring();
        StopSecondarySubtask("ExperimentRestarted");
        ResetSecondaryExperiment();
        EmitEvent("ExperimentPreparing");
        experimentStartRealtime = Time.realtimeSinceStartup;

        sourceMajorPhaseCompleted = false;
        pendingScheduledSwitch = false;
        pendingScheduledSwitchDetail = string.Empty;
        scheduledSwitchCountdownActive = false;
        scheduledSwitchAwaitingCarryoverPhase = false;
        scheduledSwitchDueRealtime = 0f;
        scheduledSwitchDelaySeconds = 0f;
        currentSwitchIndex = 0;
        completedSwitchCount = 0;
        EmitEvent("SwitchScheduleLoaded", $"Count={scheduledSwitches.Count};File={(switchScheduleCsv!=null?switchScheduleCsv.name:"")}");
        EmitEvent("WorkConditionsLoaded", $"Count={experimentWorkConditions.Count};SourceCycles={sourceTotalCycleCount}");
        pendingOperationInputUnlock = false;
        confirmationAcceptedUntilRealtime = 0f;
        HideAutomaticOperationObjects();
        HideTransitionPanels();

        activeCraneCountBeforeExperiment = craneRegistry.ActiveCraneCount;

        int requiredCraneCount = Mathf.Max(
            sourceCondition.craneIndex,
            targetCondition.craneIndex
        ) + 1;

        // 通常管理モードで設定済みの基数は減らしません。
        // 指定クレーンが現在の有効範囲外の場合だけ一時的に増やします。
        if (activeCraneCountBeforeExperiment < requiredCraneCount)
        {
            craneRegistry.SetActiveCraneCount(requiredCraneCount);
        }

        craneOperationManager.BeginTaskSwitchExperimentMode();

        if (initializeScenariosOnStartExperiment)
        {
            if (!PrepareCondition(sourceCondition, sourcePhaseTracker) ||
                !PrepareCondition(targetCondition, targetPhaseTracker))
            {
                craneOperationManager.EndTaskSwitchExperimentMode();
                ClearPreparedScenarios();
                ReleasePreparedTargets();
                RestoreActiveCraneCount();
                RestoreAutomaticOperationObjects();
                return;
            }
        }

        if (!craneOperationManager.SelectCraneForTaskSwitch(
                sourceCondition.craneIndex
            ))
        {
            craneOperationManager.EndTaskSwitchExperimentMode();
            ClearPreparedScenarios();
            ReleasePreparedTargets();
            RestoreActiveCraneCount();
            RestoreAutomaticOperationObjects();
            return;
        }

        craneOperationManager.SetTaskSwitchOperationInputLocked(false);
        SetState(TaskSwitchExperimentState.OperatingSource);
        SetPanelActive(confirmationPanel, true);

        StartSourceWorkPhaseMonitoring();

        EmitEvent("ExperimentStarted");
        StartSecondarySubtask();
        EmitEvent("SourceOperationStarted");
    }

    public void RequestSwitch()
    {
        if (!CanRequestSwitch)
        {
            Debug.LogWarning(
                $"現在の状態では切替要求を受け付けません: {currentState}"
            );
            return;
        }

        int nextSwitchIndex = currentSwitchIndex + 1;

        // 手動要求またはCSVタイマー発火のどちらでも、
        // 進行中の予約タイマーをここで終了します。
        scheduledSwitchCountdownActive = false;
        scheduledSwitchAwaitingCarryoverPhase = false;

        SelectTargetCondition(nextSwitchIndex);
        if (workConditionsCsv != null && activeTargetWorkCondition == null)
        {
            EmitEvent("NextTargetPreparationFailed", $"Missing Target CSV row: {nextSwitchIndex}");
            return;
        }
        ApplyTargetCycleStartCondition();

        // Each request prepares its own pattern/condition before changing source control.
        if (initializeScenariosOnStartExperiment &&
            !PrepareCondition(targetCondition, targetPhaseTracker))
        {
            EmitEvent(
                "NextTargetPreparationFailed",
                $"SwitchIndex={nextSwitchIndex}"
            );
            return;
        }

        currentSwitchIndex = nextSwitchIndex;
        sourceMajorPhaseCompleted = false;

        // 旧版でSource復帰後に使用していた状態から要求された場合も、
        // 以降の判定を通常のSource操作状態へ統一します。
        if (currentState ==
            TaskSwitchExperimentState.OperatingReturnedSource)
        {
            SetState(TaskSwitchExperimentState.OperatingSource);
        }

        EmitEvent(
            "SwitchRequested",
            $"SwitchIndex={currentSwitchIndex}"
        );

        switch (switchMethod)
        {
            case TaskSwitchMethod.ConfirmAfterDisplaySwitch:
                StartConfirmationSwitch();
                break;
            case TaskSwitchMethod.Countdown:
                StartCountdownSwitch();
                break;
            case TaskSwitchMethod.PhaseBoundary:
                StartPhaseBoundarySwitch();
                break;
            case TaskSwitchMethod.OperatorInitiated:
                SetState(TaskSwitchExperimentState.WaitingForOperatorSwitch);
                SetPanelActive(confirmationPanel, true);
                EmitEvent("OperatorSwitchAvailable");
                break;
        }
    }

    public void ConfirmTargetTask()
    {
        // 既存のUI ButtonとJoyStick2RedButtonの両方から呼び出します。
        if (!CanAcceptConfirmationInput())
        {
            return;
        }

        if (currentState == TaskSwitchExperimentState.WaitingForOperatorSwitch)
        {
            AcceptOperatorSwitch();
            return;
        }

        ShowConfirmationAcceptedFeedback();
        ScheduleOperationInputUnlockAfterButtonRelease();

        if (currentState ==
            TaskSwitchExperimentState.WaitingForSourceConfirmation)
        {
            EmitEvent("SourceReturnConfirmationPressed");
            CompleteSourceReturnAfterConfirmation();
            return;
        }

        string confirmationDetail =
            string.IsNullOrEmpty(pendingTargetConfirmationDetail)
                ? "Confirmation"
                : pendingTargetConfirmationDetail;

        EmitEvent("ConfirmationPressed", confirmationDetail);
        SetState(TaskSwitchExperimentState.OperatingTarget);

        if (!StartTargetOperation())
        {
            RecoverAfterFailedTargetOperationStart(
                confirmationDetail
            );
            return;
        }

        pendingTargetConfirmationDetail = string.Empty;
        EmitEvent(
            "TargetOperationStarted",
            confirmationDetail
        );
    }

    public void NotifySourcePhaseBoundary()
    {
        if (ExperimentPauseManager.IsPaused) return;
        if (!allowLegacyPhaseBoundaryFallback)
        {
            Debug.LogWarning(
                "旧フェーズ境界フォールバックが無効なため、" +
                "手動境界通知は使用できません。"
            );
            return;
        }

        CompletePhaseBoundarySwitch("ManualNotification");
    }

    public void CompleteExperiment()
    {
        if (currentState == TaskSwitchExperimentState.Idle ||
            currentState == TaskSwitchExperimentState.Completed)
        {
            return;
        }

        StopSecondarySubtask("ExperimentCompleted");
        craneOperationManager.SetTaskSwitchOperationInputLocked(true);
        StopSourceWorkPhaseMonitoring();
        if (sourceCycleController != null && sourceCycleController.IsRunning) sourceCycleController.StopCycleAndTracker();
        StopTargetWorkPhaseMonitoring();
        EmitEvent(
            "ExperimentCompleted",
            $"Requested={currentSwitchIndex};" +
            $"Completed={completedSwitchCount}"
        );
        SetState(TaskSwitchExperimentState.Completed);
        HideTransitionPanels();
    }

    public void ExitExperimentMode()
    {
        StopSecondarySubtask("ExperimentExited");
        HideTransitionPanels();
        StopSourceWorkPhaseMonitoring();
        StopTargetWorkPhaseMonitoring();
        if (sourceCycleController != null && sourceCycleController.IsRunning) sourceCycleController.StopCycleAndTracker();
        ClearExperimentWorkConditions();

        if (craneOperationManager != null &&
            craneOperationManager.IsTaskSwitchExperimentMode)
        {
            craneOperationManager.EndTaskSwitchExperimentMode();
        }

        if (clearScenariosOnExit)
        {
            ClearPreparedScenarios();
        }

        ReleasePreparedTargets();

        RestoreActiveCraneCount();

        RestoreAutomaticOperationObjects();
        SetState(TaskSwitchExperimentState.Idle);
    }

    public void SetSwitchMethod(int methodIndex)
    {
        int maxIndex = Enum.GetValues(typeof(TaskSwitchMethod)).Length - 1;
        switchMethod = (TaskSwitchMethod)Mathf.Clamp(
            methodIndex,
            0,
            maxIndex
        );
        UpdateStateText();
    }

    public void SetCountdownSeconds(float seconds)
    {
        countdownSeconds = Mathf.Max(0f, seconds);
    }

    public void SetSourceCraneIndex(int craneIndex)
    {
        sourceCondition.craneIndex = Mathf.Max(0, craneIndex);
    }

    public void SetTargetCraneIndex(int craneIndex)
    {
        targetCondition.craneIndex = Mathf.Max(0, craneIndex);
    }

    public void SetSourcePhase(int phaseIndex)
    {
        sourceCondition.workPhase = ToWorkPhase(phaseIndex);
        UpdateStateText();
    }

    public void SetTargetPhase(int phaseIndex)
    {
        targetCondition.workPhase = ToWorkPhase(phaseIndex);
        UpdateStateText();
    }

    public void SetSourceErrorType(int errorTypeIndex)
    {
        sourceCondition.errorType = ToErrorType(errorTypeIndex);
        UpdateStateText();
    }

    public void SetTargetErrorType(int errorTypeIndex)
    {
        targetCondition.errorType = ToErrorType(errorTypeIndex);
        UpdateStateText();
    }

    public void SetSourceTaskName(string taskName)
    {
        sourceCondition.taskName = string.IsNullOrWhiteSpace(taskName)
            ? "Source Task"
            : taskName;
        UpdateStateText();
    }

    public void SetTargetTaskName(string taskName)
    {
        targetCondition.taskName = string.IsNullOrWhiteSpace(taskName)
            ? "Target Task"
            : taskName;
        UpdateStateText();
    }

    private void StartConfirmationSwitch()
    {
        craneOperationManager.SetTaskSwitchOperationInputLocked(true);
        PauseSourceWorkForSwitch();

        if (!craneOperationManager.SelectCraneForTaskSwitch(
                targetCondition.craneIndex
            ))
        {
            RecoverAfterFailedTargetSelection("Confirmation");
            return;
        }

        pendingTargetConfirmationDetail = "Confirmation";
        SetState(TaskSwitchExperimentState.WaitingForConfirmation);
        SetPanelActive(confirmationPanel, true);
        EmitEvent("TargetDisplaySwitched", "Confirmation");
        EmitEvent("ConfirmationDisplayed", "Confirmation");
    }

    private void StartCountdownSwitch()
    {
        countdownRemaining = countdownSeconds;
        SetState(TaskSwitchExperimentState.CountingDown);
        SetPanelActive(countdownPanel, true);
        UpdateCountdownText();
        EmitEvent("CountdownStarted", countdownSeconds.ToString("F2"));

        if (countdownRemaining <= 0f)
        {
            EmitEvent("CountdownCompleted");
            SwitchControlToTarget("Countdown");
        }
    }

    private void StartPhaseBoundarySwitch()
    {
        if (sourceCycleController != null &&
            sourceCycleController.IsRunning)
        {
            sourceCycleController.RequestHoldAfterCurrentPhase();
        }

        SetState(TaskSwitchExperimentState.WaitingForPhaseBoundary);
        EmitEvent("WaitingForSourcePhaseBoundary");

        // 切替要求より先に大フェーズが完了していた場合は、
        // すでに到達済みの境界を使用して直ちに切り替えます。
        if (sourceMajorPhaseCompleted)
        {
            CompletePhaseBoundarySwitch("RealWorkAlreadyCompleted");
        }
    }

    private void SwitchControlToTarget(string detail)
    {
        craneOperationManager.SetTaskSwitchOperationInputLocked(true);
        PauseSourceWorkForSwitch();

        if (!craneOperationManager.SelectCraneForTaskSwitch(
                targetCondition.craneIndex
            ))
        {
            RecoverAfterFailedTargetSelection(detail);
            return;
        }

        SetPanelActive(countdownPanel, false);
        EmitEvent("TargetDisplaySwitched", detail);

        // Countdown / PhaseBoundaryでも、表示切替直後には
        // Target作業を開始せず、確認ボタン入力を待ちます。
        pendingTargetConfirmationDetail = detail;
        SetState(TaskSwitchExperimentState.WaitingForConfirmation);
        SetPanelActive(confirmationPanel, true);
        EmitEvent("ConfirmationDisplayed", detail);
    }

    /// <summary>
    /// 位置・板・人・トレーラの処理を既存Managerへ一括委譲します。
    /// </summary>
    private bool PrepareCondition(
        TaskSwitchCraneCondition condition,
        TaskSwitchPhaseTracker phaseTracker
    )
    {
        CraneInstance craneInstance =
            craneRegistry.GetCraneByRuntimeIndex(condition.craneIndex);

        if (craneInstance == null || craneInstance.CraneUnit == null)
        {
            Debug.LogWarning(
                $"Crane {condition.craneIndex + 1} の開始条件を" +
                "適用できませんでした。"
            );
            return false;
        }

        if (!PrepareFixedWorkTarget(condition, craneInstance))
        {
            return false;
        }

        float? interventionStartLocalZ = null;

        if (condition.useSchematicStartZ &&
            craneOperationManager.TryGetInterventionStartLocalZ(
                condition.craneIndex,
                out float schematicLocalZ
            ))
        {
            interventionStartLocalZ = schematicLocalZ;
        }

        interventionScenarioManager.ClearScenarioByCraneIndex(
            condition.craneIndex
        );

        interventionScenarioManager.SetupInterventionState(
            craneInstance.CraneUnit,
            condition.workPhase,
            condition.errorType,
            condition.craneIndex,
            interventionStartLocalZ,
            !(condition == targetCondition && activeTargetWorkCondition != null &&
              ActiveTargetTaskPattern == TaskSwitchTargetTaskPattern.Move2ToPlace)
        );

        if (!PrepareCsvWorkCondition(condition, craneInstance)) return false;

        if (phaseTracker != null)
        {
            phaseTracker.Configure(
                condition.craneIndex,
                condition.taskName,
                condition.workPhase
            );
        }

        return true;
    }

    /// <summary>
    /// Task Switchでは自動フェーズを進めず、開始時に決めた目標を固定します。
    /// 座標計算は既存模式図と共通のCraneWorkCoordinateUtilityを使用します。
    /// </summary>
    private bool PrepareFixedWorkTarget(
        TaskSwitchCraneCondition condition,
        CraneInstance craneInstance
    )
    {
        CraneWorkTargetManager targetManager =
            craneInstance.GetComponent<CraneWorkTargetManager>();

        if (targetManager == null)
        {
            targetManager =
                craneInstance.GetComponentInChildren<
                    CraneWorkTargetManager
                >(true);
        }

        if (targetManager == null)
        {
            Debug.LogError(
                $"Crane {condition.craneIndex + 1}に" +
                "CraneWorkTargetManagerがありません。",
                craneInstance
            );
            return false;
        }

        int pointIndex = ResolveTargetPointIndex(condition);
        CraneWorkTargetKind targetKind =
            GetTargetKind(pointIndex);

        TaskSwitchWorkCondition csvCondition = condition == sourceCondition ? SourceWorkCondition(1) : activeTargetWorkCondition;
        if (csvCondition != null)
        {
            ApplyCsvTarget(targetManager, condition.workPhase, csvCondition);
            return true;
        }

        return targetManager.SetFixedTargetFromPoint(
            pointIndex,
            condition.targetXSelection,
            targetKind,
            CraneWorkTargetSource.ExperimentCondition
        );
    }

    private int ResolveTargetPointIndex(
        TaskSwitchCraneCondition condition
    )
    {
        int configuredPoint = Mathf.Clamp(
            condition.targetPointIndex,
            0,
            CraneWorkCoordinateUtility.PointCount - 1
        );

        if (!condition.useDefaultTargetPointForPhase)
        {
            return configuredPoint;
        }

        switch (condition.workPhase)
        {
            case CraneStatusManager.WorkPhase.Move1:
            case CraneStatusManager.WorkPhase.LiftUp:
                return 0;

            case CraneStatusManager.WorkPhase.PlaceToTrack:
                return CraneWorkCoordinateUtility.PointCount - 1;

            case CraneStatusManager.WorkPhase.Move2:
            case CraneStatusManager.WorkPhase.Place:
                return condition.errorType ==
                       CraneStatusManager.ErrorType.ErrorC
                    ? CraneWorkCoordinateUtility.PointCount - 1
                    : configuredPoint;

            default:
                return configuredPoint;
        }
    }

    private CraneWorkTargetKind GetTargetKind(int pointIndex)
    {
        if (pointIndex == 0)
        {
            return CraneWorkTargetKind.Pickup;
        }

        if (pointIndex ==
            CraneWorkCoordinateUtility.PointCount - 1)
        {
            return CraneWorkTargetKind.Trailer;
        }

        return CraneWorkTargetKind.NormalPlacement;
    }

    private void ReleasePreparedTargets()
    {
        ReleaseTargetForCrane(sourceCondition.craneIndex);

        if (targetCondition.craneIndex != sourceCondition.craneIndex)
        {
            ReleaseTargetForCrane(targetCondition.craneIndex);
        }
    }

    private void ReleaseTargetForCrane(int craneIndex)
    {
        if (craneRegistry == null)
        {
            return;
        }

        CraneInstance craneInstance =
            craneRegistry.GetCraneByRuntimeIndex(craneIndex);

        if (craneInstance == null)
        {
            return;
        }

        CraneWorkTargetManager targetManager =
            craneInstance.GetComponent<CraneWorkTargetManager>();

        if (targetManager == null)
        {
            targetManager =
                craneInstance.GetComponentInChildren<
                    CraneWorkTargetManager
                >(true);
        }

        if (targetManager != null)
        {
            targetManager.ReleaseFixedTarget(true);
        }
    }

    private void ClearPreparedScenarios()
    {
        if (interventionScenarioManager == null)
        {
            return;
        }

        interventionScenarioManager.ClearScenarioByCraneIndex(
            sourceCondition.craneIndex
        );

        if (targetCondition.craneIndex != sourceCondition.craneIndex)
        {
            interventionScenarioManager.ClearScenarioByCraneIndex(
                targetCondition.craneIndex
            );
        }
    }

    private void RestoreActiveCraneCount()
    {
        if (craneRegistry == null || activeCraneCountBeforeExperiment < 0)
        {
            return;
        }

        if (craneRegistry.ActiveCraneCount !=
            activeCraneCountBeforeExperiment)
        {
            craneRegistry.SetActiveCraneCount(
                activeCraneCountBeforeExperiment
            );
        }

        activeCraneCountBeforeExperiment = -1;
    }

    private void SubscribeToWorkPhaseEvents()
    {
        if (useRealWorkPhaseBoundary &&
            !realSourceBoundarySubscribed &&
            sourceWorkPhaseTracker != null)
        {
            sourceWorkPhaseTracker.MajorPhaseCompleted +=
                HandleSourceMajorPhaseCompleted;
            realSourceBoundarySubscribed = true;
        }

        if (allowLegacyPhaseBoundaryFallback &&
            !legacySourceBoundarySubscribed &&
            sourcePhaseTracker != null)
        {
            sourcePhaseTracker.PhaseBoundaryReached +=
                HandleLegacySourcePhaseBoundary;
            legacySourceBoundarySubscribed = true;
        }

        if (!targetWorkPhaseSubscribed &&
            targetWorkPhaseTracker != null)
        {
            targetWorkPhaseTracker.MajorPhaseCompleted +=
                HandleTargetMajorPhaseCompleted;
            targetWorkPhaseSubscribed = true;
        }
    }

    private void UnsubscribeFromWorkPhaseEvents()
    {
        if (realSourceBoundarySubscribed &&
            sourceWorkPhaseTracker != null)
        {
            sourceWorkPhaseTracker.MajorPhaseCompleted -=
                HandleSourceMajorPhaseCompleted;
        }

        if (legacySourceBoundarySubscribed &&
            sourcePhaseTracker != null)
        {
            sourcePhaseTracker.PhaseBoundaryReached -=
                HandleLegacySourcePhaseBoundary;
        }

        if (targetWorkPhaseSubscribed &&
            targetWorkPhaseTracker != null)
        {
            targetWorkPhaseTracker.MajorPhaseCompleted -=
                HandleTargetMajorPhaseCompleted;
        }

        realSourceBoundarySubscribed = false;
        legacySourceBoundarySubscribed = false;
        targetWorkPhaseSubscribed = false;
    }

    private void HandleSourceSchedulePhaseStarted(
        CraneWorkCycleController controller,
        CraneStatusManager.WorkPhase phase,
        int cycleNumber
    )
    {
        if (!useSwitchScheduleCsv ||
            controller != sourceCycleController ||
            scheduledSwitches.Count == 0)
        {
            return;
        }

        if (scheduledSwitchCountdownActive)
        {
            scheduledSwitchAwaitingCarryoverPhase = false;

            float remainingSeconds = Mathf.Max(
                0f,
                scheduledSwitchDueRealtime -
                    (float)ExperimentPauseManager.ActiveRealtime
            );

            if (cycleNumber != scheduledSwitchOriginCycle ||
                phase != scheduledSwitchOriginPhase)
            {
                EmitEvent(
                    "SwitchScheduleCarriedOver",
                    $"ScheduledCycle={scheduledSwitchOriginCycle};" +
                    $"ScheduledPhase={scheduledSwitchOriginPhase};" +
                    $"CurrentCycle={cycleNumber};" +
                    $"CurrentPhase={phase};" +
                    $"RemainingSeconds={remainingSeconds:F3}"
                );
            }

            return;
        }

        if (pendingScheduledSwitch)
        {
            return;
        }

        if (!CanRequestSwitch) return;

        foreach (ScheduledSwitchEntry entry in scheduledSwitches)
        {
            if (entry.triggered ||
                entry.switchIndex <= currentSwitchIndex)
            {
                continue;
            }

            if (entry.switchIndex != currentSwitchIndex + 1 ||
                entry.sourceCycle != cycleNumber ||
                entry.sourcePhase != phase)
            {
                continue;
            }

            entry.triggered = true;

            float minimumDelay = Mathf.Max(
                0f,
                entry.minimumDelaySeconds
            );
            float maximumDelay = Mathf.Max(
                minimumDelay,
                entry.maximumDelaySeconds
            );

            scheduledSwitchDelaySeconds =
                Mathf.Approximately(minimumDelay, maximumDelay)
                    ? minimumDelay
                    : UnityEngine.Random.Range(
                        minimumDelay,
                        maximumDelay
                    );

            scheduledSwitchOriginCycle = cycleNumber;
            scheduledSwitchOriginPhase = phase;
            scheduledSwitchDueRealtime =
                (float)ExperimentPauseManager.ActiveRealtime +
                scheduledSwitchDelaySeconds;
            scheduledSwitchCountdownActive = true;
            scheduledSwitchAwaitingCarryoverPhase = false;

            pendingScheduledSwitchDetail =
                $"SwitchIndex={entry.switchIndex};" +
                $"ScheduledCycle={cycleNumber};" +
                $"ScheduledPhase={phase};" +
                $"RandomDelaySeconds=" +
                $"{scheduledSwitchDelaySeconds:F3}";

            EmitEvent(
                "SwitchScheduleMatched",
                pendingScheduledSwitchDetail
            );
            EmitEvent(
                "SwitchRandomDelayStarted",
                pendingScheduledSwitchDetail
            );
            break;
        }
    }

    private void HandleSourceMajorPhaseCompleted(
        CraneWorkPhaseTracker tracker,
        CraneStatusManager.WorkPhase completedPhase
    )
    {
        if (tracker == sourceWorkPhaseTracker &&
            scheduledSwitchCountdownActive)
        {
            scheduledSwitchAwaitingCarryoverPhase = true;

            float remainingSeconds = Mathf.Max(
                0f,
                scheduledSwitchDueRealtime -
                    (float)ExperimentPauseManager.ActiveRealtime
            );

            EmitEvent(
                "SwitchScheduleWaitingForCarryoverPhase",
                $"ScheduledCycle={scheduledSwitchOriginCycle};" +
                $"ScheduledPhase={scheduledSwitchOriginPhase};" +
                $"CompletedPhase={completedPhase};" +
                $"RemainingSeconds={remainingSeconds:F3}"
            );
        }
        if (tracker != sourceWorkPhaseTracker)
        {
            return;
        }

        if (currentState ==
            TaskSwitchExperimentState.OperatingReturnedSource)
        {
            EmitEvent(
                "ReturnedSourceMajorPhaseCompleted",
                completedPhase.ToString()
            );
            return;
        }

        // Phase Boundary方式では、切替要求を受けた時点で
        // 実行中だった大フェーズの完了を直近境界として使用します。
        // InspectorのSource Conditionに残っている開始フェーズとは
        // 比較しません。
        if (currentState ==
            TaskSwitchExperimentState.WaitingForPhaseBoundary)
        {
            sourceMajorPhaseCompleted = true;
            sourceCondition.workPhase = completedPhase;

            EmitEvent(
                "SourceMajorPhaseCompleted",
                completedPhase.ToString()
            );

            CompletePhaseBoundarySwitch(
                $"RealWork:{completedPhase}"
            );
            return;
        }

        if (completedPhase != sourceCondition.workPhase)
        {
            return;
        }

        sourceMajorPhaseCompleted = true;
        EmitEvent(
            "SourceMajorPhaseCompleted",
            completedPhase.ToString()
        );
    }

    private void HandleTargetMajorPhaseCompleted(
        CraneWorkPhaseTracker tracker,
        CraneStatusManager.WorkPhase completedPhase
    )
    {
        if (tracker != targetWorkPhaseTracker ||
            currentState != TaskSwitchExperimentState.OperatingTarget)
        {
            return;
        }

        if (completedPhase != TaskSwitchConditionCsv.EndPhase(ActiveTargetTaskPattern)) return;
        EmitEvent("TargetMajorPhaseCompleted", $"Pattern={ActiveTargetTaskPattern};EndPhase={completedPhase}");
        // The cycle controller closes its segment and emits completion before we stop it.
        // Avoid stopping it from the tracker callback before its cycle counters/CSV are updated.
    }

    private void HandleSourceAllCyclesCompleted(
        CraneWorkCycleController controller,
        int completedCycles
    )
    {
        if (controller != sourceCycleController ||
            currentState == TaskSwitchExperimentState.Idle ||
            currentState == TaskSwitchExperimentState.Completed)
        {
            return;
        }

        EmitEvent(
            "SourceAllCyclesCompleted",
            $"CompletedCycles={completedCycles};" +
            $"TargetCycles={sourceTotalCycleCount}"
        );
        CompleteExperiment();
    }

    private void HandleTargetAllCyclesCompleted(
        CraneWorkCycleController controller,
        int completedCycles
    )
    {
        if (controller != targetCycleController ||
            currentState != TaskSwitchExperimentState.OperatingTarget)
        {
            return;
        }

        EmitEvent(
            "TargetWorkCycleCompleted",
            $"CompletedSegments={completedCycles};Pattern={ActiveTargetTaskPattern};EndPhase={controller.CurrentPhase}"
        );

        ReturnControlToSource(controller.CurrentPhase);
    }

    private void HandleLegacySourcePhaseBoundary(
        TaskSwitchPhaseTracker tracker,
        CraneStatusManager.WorkPhase previousPhase,
        CraneStatusManager.WorkPhase newPhase
    )
    {
        CompletePhaseBoundarySwitch(
            $"Legacy:{previousPhase}->{newPhase}"
        );
    }

    private void CompletePhaseBoundarySwitch(string detail)
    {
        if (currentState !=
            TaskSwitchExperimentState.WaitingForPhaseBoundary)
        {
            return;
        }

        EmitEvent("SourcePhaseBoundaryReached", detail);
        SwitchControlToTarget("PhaseBoundary");
    }

    private void StartSourceWorkPhaseMonitoring()
    {
        if (!useRealWorkPhaseBoundary)
        {
            return;
        }

        if (sourceWorkPhaseTracker == null)
        {
            Debug.LogWarning(
                "SourceのCraneWorkPhaseTrackerが見つからないため、" +
                "実作業フェーズ境界の監視を開始できません。"
            );
            return;
        }

        if (!sourceWorkPhaseTracker.ConfigurePhase(
                sourceCondition.workPhase,
                true
            ))
        {
            Debug.LogWarning(
                $"Sourceの実作業監視を開始できませんでした: " +
                $"{sourceCondition.workPhase}"
            );
            return;
        }

        EmitEvent(
            "SourceWorkPhaseMonitoringStarted",
            sourceCondition.workPhase.ToString()
        );

        if (sourceCycleController != null &&
            !sourceCycleController.IsRunning)
        {
            sourceCycleController.AdoptRunningTrackerAsCycleStart();
        }
    }

    private void PauseSourceWorkForSwitch()
    {
        if (sourceCycleController != null &&
            sourceCycleController.IsRunning)
        {
            // Phase Boundaryで既に境界待機中なら、保留状態を維持します。
            if (!sourceCycleController.IsWaitingAtBoundary &&
                !sourceCycleController.HasPendingNextPhase)
            {
                sourceCycleController.PauseCycle();
            }

            EmitEvent("SourceWorkSuspended");
            return;
        }

        StopSourceWorkPhaseMonitoring();
        EmitEvent("SourceWorkSuspended");
    }

    private void StopSourceWorkPhaseMonitoring()
    {
        if (sourceWorkPhaseTracker != null &&
            sourceWorkPhaseTracker.IsMonitoring)
        {
            sourceWorkPhaseTracker.StopMonitoring();
        }
    }

    private void StartTargetWorkPhaseMonitoring()
    {
        if (targetWorkPhaseTracker == null)
        {
            Debug.LogWarning(
                "TargetのCraneWorkPhaseTrackerが見つからないため、" +
                "Target完了後のSource復帰を実行できません。"
            );
            return;
        }

        if (!targetWorkPhaseTracker.ConfigurePhase(
                targetCondition.workPhase,
                true
            ))
        {
            Debug.LogWarning(
                $"Targetの実作業監視を開始できませんでした: " +
                $"{targetCondition.workPhase}"
            );
            return;
        }

        EmitEvent(
            "TargetWorkPhaseMonitoringStarted",
            targetCondition.workPhase.ToString()
        );
    }

    private bool StartTargetOperation()
    {
        if (targetCycleController == null) return false;
        if (targetCycleController.IsRunning) targetCycleController.StopCycleAndTracker();
        ApplyTargetCycleStartCondition();
        targetCycleController.SetExperimentConditions(activeTargetWorkCondition == null ? null : new [] { activeTargetWorkCondition });
        bool started = targetCycleController.StartTaskSegment(
            TaskSwitchConditionCsv.StartPhase(ActiveTargetTaskPattern), TaskSwitchConditionCsv.EndPhase(ActiveTargetTaskPattern));
        if (!started) return false;
        if (activeTargetWorkCondition == null && ActiveTargetTaskPattern == TaskSwitchTargetTaskPattern.Move1ToLiftUp && !PrepareTargetFullCycleLoadPlan())
        {
            targetCycleController.StopCycleAndTracker();
            return false;
        }
        EmitEvent("TargetTaskSegmentStarted", $"Pattern={ActiveTargetTaskPattern};StartPhase={targetCondition.workPhase};EndPhase={TaskSwitchConditionCsv.EndPhase(ActiveTargetTaskPattern)}");
        return true;
    }

    private void StopTargetWorkPhaseMonitoring()
    {
        if (targetCycleController != null &&
            targetCycleController.IsRunning)
        {
            targetCycleController.StopCycleAndTracker();
            return;
        }

        if (targetWorkPhaseTracker != null &&
            targetWorkPhaseTracker.IsMonitoring)
        {
            targetWorkPhaseTracker.StopMonitoring();
        }
    }

    private void ReturnControlToSource(
        CraneStatusManager.WorkPhase completedTargetPhase
    )
    {
        craneOperationManager.SetTaskSwitchOperationInputLocked(true);
        StopTargetWorkPhaseMonitoring();
        SetState(TaskSwitchExperimentState.ReturningToSource);
        EmitEvent(
            "ReturningToSource",
            completedTargetPhase.ToString()
        );

        if (!craneOperationManager.SelectCraneForTaskSwitch(
                sourceCondition.craneIndex
            ))
        {
            Debug.LogError(
                "Sourceクレーンへ操作を戻せませんでした。"
            );

            // 選択失敗時に入力ロック状態で取り残さないよう、
            // Target側の操作状態へ戻します。実験は手動終了できます。
            craneOperationManager.SetTaskSwitchOperationInputLocked(false);
            SetState(TaskSwitchExperimentState.OperatingTarget);
            EmitEvent(
                "SourceReturnFailed",
                completedTargetPhase.ToString()
            );
            return;
        }

        EmitEvent("SourceDisplayRestored");

        // Source画面へ戻した後も入力ロックを維持し、
        // 確認ボタンが押されるまで中断作業を再開しません。
        SetState(
            TaskSwitchExperimentState.WaitingForSourceConfirmation
        );
        SetPanelActive(confirmationPanel, true);
        EmitEvent(
            "SourceReturnConfirmationDisplayed",
            completedTargetPhase.ToString()
        );
    }

    private void CompleteSourceReturnAfterConfirmation()
    {
        if (currentState !=
            TaskSwitchExperimentState.WaitingForSourceConfirmation)
        {
            return;
        }

        string resumeDetail = ResumeSourceWorkAfterReturn();

        completedSwitchCount = Mathf.Max(
            completedSwitchCount,
            currentSwitchIndex
        );
        SetState(TaskSwitchExperimentState.OperatingSource);
        EmitEvent(
            "SourceOperationResumed",
            $"SwitchIndex={currentSwitchIndex};{resumeDetail}"
        );
        EmitEvent(
            "SwitchCompleted",
            $"SwitchIndex={currentSwitchIndex}"
        );
    }

    private void RecoverAfterFailedTargetSelection(string detail)
    {
        SetPanelActive(confirmationPanel, true);
        SetPanelActive(countdownPanel, false);

        string resumeDetail = ResumeSourceWorkAfterReturn();

        craneOperationManager.SetTaskSwitchOperationInputLocked(false);
        SetState(TaskSwitchExperimentState.OperatingSource);
        EmitEvent(
            "TargetSelectionFailed",
            $"{detail};{resumeDetail}"
        );
    }

    private void RecoverAfterFailedTargetOperationStart(string detail)
    {
        EmitEvent("TargetOperationStartFailed", detail);
        SetPanelActive(confirmationPanel, true);
        SetPanelActive(countdownPanel, false);

        if (!craneOperationManager.SelectCraneForTaskSwitch(
                sourceCondition.craneIndex
            ))
        {
            craneOperationManager.SetTaskSwitchOperationInputLocked(false);
            EmitEvent("SourceReturnFailed", "TargetStartFailure");
            return;
        }

        string resumeDetail = ResumeSourceWorkAfterReturn();
        craneOperationManager.SetTaskSwitchOperationInputLocked(false);
        SetState(TaskSwitchExperimentState.OperatingSource);
        EmitEvent(
            "SourceOperationResumed",
            $"TargetStartFailure;{resumeDetail}"
        );
    }

    private string ResumeSourceWorkAfterReturn()
    {
        if (sourceCycleController != null &&
            sourceCycleController.IsRunning)
        {
            bool resumed;
            string resumeKind;

            if (sourceCycleController.IsWaitingAtBoundary ||
                sourceCycleController.HasPendingNextPhase)
            {
                resumed = sourceCycleController.ContinueAfterBoundary();
                resumeKind = "ContinueAfterBoundary";
            }
            else
            {
                resumed = sourceCycleController.ResumeCycle();
                resumeKind = "ResumeCycle";
            }

            sourceMajorPhaseCompleted = false;
            sourceCondition.workPhase =
                sourceCycleController.CurrentPhase;

            return resumed
                ? $"{resumeKind}:" +
                  $"{sourceCycleController.CurrentPhase}/" +
                  $"Cycle={sourceCycleController.CurrentCycleNumber}"
                : $"{resumeKind}Failed";
        }

        if (sourceWorkPhaseTracker == null)
        {
            return "TrackerUnavailable";
        }

        // Confirm・Countdownなど、フェーズ途中で切り替えた場合は
        // 詳細ステップと吸着基準高さを保持したまま再開します。
        // 連続安定時間だけはTracker側で0へ戻します。
        if (!sourceMajorPhaseCompleted)
        {
            bool resumed = sourceWorkPhaseTracker.ResumeMonitoring();
            return resumed
                ? $"ResumeSamePhase:{sourceWorkPhaseTracker.CurrentMajorPhase}/" +
                  $"{sourceWorkPhaseTracker.CurrentStepId}"
                : "ResumeSamePhaseFailed";
        }

        // PhaseBoundaryなど、Sourceフェーズ完了後に切り替えた場合は
        // 作業サイクル上の次フェーズへ進めます。
        CraneStatusManager.WorkPhase nextPhase =
            GetNextWorkPhase(
                sourceWorkPhaseTracker.CurrentMajorPhase,
                sourceCondition.errorType
            );

        sourceCondition.workPhase = nextPhase;

        if (sourcePhaseTracker != null)
        {
            sourcePhaseTracker.Configure(
                sourceCondition.craneIndex,
                sourceCondition.taskName,
                nextPhase
            );
        }

        CraneInstance sourceCrane =
            craneRegistry.GetCraneByRuntimeIndex(
                sourceCondition.craneIndex
            );

        if (sourceCrane != null)
        {
            PrepareFixedWorkTarget(sourceCondition, sourceCrane);
        }

        sourceMajorPhaseCompleted = false;

        bool configured = sourceWorkPhaseTracker.ConfigurePhase(
            nextPhase,
            true
        );

        return configured
            ? $"StartNextPhase:{nextPhase}"
            : $"StartNextPhaseFailed:{nextPhase}";
    }

    private static CraneStatusManager.WorkPhase GetNextWorkPhase(
        CraneStatusManager.WorkPhase completedPhase,
        CraneStatusManager.ErrorType errorType
    )
    {
        switch (completedPhase)
        {
            case CraneStatusManager.WorkPhase.Move1:
                return CraneStatusManager.WorkPhase.LiftUp;

            case CraneStatusManager.WorkPhase.LiftUp:
                return CraneStatusManager.WorkPhase.Move2;

            case CraneStatusManager.WorkPhase.Move2:
                return errorType == CraneStatusManager.ErrorType.ErrorC
                    ? CraneStatusManager.WorkPhase.PlaceToTrack
                    : CraneStatusManager.WorkPhase.Place;

            case CraneStatusManager.WorkPhase.Place:
            case CraneStatusManager.WorkPhase.PlaceToTrack:
            default:
                return CraneStatusManager.WorkPhase.Move1;
        }
    }

    private void ApplyTargetCycleStartCondition()
    {
        targetCondition.workPhase = TaskSwitchConditionCsv.StartPhase(ActiveTargetTaskPattern);
        // CSV conditions preload actual generated boards for Move2; no synthetic ErrorC board.
        targetCondition.errorType = CraneStatusManager.ErrorType.None;
    }

    private bool PrepareTargetFullCycleLoadPlan()
    {
        CraneInstance targetCrane =
            craneRegistry.GetCraneByRuntimeIndex(
                targetCondition.craneIndex
            );

        if (targetCrane == null)
        {
            Debug.LogError(
                "Targetクレーンを取得できないため、" +
                "重量計画を設定できません。",
                this
            );
            return false;
        }

        CraneWorkLoadPlanManager targetLoadPlan =
            targetCrane.GetComponent<CraneWorkLoadPlanManager>();

        if (targetLoadPlan == null)
        {
            targetLoadPlan =
                targetCrane.GetComponentInChildren<
                    CraneWorkLoadPlanManager
                >(true);
        }

        if (targetLoadPlan == null)
        {
            Debug.LogError(
                $"Crane {targetCondition.craneIndex + 1}に" +
                "CraneWorkLoadPlanManagerがありません。",
                targetCrane
            );
            return false;
        }

        if (!targetLoadPlan.RefreshPickupTargetFromCurrentTarget())
        {
            Debug.LogError(
                "Targetの選択座標にある板のCSV重量を" +
                "取得できないため、吊り上げ作業を開始できません。" +
                "BoardGeneratorのCSV、Spawn Positions、" +
                "Default Pickup Countを確認してください。",
                targetCrane
            );
            return false;
        }

        targetLoadPlan.SetTargetRemainingWeightKg(0f);

        EmitEvent(
            "TargetFullCycleLoadPlanPrepared",
            $"Pickup={targetLoadPlan.PickupTargetWeightKg:F1}kg;" +
            "RemainingAfterPlacement=0.0kg;Source=BoardCsv"
        );

        return true;
    }

    private void ResolveCycleControllers()
    {
        if (craneRegistry == null ||
            sourceCondition == null ||
            targetCondition == null)
        {
            return;
        }

        CraneWorkCycleController resolvedSource =
            FindCycleController(sourceCondition.craneIndex);
        CraneWorkCycleController resolvedTarget =
            FindCycleController(targetCondition.craneIndex);

        bool sourceChanged =
            resolvedSource != null &&
            resolvedSource != sourceCycleController;
        bool targetChanged =
            resolvedTarget != null &&
            resolvedTarget != targetCycleController;

        if (!sourceChanged && !targetChanged)
        {
            return;
        }

        UnsubscribeFromCycleEvents();

        if (sourceChanged)
        {
            sourceCycleController = resolvedSource;
        }

        if (targetChanged)
        {
            targetCycleController = resolvedTarget;
        }

        SubscribeToCycleEvents();
    }

    private CraneWorkCycleController FindCycleController(int craneIndex)
    {
        CraneInstance crane =
            craneRegistry.GetCraneByRuntimeIndex(craneIndex);

        if (crane == null)
        {
            return null;
        }

        CraneWorkCycleController controller =
            crane.GetComponent<CraneWorkCycleController>();

        if (controller == null)
        {
            controller =
                crane.GetComponentInChildren<
                    CraneWorkCycleController
                >(true);
        }

        return controller;
    }

    private void SubscribeToCycleEvents()
    {
        if (!sourceCycleSubscribed &&
            sourceCycleController != null)
        {
            sourceCycleController.AllCyclesCompleted +=
                HandleSourceAllCyclesCompleted;
            sourceCycleSubscribed = true;
        }

        if (useSwitchScheduleCsv &&
            !sourceSchedulePhaseSubscribed &&
            sourceCycleController != null)
        {
            sourceCycleController.PhaseStarted +=
                HandleSourceSchedulePhaseStarted;
            sourceSchedulePhaseSubscribed = true;
        }

        if (!targetCycleSubscribed &&
            targetCycleController != null)
        {
            targetCycleController.AllCyclesCompleted +=
                HandleTargetAllCyclesCompleted;
            targetCycleSubscribed = true;
        }
    }

    private void UnsubscribeFromCycleEvents()
    {
        if (sourceCycleSubscribed &&
            sourceCycleController != null)
        {
            sourceCycleController.AllCyclesCompleted -=
                HandleSourceAllCyclesCompleted;
        }

        if (sourceSchedulePhaseSubscribed &&
            sourceCycleController != null)
        {
            sourceCycleController.PhaseStarted -=
                HandleSourceSchedulePhaseStarted;
        }

        if (targetCycleSubscribed &&
            targetCycleController != null)
        {
            targetCycleController.AllCyclesCompleted -=
                HandleTargetAllCyclesCompleted;
        }

        sourceCycleSubscribed = false;
        sourceSchedulePhaseSubscribed = false;
        targetCycleSubscribed = false;
    }

    private void ResolveWorkPhaseTrackers()
    {
        if (craneRegistry == null ||
            sourceCondition == null ||
            targetCondition == null)
        {
            return;
        }

        CraneWorkPhaseTracker resolvedSource =
            FindWorkPhaseTracker(sourceCondition.craneIndex);
        CraneWorkPhaseTracker resolvedTarget =
            FindWorkPhaseTracker(targetCondition.craneIndex);

        bool sourceChanged =
            resolvedSource != null &&
            resolvedSource != sourceWorkPhaseTracker;
        bool targetChanged =
            resolvedTarget != null &&
            resolvedTarget != targetWorkPhaseTracker;

        if (!sourceChanged && !targetChanged)
        {
            return;
        }

        UnsubscribeFromWorkPhaseEvents();

        if (sourceChanged)
        {
            sourceWorkPhaseTracker = resolvedSource;
        }

        if (targetChanged)
        {
            targetWorkPhaseTracker = resolvedTarget;
        }

        SubscribeToWorkPhaseEvents();
    }

    private CraneWorkPhaseTracker FindWorkPhaseTracker(int craneIndex)
    {
        CraneInstance crane =
            craneRegistry.GetCraneByRuntimeIndex(craneIndex);

        if (crane == null)
        {
            return null;
        }

        CraneWorkPhaseTracker resolvedTracker =
            crane.GetComponent<CraneWorkPhaseTracker>();

        if (resolvedTracker == null)
        {
            resolvedTracker =
                crane.GetComponentInChildren<
                    CraneWorkPhaseTracker
                >(true);
        }

        return resolvedTracker;
    }

    private void HideAutomaticOperationObjects()
    {
        previousActiveStates.Clear();

        if (objectsHiddenDuringExperiment == null)
        {
            return;
        }

        foreach (GameObject target in objectsHiddenDuringExperiment)
        {
            if (target == null || previousActiveStates.ContainsKey(target))
            {
                continue;
            }

            previousActiveStates.Add(target, target.activeSelf);
            target.SetActive(false);
        }
    }

    private void RestoreAutomaticOperationObjects()
    {
        foreach (KeyValuePair<GameObject, bool> pair in previousActiveStates)
        {
            if (pair.Key != null)
            {
                pair.Key.SetActive(pair.Value);
            }
        }

        previousActiveStates.Clear();
    }

    private void HideTransitionPanels()
    {
        SetPanelActive(confirmationPanel, false);
        SetPanelActive(countdownPanel, false);
    }

    private static void SetPanelActive(GameObject panel, bool active)
    {
        if (panel != null)
        {
            panel.SetActive(active);
        }
    }

    private void UpdateCountdownText()
    {
        if (countdownText != null)
        {
            countdownText.text = Mathf.CeilToInt(countdownRemaining).ToString();
        }
    }

    private void SetState(TaskSwitchExperimentState newState)
    {
        currentState = newState;

        if (CanAcceptConfirmationInput())
        {
            confirmationAcceptedUntilRealtime = 0f;
        }

        UpdateStateText();
        UpdateConfirmationButtonVisual();
    }

    private bool CanAcceptConfirmationInput()
    {
        return !ExperimentPauseManager.IsPaused &&
            (currentState == TaskSwitchExperimentState.WaitingForOperatorSwitch ||
             currentState == TaskSwitchExperimentState.WaitingForConfirmation ||
             currentState == TaskSwitchExperimentState.WaitingForSourceConfirmation);
    }

    private void ScheduleOperationInputUnlockAfterButtonRelease()
    {
        pendingOperationInputUnlock = true;

        if (craneOperationManager != null)
        {
            craneOperationManager.SetTaskSwitchOperationInputLocked(true);
        }
    }

    private void UpdatePendingOperationInputUnlock()
    {
        if (!pendingOperationInputUnlock)
        {
            return;
        }

        if (!Input.GetButton(confirmationInputButton))
        {
            pendingOperationInputUnlock = false;

            if (craneOperationManager != null)
            {
                craneOperationManager.SetTaskSwitchOperationInputLocked(
                    false
                );
                EmitEvent(craneOperationManager.CurrentCraneIndex == sourceCondition.craneIndex
                    ? "SourceInputUnlocked" : "TargetInputUnlocked");
            }
        }
    }

    private void ShowConfirmationAcceptedFeedback()
    {
        confirmationAcceptedUntilRealtime =
            (float)ExperimentPauseManager.ActiveRealtime +
            Mathf.Max(0f, confirmationAcceptedDisplaySeconds);

        UpdateConfirmationButtonVisual();
    }

    private void ResolveConfirmationButtonReferences()
    {
        if (confirmationPanel == null)
        {
            return;
        }

        if (confirmationButton == null)
        {
            confirmationButton =
                confirmationPanel.GetComponent<Button>();

            if (confirmationButton == null)
            {
                confirmationButton =
                    confirmationPanel.GetComponentInChildren<Button>(
                        true
                    );
            }
        }

        if (confirmationButtonText == null &&
            confirmationButton != null)
        {
            confirmationButtonText =
                confirmationButton.GetComponentInChildren<Text>(true);
        }
    }

    private void UpdateConfirmationButtonVisual()
    {
        ResolveConfirmationButtonReferences();

        bool acceptedFeedbackActive =
            (float)ExperimentPauseManager.ActiveRealtime <
            confirmationAcceptedUntilRealtime;

        bool confirmationAvailable =
            CanAcceptConfirmationInput();

        string displayText;
        Color displayColor;
        bool interactable;

        if (acceptedFeedbackActive)
        {
            displayText = confirmationAcceptedText;
            displayColor = confirmationAcceptedColor;
            interactable = false;
        }
        else if (currentState == TaskSwitchExperimentState.WaitingForOperatorSwitch)
        {
            displayText = "赤ボタンで作業切替";
            displayColor = confirmationReadyColor;
            interactable = confirmationAvailable;
        }
        else if (currentState ==
                 TaskSwitchExperimentState.WaitingForConfirmation)
        {
            displayText = targetConfirmationReadyText;
            displayColor = confirmationReadyColor;
            interactable = true;
        }
        else if (currentState ==
                 TaskSwitchExperimentState.WaitingForSourceConfirmation)
        {
            displayText = sourceConfirmationReadyText;
            displayColor = confirmationReadyColor;
            interactable = true;
        }
        else
        {
            displayText = confirmationStandbyText;
            displayColor = confirmationStandbyColor;
            interactable = false;
        }

        if (confirmationButtonText != null)
        {
            confirmationButtonText.text = displayText;
        }

        if (confirmationButton == null)
        {
            return;
        }

        ColorBlock colors = confirmationButton.colors;
        colors.normalColor = displayColor;
        colors.highlightedColor = displayColor;
        colors.selectedColor = displayColor;
        colors.pressedColor = confirmationAcceptedColor;
        colors.disabledColor = displayColor;
        confirmationButton.colors = colors;
        confirmationButton.interactable = interactable && confirmationAvailable;

        if (confirmationButton.targetGraphic != null)
        {
            confirmationButton.targetGraphic.color = displayColor;
        }
    }

    private void UpdateStateText()
    {
        if (experimentStateText == null)
        {
            return;
        }

        experimentStateText.text =
            $"{switchMethod} / {currentState}\n" +
            $"Switch: {currentSwitchIndex}  " +
            $"Completed: {completedSwitchCount}\n" +
            $"Source: {sourceCondition.workPhase}, " +
            $"{sourceCondition.errorType}  ->  " +
            $"Target: " +
            $"{ActiveTargetTaskPattern}, " +
            $"{targetCondition.errorType}";
    }

    private void EmitEvent(string eventName, string detail = "")
    {
        TaskSwitchEventData eventData = new TaskSwitchEventData
        {
            eventName = eventName,
            switchMethod = switchMethod,
            state = currentState,
            sourceCraneIndex = sourceCondition.craneIndex,
            targetCraneIndex = targetCondition.craneIndex,
            sourcePhase = sourceWorkPhaseTracker != null
                ? sourceWorkPhaseTracker.CurrentMajorPhase
                : sourcePhaseTracker != null
                    ? sourcePhaseTracker.CurrentPhase
                : sourceCondition.workPhase,
            targetPhase = targetWorkPhaseTracker != null
                ? targetWorkPhaseTracker.CurrentMajorPhase
                : targetPhaseTracker != null
                    ? targetPhaseTracker.CurrentPhase
                : targetCondition.workPhase,
            sourceErrorType = sourceCondition.errorType,
            targetErrorType = targetCondition.errorType,
            realtimeSinceExperimentStart =
                Time.realtimeSinceStartup - experimentStartRealtime,
            detail = detail
        };

        ExperimentEventOccurred?.Invoke(eventData);

        Debug.Log(
            $"TaskSwitch: {eventName}, Method={switchMethod}, " +
            $"State={currentState}, Switch={currentSwitchIndex}, " +
            $"Source={sourceCondition.craneIndex + 1}" +
            $"({eventData.sourcePhase}/{sourceCondition.errorType}), " +
            $"Target={targetCondition.craneIndex + 1}" +
            $"({eventData.targetPhase}/{targetCondition.errorType}), " +
            $"Time={eventData.realtimeSinceExperimentStart:F3}, " +
            $"Detail={detail}"
        );
    }

    private bool ValidateConfiguration()
    {
        if (craneOperationManager == null || craneRegistry == null ||
            interventionScenarioManager == null)
        {
            Debug.LogError(
                "TaskSwitchExperimentManagerの既存Manager参照が不足しています。"
            );
            return false;
        }

        if (sourceCondition == null || targetCondition == null)
        {
            Debug.LogError("Source/Target Conditionが設定されていません。");
            return false;
        }

        if (sourceCondition.craneIndex == targetCondition.craneIndex)
        {
            Debug.LogError("切替元と切替先には別のクレーンを指定してください。");
            return false;
        }

        if (targetCycleController == null)
        {
            Debug.LogError(
                "Targetの部分作業に必要な" +
                "CraneWorkCycleControllerがありません。",
                this
            );
            return false;
        }

        if (sourceCycleController == null)
        {
            Debug.LogError("Sourceの5サイクル進行にCraneWorkCycleControllerが必要です。", this);
            return false;
        }

        return true;
    }

    private void FindReferences()
    {
        if (craneOperationManager == null)
        {
            craneOperationManager =
                FindObjectOfType<CraneOperationManager>(true);
        }

        if (craneRegistry == null)
        {
            craneRegistry = FindObjectOfType<CraneRegistry>(true);
        }

        if (interventionScenarioManager == null)
        {
            interventionScenarioManager =
                FindObjectOfType<CraneInterventionScenarioManager>(true);
        }
    }

    private static CraneStatusManager.WorkPhase ToWorkPhase(int index)
    {
        int maxIndex =
            Enum.GetValues(typeof(CraneStatusManager.WorkPhase)).Length - 1;
        return (CraneStatusManager.WorkPhase)Mathf.Clamp(index, 0, maxIndex);
    }

    private static CraneStatusManager.ErrorType ToErrorType(int index)
    {
        int maxIndex =
            Enum.GetValues(typeof(CraneStatusManager.ErrorType)).Length - 1;
        return (CraneStatusManager.ErrorType)Mathf.Clamp(index, 0, maxIndex);
    }
}

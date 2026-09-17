using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 2基間の切替タイミングだけを管理します。
/// 作業開始状態は既存のCraneInterventionScenarioManagerへ委譲します。
/// </summary>
[DisallowMultipleComponent]
public class TaskSwitchExperimentManager : MonoBehaviour
{
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
    [SerializeField] private GameObject countdownPanel;
    [SerializeField] private Text countdownText;
    [SerializeField] private Text experimentStateText;

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
    private bool sourceMajorPhaseCompleted;
    private int activeCraneCountBeforeExperiment = -1;

    public TaskSwitchMethod SwitchMethod => switchMethod;
    public TaskSwitchExperimentState CurrentState => currentState;
    public float CountdownRemaining => countdownRemaining;
    public TaskSwitchCraneCondition SourceCondition => sourceCondition;
    public TaskSwitchCraneCondition TargetCondition => targetCondition;

    public event Action<TaskSwitchEventData> ExperimentEventOccurred;

    private void Awake()
    {
        FindReferences();
        HideTransitionPanels();
        UpdateStateText();
    }

    private void OnEnable()
    {
        ResolveWorkPhaseTrackers();
        SubscribeToWorkPhaseEvents();
    }

    private void OnDisable()
    {
        UnsubscribeFromWorkPhaseEvents();
    }

    private void OnDestroy()
    {
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
        FindReferences();
        ResolveWorkPhaseTrackers();
        SubscribeToWorkPhaseEvents();

        if (!ValidateConfiguration())
        {
            return;
        }

        experimentStartRealtime = Time.realtimeSinceStartup;
        sourceMajorPhaseCompleted = false;
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

        StartSourceWorkPhaseMonitoring();

        EmitEvent("ExperimentStarted");
        EmitEvent("SourceOperationStarted");
    }

    public void RequestSwitch()
    {
        if (currentState != TaskSwitchExperimentState.OperatingSource)
        {
            Debug.LogWarning(
                $"現在の状態では切替要求を受け付けません: {currentState}"
            );
            return;
        }

        EmitEvent("SwitchRequested");

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
        }
    }

    public void ConfirmTargetTask()
    {
        if (currentState !=
            TaskSwitchExperimentState.WaitingForConfirmation)
        {
            return;
        }

        EmitEvent("ConfirmationPressed");
        SetPanelActive(confirmationPanel, false);
        craneOperationManager.SetTaskSwitchOperationInputLocked(false);
        SetState(TaskSwitchExperimentState.OperatingTarget);
        StartTargetWorkPhaseMonitoring();
        EmitEvent("TargetOperationStarted", "Confirmation");
    }

    public void NotifySourcePhaseBoundary()
    {
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

        craneOperationManager.SetTaskSwitchOperationInputLocked(true);
        StopSourceWorkPhaseMonitoring();
        StopTargetWorkPhaseMonitoring();
        EmitEvent("ExperimentCompleted");
        SetState(TaskSwitchExperimentState.Completed);
        HideTransitionPanels();
    }

    public void ExitExperimentMode()
    {
        HideTransitionPanels();
        StopSourceWorkPhaseMonitoring();
        StopTargetWorkPhaseMonitoring();

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
        StopSourceWorkPhaseMonitoring();

        if (!craneOperationManager.SelectCraneForTaskSwitch(
                targetCondition.craneIndex
            ))
        {
            RecoverAfterFailedTargetSelection("Confirmation");
            return;
        }

        SetState(TaskSwitchExperimentState.WaitingForConfirmation);
        SetPanelActive(confirmationPanel, true);
        EmitEvent("TargetDisplaySwitched", "Confirmation");
        EmitEvent("ConfirmationDisplayed");
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
        StopSourceWorkPhaseMonitoring();

        if (!craneOperationManager.SelectCraneForTaskSwitch(
                targetCondition.craneIndex
            ))
        {
            RecoverAfterFailedTargetSelection(detail);
            return;
        }

        SetPanelActive(countdownPanel, false);
        EmitEvent("TargetDisplaySwitched", detail);
        craneOperationManager.SetTaskSwitchOperationInputLocked(false);
        SetState(TaskSwitchExperimentState.OperatingTarget);
        StartTargetWorkPhaseMonitoring();
        EmitEvent("TargetOperationStarted", detail);
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
            interventionStartLocalZ
        );

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

    private void HandleSourceMajorPhaseCompleted(
        CraneWorkPhaseTracker tracker,
        CraneStatusManager.WorkPhase completedPhase
    )
    {
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

        if (completedPhase != sourceCondition.workPhase)
        {
            return;
        }

        sourceMajorPhaseCompleted = true;
        EmitEvent(
            "SourceMajorPhaseCompleted",
            completedPhase.ToString()
        );

        CompletePhaseBoundarySwitch(
            $"RealWork:{completedPhase}"
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

        if (completedPhase != targetCondition.workPhase)
        {
            return;
        }

        EmitEvent(
            "TargetMajorPhaseCompleted",
            completedPhase.ToString()
        );

        ReturnControlToSource(completedPhase);
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

    private void StopTargetWorkPhaseMonitoring()
    {
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

        string resumeDetail = ResumeSourceWorkAfterReturn();

        craneOperationManager.SetTaskSwitchOperationInputLocked(false);
        SetState(TaskSwitchExperimentState.OperatingReturnedSource);
        EmitEvent("SourceOperationResumed", resumeDetail);
    }

    private void RecoverAfterFailedTargetSelection(string detail)
    {
        SetPanelActive(confirmationPanel, false);
        SetPanelActive(countdownPanel, false);

        // Confirm・Countdownで停止した実作業監視だけを再開します。
        // PhaseBoundaryでは完了済みのため、再監視せず切替再要求を待ちます。
        if (!sourceMajorPhaseCompleted &&
            sourceWorkPhaseTracker != null &&
            !sourceWorkPhaseTracker.IsMonitoring)
        {
            sourceWorkPhaseTracker.ResumeMonitoring();
        }

        craneOperationManager.SetTaskSwitchOperationInputLocked(false);
        SetState(TaskSwitchExperimentState.OperatingSource);
        EmitEvent("TargetSelectionFailed", detail);
    }

    private string ResumeSourceWorkAfterReturn()
    {
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
        UpdateStateText();
    }

    private void UpdateStateText()
    {
        if (experimentStateText == null)
        {
            return;
        }

        experimentStateText.text =
            $"{switchMethod} / {currentState}\n" +
            $"Source: {sourceCondition.workPhase}, " +
            $"{sourceCondition.errorType}  ->  " +
            $"Target: {targetCondition.workPhase}, " +
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
            sourcePhase = sourcePhaseTracker != null
                ? sourcePhaseTracker.CurrentPhase
                : sourceCondition.workPhase,
            targetPhase = targetPhaseTracker != null
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
            $"State={currentState}, Source={sourceCondition.craneIndex + 1}" +
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

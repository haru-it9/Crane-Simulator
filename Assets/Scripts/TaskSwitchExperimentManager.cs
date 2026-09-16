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
    private bool sourceBoundarySubscribed;
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
        SubscribeToSourcePhaseBoundary();
    }

    private void OnDisable()
    {
        UnsubscribeFromSourcePhaseBoundary();
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

        if (!ValidateConfiguration())
        {
            return;
        }

        experimentStartRealtime = Time.realtimeSinceStartup;
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
            RestoreActiveCraneCount();
            RestoreAutomaticOperationObjects();
            return;
        }

        craneOperationManager.SetTaskSwitchOperationInputLocked(false);
        SetState(TaskSwitchExperimentState.OperatingSource);
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
        EmitEvent("TargetOperationStarted", "Confirmation");
    }

    public void NotifySourcePhaseBoundary()
    {
        if (currentState !=
            TaskSwitchExperimentState.WaitingForPhaseBoundary)
        {
            return;
        }

        EmitEvent("SourcePhaseBoundaryReached");
        SwitchControlToTarget("PhaseBoundary");
    }

    public void CompleteExperiment()
    {
        if (currentState == TaskSwitchExperimentState.Idle ||
            currentState == TaskSwitchExperimentState.Completed)
        {
            return;
        }

        craneOperationManager.SetTaskSwitchOperationInputLocked(true);
        EmitEvent("ExperimentCompleted");
        SetState(TaskSwitchExperimentState.Completed);
        HideTransitionPanels();
    }

    public void ExitExperimentMode()
    {
        HideTransitionPanels();

        if (craneOperationManager != null &&
            craneOperationManager.IsTaskSwitchExperimentMode)
        {
            craneOperationManager.EndTaskSwitchExperimentMode();
        }

        if (clearScenariosOnExit)
        {
            ClearPreparedScenarios();
        }

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

        if (!craneOperationManager.SelectCraneForTaskSwitch(
                targetCondition.craneIndex
            ))
        {
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
    }

    private void SwitchControlToTarget(string detail)
    {
        craneOperationManager.SetTaskSwitchOperationInputLocked(true);

        if (!craneOperationManager.SelectCraneForTaskSwitch(
                targetCondition.craneIndex
            ))
        {
            return;
        }

        SetPanelActive(countdownPanel, false);
        EmitEvent("TargetDisplaySwitched", detail);
        craneOperationManager.SetTaskSwitchOperationInputLocked(false);
        SetState(TaskSwitchExperimentState.OperatingTarget);
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

    private void SubscribeToSourcePhaseBoundary()
    {
        if (sourceBoundarySubscribed || sourcePhaseTracker == null)
        {
            return;
        }

        sourcePhaseTracker.PhaseBoundaryReached +=
            HandleSourcePhaseBoundary;
        sourceBoundarySubscribed = true;
    }

    private void UnsubscribeFromSourcePhaseBoundary()
    {
        if (!sourceBoundarySubscribed || sourcePhaseTracker == null)
        {
            return;
        }

        sourcePhaseTracker.PhaseBoundaryReached -=
            HandleSourcePhaseBoundary;
        sourceBoundarySubscribed = false;
    }

    private void HandleSourcePhaseBoundary(
        TaskSwitchPhaseTracker tracker,
        CraneStatusManager.WorkPhase previousPhase,
        CraneStatusManager.WorkPhase newPhase
    )
    {
        if (currentState !=
            TaskSwitchExperimentState.WaitingForPhaseBoundary)
        {
            return;
        }

        EmitEvent(
            "SourcePhaseBoundaryReached",
            $"{previousPhase}->{newPhase}"
        );
        SwitchControlToTarget("PhaseBoundary");
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

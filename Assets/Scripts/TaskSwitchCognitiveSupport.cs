using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public enum TaskSwitchSupportContent
{
    PhaseOnly,
    InputDirectionOnly,
    PhaseAndInputDirection
}

/// <summary>
/// Optional, presentation-only support at Target display switch and Source display return.
/// Does not write targets, accept input, release locks, or advance work phases.
/// </summary>
[DisallowMultipleComponent]
public class TaskSwitchCognitiveSupport : MonoBehaviour
{
    [Header("実験・操作Manager")]
    [SerializeField] private TaskSwitchExperimentManager experimentManager;
    [SerializeField] private CraneOperationManager operationManager;

    [Header("支援内容（3パターン）")]
    [SerializeField] private TaskSwitchSupportContent supportContent =
        TaskSwitchSupportContent.PhaseAndInputDirection;

    [Header("表示時間")]
    [Tooltip("画面切替・復帰からの表示秒数。一時停止時間を除く固定時間です。")]
    [SerializeField, Min(0.1f)] private float displaySeconds = 8f;

    [Header("配置・表示（RectTransformで位置や大きさを編集できます）")]
    [SerializeField] private GameObject supportRoot;
    [SerializeField] private Text phaseText;
    [SerializeField] private RectTransform leftXGroup;
    [SerializeField] private TaskSwitchSupportArrow leftXArrow;
    [SerializeField] private RectTransform rightYGroup;
    [SerializeField] private TaskSwitchSupportArrow rightYArrow;
    [SerializeField] private RectTransform rightZGroup;
    [SerializeField] private TaskSwitchSupportArrow rightZArrow;

    [Header("正の移動指令に対応するレバー方向")]
    [Tooltip("既定のJoystick3Verticalは符号反転されてX指令になります。実機に合わせて反転可能です。")]
    [SerializeField] private bool positiveXPointsUp = true;
    [Tooltip("既定のJoystick2Verticalは符号反転なし。通常の非反転Y軸では手前側が正です。")]
    [SerializeField] private bool positiveYPointsUp = false;
    [Tooltip("既定のJoystick2Horizontalは符号反転されてZ指令になります。")]
    [SerializeField] private bool positiveZPointsRight = false;

    private bool subscribed, visible, targetSide, awaitingConfirmation;
    private double hideAt;
    private CraneInstance selectedCrane;
    private CraneWorkPhaseTracker tracker;
    private CraneWorkCycleController cycle;
    private CraneWorkTargetManager target;
    private CraneWorkStepDefinition previewStep;
    private CraneStatusManager.WorkPhase previewPhase;
    private string lastPresentation = string.Empty;

    public TaskSwitchSupportContent SupportContent => supportContent;
    public float DisplaySeconds => displaySeconds;
    public bool IsVisible => visible;

    private void OnEnable()
    {
        if (experimentManager == null)
            experimentManager = GetComponent<TaskSwitchExperimentManager>();
        if (operationManager == null)
            operationManager = FindObjectOfType<CraneOperationManager>();
        if (experimentManager != null)
        {
            experimentManager.ExperimentEventOccurred += HandleExperimentEvent;
            subscribed = true;
        }
        Hide("Enabled");
    }

    private void OnDisable()
    {
        Hide("Disabled");
        if (subscribed && experimentManager != null)
            experimentManager.ExperimentEventOccurred -= HandleExperimentEvent;
        subscribed = false;
    }

    private void LateUpdate()
    {
        if (!visible) return;
        if (operationManager == null || !operationManager.IsTaskSwitchExperimentMode ||
            experimentManager == null || !experimentManager.isActiveAndEnabled ||
            experimentManager.CurrentState == TaskSwitchExperimentState.Idle ||
            experimentManager.CurrentState == TaskSwitchExperimentState.Completed)
        {
            Hide("ExperimentInactive");
            return;
        }
        if (ExperimentPauseManager.IsPaused) return;
        if (ExperimentPauseManager.ActiveRealtime >= hideAt)
        {
            Hide("Timeout");
            return;
        }
        RefreshPresentation();
    }

    private void HandleExperimentEvent(TaskSwitchEventData data)
    {
        switch (data.eventName)
        {
            case "TargetDisplaySwitched": Show(true); break;
            case "SourceDisplayRestored": Show(false); break;
            case "TargetOperationStarted":
            case "SourceOperationResumed":
                awaitingConfirmation = false;
                if (visible) RefreshPresentation(); // Preserve the original switch-time deadline.
                break;
            case "ExperimentPreparing":
            case "ExperimentCompleted":
            case "ExperimentExited":
            case "TargetSelectionFailed":
            case "TargetOperationStartFailed":
            case "SourceReturnFailed": Hide(data.eventName); break;
        }
    }

    private void Show(bool isTarget)
    {
        Hide("Replaced");
        if (supportRoot == null || experimentManager == null || operationManager == null)
            return;
        selectedCrane = operationManager.CurrentCraneInstance;
        if (selectedCrane == null) return;
        tracker = selectedCrane.GetComponentInChildren<CraneWorkPhaseTracker>(true);
        cycle = selectedCrane.GetComponentInChildren<CraneWorkCycleController>(true);
        target = selectedCrane.GetComponentInChildren<CraneWorkTargetManager>(true);
        targetSide = isTarget;
        awaitingConfirmation = true;
        previewStep = null;
        lastPresentation = string.Empty;
        hideAt = ExperimentPauseManager.ActiveRealtime + Mathf.Max(0.1f, displaySeconds);
        visible = true;
        supportRoot.SetActive(true);
        Record("Shown", "Anchor=DisplaySwitch;Role=" + (targetSide ? "Target" : "Source") +
            ";Content=" + supportContent + ";DurationSeconds=" +
            displaySeconds.ToString("F3", CultureInfo.InvariantCulture) +
            ";PositiveXUp=" + positiveXPointsUp + ";PositiveYUp=" + positiveYPointsUp +
            ";PositiveZRight=" + positiveZPointsRight);
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        // Never let an unexpected active-crane change leave the previous crane's arrows visible.
        if (operationManager.CurrentCraneInstance != selectedCrane)
        {
            Hide("CraneChanged");
            return;
        }
        CraneStatusManager.WorkPhase phase = tracker != null
            ? tracker.CurrentMajorPhase : targetSide
                ? experimentManager.TargetCondition.workPhase
                : experimentManager.SourceCondition.workPhase;
        bool pendingPhase = awaitingConfirmation && !targetSide &&
            cycle != null && cycle.HasPendingNextPhase;
        if (awaitingConfirmation && targetSide) phase = experimentManager.TargetCondition.workPhase;
        if (pendingPhase) phase = cycle.PendingNextPhase;

        CraneWorkStepDefinition step = tracker != null ? tracker.CurrentStepDefinition : null;
        bool usePreview = awaitingConfirmation && (targetSide || pendingPhase ||
            step == null || step.majorPhase != phase);
        if (usePreview)
        {
            if (previewStep == null || previewPhase != phase)
            {
                previewPhase = phase;
                previewStep = tracker != null ? tracker.GetFirstStepDefinition(phase) : null;
            }
            step = previewStep;
        }
        else if (step != null && step.majorPhase != phase)
        {
            step = null;
        }

        float goalX = 0f, goalZ = 0f;
        bool hasGoal = awaitingConfirmation
            ? experimentManager.TryGetCognitiveSupportTargetPosition(
                operationManager.CurrentCraneIndex, phase, out goalX, out goalZ)
            : tracker != null && tracker.TryGetTargetPosition(out goalX, out goalZ);
        // With no CSV and a pending phase, its future random goal is not known yet.
        if (!hasGoal && !pendingPhase && target != null)
            hasGoal = target.TryGetTarget(out goalX, out goalZ);
        Transform position = selectedCrane.InformationTarget;
        CraneUnit unit = selectedCrane.CraneUnit;
        float localY = 0f;
        bool hasY = unit != null && unit.TryGetMainLifMagLocalY(out localY);
        Vector3 movement = position != null && tracker != null
            ? TaskSwitchSupportGuidance.RequiredMovement(step, position.position,
                hasGoal, goalX, goalZ, unit == null || unit.IsDownwardMovementBlockedByTouchdown,
                hasY, localY) : Vector3.zero;

        bool showPhase = supportContent != TaskSwitchSupportContent.InputDirectionOnly;
        bool showDirection = supportContent != TaskSwitchSupportContent.PhaseOnly;
        string label = TaskSwitchSupportGuidance.PhaseLabel(phase);
        if (phaseText != null)
        {
            phaseText.gameObject.SetActive(showPhase);
            phaseText.text = label;
        }
        SetArrow(leftXGroup, leftXArrow, showDirection ? movement.x : 0f,
            positiveXPointsUp, true);
        SetArrow(rightYGroup, rightYArrow, showDirection ? movement.y : 0f,
            positiveYPointsUp, true);
        SetArrow(rightZGroup, rightZArrow, showDirection ? movement.z : 0f,
            positiveZPointsRight, false);

        string presentation = "Phase=" + phase + ";Step=" + (step != null ? step.stepId : "") +
            ";X=" + movement.x + ";Y=" + movement.y + ";Z=" + movement.z +
            ";Content=" + supportContent + ";GoalAvailable=" + hasGoal;
        if (presentation != lastPresentation)
        {
            lastPresentation = presentation;
            Record("Updated", presentation);
        }
    }

    private static void SetArrow(RectTransform group, TaskSwitchSupportArrow arrow,
        float command, bool positiveDirection, bool vertical)
    {
        if (group != null) group.gameObject.SetActive(command != 0f);
        if (arrow == null) return;
        bool positive = (command > 0f) == positiveDirection;
        float angle = vertical ? (positive ? 0f : 180f) : (positive ? -90f : 90f);
        arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, angle);
    }

    private void Hide(string reason)
    {
        bool wasVisible = visible;
        visible = false;
        if (supportRoot != null) supportRoot.SetActive(false);
        if (wasVisible) Record("Hidden", "Reason=" + reason);
    }

    private void Record(string action, string detail)
    {
        if (experimentManager != null)
            experimentManager.RecordCognitiveSupportEvent(action, detail);
    }
}

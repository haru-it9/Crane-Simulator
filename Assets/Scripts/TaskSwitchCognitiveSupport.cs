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

    [Header("入力方向の表示パターン")]
    [SerializeField] private TaskSwitchDirectionPresentation directionPresentation =
        TaskSwitchDirectionPresentation.SingleArrow;
    private const int maximumChevrons = 3;
    [Tooltip("切替・復帰時点の残り距離に対する山形記号1個分の距離。個数は次の切替・復帰まで固定します。")]
    [SerializeField, Min(0.01f)] private float xDistancePerChevron = 0.5f;
    [SerializeField, Min(0.01f)] private float yDistancePerChevron = 0.5f;
    [SerializeField, Min(0.01f)] private float zDistancePerChevron = 2f;

    [Header("表示時間")]
    [Tooltip("操作入力が解禁されてから完全に消えるまでの秒数。確認待ち・一時停止時間は含みません。")]
    [SerializeField, Min(0.1f)] private float displaySeconds = 8f;
    [Tooltip("完全に消える時刻はDisplay Secondsのまま、最後のこの秒数でフェードします。")]
    [SerializeField, Range(0.5f, 1f)] private float fadeOutSeconds = 0.75f;

    [Header("配置・表示（RectTransformで位置や大きさを編集できます）")]
    [SerializeField] private GameObject supportRoot;
    [SerializeField] private Text phaseText;
    [SerializeField] private RectTransform leftXGroup;
    [SerializeField] private TaskSwitchSupportArrow leftXArrow;
    [SerializeField] private RectTransform rightYGroup;
    [SerializeField] private TaskSwitchSupportArrow rightYArrow;
    [SerializeField] private RectTransform rightZGroup;
    [SerializeField] private TaskSwitchSupportArrow rightZArrow;
    [SerializeField] private Text rightYLabel;
    [SerializeField] private LifMagAccumValueText[] currentValueDisplays = new LifMagAccumValueText[0];

    [Header("切替時の電流入力案内")]
    [SerializeField] private Text currentInputLabel;
    [Tooltip("コントローラの電流スライダーを動かし始めてからフェードを開始するまでの秒数。")]
    [SerializeField, Min(0f)] private float currentInputLabelDelaySeconds = 1f;
    [Tooltip("待ち時間の後、見出しが白へ戻るまでのフェード秒数。")]
    [SerializeField, Range(0.5f, 1f)] private float currentInputLabelFadeSeconds = 0.75f;
    [Tooltip("切替時の生のスライダー軸値からの変化量。電流制御の受付やロック解除を待たず、この値以上の動きでフェードします。")]
    [SerializeField, Min(0.001f)] private float currentInputMovementThreshold = 0.01f;

    [Header("電流の適正範囲")]
    [Tooltip("ON: 適正電流のみ座標と同じ緑色にし、切替時のフェーズ色による強調は行いません。OFF: 従来の着床開始時の強調。")]
    [SerializeField] private bool useCurrentRangeColoring = true;
    [Tooltip("座標の目標範囲内の緑色を共有します。未指定ならシーン内のCraneInformationDisplayを自動取得します。")]
    [SerializeField] private CraneInformationDisplay coordinateColorSource;
    [Tooltip("保持板の下面から板山・台までの距離がこの値以上なら、電流上限をMAXまで許容します。ワールド座標単位。")]
    [SerializeField, Min(0.01f)] private float currentRangeClearance = 0.1f;

    [Header("正の移動指令に対応するレバー方向")]
    [Tooltip("既定のJoystick3Verticalは符号反転されてX指令になります。実機に合わせて反転可能です。")]
    [SerializeField] private bool positiveXPointsUp = true;
    [Tooltip("既定のJoystick2Verticalは符号反転なし。通常の非反転Y軸では手前側が正です。")]
    [SerializeField] private bool positiveYPointsUp = false;
    [Tooltip("既定のJoystick2Horizontalは符号反転されてZ指令になります。")]
    [SerializeField] private bool positiveZPointsRight = false;

    private bool subscribed, visible, targetSide, awaitingConfirmation;
    private double hideAt;
    private bool arrowTimerStarted;
    private bool currentHighlightWindowActive;
    private bool currentFeedbackActive;
    private bool currentHighlightLatched, hasCurrentLiftSample;
    private float currentLiftBaselineLocalY, previousCurrentLiftLocalY;
    private CraneStatusManager.WorkPhase currentHighlightPhase;
    private const float MinimumConfirmedRiseLocalY = 0.01f;
    private CraneInstance selectedCrane;
    private CraneWorkPhaseTracker tracker;
    private CraneWorkCycleController cycle;
    private CraneWorkTargetManager target;
    private CraneWorkStepDefinition previewStep;
    private CraneStatusManager.WorkPhase previewPhase;
    private string lastPresentation = string.Empty;
    private bool chevronCountsCaptured, capturedVerticalDistanceKnown;
    private int capturedXCount = 1, capturedYCount = 1, capturedZCount = 1;
    private CraneWorkLoadPlanManager loadPlan;
    private ArrowFade leftFade, rightYFade, rightZFade;
    private bool currentLabelDecisionPending, currentLabelHighlighted, currentLabelFading;
    private bool currentLabelFadeLogged;
    private double currentLabelFadeStarted;
    private Color currentLabelFadeColor;
    private LifMagSystem currentLabelInputSource;
    private float currentLabelControllerBaseline;
    private bool hasCurrentLabelControllerBaseline;

    private sealed class ArrowFade
    {
        public RectTransform group;
        public CanvasGroup canvas;
        public bool requested;
        public double fadeStarted;
    }

    public TaskSwitchSupportContent SupportContent => supportContent;
    public float DisplaySeconds => displaySeconds;
    public bool IsVisible => visible;

    private void OnEnable()
    {
        if (experimentManager == null)
            experimentManager = GetComponent<TaskSwitchExperimentManager>();
        if (operationManager == null)
            operationManager = FindObjectOfType<CraneOperationManager>();
        if (coordinateColorSource == null)
            coordinateColorSource = FindObjectOfType<CraneInformationDisplay>();
        if (experimentManager != null)
        {
            experimentManager.ExperimentEventOccurred += HandleExperimentEvent;
            subscribed = true;
        }
        leftFade = CreateFade(leftXGroup);
        rightYFade = CreateFade(rightYGroup);
        rightZFade = CreateFade(rightZGroup);
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
        if (!visible && !currentHighlightWindowActive && !currentFeedbackActive && !useCurrentRangeColoring) return;
        if (operationManager == null || !operationManager.IsTaskSwitchExperimentMode ||
            experimentManager == null || !experimentManager.isActiveAndEnabled ||
            experimentManager.CurrentState == TaskSwitchExperimentState.Idle ||
            experimentManager.CurrentState == TaskSwitchExperimentState.Completed)
        {
            Hide("ExperimentInactive");
            return;
        }
        if (ExperimentPauseManager.IsPaused)
        {
            // Movement during pause is not a new operation when the experiment resumes.
            if (currentLabelHighlighted && !currentLabelFading) CaptureCurrentControllerBaseline();
            return;
        }
        EnsureNormalCurrentFeedback();
        if (visible && ExperimentPauseManager.ActiveRealtime >= hideAt)
            HideArrows("Timeout");
        if (visible || currentHighlightWindowActive || currentFeedbackActive) RefreshPresentation();
        UpdateCurrentInputLabel();
    }

    private void EnsureNormalCurrentFeedback()
    {
        if (!useCurrentRangeColoring || supportContent == TaskSwitchSupportContent.PhaseOnly) return;
        if (currentFeedbackActive && selectedCrane == operationManager.CurrentCraneInstance) return;
        TaskSwitchExperimentState state = experimentManager.CurrentState;
        // Confirmation/return transitions still use Show's CSV preview and event anchors.
        bool normalWork = state == TaskSwitchExperimentState.OperatingSource ||
            state == TaskSwitchExperimentState.OperatingReturnedSource ||
            state == TaskSwitchExperimentState.OperatingTarget ||
            state == TaskSwitchExperimentState.CountingDown ||
            state == TaskSwitchExperimentState.WaitingForPhaseBoundary ||
            state == TaskSwitchExperimentState.WaitingForOperatorSwitch;
        if (!normalWork) return;
        Hide("NormalCurrentFeedback");
        BindSelectedCrane();
        if (selectedCrane == null) return;
        targetSide = state == TaskSwitchExperimentState.OperatingTarget;
        awaitingConfirmation = false;
        previewStep = null;
        lastPresentation = string.Empty;
        // Normal work only updates current status. No arrows, timer or switch snapshot.
        chevronCountsCaptured = true;
        currentFeedbackActive = true;
    }

    private void BindSelectedCrane()
    {
        selectedCrane = operationManager.CurrentCraneInstance;
        tracker = selectedCrane != null ? selectedCrane.GetComponentInChildren<CraneWorkPhaseTracker>(true) : null;
        cycle = selectedCrane != null ? selectedCrane.GetComponentInChildren<CraneWorkCycleController>(true) : null;
        target = selectedCrane != null ? selectedCrane.GetComponentInChildren<CraneWorkTargetManager>(true) : null;
        loadPlan = selectedCrane != null ? selectedCrane.GetComponentInChildren<CraneWorkLoadPlanManager>(true) : null;
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
                if (visible || currentHighlightWindowActive || currentFeedbackActive) RefreshPresentation();
                break;
            case "TargetInputUnlocked":
                if (targetSide) StartArrowTimer();
                break;
            case "SourceInputUnlocked":
                if (!targetSide) StartArrowTimer();
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
        BindSelectedCrane();
        if (selectedCrane == null) return;
        targetSide = isTarget;
        awaitingConfirmation = true;
        previewStep = null;
        chevronCountsCaptured = false;
        lastPresentation = string.Empty;
        hideAt = double.PositiveInfinity;
        arrowTimerStarted = false;
        // Eligibility is a snapshot of confirmed touchdown at the actual display switch/return.
        // A touchdown reached later in this episode must not start current highlighting.
        bool touchdownAtSwitch = selectedCrane.CraneUnit != null &&
            selectedCrane.CraneUnit.IsDownwardMovementBlockedByTouchdown;
        currentHighlightWindowActive = touchdownAtSwitch;
        currentFeedbackActive = true;
        currentHighlightLatched = hasCurrentLiftSample = false;
        currentLabelDecisionPending = true;
        visible = true;
        supportRoot.SetActive(true);
        Record("Shown", "Anchor=DisplaySwitch;Role=" + (targetSide ? "Target" : "Source") +
            ";Content=" + supportContent + ";DurationSeconds=" +
            displaySeconds.ToString("F3", CultureInfo.InvariantCulture) +
            ";DurationAnchor=InputUnlocked;CurrentHighlightEnd=CorrectWeightRaised" +
            ";CurrentRule=ZeroOrTargetTonnesTimes10ToPlus10A_ClearLoadToMax" +
            ";CurrentColorMode=" + (useCurrentRangeColoring ? "RangeStatus" : "TouchdownReminder") +
            ";CurrentRangeClearance=" + currentRangeClearance.ToString(CultureInfo.InvariantCulture) +
            ";CurrentInputLabelFadeSeconds=" + Mathf.Clamp(currentInputLabelFadeSeconds, 0.5f, 1f).ToString(CultureInfo.InvariantCulture) +
            ";CurrentInputLabelDelaySeconds=" + Mathf.Max(0f, currentInputLabelDelaySeconds).ToString(CultureInfo.InvariantCulture) +
            ";CurrentInputMovementThreshold=" + currentInputMovementThreshold.ToString(CultureInfo.InvariantCulture) +
            ";CurrentHighlightEligibleAtSwitch=" + touchdownAtSwitch +
            ";DirectionPresentation=" + directionPresentation + ";MaximumChevrons=" + maximumChevrons +
            ";ChevronCountAnchor=DisplaySwitch;ChevronCountsFixed=True" +
            ";FadeOutSeconds=" + EffectiveFadeSeconds.ToString(CultureInfo.InvariantCulture) +
            ";DistancePerChevron=" + xDistancePerChevron.ToString(CultureInfo.InvariantCulture) + "," +
            yDistancePerChevron.ToString(CultureInfo.InvariantCulture) + "," +
            zDistancePerChevron.ToString(CultureInfo.InvariantCulture) +
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

        bool currentRangeExpanded;
        string currentGuidance = EvaluateCurrentGuidance(phase, pendingPhase, out currentRangeExpanded);
        CraneWorkStepDefinition raisingStep = null;
        bool noMovement = movement.x == 0f && movement.y == 0f && movement.z == 0f;
        float ignoredHeight;
        bool currentAdjustmentStage = noMovement && step != null && unit != null && tracker != null &&
            !TaskSwitchSupportGuidance.TryGetRaisingTarget(step, out ignoredHeight) &&
            IsCurrentAdjustmentStage(step, unit);
        bool currentOperationPhase = phase == CraneStatusManager.WorkPhase.LiftUp ||
            phase == CraneStatusManager.WorkPhase.Place || phase == CraneStatusManager.WorkPhase.PlaceToTrack;
        CaptureCurrentInputLabel(currentOperationPhase && (currentAdjustmentStage || movement.y > 0f), currentGuidance);
        // The tracker may already be on a raising step based on held weight. That must not
        // bypass the displayed-current check, including on a mid-raising Source return.
        if (currentAdjustmentStage || movement.y > 0f)
        {
            bool lifting = phase == CraneStatusManager.WorkPhase.LiftUp;
            bool placing = phase == CraneStatusManager.WorkPhase.Place || phase == CraneStatusManager.WorkPhase.PlaceToTrack;
            if (lifting || placing)
            {
                if (currentAdjustmentStage) ArmCurrentHighlight(phase, unit);
                if (currentGuidance == "ReadyToRaise" && currentAdjustmentStage)
                {
                    raisingStep = tracker.GetRaisingStepDefinition(phase);
                    movement = TaskSwitchSupportGuidance.RequiredMovement(raisingStep,
                        position != null ? position.position : Vector3.zero, hasGoal, goalX, goalZ,
                        unit.IsDownwardMovementBlockedByTouchdown, hasY, localY);
                    currentGuidance = "ReadyToRaise";
                }
                else if (currentGuidance != "ReadyToRaise")
                {
                    movement.y = 0f;
                    ArmCurrentHighlight(phase, unit);
                }
            }
        }

        bool showPhase = supportContent != TaskSwitchSupportContent.InputDirectionOnly;
        bool directionEnabled = supportContent != TaskSwitchSupportContent.PhaseOnly;
        bool showDirection = visible && directionEnabled;
        if (!chevronCountsCaptured)
            CaptureChevronCounts(raisingStep ?? step, position, unit, movement, hasGoal, goalX, goalZ);
        int xCount = capturedXCount, yCount = capturedYCount, zCount = capturedZCount;
        string label = TaskSwitchSupportGuidance.PhaseLabel(phase);
        if (phaseText != null)
        {
            phaseText.gameObject.SetActive(visible && showPhase);
            phaseText.text = label;
        }
        CheckCurrentLiftConfirmation(unit);
        bool showCurrentHighlight = !useCurrentRangeColoring && directionEnabled && currentHighlightLatched && currentHighlightWindowActive;
        bool showAppropriateCurrent = useCurrentRangeColoring && directionEnabled && currentFeedbackActive && currentGuidance == "ReadyToRaise";
        SetCurrentHighlight(showCurrentHighlight || showAppropriateCurrent ? 1f : 0f, showAppropriateCurrent);
        if (!showDirection)
        {
            ResetFade(leftFade); ResetFade(rightYFade); ResetFade(rightZFade);
        }
        SetArrow(leftFade, leftXArrow, showDirection ? movement.x : 0f,
            positiveXPointsUp, true, xCount);
        SetArrow(rightYFade, rightYArrow, showDirection ? movement.y : 0f,
            positiveYPointsUp, true, yCount);
        SetArrow(rightZFade, rightZArrow, showDirection ? movement.z : 0f,
            positiveZPointsRight, false, zCount);
        if (rightYLabel != null && movement.y != 0f && showDirection)
        {
            bool pointsUp = (movement.y > 0f) == positiveYPointsUp;
            rightYLabel.text = pointsUp ? "下降" : "上昇";
        }

        string presentation = "Phase=" + phase + ";Step=" + (step != null ? step.stepId : "") +
            ";X=" + movement.x + ";Y=" + movement.y + ";Z=" + movement.z +
            ";Content=" + supportContent + ";GoalAvailable=" + hasGoal +
            ";DirectionPresentation=" + directionPresentation +
            ";ChevronsX=" + DisplayedCount(showDirection, movement.x, xCount) +
            ";ChevronsY=" + DisplayedCount(showDirection, movement.y, yCount) +
            ";ChevronsZ=" + DisplayedCount(showDirection, movement.z, zCount) +
            ";VerticalDistanceKnown=" + capturedVerticalDistanceKnown +
            ";CurrentGuidance=" + currentGuidance +
            ";CurrentColorMode=" + (useCurrentRangeColoring ? "RangeStatus" : "TouchdownReminder") +
            ";CurrentRangeExpanded=" + currentRangeExpanded +
            ";CurrentAppropriateVisible=" + showAppropriateCurrent +
            ";CurrentHighlightVisible=" + (showCurrentHighlight && !showAppropriateCurrent) +
            ";ChevronCountAnchor=DisplaySwitch;ChevronCountsFixed=True";
        if (presentation != lastPresentation)
        {
            lastPresentation = presentation;
            Record("Updated", presentation);
        }
    }

    private void CaptureCurrentInputLabel(bool needsCurrentOperation, string guidance)
    {
        if (!currentLabelDecisionPending) return;
        currentLabelDecisionPending = false;
        bool needsAdjustment = guidance == "IncreaseCurrent" || guidance == "DecreaseCurrent" || guidance == "SetCurrentToZero";
        if (currentInputLabel == null || supportContent == TaskSwitchSupportContent.PhaseOnly ||
            !needsCurrentOperation || !needsAdjustment || selectedCrane.LifMagSystem == null) return;
        currentLabelHighlighted = true;
        currentInputLabel.color = phaseText != null ? phaseText.color : Color.white;
        currentLabelInputSource = selectedCrane.LifMagSystem;
        CaptureCurrentControllerBaseline();
        Record("CurrentInputLabelShown", "Role=" + (targetSide ? "Target" : "Source") + ";CurrentGuidance=" + guidance);
    }

    private float ReadCurrentControllerAxis()
    {
        if (currentLabelInputSource == null || string.IsNullOrEmpty(currentLabelInputSource.CurrentSliderAxis)) return float.NaN;
        return Input.GetAxisRaw(currentLabelInputSource.CurrentSliderAxis);
    }

    private void CaptureCurrentControllerBaseline()
    {
        currentLabelControllerBaseline = ReadCurrentControllerAxis();
        hasCurrentLabelControllerBaseline = !float.IsNaN(currentLabelControllerBaseline) && !float.IsInfinity(currentLabelControllerBaseline);
    }

    private void TryStartCurrentInputLabelFade()
    {
        if (currentLabelInputSource == null || operationManager == null ||
            operationManager.CurrentCraneInstance != selectedCrane) return;
        float input = ReadCurrentControllerAxis();
        if (float.IsNaN(input) || float.IsInfinity(input)) return;
        if (!hasCurrentLabelControllerBaseline)
        {
            CaptureCurrentControllerBaseline();
            return;
        }
        float delta = Mathf.Abs(input - currentLabelControllerBaseline);
        if (delta < Mathf.Max(0.001f, currentInputMovementThreshold)) return;
        currentLabelFading = true;
        currentLabelFadeStarted = ExperimentPauseManager.ActiveRealtime + Mathf.Max(0f, currentInputLabelDelaySeconds);
        currentLabelFadeColor = currentInputLabel.color;
        string axis = currentLabelInputSource.CurrentSliderAxis;
        currentLabelInputSource = null;
        Record("CurrentInputLabelInputStarted", "Anchor=ControllerSliderMovement;Axis=" + axis +
            ";FadeDelaySeconds=" + Mathf.Max(0f, currentInputLabelDelaySeconds).ToString(CultureInfo.InvariantCulture) +
            ";Input=" + input.ToString(CultureInfo.InvariantCulture) + ";Delta=" + delta.ToString(CultureInfo.InvariantCulture));
    }

    private void UpdateCurrentInputLabel()
    {
        if (supportContent == TaskSwitchSupportContent.PhaseOnly)
        {
            ResetCurrentInputLabel("ContentDisabled");
            return;
        }
        if (!currentLabelHighlighted || currentInputLabel == null) return;
        if (!currentLabelFading) TryStartCurrentInputLabelFade();
        if (!currentLabelFading) return;
        if (ExperimentPauseManager.ActiveRealtime < currentLabelFadeStarted) return;
        if (!currentLabelFadeLogged)
        {
            currentLabelFadeLogged = true;
            Record("CurrentInputLabelFadeStarted", "Anchor=ControllerSliderMovementPlusDelay");
        }
        float progress = Mathf.Clamp01((float)(ExperimentPauseManager.ActiveRealtime - currentLabelFadeStarted) /
            Mathf.Clamp(currentInputLabelFadeSeconds, 0.5f, 1f));
        currentInputLabel.color = Color.Lerp(currentLabelFadeColor, Color.white, progress);
        if (progress >= 1f) ResetCurrentInputLabel("ControllerInputStarted");
    }

    private void ResetCurrentInputLabel(string reason)
    {
        currentLabelInputSource = null;
        hasCurrentLabelControllerBaseline = false;
        bool wasHighlighted = currentLabelHighlighted;
        currentLabelDecisionPending = currentLabelHighlighted = currentLabelFading = false;
        currentLabelFadeLogged = false;
        if (currentInputLabel != null) currentInputLabel.color = Color.white;
        if (wasHighlighted) Record("CurrentInputLabelHidden", "Reason=" + reason);
    }

    private string EvaluateCurrentGuidance(CraneStatusManager.WorkPhase phase, bool pendingPhase,
        out bool rangeExpanded)
    {
        rangeExpanded = false;
        LifMagSystem magnet = selectedCrane.LifMagSystem;
        if (magnet == null || !magnet.IsInputValueLiftMode) return "CurrentTargetUnavailable";
        float targetWeight = 0f;
        bool hasWeight = false;
        bool pickup = phase == CraneStatusManager.WorkPhase.Move1 || phase == CraneStatusManager.WorkPhase.LiftUp;
        if (pickup)
        {
            hasWeight = awaitingConfirmation && (targetSide || pendingPhase) &&
                experimentManager.TryGetCognitiveSupportPickupTargetWeightKg(operationManager.CurrentCraneIndex, out targetWeight);
            if (!hasWeight && !pendingPhase && loadPlan != null)
                hasWeight = loadPlan.TryGetPickupTargetWeightKg(out targetWeight);
        }
        else if (!pendingPhase && loadPlan != null)
            hasWeight = loadPlan.TryGetDisplayTargetWeightKg(phase, magnet.GetAttachedTotalWeightKgForDisplay(), out targetWeight);
        if (!hasWeight) return "CurrentTargetUnavailable";

        // Query the current physical gap, not time since switching or a fixed world height.
        // Unknown gaps stay strict; lowering back toward the stack restores the +10 A limit.
        CraneUnit unit = selectedCrane.CraneUnit;
        float distance;
        if (targetWeight > 0f && magnet.HasAttachedBoard && unit != null &&
            !unit.IsDownwardMovementBlockedByTouchdown &&
            unit.TryGetSupportLoweringDistance(out distance) &&
            !float.IsNaN(distance) && !float.IsInfinity(distance) &&
            distance >= Mathf.Max(0.01f, currentRangeClearance))
            rangeExpanded = true;
        return TaskSwitchSupportGuidance.CurrentGuidance(targetWeight, magnet.CurrentElectricCurrentA,
            magnet.MaximumCurrentAmpere, rangeExpanded);
    }

    private void CaptureChevronCounts(CraneWorkStepDefinition step, Transform position,
        CraneUnit unit, Vector3 movement, bool hasGoal, float goalX, float goalZ)
    {
        // Called once in the display-switch/return event. Confirmation, movement and phase
        // changes may update visibility/direction, but must never recalculate the glyph counts.
        Vector3 distances = position != null
            ? TaskSwitchSupportGuidance.RemainingTravelDistance(step, position.position, hasGoal, goalX, goalZ)
            : Vector3.zero;
        capturedVerticalDistanceKnown = false;
        float verticalDistance = -1f, raisingTarget;
        if (directionPresentation == TaskSwitchDirectionPresentation.DistanceChevrons && unit != null)
        {
            if (movement.y < 0f)
                capturedVerticalDistanceKnown = unit.TryGetSupportLoweringDistance(out verticalDistance);
            else if (movement.y > 0f && TaskSwitchSupportGuidance.TryGetRaisingTarget(step, out raisingTarget))
                capturedVerticalDistanceKnown = unit.TryGetSupportRaisingDistance(raisingTarget, out verticalDistance);
        }
        capturedXCount = TaskSwitchSupportGuidance.ChevronCount(distances.x, xDistancePerChevron, maximumChevrons);
        capturedYCount = TaskSwitchSupportGuidance.ChevronCount(capturedVerticalDistanceKnown ? verticalDistance : -1f,
            yDistancePerChevron, maximumChevrons);
        capturedZCount = TaskSwitchSupportGuidance.ChevronCount(distances.z, zDistancePerChevron, maximumChevrons);
        chevronCountsCaptured = true;
    }

    private int DisplayedCount(bool shown, float command, int count)
    {
        if (!shown || command == 0f) return 0;
        return directionPresentation == TaskSwitchDirectionPresentation.DistanceChevrons ? count : 1;
    }

    private float EffectiveFadeSeconds => Mathf.Min(Mathf.Max(0.1f, displaySeconds), Mathf.Clamp(fadeOutSeconds, 0.5f, 1f));
    private float WindowOpacity => Mathf.Clamp01((float)(hideAt - ExperimentPauseManager.ActiveRealtime) / EffectiveFadeSeconds);

    private void StartArrowTimer()
    {
        if (!visible || arrowTimerStarted || awaitingConfirmation || operationManager == null ||
            operationManager.CurrentCraneInstance != selectedCrane) return;
        arrowTimerStarted = true;
        hideAt = ExperimentPauseManager.ActiveRealtime + Mathf.Max(0.1f, displaySeconds);
        Record("ArrowTimerStarted", "Anchor=InputUnlocked;Role=" + (targetSide ? "Target" : "Source") +
            ";DurationSeconds=" + displaySeconds.ToString("F3", CultureInfo.InvariantCulture));
        RefreshPresentation();
    }

    private void ArmCurrentHighlight(CraneStatusManager.WorkPhase phase, CraneUnit unit)
    {
        if (useCurrentRangeColoring || !currentHighlightWindowActive || currentHighlightLatched || supportContent == TaskSwitchSupportContent.PhaseOnly) return;
        currentHighlightLatched = true;
        currentHighlightPhase = phase;
        hasCurrentLiftSample = unit != null && unit.TryGetMainLifMagLocalY(out currentLiftBaselineLocalY);
        previousCurrentLiftLocalY = currentLiftBaselineLocalY;
        Record("CurrentHighlightStarted", "Phase=" + phase + ";End=CorrectWeightRaised");
    }

    private void CheckCurrentLiftConfirmation(CraneUnit unit)
    {
        if (useCurrentRangeColoring || !currentHighlightWindowActive || !currentHighlightLatched || unit == null) return;
        float localY;
        if (!unit.TryGetMainLifMagLocalY(out localY) || float.IsNaN(localY) || float.IsInfinity(localY)) return;
        if (!hasCurrentLiftSample)
        {
            currentLiftBaselineLocalY = previousCurrentLiftLocalY = localY;
            hasCurrentLiftSample = true;
            return;
        }
        currentLiftBaselineLocalY = Mathf.Min(currentLiftBaselineLocalY, localY);
        bool risingNow = localY > previousCurrentLiftLocalY + 0.000001f;
        previousCurrentLiftLocalY = localY;
        if (arrowTimerStarted && risingNow && localY - currentLiftBaselineLocalY >= MinimumConfirmedRiseLocalY &&
            tracker != null && tracker.IsSupportWeightWithinTarget(currentHighlightPhase))
            EndCurrentHighlight("CorrectWeightRaised");
    }

    private static ArrowFade CreateFade(RectTransform group)
    {
        if (group == null) return null;
        CanvasGroup canvas = group.GetComponent<CanvasGroup>();
        if (canvas == null) canvas = group.gameObject.AddComponent<CanvasGroup>();
        canvas.interactable = false;
        canvas.blocksRaycasts = false;
        return new ArrowFade { group = group, canvas = canvas };
    }

    private static void ResetFade(ArrowFade fade)
    {
        if (fade == null) return;
        fade.requested = false;
        fade.canvas.alpha = 0f;
        fade.group.gameObject.SetActive(false);
    }

    private void SetArrow(ArrowFade fade, TaskSwitchSupportArrow arrow,
        float command, bool positiveDirection, bool vertical, int count)
    {
        if (fade != null)
        {
            bool requested = command != 0f;
            double now = ExperimentPauseManager.ActiveRealtime;
            if (!requested && fade.requested) fade.fadeStarted = now;
            float localOpacity = requested ? 1f : fade.group.gameObject.activeSelf
                ? Mathf.Clamp01(1f - (float)(now - fade.fadeStarted) / EffectiveFadeSeconds) : 0f;
            fade.requested = requested;
            fade.canvas.alpha = localOpacity * WindowOpacity;
            fade.group.gameObject.SetActive(fade.canvas.alpha > 0f);
        }
        // Keep the previous direction and label throughout its disappearance.
        if (arrow == null || command == 0f) return;
        arrow.SetPresentation(directionPresentation, count, maximumChevrons);
        bool positive = (command > 0f) == positiveDirection;
        float angle = vertical ? (positive ? 0f : 180f) : (positive ? -90f : 90f);
        arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, angle);
    }

    private void Hide(string reason)
    {
        ResetCurrentInputLabel(reason);
        currentFeedbackActive = false;
        HideArrows(reason);
        EndCurrentHighlight(reason);
    }

    private void HideArrows(string reason)
    {
        bool wasVisible = visible;
        visible = false;
        if (supportRoot != null) supportRoot.SetActive(false);
        ResetFade(leftFade); ResetFade(rightYFade); ResetFade(rightZFade);
        if (wasVisible) Record("Hidden", "Reason=" + reason);
    }

    private void EndCurrentHighlight(string reason)
    {
        bool wasActive = currentHighlightWindowActive;
        currentHighlightWindowActive = false;
        currentHighlightLatched = hasCurrentLiftSample = false;
        SetCurrentHighlight(0f);
        if (wasActive) Record("CurrentHighlightEnded", "Reason=" + reason);
    }

    private void Record(string action, string detail)
    {
        if (experimentManager != null)
            experimentManager.RecordCognitiveSupportEvent(action, detail);
    }

    private static bool IsCurrentAdjustmentStage(CraneWorkStepDefinition step, CraneUnit unit)
    {
        if (unit.IsDownwardMovementBlockedByTouchdown) return true;
        if (step.completionConditions == null) return false;
        foreach (CraneWorkConditionDefinition condition in step.completionConditions)
            if (condition != null && (condition.conditionType == CraneWorkConditionType.AttachedWeightWithinTarget ||
                condition.conditionType == CraneWorkConditionType.PlacementRemainingWeightWithinTarget)) return true;
        return false;
    }

    private void SetCurrentHighlight(float strength, bool appropriate = false)
    {
        if (currentValueDisplays == null) return;
        Color appropriateColor = coordinateColorSource != null
            ? coordinateColorSource.AchievedValueColor : new Color(0.65f, 1f, 0.10f, 1f);
        Color highlightColor = appropriate ? appropriateColor : phaseText != null ? phaseText.color : Color.white;
        foreach (LifMagAccumValueText display in currentValueDisplays)
            if (display != null) display.SetCognitiveSupportHighlight(strength, highlightColor);
    }
}

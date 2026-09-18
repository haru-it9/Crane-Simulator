using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// クレーンの実位置・吸着状態から、現在の大フェーズ内にある
/// 詳細ステップの完了を受動的に判定します。
/// 実験モードや既存管理モードの状態は、このComponentから変更しません。
/// </summary>
[DisallowMultipleComponent]
public class CraneWorkPhaseTracker : MonoBehaviour
{
    [Header("クレーン参照")]
    [SerializeField]
    private CraneInstance craneInstance;

    [Tooltip(
        "表示モードに依存しない共通目標座標です。" +
        "同じクレーンのManagerを登録します。"
    )]
    [SerializeField]
    private CraneWorkTargetManager workTargetManager;

    [Tooltip(
        "このクレーンに対応する既存のCraneSchematicDisplayです。" +
        "現在の目標X・Zを取得します。"
    )]
    [SerializeField]
    private CraneSchematicDisplay schematicDisplay;

    [Header("詳細ステップ構成")]
    [Tooltip(
        "未設定時は組み込みの初期構成を使用します。" +
        "複数クレーンで同じProfileを共有できます。"
    )]
    [SerializeField]
    private CraneWorkPhaseProfile phaseProfile;

    [Header("荷重・着床判定")]
    [Tooltip(
        "吸着目標重量と配置計画を保持する、同じクレーンのManagerです。"
    )]
    [SerializeField]
    private CraneWorkLoadPlanManager loadPlanManager;

    [Tooltip(
        "既存の下降停止判定を持つ同じクレーンのCraneUnitです。" +
        "通常はCraneInstanceから自動取得します。"
    )]
    [SerializeField]
    private CraneUnit craneUnit;

    [Header("初期デバッグ設定")]
    [SerializeField]
    private CraneStatusManager.WorkPhase initialMajorPhase =
        CraneStatusManager.WorkPhase.Move1;

    [Tooltip(
        "通常はOFFにします。単体デバッグ時だけPlay開始と同時に監視します。"
    )]
    [SerializeField]
    private bool monitorOnStart;

    [Header("目標座標フォールバック")]
    [Tooltip(
        "模式図から目標を取得できない場合に、下記座標を使用します。"
    )]
    [SerializeField]
    private bool useManualTargetWhenUnavailable;

    [SerializeField]
    private Vector2 manualTargetXZ;

    [Header("座標判定安定化")]
    [Tooltip(
        "一度許容範囲へ入った後、判定を解除する範囲へ加える余裕です。" +
        "境界付近のON/OFF反復を防ぎます。"
    )]
    [SerializeField]
    [Min(0f)]
    private float positionExitHysteresis = 0.05f;

    private bool runtimeTargetOverrideEnabled;
    private Vector2 runtimeTargetOverrideXZ;

    [Header("時間・ログ")]
    [SerializeField]
    private bool useUnscaledTime;

    [SerializeField]
    private bool logPhaseEvents = true;

    private readonly List<CraneWorkStepDefinition> activeSteps =
        new List<CraneWorkStepDefinition>();

    private List<CraneWorkStepDefinition> builtInSteps;
    private int currentStepIndex = -1;
    private bool isMonitoring;
    private bool majorPhaseCompleted;

    private float stepElapsedSeconds;
    private float conditionStableSeconds;
    private bool positionConditionLatched;

    private Vector3 lastObservedPosition;
    private bool hasLastObservedPosition;

    private Vector3 phaseStartPosition;
    private float attachmentReferenceY;
    private bool hasAttachmentReference;
    private bool sawBoardAttachedSincePhaseStart;
    private bool boardReleasedAfterHeld;
    private bool wasHoldingBoard;
    private GameObject lastHeldBoard;
    private GameObject releasedBoard;

    private CraneUnit subscribedCraneUnit;
    private bool touchdownObservationArmed;
    private bool touchdownObserved;
    private CraneWorkTouchdownKind expectedTouchdownKind;
    private CraneWorkTouchdownKind observedTouchdownKind;
    private bool hasTouchdownReference;
    private float touchdownMainLifMagLocalY;

    private bool warnedMissingCraneInstance;
    private bool warnedMissingInformationTarget;
    private bool warnedMissingLifMagSystem;
    private bool warnedMissingTarget;
    private bool warnedMissingLoadPlan;
    private bool warnedMissingPickupWeight;
    private bool warnedMissingPlacementPlan;
    private bool warnedMissingTouchdownSource;

    public CraneStatusManager.WorkPhase CurrentMajorPhase
    {
        get;
        private set;
    }

    public string CurrentStepId
    {
        get
        {
            CraneWorkStepDefinition step = CurrentStep;
            return step != null ? step.stepId : string.Empty;
        }
    }

    public string CurrentStepDisplayName
    {
        get
        {
            CraneWorkStepDefinition step = CurrentStep;
            return step != null ? step.displayName : string.Empty;
        }
    }

    public bool IsMonitoring => isMonitoring;
    public bool IsMajorPhaseCompleted => majorPhaseCompleted;
    public float CurrentTargetErrorX { get; private set; }
    public float CurrentTargetErrorZ { get; private set; }
    public bool IsHoldingBoard { get; private set; }
    public float CurrentHorizontalSpeed { get; private set; }
    public float CurrentVerticalSpeed { get; private set; }
    public float CurrentAttachedWeightKg { get; private set; }
    public float CurrentWeightErrorKg { get; private set; }
    public float CurrentLiftMagClearance { get; private set; }

    private CraneWorkStepDefinition CurrentStep
    {
        get
        {
            if (currentStepIndex < 0 ||
                currentStepIndex >= activeSteps.Count)
            {
                return null;
            }

            return activeSteps[currentStepIndex];
        }
    }

    public event Action<
        CraneWorkPhaseTracker,
        CraneStatusManager.WorkPhase,
        string
    > StepStarted;

    public event Action<
        CraneWorkPhaseTracker,
        CraneStatusManager.WorkPhase,
        string
    > StepCompleted;

    public event Action<
        CraneWorkPhaseTracker,
        CraneStatusManager.WorkPhase,
        string
    > StepResumed;

    public event Action<
        CraneWorkPhaseTracker,
        CraneStatusManager.WorkPhase
    > MajorPhaseCompleted;

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        RefreshTouchdownSubscription();
    }

    private void OnDisable()
    {
        RemoveTouchdownSubscription();
    }

    private void Start()
    {
        if (monitorOnStart)
        {
            ConfigurePhase(initialMajorPhase, true);
        }
    }

    private void Update()
    {
        if (!isMonitoring || majorPhaseCompleted)
        {
            return;
        }

        float deltaTime = useUnscaledTime
            ? Time.unscaledDeltaTime
            : Time.deltaTime;

        UpdateObservedState(deltaTime);
        EvaluateCurrentStep(deltaTime);
    }

    /// <summary>
    /// 監視する大フェーズを設定します。
    /// 既存Managerの状態は変更しません。
    /// </summary>
    public bool ConfigurePhase(
        CraneStatusManager.WorkPhase phase,
        bool startMonitoring = true
    )
    {
        ResolveReferences();
        RefreshTouchdownSubscription();

        CurrentMajorPhase = phase;
        LoadStepsForPhase(phase);

        if (activeSteps.Count == 0)
        {
            Debug.LogWarning(
                $"{name}: {phase}の詳細ステップが設定されていません。",
                this
            );
            isMonitoring = false;
            currentStepIndex = -1;
            return false;
        }

        Transform informationTarget = GetInformationTarget();
        phaseStartPosition = informationTarget != null
            ? informationTarget.position
            : Vector3.zero;

        lastObservedPosition = phaseStartPosition;
        hasLastObservedPosition = informationTarget != null;
        CurrentHorizontalSpeed = 0f;
        CurrentVerticalSpeed = 0f;

        LifMagSystem lifMagSystem = GetLifMagSystem();
        bool isHolding =
            lifMagSystem != null && lifMagSystem.HasAttachedBoard;

        IsHoldingBoard = isHolding;
        CurrentAttachedWeightKg = GetAttachedWeightKg(lifMagSystem);
        wasHoldingBoard = isHolding;
        sawBoardAttachedSincePhaseStart = isHolding;
        boardReleasedAfterHeld = false;
        lastHeldBoard = isHolding && lifMagSystem != null
            ? lifMagSystem.LastAttachedBoard
            : null;
        releasedBoard = null;

        if (phase == CraneStatusManager.WorkPhase.Place ||
            phase == CraneStatusManager.WorkPhase.PlaceToTrack)
        {
            PreparePlacementPlan();
        }

        hasAttachmentReference = isHolding && informationTarget != null;
        attachmentReferenceY = informationTarget != null
            ? informationTarget.position.y
            : 0f;

        // 新しい大フェーズでは着床基準を作り直します。
        // StopMonitoring / ResumeMonitoringではここを初期化しないため、
        // Task Switchの中断前後で着床高さを保持できます。
        touchdownObservationArmed = false;
        touchdownObserved = false;
        hasTouchdownReference = false;
        CurrentLiftMagClearance = 0f;

        majorPhaseCompleted = false;
        currentStepIndex = 0;
        ResetStepTimers();

        isMonitoring = startMonitoring;

        if (startMonitoring)
        {
            NotifyStepStarted();
        }

        return true;
    }

    public void StartMonitoring()
    {
        if (activeSteps.Count == 0)
        {
            if (!ConfigurePhase(CurrentMajorPhase, false))
            {
                return;
            }
        }

        if (majorPhaseCompleted)
        {
            Debug.LogWarning(
                $"{name}: 完了済みフェーズを再監視する場合は" +
                "ConfigurePhaseを呼び直してください。",
                this
            );
            return;
        }

        isMonitoring = true;
        NotifyStepStarted();
    }

    /// <summary>
    /// 中断時の詳細ステップと吸着基準高さを保持したまま監視を再開します。
    /// 中断前後の時間を連続安定時間として合算しないよう、
    /// 条件安定時間だけは0へ戻します。
    /// </summary>
    public bool ResumeMonitoring()
    {
        if (activeSteps.Count == 0 || currentStepIndex < 0)
        {
            Debug.LogWarning(
                $"{name}: 再開可能な作業フェーズがありません。",
                this
            );
            return false;
        }

        if (majorPhaseCompleted)
        {
            Debug.LogWarning(
                $"{name}: 完了済みフェーズは途中再開できません。" +
                "次フェーズをConfigurePhaseで設定してください。",
                this
            );
            return false;
        }

        conditionStableSeconds = 0f;
        isMonitoring = true;

        CraneWorkStepDefinition step = CurrentStep;
        string stepId = step != null ? step.stepId : string.Empty;

        if (logPhaseEvents)
        {
            Debug.Log(
                $"CraneWork: StepResumed, " +
                $"Crane={GetCraneLabel()}, " +
                $"Phase={CurrentMajorPhase}, " +
                $"Step={stepId}",
                this
            );
        }

        StepResumed?.Invoke(
            this,
            CurrentMajorPhase,
            stepId
        );

        return true;
    }

    public void StopMonitoring()
    {
        isMonitoring = false;
    }

    public void SetManualTarget(float targetX, float targetZ)
    {
        manualTargetXZ = new Vector2(targetX, targetZ);
    }

    /// <summary>
    /// モード側で目標を明示する場合の実行時Overrideです。
    /// Override中は模式図よりこちらを優先します。
    /// </summary>
    public void SetTargetOverride(float targetX, float targetZ)
    {
        runtimeTargetOverrideXZ = new Vector2(targetX, targetZ);
        runtimeTargetOverrideEnabled = true;
    }

    public void ClearTargetOverride()
    {
        runtimeTargetOverrideEnabled = false;
    }

    public void SetPickupTargetWeightKg(float targetWeightKg)
    {
        ResolveReferences();

        if (loadPlanManager != null)
        {
            loadPlanManager.SetPickupTargetWeightKg(targetWeightKg);
            warnedMissingPickupWeight = false;
        }
        else
        {
            WarnMissingLoadPlan();
        }
    }

    public void SetPlannedReleaseWeightKg(float releaseWeightKg)
    {
        ResolveReferences();

        if (loadPlanManager != null)
        {
            loadPlanManager.SetPlannedReleaseWeightKg(releaseWeightKg);
            warnedMissingPlacementPlan = false;
        }
        else
        {
            WarnMissingLoadPlan();
        }
    }

    public void SetTargetRemainingWeightKg(float remainingWeightKg)
    {
        ResolveReferences();

        if (loadPlanManager != null)
        {
            loadPlanManager.SetTargetRemainingWeightKg(remainingWeightKg);
            warnedMissingPlacementPlan = false;
        }
        else
        {
            WarnMissingLoadPlan();
        }
    }

    /// <summary>
    /// Inspectorで選んだInitial Major Phaseの監視を開始します。
    /// 動作確認用Buttonから引数なしで呼び出せます。
    /// </summary>
    public void StartInitialPhaseMonitoring()
    {
        ConfigurePhase(initialMajorPhase, true);
    }

    /// <summary>
    /// Inspectorのデバッグボタン等から現在フェーズを再設定するための入口です。
    /// </summary>
    public void RestartCurrentPhase()
    {
        ConfigurePhase(CurrentMajorPhase, true);
    }

    private void LoadStepsForPhase(
        CraneStatusManager.WorkPhase phase
    )
    {
        activeSteps.Clear();

        if (phaseProfile != null)
        {
            phaseProfile.GetStepsForPhase(phase, activeSteps);
            return;
        }

        if (builtInSteps == null)
        {
            builtInSteps =
                CraneWorkPhaseProfile.CreateBuiltInDefaults();
        }

        foreach (CraneWorkStepDefinition definition in builtInSteps)
        {
            if (definition != null && definition.majorPhase == phase)
            {
                activeSteps.Add(definition);
            }
        }
    }

    private void PreparePlacementPlan()
    {
        if (loadPlanManager == null)
        {
            WarnMissingLoadPlan();
            return;
        }

        if (!loadPlanManager.PreparePlacementPlan(
                CurrentAttachedWeightKg
            ) &&
            !warnedMissingPlacementPlan)
        {
            warnedMissingPlacementPlan = true;
            Debug.LogWarning(
                $"{name}: 配置計画を確定できません。" +
                "CraneWorkLoadPlanManagerで配置重量または" +
                "配置後残存重量を設定してください。",
                this
            );
        }
    }

    private void UpdateObservedState(float deltaTime)
    {
        Transform informationTarget = GetInformationTarget();
        LifMagSystem lifMagSystem = GetLifMagSystem();

        if (informationTarget != null)
        {
            Vector3 currentPosition = informationTarget.position;

            if (hasLastObservedPosition && deltaTime > 0f)
            {
                Vector3 delta =
                    currentPosition - lastObservedPosition;

                CurrentHorizontalSpeed = new Vector2(
                    delta.x,
                    delta.z
                ).magnitude / deltaTime;

                CurrentVerticalSpeed = delta.y / deltaTime;
            }

            lastObservedPosition = currentPosition;
            hasLastObservedPosition = true;
        }

        bool isHolding =
            lifMagSystem != null && lifMagSystem.HasAttachedBoard;

        IsHoldingBoard = isHolding;
        CurrentAttachedWeightKg = GetAttachedWeightKg(lifMagSystem);

        if (isHolding)
        {
            sawBoardAttachedSincePhaseStart = true;

            if (lifMagSystem != null &&
                lifMagSystem.LastAttachedBoard != null)
            {
                lastHeldBoard = lifMagSystem.LastAttachedBoard;
            }
        }

        if (isHolding && !wasHoldingBoard && informationTarget != null)
        {
            attachmentReferenceY = informationTarget.position.y;
            hasAttachmentReference = true;
        }

        if (!isHolding &&
            wasHoldingBoard &&
            sawBoardAttachedSincePhaseStart)
        {
            boardReleasedAfterHeld = true;
            releasedBoard = lastHeldBoard;
        }

        wasHoldingBoard = isHolding;
    }

    private void EvaluateCurrentStep(float deltaTime)
    {
        CraneWorkStepDefinition step = CurrentStep;

        if (step == null)
        {
            return;
        }

        stepElapsedSeconds += Mathf.Max(0f, deltaTime);

        bool allConditionsSatisfied =
            AreAllConditionsSatisfied(step);

        if (allConditionsSatisfied)
        {
            conditionStableSeconds += Mathf.Max(0f, deltaTime);
        }
        else
        {
            conditionStableSeconds = 0f;
        }

        if (conditionStableSeconds >= step.requiredStableSeconds)
        {
            CompleteCurrentStep();
        }
    }

    private bool AreAllConditionsSatisfied(
        CraneWorkStepDefinition step
    )
    {
        if (step.completionConditions == null ||
            step.completionConditions.Count == 0)
        {
            return false;
        }

        foreach (CraneWorkConditionDefinition condition in
                 step.completionConditions)
        {
            if (condition == null || !EvaluateCondition(condition))
            {
                return false;
            }
        }

        return true;
    }

    private bool EvaluateCondition(
        CraneWorkConditionDefinition condition
    )
    {
        Transform informationTarget = GetInformationTarget();

        switch (condition.conditionType)
        {
            case CraneWorkConditionType.Always:
                return true;

            case CraneWorkConditionType.PositionWithinTarget:
                if (informationTarget == null ||
                    !TryGetTargetPosition(
                        out float targetX,
                        out float targetZ
                    ))
                {
                    return false;
                }

                CurrentTargetErrorX = Mathf.Abs(
                    informationTarget.position.x - targetX
                );
                CurrentTargetErrorZ = Mathf.Abs(
                    informationTarget.position.z - targetZ
                );

                float allowedX = condition.threshold +
                    (positionConditionLatched
                        ? positionExitHysteresis
                        : 0f);

                float allowedZ = condition.secondaryThreshold +
                    (positionConditionLatched
                        ? positionExitHysteresis
                        : 0f);

                bool isWithinPosition =
                    CurrentTargetErrorX <= allowedX &&
                    CurrentTargetErrorZ <= allowedZ;

                if (isWithinPosition)
                {
                    positionConditionLatched = true;
                }
                else if (positionConditionLatched)
                {
                    positionConditionLatched = false;
                }

                return isWithinPosition;

            case CraneWorkConditionType.BoardAttached:
                return IsHoldingBoard;

            case CraneWorkConditionType.BoardNotAttached:
                return !IsHoldingBoard;

            case CraneWorkConditionType.HorizontalMovementObserved:
                if (informationTarget == null)
                {
                    return false;
                }

                Vector2 start = new Vector2(
                    phaseStartPosition.x,
                    phaseStartPosition.z
                );
                Vector2 current = new Vector2(
                    informationTarget.position.x,
                    informationTarget.position.z
                );

                return Vector2.Distance(start, current) >=
                       condition.threshold;

            case CraneWorkConditionType.HorizontalSpeedBelow:
                return CurrentHorizontalSpeed <= condition.threshold;

            case CraneWorkConditionType.VerticalSpeedBelow:
                return Mathf.Abs(CurrentVerticalSpeed) <=
                       condition.threshold;

            case CraneWorkConditionType.LiftHeightFromAttachment:
                if (informationTarget == null ||
                    !hasAttachmentReference ||
                    !IsHoldingBoard)
                {
                    return false;
                }

                float liftedHeight =
                    informationTarget.position.y - attachmentReferenceY;

                return liftedHeight >= condition.threshold;

            case CraneWorkConditionType.TouchdownObserved:
                if (craneUnit == null)
                {
                    WarnMissingTouchdownSource();
                    return false;
                }

                return touchdownObserved &&
                       observedTouchdownKind == expectedTouchdownKind;

            case CraneWorkConditionType.AttachedWeightWithinTarget:
                return IsAttachedWeightWithinTarget(
                    condition.threshold,
                    condition.secondaryThreshold
                );

            case CraneWorkConditionType.PlacementRemainingWeightWithinTarget:
                return IsPlacementRemainingWeightWithinTarget(
                    condition.threshold,
                    condition.secondaryThreshold
                );

            case CraneWorkConditionType.LiftMagClearanceFromTouchdown:
                if (craneUnit == null)
                {
                    WarnMissingTouchdownSource();
                    return false;
                }

                if (!TryGetLiftMagClearanceFromTouchdown(
                        out float clearanceHeight
                    ))
                {
                    return false;
                }

                CurrentLiftMagClearance = clearanceHeight;
                return clearanceHeight >= condition.threshold;

            case CraneWorkConditionType.BoardReleasedAfterHeld:
                return boardReleasedAfterHeld && !IsHoldingBoard;

            case CraneWorkConditionType.ReleasedBoardWithinTarget:
                if (releasedBoard == null ||
                    !TryGetTargetPosition(
                        out float releasedTargetX,
                        out float releasedTargetZ
                    ))
                {
                    return false;
                }

                float releasedBoardErrorX = Mathf.Abs(
                    releasedBoard.transform.position.x - releasedTargetX
                );
                float releasedBoardErrorZ = Mathf.Abs(
                    releasedBoard.transform.position.z - releasedTargetZ
                );

                return releasedBoardErrorX <= condition.threshold &&
                       releasedBoardErrorZ <=
                       condition.secondaryThreshold;

            case CraneWorkConditionType.ReleasedBoardStable:
                if (releasedBoard == null)
                {
                    return false;
                }

                Rigidbody releasedBody =
                    releasedBoard.GetComponent<Rigidbody>();

                if (releasedBody == null)
                {
                    return true;
                }

                return releasedBody.velocity.magnitude <=
                       condition.threshold &&
                       releasedBody.angularVelocity.magnitude <=
                       condition.secondaryThreshold;

            case CraneWorkConditionType.MinimumStepElapsedTime:
                return stepElapsedSeconds >= condition.threshold;

            default:
                return false;
        }
    }

    private bool IsAttachedWeightWithinTarget(
        float lowerToleranceKg,
        float upperToleranceKg
    )
    {
        if (loadPlanManager == null)
        {
            WarnMissingLoadPlan();
            return false;
        }

        if (!loadPlanManager.TryGetPickupTargetWeightKg(
                out float targetWeightKg
            ))
        {
            if (!warnedMissingPickupWeight)
            {
                warnedMissingPickupWeight = true;
                Debug.LogWarning(
                    $"{name}: 吸着目標重量が未設定です。" +
                    "CraneWorkLoadPlanManagerのPickup Target Weight Kgを" +
                    "設定してください。",
                    this
                );
            }

            return false;
        }

        CurrentWeightErrorKg =
            CurrentAttachedWeightKg - targetWeightKg;

        return CurrentAttachedWeightKg >=
                   targetWeightKg - Mathf.Max(0f, lowerToleranceKg) &&
               CurrentAttachedWeightKg <=
                   targetWeightKg + Mathf.Max(0f, upperToleranceKg);
    }

    private bool IsPlacementRemainingWeightWithinTarget(
        float lowerToleranceKg,
        float upperToleranceKg
    )
    {
        if (loadPlanManager == null)
        {
            WarnMissingLoadPlan();
            return false;
        }

        if (!loadPlanManager.TryGetTargetRemainingWeightKg(
                out float targetRemainingWeightKg
            ))
        {
            if (!warnedMissingPlacementPlan)
            {
                warnedMissingPlacementPlan = true;
                Debug.LogWarning(
                    $"{name}: 配置後の残存目標重量が未確定です。",
                    this
                );
            }

            return false;
        }

        CurrentWeightErrorKg =
            CurrentAttachedWeightKg - targetRemainingWeightKg;

        return CurrentAttachedWeightKg >=
                   targetRemainingWeightKg -
                   Mathf.Max(0f, lowerToleranceKg) &&
               CurrentAttachedWeightKg <=
                   targetRemainingWeightKg +
                   Mathf.Max(0f, upperToleranceKg);
    }

    private void CompleteCurrentStep()
    {
        CraneWorkStepDefinition completedStep = CurrentStep;

        if (completedStep == null)
        {
            return;
        }

        if (logPhaseEvents)
        {
            Debug.Log(
                $"CraneWork: StepCompleted, " +
                $"Crane={GetCraneLabel()}, " +
                $"Phase={CurrentMajorPhase}, " +
                $"Step={completedStep.stepId}",
                this
            );
        }

        StepCompleted?.Invoke(
            this,
            CurrentMajorPhase,
            completedStep.stepId
        );

        currentStepIndex++;

        if (currentStepIndex < activeSteps.Count)
        {
            ResetStepTimers();
            NotifyStepStarted();
            return;
        }

        majorPhaseCompleted = true;

        if (logPhaseEvents)
        {
            Debug.Log(
                $"CraneWork: MajorPhaseCompleted, " +
                $"Crane={GetCraneLabel()}, " +
                $"Phase={CurrentMajorPhase}",
                this
            );
        }

        MajorPhaseCompleted?.Invoke(this, CurrentMajorPhase);
        isMonitoring = false;
    }

    private void NotifyStepStarted()
    {
        CraneWorkStepDefinition step = CurrentStep;

        if (step == null)
        {
            return;
        }

        PrepareStepTouchdownObservation(step);

        if (logPhaseEvents)
        {
            Debug.Log(
                $"CraneWork: StepStarted, " +
                $"Crane={GetCraneLabel()}, " +
                $"Phase={CurrentMajorPhase}, " +
                $"Step={step.stepId}",
                this
            );
        }

        StepStarted?.Invoke(
            this,
            CurrentMajorPhase,
            step.stepId
        );
    }

    private void PrepareStepTouchdownObservation(
        CraneWorkStepDefinition step
    )
    {
        if (step == null || step.completionConditions == null)
        {
            return;
        }

        bool needsTouchdown = false;

        foreach (CraneWorkConditionDefinition condition in
                 step.completionConditions)
        {
            if (condition != null &&
                condition.conditionType ==
                CraneWorkConditionType.TouchdownObserved)
            {
                needsTouchdown = true;
                break;
            }
        }

        if (!needsTouchdown)
        {
            return;
        }

        if (craneUnit == null)
        {
            WarnMissingTouchdownSource();
            return;
        }

        expectedTouchdownKind =
            CurrentMajorPhase == CraneStatusManager.WorkPhase.LiftUp
                ? CraneWorkTouchdownKind.Pickup
                : CraneWorkTouchdownKind.Placement;

        touchdownObservationArmed = true;
        touchdownObserved = false;
        hasTouchdownReference = false;
        CurrentLiftMagClearance = 0f;
    }

    private void HandleTouchdownDetected(
        CraneUnit source,
        CraneWorkTouchdownKind kind,
        float mainLifMagLocalY
    )
    {
        if (!isMonitoring ||
            !touchdownObservationArmed ||
            source != craneUnit ||
            kind != expectedTouchdownKind)
        {
            return;
        }

        touchdownObservationArmed = false;
        touchdownObserved = true;
        observedTouchdownKind = kind;
        touchdownMainLifMagLocalY = mainLifMagLocalY;
        hasTouchdownReference = true;
        CurrentLiftMagClearance = 0f;

        if (logPhaseEvents)
        {
            Debug.Log(
                $"CraneWork: TouchdownAccepted, " +
                $"Crane={GetCraneLabel()}, " +
                $"Phase={CurrentMajorPhase}, " +
                $"Kind={kind}, " +
                $"MainLifMagLocalY={mainLifMagLocalY:F3}",
                this
            );
        }
    }

    private bool TryGetLiftMagClearanceFromTouchdown(
        out float clearanceHeight
    )
    {
        clearanceHeight = 0f;

        if (!hasTouchdownReference ||
            craneUnit == null ||
            !craneUnit.TryGetMainLifMagLocalY(out float currentLocalY))
        {
            return false;
        }

        clearanceHeight =
            currentLocalY - touchdownMainLifMagLocalY;
        return true;
    }

    private void RefreshTouchdownSubscription()
    {
        if (subscribedCraneUnit == craneUnit)
        {
            return;
        }

        RemoveTouchdownSubscription();
        subscribedCraneUnit = craneUnit;

        if (subscribedCraneUnit != null)
        {
            subscribedCraneUnit.TouchdownDetected +=
                HandleTouchdownDetected;
        }
    }

    private void RemoveTouchdownSubscription()
    {
        if (subscribedCraneUnit != null)
        {
            subscribedCraneUnit.TouchdownDetected -=
                HandleTouchdownDetected;
            subscribedCraneUnit = null;
        }
    }

    private void ResetStepTimers()
    {
        stepElapsedSeconds = 0f;
        conditionStableSeconds = 0f;
        positionConditionLatched = false;
    }

    private bool TryGetTargetPosition(
        out float targetX,
        out float targetZ
    )
    {
        if (runtimeTargetOverrideEnabled)
        {
            targetX = runtimeTargetOverrideXZ.x;
            targetZ = runtimeTargetOverrideXZ.y;
            return true;
        }

        if (workTargetManager != null &&
            workTargetManager.TryGetTarget(
                out targetX,
                out targetZ
            ))
        {
            warnedMissingTarget = false;
            return true;
        }

        if (schematicDisplay != null &&
            schematicDisplay.TryGetCurrentTargetPosition(
                out targetX,
                out targetZ
            ))
        {
            warnedMissingTarget = false;
            return true;
        }

        if (useManualTargetWhenUnavailable)
        {
            targetX = manualTargetXZ.x;
            targetZ = manualTargetXZ.y;
            return true;
        }

        targetX = 0f;
        targetZ = 0f;

        if (!warnedMissingTarget)
        {
            warnedMissingTarget = true;
            Debug.LogWarning(
                $"{name}: 目標座標を取得できません。" +
                "対応するCraneSchematicDisplayを設定してください。",
                this
            );
        }

        return false;
    }

    private Transform GetInformationTarget()
    {
        if (craneInstance == null)
        {
            if (!warnedMissingCraneInstance)
            {
                warnedMissingCraneInstance = true;
                Debug.LogWarning(
                    $"{name}: CraneInstanceが設定されていません。",
                    this
                );
            }

            return null;
        }

        Transform target = craneInstance.InformationTarget;

        if (target == null && !warnedMissingInformationTarget)
        {
            warnedMissingInformationTarget = true;
            Debug.LogWarning(
                $"{name}: CraneInstance.InformationTargetが未設定です。",
                this
            );
        }

        return target;
    }

    private LifMagSystem GetLifMagSystem()
    {
        if (craneInstance == null)
        {
            return null;
        }

        LifMagSystem lifMagSystem = craneInstance.LifMagSystem;

        if (lifMagSystem == null && !warnedMissingLifMagSystem)
        {
            warnedMissingLifMagSystem = true;
            Debug.LogWarning(
                $"{name}: CraneInstance.LifMagSystemが未設定です。",
                this
            );
        }

        return lifMagSystem;
    }

    private float GetAttachedWeightKg(LifMagSystem lifMagSystem)
    {
        return lifMagSystem != null
            ? Mathf.Max(
                0f,
                lifMagSystem.GetAttachedTotalWeightKgForDisplay()
            )
            : 0f;
    }

    private void WarnMissingLoadPlan()
    {
        if (warnedMissingLoadPlan)
        {
            return;
        }

        warnedMissingLoadPlan = true;
        Debug.LogWarning(
            $"{name}: CraneWorkLoadPlanManagerが設定されていません。",
            this
        );
    }

    private void WarnMissingTouchdownSource()
    {
        if (warnedMissingTouchdownSource)
        {
            return;
        }

        warnedMissingTouchdownSource = true;
        Debug.LogWarning(
            $"{name}: 着床判定に使用するCraneUnitが設定されていません。" +
            "CraneInstance.CraneUnitを確認してください。",
            this
        );
    }

    private void ResolveReferences()
    {
        if (craneInstance == null)
        {
            craneInstance = GetComponent<CraneInstance>();
        }

        if (craneInstance == null)
        {
            craneInstance = GetComponentInParent<CraneInstance>();
        }

        if (craneInstance == null)
        {
            craneInstance = GetComponentInChildren<CraneInstance>(true);
        }

        if (workTargetManager == null)
        {
            workTargetManager = GetComponent<CraneWorkTargetManager>();
        }

        if (workTargetManager == null)
        {
            workTargetManager =
                GetComponentInParent<CraneWorkTargetManager>();
        }

        if (workTargetManager == null)
        {
            workTargetManager =
                GetComponentInChildren<CraneWorkTargetManager>(true);
        }

        if (loadPlanManager == null)
        {
            loadPlanManager = GetComponent<CraneWorkLoadPlanManager>();
        }

        if (loadPlanManager == null)
        {
            loadPlanManager =
                GetComponentInParent<CraneWorkLoadPlanManager>();
        }

        if (loadPlanManager == null)
        {
            loadPlanManager =
                GetComponentInChildren<CraneWorkLoadPlanManager>(true);
        }

        if (craneUnit == null && craneInstance != null)
        {
            craneUnit = craneInstance.CraneUnit;
        }

        if (craneUnit == null)
        {
            craneUnit = GetComponent<CraneUnit>();
        }

        if (craneUnit == null)
        {
            craneUnit = GetComponentInParent<CraneUnit>();
        }

        if (craneUnit == null)
        {
            craneUnit = GetComponentInChildren<CraneUnit>(true);
        }

        if (isActiveAndEnabled)
        {
            RefreshTouchdownSubscription();
        }
    }

    private string GetCraneLabel()
    {
        if (craneInstance == null)
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(craneInstance.DisplayName)
            ? $"Crane {craneInstance.CraneId}"
            : craneInstance.DisplayName;
    }

    private void OnValidate()
    {
        positionExitHysteresis = Mathf.Max(
            0f,
            positionExitHysteresis
        );
    }
}

using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 実作業の大フェーズを1サイクルとして順番に進めます。
/// DisplayModeやTaskSwitchには依存せず、クレーン1基ごとに使用します。
/// </summary>
[DisallowMultipleComponent]
public class CraneWorkCycleController : MonoBehaviour
{
    [Header("共通コンポーネント")]
    [SerializeField]
    private CraneWorkPhaseTracker phaseTracker;

    [SerializeField]
    private CraneWorkTargetManager targetManager;

    [SerializeField]
    private CraneWorkLoadPlanManager loadPlanManager;

    [SerializeField]
    private CraneInstance craneInstance;

    [Header("サイクル設定")]
    [SerializeField]
    [Min(1)]
    private int totalCycleCount = 3;

    [SerializeField]
    private CraneStatusManager.WorkPhase initialPhase =
        CraneStatusManager.WorkPhase.Move1;

    [Tooltip(
        "ONの場合、Move2の次をPlaceToTrackにします。" +
        "OFFの場合は通常のPlaceです。"
    )]
    [SerializeField]
    private bool useTrailerPlacement;

    [Header("目標地点")]
    [SerializeField]
    private int pickupPointIndex = 0;

    [SerializeField]
    private CraneWorkTargetXSelection pickupXSelection =
        CraneWorkTargetXSelection.First;

    [SerializeField]
    private int destinationPointIndex = 6;

    [SerializeField]
    private CraneWorkTargetXSelection destinationXSelection =
        CraneWorkTargetXSelection.First;

    [SerializeField]
    private int trailerPointIndex = 12;

    [SerializeField]
    private CraneWorkTargetXSelection trailerXSelection =
        CraneWorkTargetXSelection.First;

    [SerializeField]
    private CraneWorkTargetSource targetSource =
        CraneWorkTargetSource.ExperimentCondition;

    [Header("進行設定")]
    [Tooltip(
        "TaskSwitchExperimentManagerが開始したTrackerを検出し、" +
        "そのフェーズからサイクル制御を引き継ぎます。"
    )]
    [SerializeField]
    private bool autoAdoptRunningTracker;

    [SerializeField]
    private bool automaticPhaseAdvance = true;

    [SerializeField]
    [Min(0f)]
    private float phaseAdvanceDelaySeconds = 0.05f;

    [SerializeField]
    private bool useUnscaledAdvanceDelay = true;

    [Tooltip(
        "ONの場合、目標地点を設定できなければサイクルを停止します。"
    )]
    [SerializeField]
    private bool requireValidTarget = true;

    [Header("ログ")]
    [SerializeField]
    private bool logCycleEvents = true;

    [Header("実行時確認用")]
    [SerializeField]
    private bool isRunning;

    [SerializeField]
    private bool isPaused;

    [SerializeField]
    private bool waitingAtBoundary;

    [SerializeField]
    private bool holdAfterCurrentPhase;

    [SerializeField]
    private int completedCycleCount;

    [SerializeField]
    private CraneStatusManager.WorkPhase currentPhase =
        CraneStatusManager.WorkPhase.Move1;

    [SerializeField]
    private CraneStatusManager.WorkPhase pendingNextPhase =
        CraneStatusManager.WorkPhase.Move1;

    [SerializeField]
    private bool hasPendingNextPhase;

    [SerializeField]
    private int activePickupPointIndex;

    [SerializeField]
    private int activeDestinationPointIndex;

    [SerializeField]
    private int activeTrailerPointIndex;

    [SerializeField]
    private CraneWorkTargetXSelection activePickupXSelection =
        CraneWorkTargetXSelection.First;

    [SerializeField]
    private CraneWorkTargetXSelection activeDestinationXSelection =
        CraneWorkTargetXSelection.First;

    [SerializeField]
    private CraneWorkTargetXSelection activeTrailerXSelection =
        CraneWorkTargetXSelection.First;

    private bool trackerSubscribed;
    private bool autoAdoptConsumed;
    private Coroutine advanceRoutine;

    private const string LoadAcquisitionStepId =
        "LiftUp.LoadAcquisition";

    public bool IsRunning => isRunning;
    public bool IsPaused => isPaused;
    public bool IsWaitingAtBoundary => waitingAtBoundary;
    public int CompletedCycleCount => completedCycleCount;
    public int TotalCycleCount => totalCycleCount;
    public int CurrentCycleNumber => Mathf.Clamp(
        completedCycleCount + 1,
        1,
        Mathf.Max(1, totalCycleCount)
    );
    public CraneStatusManager.WorkPhase CurrentPhase => currentPhase;
    public bool HasPendingNextPhase => hasPendingNextPhase;
    public CraneStatusManager.WorkPhase PendingNextPhase =>
        pendingNextPhase;

    public event Action<
        CraneWorkCycleController,
        CraneStatusManager.WorkPhase,
        int
    > PhaseStarted;

    public event Action<
        CraneWorkCycleController,
        CraneStatusManager.WorkPhase,
        int
    > PhaseCompleted;

    public event Action<CraneWorkCycleController, string> PickupWeightRollback;
    public event Action<CraneWorkCycleController, int> CycleCompleted;

    public event Action<CraneWorkCycleController, int>
        AllCyclesCompleted;

    public event Action<
        CraneWorkCycleController,
        CraneStatusManager.WorkPhase,
        CraneStatusManager.WorkPhase,
        int
    > BoundaryHeld;

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
        SubscribeToTracker();
    }

    private void OnDisable()
    {
        StopAdvanceRoutine();
        UnsubscribeFromTracker();
    }

    private void Update()
    {
        if (ExperimentPauseManager.IsPaused) return;
        if (!autoAdoptRunningTracker ||
            autoAdoptConsumed ||
            isRunning ||
            phaseTracker == null ||
            !phaseTracker.IsMonitoring ||
            phaseTracker.IsMajorPhaseCompleted)
        {
            return;
        }

        AdoptRunningTrackerAsCycleStart();
    }

    /// <summary>
    /// Initial Phaseから新しくサイクルを開始します。
    /// </summary>
    public bool StartCycle(
        int requestedTotalCycleCount,
        CraneStatusManager.WorkPhase requestedInitialPhase
    )
    {
        totalCycleCount = Mathf.Max(1, requestedTotalCycleCount);
        initialPhase = requestedInitialPhase;
        return StartCycle();
    }

    public void SetTotalCycleCount(int requestedTotalCycleCount)
    {
        totalCycleCount = Mathf.Max(1, requestedTotalCycleCount);
    }

    /// <summary>
    /// Move1から1サイクルだけ実行します。
    /// Task Switchの切替先作業で使用します。
    /// </summary>
    public bool StartSingleCycle()
    {
        return StartCycle(
            1,
            CraneStatusManager.WorkPhase.Move1
        );
    }

    public bool StartCycle()
    {
        ResolveReferences();
        SubscribeToTracker();

        if (!ValidateRequiredReferences())
        {
            return false;
        }

        StopAdvanceRoutine();
        ResetRuntimeState();
        isRunning = true;
        autoAdoptConsumed = true;
        ResolveActiveTargets(false);

        Log(
            $"Started, Crane={GetCraneLabel()}, " +
            $"Cycles={totalCycleCount}, InitialPhase={initialPhase}"
        );

        return StartPhase(initialPhase);
    }

    /// <summary>
    /// すでに別Managerが開始したTrackerを再設定せずに引き継ぎます。
    /// Task Switch Modeの連続サイクル確認ではこちらを使用します。
    /// </summary>
    public bool AdoptRunningTrackerAsCycleStart()
    {
        ResolveReferences();
        SubscribeToTracker();

        if (!ValidateRequiredReferences() ||
            !phaseTracker.IsMonitoring ||
            phaseTracker.IsMajorPhaseCompleted)
        {
            return false;
        }

        StopAdvanceRoutine();
        ResetRuntimeState();

        currentPhase = phaseTracker.CurrentMajorPhase;
        isRunning = true;
        autoAdoptConsumed = true;
        ResolveActiveTargets(true);

        Log(
            $"Adopted, Crane={GetCraneLabel()}, " +
            $"Cycle={CurrentCycleNumber}/{totalCycleCount}, " +
            $"Phase={currentPhase}, Step={phaseTracker.CurrentStepId}"
        );

        PhaseStarted?.Invoke(
            this,
            currentPhase,
            CurrentCycleNumber
        );

        return true;
    }

    /// <summary>
    /// 現在フェーズの詳細ステップを保持して一時停止します。
    /// </summary>
    public void PauseCycle()
    {
        if (!isRunning || isPaused)
        {
            return;
        }

        isPaused = true;
        StopAdvanceRoutine();

        if (phaseTracker != null && phaseTracker.IsMonitoring)
        {
            phaseTracker.StopMonitoring();
        }

        Log(
            $"Paused, Crane={GetCraneLabel()}, " +
            $"Phase={currentPhase}, Step=" +
            $"{(phaseTracker != null ? phaseTracker.CurrentStepId : string.Empty)}"
        );
    }

    /// <summary>
    /// フェーズ途中なら同じ詳細ステップを再開し、
    /// 境界停止中なら保留中の次フェーズを開始します。
    /// </summary>
    public bool ResumeCycle()
    {
        if (!isRunning)
        {
            return false;
        }

        if (waitingAtBoundary || hasPendingNextPhase)
        {
            isPaused = false;
            return ContinueAfterBoundary();
        }

        if (phaseTracker == null)
        {
            return false;
        }

        // 中断前の目標はクレーンごとのTargetManagerが保持しています。
        // Tracker引継ぎ時の実験目標とサイクル設定の地点が異なる場合も、
        // フェーズ途中の復帰では保持中の座標をそのまま使用します。
        // 目標が失われている場合だけ、サイクル設定から復元します。
        if ((targetManager == null || !targetManager.HasTarget) &&
            !ApplyTargetForPhase(currentPhase) && requireValidTarget)
        {
            isPaused = true;

            Debug.LogError(
                $"CraneWorkCycle: {GetCraneLabel()}の復帰時に" +
                $"{currentPhase}目標を再設定できないため、" +
                "作業再開を中止します。",
                this
            );

            return false;
        }

        isPaused = false;
        bool resumed = phaseTracker.ResumeMonitoring();

        if (resumed)
        {
            Log(
                $"Resumed, Crane={GetCraneLabel()}, " +
                $"Phase={currentPhase}, Step={phaseTracker.CurrentStepId}"
            );
        }

        return resumed;
    }

    /// <summary>
    /// 次に現在フェーズが完了した時、次フェーズを開始せず停止します。
    /// Phase Boundary方式のTask Switch接続時に使用します。
    /// </summary>
    public void RequestHoldAfterCurrentPhase()
    {
        holdAfterCurrentPhase = true;

        Log(
            $"BoundaryHoldRequested, Crane={GetCraneLabel()}, " +
            $"Phase={currentPhase}"
        );
    }

    public void CancelBoundaryHoldRequest()
    {
        holdAfterCurrentPhase = false;

        if (waitingAtBoundary && automaticPhaseAdvance && !isPaused)
        {
            ContinueAfterBoundary();
        }
    }

    /// <summary>
    /// 境界で保留している次フェーズを開始します。
    /// </summary>
    public bool ContinueAfterBoundary()
    {
        if (!isRunning || !hasPendingNextPhase)
        {
            return false;
        }

        CraneStatusManager.WorkPhase nextPhase = pendingNextPhase;

        waitingAtBoundary = false;
        holdAfterCurrentPhase = false;
        hasPendingNextPhase = false;
        isPaused = false;

        return StartPhase(nextPhase);
    }

    public void StopCycle()
    {
        StopCycleInternal(false);
    }

    public void StopCycleAndTracker()
    {
        StopCycleInternal(true);
    }

    /// <summary>
    /// 完了数と自動引継ぎ済み状態をリセットします。
    /// Trackerの現在状態は変更しません。
    /// </summary>
    public void ResetCycleController()
    {
        StopAdvanceRoutine();
        ResetRuntimeState();
        autoAdoptConsumed = false;

        Log($"Reset, Crane={GetCraneLabel()}");
    }

    public void SetAutomaticPhaseAdvance(bool enabled)
    {
        automaticPhaseAdvance = enabled;

        if (enabled &&
            waitingAtBoundary &&
            !holdAfterCurrentPhase &&
            !isPaused)
        {
            ContinueAfterBoundary();
        }
    }

    private void HandleMajorPhaseCompleted(
        CraneWorkPhaseTracker tracker,
        CraneStatusManager.WorkPhase completedPhase
    )
    {
        if (!isRunning || tracker != phaseTracker)
        {
            return;
        }

        currentPhase = completedPhase;

        Log(
            $"PhaseCompleted, Crane={GetCraneLabel()}, " +
            $"Cycle={CurrentCycleNumber}/{totalCycleCount}, " +
            $"Phase={completedPhase}"
        );

        PhaseCompleted?.Invoke(
            this,
            completedPhase,
            CurrentCycleNumber
        );

        if (IsCycleEndingPhase(completedPhase))
        {
            completedCycleCount++;
            CycleCompleted?.Invoke(this, completedCycleCount);

            Log(
                $"CycleCompleted, Crane={GetCraneLabel()}, " +
                $"Completed={completedCycleCount}/{totalCycleCount}"
            );

            if (completedCycleCount >= totalCycleCount)
            {
                FinishAllCycles();
                return;
            }

            // Random指定の場合も、同じサイクル内のMove1/LiftUp、
            // Move2/Placeでは同じXを維持し、次サイクル開始時だけ
            // 新しいX候補を選び直します。
            ResolveActiveTargets(false);
        }

        pendingNextPhase = GetNextPhase(completedPhase);
        hasPendingNextPhase = true;

        if (holdAfterCurrentPhase ||
            !automaticPhaseAdvance ||
            isPaused)
        {
            EnterBoundaryWait(completedPhase);
            return;
        }

        ScheduleNextPhase(completedPhase);
    }

    private void HandlePickupWeightInvalidated(
        CraneWorkPhaseTracker tracker,
        CraneStatusManager.WorkPhase invalidatedPhase,
        string invalidatedStepId
    )
    {
        if (!isRunning || tracker != phaseTracker)
        {
            return;
        }

        StopAdvanceRoutine();
        waitingAtBoundary = false;
        hasPendingNextPhase = false;
        isPaused = false;

        CraneStatusManager.WorkPhase restartPhase =
            CraneStatusManager.WorkPhase.LiftUp;

        if (!ApplyTargetForPhase(restartPhase) && requireValidTarget)
        {
            Debug.LogError(
                $"CraneWorkCycle: {GetCraneLabel()}の重量逸脱後に" +
                "吊り上げ先目標を復元できないため停止します。",
                this
            );
            StopCycleInternal(true);
            return;
        }

        currentPhase = restartPhase;

        if (!phaseTracker.ConfigurePhaseAtStep(
                restartPhase,
                LoadAcquisitionStepId,
                true
            ))
        {
            Debug.LogError(
                $"CraneWorkCycle: {GetCraneLabel()}を" +
                $"{LoadAcquisitionStepId}へ戻せないため停止します。",
                this
            );
            StopCycleInternal(false);
            return;
        }

        Log(
            $"PickupWeightRollback, Crane={GetCraneLabel()}, " +
            $"From={invalidatedPhase}/{invalidatedStepId}, " +
            $"To={restartPhase}/{LoadAcquisitionStepId}, " +
            $"Cycle={CurrentCycleNumber}/{totalCycleCount}"
        );

        PickupWeightRollback?.Invoke(this, $"From={invalidatedPhase}/{invalidatedStepId};To={restartPhase}/{LoadAcquisitionStepId}");
        PhaseStarted?.Invoke(
            this,
            restartPhase,
            CurrentCycleNumber
        );
    }

    private void ScheduleNextPhase(
        CraneStatusManager.WorkPhase completedPhase
    )
    {
        StopAdvanceRoutine();
        advanceRoutine = StartCoroutine(
            AdvanceAfterDelay(completedPhase)
        );
    }

    private IEnumerator AdvanceAfterDelay(
        CraneStatusManager.WorkPhase completedPhase
    )
    {
        // Keep the pending phase and the remaining delay, including unscaled delay mode.
        float remaining = Mathf.Max(0, phaseAdvanceDelaySeconds);
        do
        {
            yield return null;
            if (!ExperimentPauseManager.IsPaused)
                remaining -= useUnscaledAdvanceDelay ? Time.unscaledDeltaTime : Time.deltaTime;
        } while (ExperimentPauseManager.IsPaused || remaining > 0);

        advanceRoutine = null;

        if (!isRunning || !hasPendingNextPhase)
        {
            yield break;
        }

        if (holdAfterCurrentPhase ||
            !automaticPhaseAdvance ||
            isPaused)
        {
            EnterBoundaryWait(completedPhase);
            yield break;
        }

        CraneStatusManager.WorkPhase nextPhase = pendingNextPhase;
        hasPendingNextPhase = false;
        StartPhase(nextPhase);
    }

    private void EnterBoundaryWait(
        CraneStatusManager.WorkPhase completedPhase
    )
    {
        waitingAtBoundary = true;

        Log(
            $"BoundaryHeld, Crane={GetCraneLabel()}, " +
            $"Completed={completedPhase}, Next={pendingNextPhase}, " +
            $"Cycle={CurrentCycleNumber}/{totalCycleCount}"
        );

        BoundaryHeld?.Invoke(
            this,
            completedPhase,
            pendingNextPhase,
            CurrentCycleNumber
        );
    }

    private bool StartPhase(CraneStatusManager.WorkPhase phase)
    {
        if (!isRunning || phaseTracker == null)
        {
            return false;
        }

        if (!ApplyTargetForPhase(phase) && requireValidTarget)
        {
            Debug.LogError(
                $"CraneWorkCycle: {GetCraneLabel()}の" +
                $"{phase}目標を設定できないため停止します。",
                this
            );
            StopCycleInternal(true);
            return false;
        }

        currentPhase = phase;
        waitingAtBoundary = false;
        hasPendingNextPhase = false;
        isPaused = false;

        if (!phaseTracker.ConfigurePhase(phase, true))
        {
            Debug.LogError(
                $"CraneWorkCycle: {GetCraneLabel()}の" +
                $"{phase}を開始できないため停止します。",
                this
            );
            StopCycleInternal(false);
            return false;
        }

        Log(
            $"PhaseStarted, Crane={GetCraneLabel()}, " +
            $"Cycle={CurrentCycleNumber}/{totalCycleCount}, " +
            $"Phase={phase}, TargetPoint={GetTargetPointForPhase(phase)}"
        );

        PhaseStarted?.Invoke(this, phase, CurrentCycleNumber);
        return true;
    }

    private bool ApplyTargetForPhase(
        CraneStatusManager.WorkPhase phase
    )
    {
        if (targetManager == null)
        {
            return false;
        }

        int pointIndex = GetTargetPointForPhase(phase);
        CraneWorkTargetXSelection xSelection =
            GetTargetXSelectionForPhase(phase);
        CraneWorkTargetKind targetKind =
            GetTargetKindForPhase(phase);

        return targetManager.SetFixedTargetFromPoint(
            pointIndex,
            xSelection,
            targetKind,
            targetSource
        );
    }

    private int GetTargetPointForPhase(
        CraneStatusManager.WorkPhase phase
    )
    {
        switch (phase)
        {
            case CraneStatusManager.WorkPhase.Move1:
            case CraneStatusManager.WorkPhase.LiftUp:
                return activePickupPointIndex;

            case CraneStatusManager.WorkPhase.PlaceToTrack:
                return activeTrailerPointIndex;

            case CraneStatusManager.WorkPhase.Move2:
                return useTrailerPlacement
                    ? activeTrailerPointIndex
                    : activeDestinationPointIndex;

            case CraneStatusManager.WorkPhase.Place:
            default:
                return activeDestinationPointIndex;
        }
    }

    private CraneWorkTargetXSelection GetTargetXSelectionForPhase(
        CraneStatusManager.WorkPhase phase
    )
    {
        switch (phase)
        {
            case CraneStatusManager.WorkPhase.Move1:
            case CraneStatusManager.WorkPhase.LiftUp:
                return activePickupXSelection;

            case CraneStatusManager.WorkPhase.PlaceToTrack:
                return activeTrailerXSelection;

            case CraneStatusManager.WorkPhase.Move2:
                return useTrailerPlacement
                    ? activeTrailerXSelection
                    : activeDestinationXSelection;

            case CraneStatusManager.WorkPhase.Place:
            default:
                return activeDestinationXSelection;
        }
    }

    private void ResolveActiveTargets(bool adoptCurrentTarget)
    {
        activePickupPointIndex = pickupPointIndex;
        activeDestinationPointIndex = destinationPointIndex;
        activeTrailerPointIndex = trailerPointIndex;

        activePickupXSelection =
            ResolveConfiguredXSelection(pickupXSelection);
        activeDestinationXSelection =
            ResolveConfiguredXSelection(destinationXSelection);
        activeTrailerXSelection =
            ResolveConfiguredXSelection(trailerXSelection);

        if (!adoptCurrentTarget ||
            targetManager == null ||
            !targetManager.HasTarget)
        {
            return;
        }

        CraneWorkTargetData currentTarget =
            targetManager.CurrentTarget;
        CraneWorkTargetXSelection adoptedSelection =
            GetXSelectionFromTargetX(currentTarget.targetX);
        bool canAdoptPoint =
            currentTarget.pointIndex >= 0 &&
            currentTarget.pointIndex < CraneWorkCoordinateUtility.PointCount;

        // 外部Managerが開始した作業は、地点番号と選択済みXを一緒に引き継ぎます。
        // Move1→LiftUp、Move2→Placeでも同じ作業地点を使用します。
        switch (currentPhase)
        {
            case CraneStatusManager.WorkPhase.Move1:
            case CraneStatusManager.WorkPhase.LiftUp:
                if (canAdoptPoint)
                {
                    activePickupPointIndex = currentTarget.pointIndex;
                }
                activePickupXSelection = adoptedSelection;
                break;

            case CraneStatusManager.WorkPhase.PlaceToTrack:
                if (canAdoptPoint)
                {
                    activeTrailerPointIndex = currentTarget.pointIndex;
                }
                activeTrailerXSelection = adoptedSelection;
                break;

            case CraneStatusManager.WorkPhase.Move2:
                if (useTrailerPlacement)
                {
                    if (canAdoptPoint)
                    {
                        activeTrailerPointIndex = currentTarget.pointIndex;
                    }
                    activeTrailerXSelection = adoptedSelection;
                }
                else
                {
                    if (canAdoptPoint)
                    {
                        activeDestinationPointIndex = currentTarget.pointIndex;
                    }
                    activeDestinationXSelection = adoptedSelection;
                }
                break;

            case CraneStatusManager.WorkPhase.Place:
                if (canAdoptPoint)
                {
                    activeDestinationPointIndex = currentTarget.pointIndex;
                }
                activeDestinationXSelection = adoptedSelection;
                break;
        }
    }

    private CraneWorkTargetXSelection ResolveConfiguredXSelection(
        CraneWorkTargetXSelection configuredSelection
    )
    {
        if (configuredSelection != CraneWorkTargetXSelection.Random)
        {
            return configuredSelection;
        }

        return UnityEngine.Random.Range(0, 2) == 0
            ? CraneWorkTargetXSelection.First
            : CraneWorkTargetXSelection.Second;
    }

    private CraneWorkTargetXSelection GetXSelectionFromTargetX(
        float targetX
    )
    {
        int craneIndex = targetManager != null
            ? targetManager.CraneIndex
            : 0;
        int craneIndexWithinGroup =
            Mathf.Max(0, craneIndex) %
            CraneWorkCoordinateUtility.CranesPerGroup;
        float craneXOffset =
            craneIndexWithinGroup *
            CraneWorkCoordinateUtility.CraneXInterval;

        float firstX =
            craneXOffset + CraneWorkCoordinateUtility.FirstXCandidate;
        float secondX =
            craneXOffset + CraneWorkCoordinateUtility.SecondXCandidate;

        return Mathf.Abs(targetX - firstX) <=
               Mathf.Abs(targetX - secondX)
            ? CraneWorkTargetXSelection.First
            : CraneWorkTargetXSelection.Second;
    }

    private CraneWorkTargetKind GetTargetKindForPhase(
        CraneStatusManager.WorkPhase phase
    )
    {
        switch (phase)
        {
            case CraneStatusManager.WorkPhase.Move1:
            case CraneStatusManager.WorkPhase.LiftUp:
                return CraneWorkTargetKind.Pickup;

            case CraneStatusManager.WorkPhase.PlaceToTrack:
                return CraneWorkTargetKind.Trailer;

            case CraneStatusManager.WorkPhase.Move2:
                return useTrailerPlacement
                    ? CraneWorkTargetKind.Trailer
                    : CraneWorkTargetKind.NormalPlacement;

            case CraneStatusManager.WorkPhase.Place:
            default:
                return CraneWorkTargetKind.NormalPlacement;
        }
    }

    private CraneStatusManager.WorkPhase GetNextPhase(
        CraneStatusManager.WorkPhase completedPhase
    )
    {
        switch (completedPhase)
        {
            case CraneStatusManager.WorkPhase.Move1:
                return CraneStatusManager.WorkPhase.LiftUp;

            case CraneStatusManager.WorkPhase.LiftUp:
                return CraneStatusManager.WorkPhase.Move2;

            case CraneStatusManager.WorkPhase.Move2:
                return useTrailerPlacement
                    ? CraneStatusManager.WorkPhase.PlaceToTrack
                    : CraneStatusManager.WorkPhase.Place;

            case CraneStatusManager.WorkPhase.Place:
            case CraneStatusManager.WorkPhase.PlaceToTrack:
            default:
                return CraneStatusManager.WorkPhase.Move1;
        }
    }

    private bool IsCycleEndingPhase(
        CraneStatusManager.WorkPhase phase
    )
    {
        return phase == CraneStatusManager.WorkPhase.Place ||
               phase == CraneStatusManager.WorkPhase.PlaceToTrack;
    }

    private void FinishAllCycles()
    {
        StopAdvanceRoutine();
        isRunning = false;
        isPaused = false;
        waitingAtBoundary = false;
        holdAfterCurrentPhase = false;
        hasPendingNextPhase = false;

        Log(
            $"AllCyclesCompleted, Crane={GetCraneLabel()}, " +
            $"Completed={completedCycleCount}"
        );

        AllCyclesCompleted?.Invoke(this, completedCycleCount);
    }

    private void StopCycleInternal(bool stopTracker)
    {
        StopAdvanceRoutine();

        if (stopTracker &&
            phaseTracker != null &&
            phaseTracker.IsMonitoring)
        {
            phaseTracker.StopMonitoring();
        }

        isRunning = false;
        isPaused = false;
        waitingAtBoundary = false;
        holdAfterCurrentPhase = false;
        hasPendingNextPhase = false;

        Log(
            $"Stopped, Crane={GetCraneLabel()}, " +
            $"Completed={completedCycleCount}/{totalCycleCount}"
        );
    }

    private void ResetRuntimeState()
    {
        isRunning = false;
        isPaused = false;
        waitingAtBoundary = false;
        holdAfterCurrentPhase = false;
        completedCycleCount = 0;
        currentPhase = initialPhase;
        pendingNextPhase = initialPhase;
        hasPendingNextPhase = false;
        activePickupPointIndex = pickupPointIndex;
        activeDestinationPointIndex = destinationPointIndex;
        activeTrailerPointIndex = trailerPointIndex;
        activePickupXSelection =
            CraneWorkTargetXSelection.First;
        activeDestinationXSelection =
            CraneWorkTargetXSelection.First;
        activeTrailerXSelection =
            CraneWorkTargetXSelection.First;
    }

    private bool ValidateRequiredReferences()
    {
        if (phaseTracker == null)
        {
            Debug.LogError(
                "CraneWorkCycle: CraneWorkPhaseTrackerがありません。",
                this
            );
            return false;
        }

        if (targetManager == null && requireValidTarget)
        {
            Debug.LogError(
                "CraneWorkCycle: CraneWorkTargetManagerがありません。",
                this
            );
            return false;
        }

        if (loadPlanManager == null)
        {
            Debug.LogWarning(
                "CraneWorkCycle: CraneWorkLoadPlanManagerがありません。" +
                "LiftUpとPlaceの重量判定を確認してください。",
                this
            );
        }

        return true;
    }

    private void SubscribeToTracker()
    {
        if (trackerSubscribed || phaseTracker == null)
        {
            return;
        }

        phaseTracker.MajorPhaseCompleted +=
            HandleMajorPhaseCompleted;
        phaseTracker.PickupWeightInvalidated +=
            HandlePickupWeightInvalidated;
        trackerSubscribed = true;
    }

    private void UnsubscribeFromTracker()
    {
        if (!trackerSubscribed || phaseTracker == null)
        {
            trackerSubscribed = false;
            return;
        }

        phaseTracker.MajorPhaseCompleted -=
            HandleMajorPhaseCompleted;
        phaseTracker.PickupWeightInvalidated -=
            HandlePickupWeightInvalidated;
        trackerSubscribed = false;
    }

    private void StopAdvanceRoutine()
    {
        if (advanceRoutine == null)
        {
            return;
        }

        StopCoroutine(advanceRoutine);
        advanceRoutine = null;
    }

    private void ResolveReferences()
    {
        if (phaseTracker == null)
        {
            phaseTracker = GetComponent<CraneWorkPhaseTracker>();
        }

        if (phaseTracker == null)
        {
            phaseTracker =
                GetComponentInChildren<CraneWorkPhaseTracker>(true);
        }

        if (targetManager == null)
        {
            targetManager = GetComponent<CraneWorkTargetManager>();
        }

        if (targetManager == null)
        {
            targetManager =
                GetComponentInChildren<CraneWorkTargetManager>(true);
        }

        if (loadPlanManager == null)
        {
            loadPlanManager =
                GetComponent<CraneWorkLoadPlanManager>();
        }

        if (loadPlanManager == null)
        {
            loadPlanManager =
                GetComponentInChildren<CraneWorkLoadPlanManager>(true);
        }

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
    }

    private string GetCraneLabel()
    {
        if (craneInstance != null)
        {
            return $"Crane {craneInstance.CraneId}";
        }

        if (targetManager != null)
        {
            return $"Crane {targetManager.CraneIndex + 1}";
        }

        return name;
    }

    private void Log(string message)
    {
        if (logCycleEvents)
        {
            Debug.Log($"CraneWorkCycle: {message}", this);
        }
    }

    private void OnValidate()
    {
        totalCycleCount = Mathf.Max(1, totalCycleCount);
        phaseAdvanceDelaySeconds = Mathf.Max(
            0f,
            phaseAdvanceDelaySeconds
        );

        int lastPointIndex = Mathf.Max(
            0,
            CraneWorkCoordinateUtility.PointCount - 1
        );

        pickupPointIndex = Mathf.Clamp(
            pickupPointIndex,
            0,
            lastPointIndex
        );
        destinationPointIndex = Mathf.Clamp(
            destinationPointIndex,
            0,
            lastPointIndex
        );
        trailerPointIndex = Mathf.Clamp(
            trailerPointIndex,
            0,
            lastPointIndex
        );
    }
}

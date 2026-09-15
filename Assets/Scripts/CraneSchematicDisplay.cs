using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CraneStatusManagerのフェーズに合わせて、クレーンの模式図を移動します。
/// Point0はストック、Point12はトレーラ専用地点として扱います。
/// </summary>
[DisallowMultipleComponent]
public class CraneSchematicDisplay : MonoBehaviour
{
    // Crane1を基準としたPoint0～Point12の目標Z座標です。
    private static readonly float[] BasePointTargetZValues =
    {
        19f, 10f, 8f, 6f, 4f, 2f, 0f,
        -2f, -4f, -6f, -8f, -10f, -15f
    };

    private const int CranesPerGroup = 6;
    private const float CraneXInterval = 20f;
    private const float CraneGroupZInterval = 200f;
    private const float FirstXCandidate = -4f;
    private const float SecondXCandidate = 4f;
    private const int FirstInterventionPointIndex = 1;
    private const int LastInterventionPointIndex = 11;

    [Header("状態取得")]
    [SerializeField]
    private CraneStatusManager craneStatusManager;

    [SerializeField]
    private CraneStockManager craneStockManager;

    [Tooltip("現在選択されているクレーン番号の取得に使用します。未設定時は自動検索します。")]
    [SerializeField]
    private CraneOperationManager craneOperationManager;

    [Tooltip("Crane1なら0、Crane2なら1")]
    [SerializeField]
    private int craneIndex;

    [Header("クレーン図")]
    [SerializeField]
    private RectTransform craneIcon;

    [Header("移動候補地点")]
    [Tooltip("Point番号とElement番号を一致させて登録します。")]
    [SerializeField]
    private List<RectTransform> positionCandidates =
        new List<RectTransform>();

    [Header("開始・終了地点の表示（任意）")]
    [SerializeField]
    private RectTransform startMarker;

    [SerializeField]
    private RectTransform endMarker;

    [Header("特殊地点")]
    [SerializeField]
    private int stockPointIndex = 0;

    [SerializeField]
    private int trailerPointIndex = 12;

    private Vector2 moveStartPosition;
    private Vector2 moveEndPosition;

    // 現在向かっている地点。到着後は次の移動の開始地点になります。
    private int currentPointIndex = -1;

    private bool hasPreviousPhase;
    private CraneStatusManager.WorkPhase previousPhase;
    private CraneStatusManager.CraneState observedState;

    private bool hasCurrentTargetPosition;
    private float currentTargetX;
    private float currentTargetZ;

    public int CraneIndex => craneIndex;
    public int CurrentPointIndex => currentPointIndex;
    public float CurrentTargetX => currentTargetX;
    public float CurrentTargetZ => currentTargetZ;

    private void Awake()
    {
        if (craneOperationManager == null)
        {
            craneOperationManager =
                FindObjectOfType<CraneOperationManager>(true);
        }
    }

    private void Update()
    {
        if (craneStatusManager == null || craneIcon == null)
        {
            return;
        }

        CraneStatusManager.CraneState state =
            craneStatusManager.GetCraneState(craneIndex);

        if (state == null)
        {
            return;
        }

        if (state != observedState)
        {
            observedState = state;
            InitializeSchematic(state);
        }

        if (!hasPreviousPhase ||
            state.currentPhase != previousPhase)
        {
            HandlePhaseChanged(state.currentPhase, state);
        }

        // 停止するのは模式図のクレーンアイコンだけです。
        // CraneStockManagerやCraneStockGaugeの更新は止めません。
        bool isSelectedCrane =
            craneOperationManager != null &&
            craneOperationManager.CurrentCraneIndex == craneIndex;

        bool shouldPauseCraneIcon =
            isSelectedCrane || state.IsProgressPaused;

        if (IsMovingPhase(state.currentPhase) &&
            !shouldPauseCraneIcon)
        {
            UpdateCraneIconPosition(state);
        }
    }

    /// <summary>
    /// 模式図のクレーンアイコンだけを更新します。
    /// ストック数・ストック板・ゲージは別スクリプトで常時更新されます。
    /// </summary>
    private void UpdateCraneIconPosition(
        CraneStatusManager.CraneState state
    )
    {
        float progress = state.phaseDuration > 0f
            ? Mathf.Clamp01(
                1f - state.remainingTime / state.phaseDuration
            )
            : 0f;

        craneIcon.anchoredPosition = Vector2.Lerp(
            moveStartPosition,
            moveEndPosition,
            progress
        );
    }

    private void InitializeSchematic(
        CraneStatusManager.CraneState state
    )
    {
        currentPointIndex = GetRandomNormalDestinationIndex(-1);

        if (!IsValidPointIndex(currentPointIndex))
        {
            Debug.LogWarning(
                $"{name}: 通常地点が登録されていません。",
                this
            );
            return;
        }

        Vector2 initialPosition =
            GetCandidatePosition(currentPointIndex);

        craneIcon.anchoredPosition = initialPosition;
        moveStartPosition = initialPosition;
        moveEndPosition = initialPosition;
        hasPreviousPhase = false;

        // 開始直後が移動フェーズでなかった場合にも、
        // 現在地点に対応する目標値を取得できるようにします。
        SelectTargetPosition(currentPointIndex);

        HandlePhaseChanged(state.currentPhase, state);
    }

    private void HandlePhaseChanged(
        CraneStatusManager.WorkPhase newPhase,
        CraneStatusManager.CraneState state
    )
    {
        bool completedMoveToStock =
            hasPreviousPhase &&
            previousPhase == CraneStatusManager.WorkPhase.Move1 &&
            newPhase == CraneStatusManager.WorkPhase.LiftUp &&
            currentPointIndex == stockPointIndex;

        if (hasPreviousPhase && IsMovingPhase(previousPhase))
        {
            craneIcon.anchoredPosition = moveEndPosition;
        }

        if (completedMoveToStock && craneStockManager != null)
        {
            craneStockManager.ConsumeReservedStockAtPoint0(craneIndex);
        }

        previousPhase = newPhase;
        hasPreviousPhase = true;

        if (IsMovingPhase(newPhase))
        {
            BeginNewMovement(newPhase, state);
        }
    }

    private void BeginNewMovement(
        CraneStatusManager.WorkPhase phase,
        CraneStatusManager.CraneState state
    )
    {
        if (!IsValidPointIndex(currentPointIndex))
        {
            return;
        }

        int startPointIndex = currentPointIndex;
        int endPointIndex;

        bool useStockPoint =
            phase == CraneStatusManager.WorkPhase.Move1 &&
            IsValidPointIndex(stockPointIndex) &&
            craneStockManager != null &&
            craneStockManager.TryReserveStockForMove1(craneIndex);

        bool useTrailerPoint =
            phase == CraneStatusManager.WorkPhase.Move2 &&
            state.cycleCount >= state.nextPlaceToTrackCycle &&
            IsValidPointIndex(trailerPointIndex);

        if (useStockPoint)
        {
            endPointIndex = stockPointIndex;
        }
        else if (useTrailerPoint)
        {
            endPointIndex = trailerPointIndex;
        }
        else
        {
            endPointIndex =
                GetRandomNormalDestinationIndex(startPointIndex);
        }

        if (!IsValidPointIndex(endPointIndex))
        {
            return;
        }

        // Pointの抽選結果をCraneStatusManagerへ渡し、
        // 自動操業フェーズと模式図で同じ移動時間を使用します。
        float movementDuration =
            craneStatusManager.SetMovementDurationFromPointInterval(
                craneIndex,
                startPointIndex,
                endPointIndex
            );

        moveStartPosition = craneIcon.anchoredPosition;
        moveEndPosition = GetCandidatePosition(endPointIndex);
        currentPointIndex = endPointIndex;

        // この移動で使用する目標座標を一度だけ確定します。
        // ZはEnd Point、Xは候補リストから抽選します。
        SelectTargetPosition(endPointIndex);

        UpdateStartEndMarkers();

        Debug.Log(
            $"{name}: {phase} " +
            $"Start=Point{startPointIndex}, " +
            $"End=Point{endPointIndex}, " +
            $"Duration={movementDuration:F1}s, " +
            $"TargetX={currentTargetX:F2}, " +
            $"TargetZ={currentTargetZ:F2}"
        );
    }

    /// <summary>
    /// 現在の移動先に対応する目標X・Zを返します。
    /// クレーンが選択された瞬間にCraneOperationManagerから呼び出します。
    /// </summary>
    public bool TryGetCurrentTargetPosition(
        out float targetX,
        out float targetZ
    )
    {
        // 選択イベントがUpdateより先に呼ばれた場合にも、
        // 最新フェーズの移動先を取得できるよう同期します。
        if (craneStatusManager != null)
        {
            CraneStatusManager.CraneState state =
                craneStatusManager.GetCraneState(craneIndex);

            if (state != null)
            {
                if (state != observedState)
                {
                    observedState = state;
                    InitializeSchematic(state);
                }
                else if (!hasPreviousPhase ||
                         state.currentPhase != previousPhase)
                {
                    HandlePhaseChanged(state.currentPhase, state);
                }
            }
        }

        targetX = currentTargetX;
        targetZ = currentTargetZ;
        return hasCurrentTargetPosition;
    }

    /// <summary>
    /// 現在の模式図アイコン位置をPoint1～11の区間へ投影し、
    /// 介入開始時に使用するmainCrane.localPosition.zを返します。
    /// Point間にいる場合は、両PointのZ座標を線形補間します。
    /// </summary>
    public bool TryGetCurrentInterventionLocalZ(
        out float mainCraneLocalZ
    )
    {
        mainCraneLocalZ = 0f;

        if (craneIcon == null || positionCandidates == null)
        {
            return false;
        }

        // 選択イベントが最初のUpdateより先に発生した場合にも、
        // 模式図の初期位置を確定させます。
        if (craneStatusManager != null)
        {
            CraneStatusManager.CraneState state =
                craneStatusManager.GetCraneState(craneIndex);

            if (state != null && state != observedState)
            {
                observedState = state;
                InitializeSchematic(state);
            }
        }

        int lastPointIndex = Mathf.Min(
            LastInterventionPointIndex,
            positionCandidates.Count - 1,
            BasePointTargetZValues.Length - 1
        );

        if (lastPointIndex < FirstInterventionPointIndex)
        {
            return false;
        }

        Vector2 iconPosition = craneIcon.anchoredPosition;
        float nearestSqrDistance = float.PositiveInfinity;
        bool foundSegment = false;
        float craneGroupZOffset = GetCraneGroupZOffset();

        for (int pointIndex = FirstInterventionPointIndex;
             pointIndex < lastPointIndex;
             pointIndex++)
        {
            int nextPointIndex = pointIndex + 1;

            if (!IsValidPointIndex(pointIndex) ||
                !IsValidPointIndex(nextPointIndex))
            {
                continue;
            }

            Vector2 segmentStart =
                GetCandidatePosition(pointIndex);
            Vector2 segmentEnd =
                GetCandidatePosition(nextPointIndex);
            Vector2 segment = segmentEnd - segmentStart;

            float segmentLengthSqr = segment.sqrMagnitude;
            float interpolation = segmentLengthSqr > 0.000001f
                ? Mathf.Clamp01(
                    Vector2.Dot(
                        iconPosition - segmentStart,
                        segment
                    ) / segmentLengthSqr
                )
                : 0f;

            Vector2 closestPosition = Vector2.Lerp(
                segmentStart,
                segmentEnd,
                interpolation
            );

            float sqrDistance =
                (iconPosition - closestPosition).sqrMagnitude;

            if (sqrDistance >= nearestSqrDistance)
            {
                continue;
            }

            nearestSqrDistance = sqrDistance;
            mainCraneLocalZ = Mathf.Lerp(
                BasePointTargetZValues[pointIndex],
                BasePointTargetZValues[nextPointIndex],
                interpolation
            ) + craneGroupZOffset;
            foundSegment = true;
        }

        if (foundSegment)
        {
            return true;
        }

        // 連続する有効Pointがない場合は、最も近い有効Pointを使います。
        for (int pointIndex = FirstInterventionPointIndex;
             pointIndex <= lastPointIndex;
             pointIndex++)
        {
            if (!IsValidPointIndex(pointIndex))
            {
                continue;
            }

            float sqrDistance = (
                iconPosition - GetCandidatePosition(pointIndex)
            ).sqrMagnitude;

            if (sqrDistance >= nearestSqrDistance)
            {
                continue;
            }

            nearestSqrDistance = sqrDistance;
            mainCraneLocalZ =
                BasePointTargetZValues[pointIndex] +
                craneGroupZOffset;
        }

        return !float.IsPositiveInfinity(nearestSqrDistance);
    }

    private float GetCraneGroupZOffset()
    {
        int validCraneIndex = Mathf.Max(0, craneIndex);
        int craneGroupIndex = validCraneIndex / CranesPerGroup;

        return craneGroupIndex * CraneGroupZInterval;
    }

    private void SelectTargetPosition(int pointIndex)
    {
        hasCurrentTargetPosition = false;

        if (pointIndex < 0 ||
            pointIndex >= BasePointTargetZValues.Length)
        {
            Debug.LogWarning(
                $"{name}: Point{pointIndex}に対応する目標Z座標がありません。",
                this
            );
            return;
        }

        if (craneIndex < 0)
        {
            Debug.LogWarning(
                $"{name}: Crane Indexが不正です: {craneIndex}",
                this
            );
            return;
        }

        // Crane1～6を第1グループ、Crane7～12を第2グループとして扱います。
        // Crane13以降も6基ごとに同じ規則で自動拡張されます。
        int craneGroupIndex = craneIndex / CranesPerGroup;
        int craneIndexWithinGroup = craneIndex % CranesPerGroup;

        float craneXOffset =
            craneIndexWithinGroup * CraneXInterval;

        float selectedBaseX = Random.Range(0, 2) == 0
            ? FirstXCandidate
            : SecondXCandidate;

        currentTargetX = selectedBaseX + craneXOffset;
        currentTargetZ =
            BasePointTargetZValues[pointIndex] +
            craneGroupIndex * CraneGroupZInterval;

        hasCurrentTargetPosition = true;
    }

    private int GetRandomNormalDestinationIndex(int startPointIndex)
    {
        List<int> candidates = new List<int>();

        if (positionCandidates == null)
        {
            return startPointIndex;
        }

        for (int i = 0; i < positionCandidates.Count; i++)
        {
            if (!IsValidPointIndex(i)) continue;
            if (i == startPointIndex) continue;
            if (i == stockPointIndex) continue;
            if (i == trailerPointIndex) continue;

            candidates.Add(i);
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning(
                $"{name}: 通常移動に使用できる地点がありません。",
                this
            );
            return startPointIndex;
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    private bool IsValidPointIndex(int index)
    {
        return
            positionCandidates != null &&
            index >= 0 &&
            index < positionCandidates.Count &&
            positionCandidates[index] != null;
    }

    private Vector2 GetCandidatePosition(int index)
    {
        if (!IsValidPointIndex(index))
        {
            return craneIcon.anchoredPosition;
        }

        return positionCandidates[index].anchoredPosition;
    }

    private void UpdateStartEndMarkers()
    {
        if (startMarker != null)
        {
            startMarker.anchoredPosition = moveStartPosition;
        }

        if (endMarker != null)
        {
            endMarker.anchoredPosition = moveEndPosition;
        }
    }

    private bool IsMovingPhase(CraneStatusManager.WorkPhase phase)
    {
        return
            phase == CraneStatusManager.WorkPhase.Move1 ||
            phase == CraneStatusManager.WorkPhase.Move2;
    }
}

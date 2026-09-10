using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CraneStatusManagerのフェーズに合わせて、クレーンの模式図を移動します。
/// Point0はストック、Point12はトレーラ専用地点として扱います。
/// </summary>
[DisallowMultipleComponent]
public class CraneSchematicDisplay : MonoBehaviour
{
    [Header("状態取得")]
    [SerializeField]
    private CraneStatusManager craneStatusManager;

    [SerializeField]
    private CraneStockManager craneStockManager;

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

        if (!IsMovingPhase(state.currentPhase))
        {
            return;
        }

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

        moveStartPosition = craneIcon.anchoredPosition;
        moveEndPosition = GetCandidatePosition(endPointIndex);
        currentPointIndex = endPointIndex;

        UpdateStartEndMarkers();

        Debug.Log(
            $"{name}: {phase} " +
            $"Start=Point{startPointIndex}, " +
            $"End=Point{endPointIndex}"
        );
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

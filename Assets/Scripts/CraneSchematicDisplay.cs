using System.Collections.Generic;
using UnityEngine;

public class CraneSchematicDisplay : MonoBehaviour
{
    [Header("状態取得")]
    [SerializeField]
    private CraneStatusManager craneStatusManager;

    [Tooltip("Crane1なら0、Crane2なら1")]
    [SerializeField]
    private int craneIndex;

    [Header("クレーン図")]
    [SerializeField]
    private RectTransform craneIcon;

    [Header("移動候補地点")]
    [Tooltip("クレーンが停止できる地点を登録します。")]
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

    [Header("板ストック状態")]
    [SerializeField]
    private bool stockAvailableAtPoint0 = true;

    public void SetStockAvailableAtPoint0(bool available)
    {
        stockAvailableAtPoint0 = available;
    }

    private Vector2 moveStartPosition;
    private Vector2 moveEndPosition;

    // 現在向かっている地点。
    // 移動完了後は、この地点が次の開始地点になります。
    private int currentPointIndex = -1;

    private bool hasPreviousPhase;
    private CraneStatusManager.WorkPhase previousPhase;

    private CraneStatusManager.CraneState observedState;

    private void Update()
    {
        if (craneStatusManager == null) return;
        if (craneIcon == null) return;

        CraneStatusManager.CraneState state =
            craneStatusManager.GetCraneState(craneIndex);

        // シミュレータ開始前
        if (state == null) return;

        // 状態が新しく作り直された場合
        if (state != observedState)
        {
            observedState = state;
            InitializeSchematic(state);
        }

        // フェーズが切り替わった瞬間
        if (!hasPreviousPhase ||
            state.currentPhase != previousPhase)
        {
            HandlePhaseChanged(
                state.currentPhase,
                state
            );
        }

        // Move1・Move2以外では移動しない
        if (!IsMovingPhase(state.currentPhase))
        {
            return;
        }

        float progress = state.phaseDuration > 0f
            ? Mathf.Clamp01(
                1f -
                state.remainingTime /
                state.phaseDuration
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
        List<int> validIndices = GetValidCandidateIndices();

        if (validIndices.Count == 0)
        {
            Debug.LogWarning(
                $"{name}: Position Candidatesが登録されていません。",
                this
            );

            return;
        }

        // 最初の現在位置をリストから選択
        currentPointIndex =
            GetRandomNormalDestinationIndex(-1);

        Vector2 initialPosition =
            GetCandidatePosition(currentPointIndex);

        craneIcon.anchoredPosition =
            initialPosition;

        moveStartPosition = initialPosition;
        moveEndPosition = initialPosition;

        hasPreviousPhase = false;

        HandlePhaseChanged(
            state.currentPhase,
            state
        );
    }

    private void HandlePhaseChanged(
        CraneStatusManager.WorkPhase newPhase,
        CraneStatusManager.CraneState state
    )
    {
        // 前の移動を目的地で終了させる
        if (hasPreviousPhase &&
            IsMovingPhase(previousPhase))
        {
            craneIcon.anchoredPosition =
                moveEndPosition;
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
        if (currentPointIndex < 0) return;

        int startPointIndex = currentPointIndex;
        int endPointIndex;

        if (phase == CraneStatusManager.WorkPhase.Move1 &&
            stockAvailableAtPoint0 &&
            IsValidPointIndex(stockPointIndex))
        {
            // 板のストックがある場合はPoint0へ移動
            endPointIndex = stockPointIndex;
        }
        else if (
            phase == CraneStatusManager.WorkPhase.Move2 &&
            state.cycleCount >= state.nextPlaceToTrackCycle &&
            IsValidPointIndex(trailerPointIndex)
        )
        {
            // 次がトレーラ配置の場合はPoint12へ移動
            endPointIndex = trailerPointIndex;
        }
        else
        {
            // Point0とPointPoint12を除いた通常地点から選択
            endPointIndex =
                GetRandomNormalDestinationIndex(
                    startPointIndex
                );
        }

        moveStartPosition =
            craneIcon.anchoredPosition;

        moveEndPosition =
            GetCandidatePosition(endPointIndex);

        currentPointIndex = endPointIndex;

        UpdateStartEndMarkers();

        Debug.Log(
            $"{name}: {phase} " +
            $"Start=Point{startPointIndex}, " +
            $"End=Point{endPointIndex}"
        );
    }

    private int GetRandomNormalDestinationIndex(
        int startPointIndex
    )
    {
        List<int> candidates = new List<int>();

        for (int i = 0;
            i < positionCandidates.Count;
            i++)
        {
            if (!IsValidPointIndex(i)) continue;

            // 現在地を除外
            if (i == startPointIndex) continue;

            // Point0を除外
            if (i == stockPointIndex) continue;

            // Point12を除外
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

        return candidates[
            Random.Range(0, candidates.Count)
        ];
    }

    private bool IsValidPointIndex(int index)
    {
        return
            positionCandidates != null &&
            index >= 0 &&
            index < positionCandidates.Count &&
            positionCandidates[index] != null;
    }

    private int GetRandomDestinationIndex(
        int startPointIndex
    )
    {
        List<int> validIndices =
            GetValidCandidateIndices();

        // 開始地点を候補から除外
        validIndices.Remove(startPointIndex);

        if (validIndices.Count == 0)
        {
            // 有効な地点が1つしかない場合
            return startPointIndex;
        }

        return validIndices[
            Random.Range(0, validIndices.Count)
        ];
    }

    private List<int> GetValidCandidateIndices()
    {
        List<int> validIndices = new List<int>();

        if (positionCandidates == null)
        {
            return validIndices;
        }

        for (int i = 0;
             i < positionCandidates.Count;
             i++)
        {
            if (positionCandidates[i] != null)
            {
                validIndices.Add(i);
            }
        }

        return validIndices;
    }

    private Vector2 GetCandidatePosition(int index)
    {
        if (index < 0 ||
            index >= positionCandidates.Count ||
            positionCandidates[index] == null)
        {
            return craneIcon.anchoredPosition;
        }

        return positionCandidates[index]
            .anchoredPosition;
    }

    private void UpdateStartEndMarkers()
    {
        if (startMarker != null)
        {
            startMarker.anchoredPosition =
                moveStartPosition;
        }

        if (endMarker != null)
        {
            endMarker.anchoredPosition =
                moveEndPosition;
        }
    }

    private bool IsMovingPhase(
        CraneStatusManager.WorkPhase phase
    )
    {
        return
            phase ==
            CraneStatusManager.WorkPhase.Move1 ||
            phase ==
            CraneStatusManager.WorkPhase.Move2;
    }
}
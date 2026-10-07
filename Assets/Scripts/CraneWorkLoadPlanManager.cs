using System;
using UnityEngine;

/// <summary>
/// クレーン1基分の吸着目標重量と配置計画を保持します。
/// UIやDisplayModeには依存せず、通常管理とTask Switchで共用します。
/// 重量の単位はすべてkgです。
/// </summary>
[DisallowMultipleComponent]
public class CraneWorkLoadPlanManager : MonoBehaviour
{
    public enum PlacementTargetMode
    {
        ReleaseWeight,
        RemainingWeight
    }

    [Header("目標重量CSV連携")]
    [SerializeField] private CraneWorkTargetManager workTargetManager;
    [SerializeField] private CraneInstance craneInstance;
    [SerializeField] private BoardGenerator boardGenerator;

    [Header("吸着目標 [kg]")]
    [Tooltip("吊り上げ時に吸着すべき合計重量です。0以下では未設定です。")]
    [SerializeField]
    [Min(0f)]
    private float pickupTargetWeightKg;

    [Header("配置計画 [kg]")]
    [Tooltip(
        "ONの場合、Place／PlaceToTrackの配置後残存目標を" +
        "常に0kgとして全数配置します。"
    )]
    [SerializeField]
    private bool forceZeroRemainingWeightOnPlacement = true;

    [SerializeField]
    private PlacementTargetMode placementTargetMode =
        PlacementTargetMode.ReleaseWeight;

    [Tooltip(
        "ReleaseWeight時に配置する重量です。" +
        "配置開始重量から差し引いて残存目標を求めます。"
    )]
    [SerializeField]
    [Min(0f)]
    private float plannedReleaseWeightKg;

    [Tooltip(
        "RemainingWeight時にリフマグへ残す重量です。" +
        "全数配置では0kgにします。"
    )]
    [SerializeField]
    [Min(0f)]
    private float explicitRemainingWeightKg;

    [Header("実行時確認用")]
    [SerializeField]
    private float placementStartWeightKg;

    [SerializeField]
    private float targetRemainingWeightKg;

    [SerializeField]
    private bool placementPlanPrepared;
    private int? experimentPickupCount, experimentPlacementCount;
    private int experimentRemainingBoardCount;
    public bool IsPickupBoardCountSatisfied(int attachedCount) => !experimentPickupCount.HasValue || attachedCount == experimentPickupCount.Value;
    public bool IsPlacementBoardCountSatisfied(int attachedCount) => !experimentPlacementCount.HasValue || (placementPlanPrepared && attachedCount == experimentRemainingBoardCount);

    public void SetExperimentBoardCounts(int pickupCount, int placementCount)
    {
        experimentPickupCount=pickupCount; experimentPlacementCount=placementCount;
    }
    public void ClearExperimentBoardCounts() { experimentPickupCount=experimentPlacementCount=null; }

    public float PickupTargetWeightKg => pickupTargetWeightKg;
    public float PlannedReleaseWeightKg => plannedReleaseWeightKg;
    public float ExplicitRemainingWeightKg => explicitRemainingWeightKg;
    public float PlacementStartWeightKg => placementStartWeightKg;
    public float TargetRemainingWeightKg => targetRemainingWeightKg;
    public PlacementTargetMode CurrentPlacementTargetMode =>
        placementTargetMode;
    public bool ForceZeroRemainingWeightOnPlacement =>
        forceZeroRemainingWeightOnPlacement;

    public bool HasPickupTarget => pickupTargetWeightKg > 0f;
    public bool HasPlacementPlan => placementPlanPrepared;

    public event Action<CraneWorkLoadPlanManager> PlanChanged;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (workTargetManager != null)
        {
            workTargetManager.TargetChanged +=
                HandleWorkTargetChanged;
        }

        RefreshPickupTargetFromCurrentTarget();
    }

    private void OnDisable()
    {
        if (workTargetManager != null)
        {
            workTargetManager.TargetChanged -=
                HandleWorkTargetChanged;
        }
    }

    public bool RefreshPickupTargetFromCurrentTarget()
    {
        ResolveReferences();

        if (workTargetManager == null ||
            boardGenerator == null ||
            !workTargetManager.HasTarget)
        {
            return false;
        }

        CraneWorkTargetData target =
            workTargetManager.CurrentTarget;

        if (target.targetKind != CraneWorkTargetKind.Pickup)
        {
            return false;
        }

        float csvTargetWeightKg;int pickupCount,spawnIndex;
        bool found = experimentPickupCount.HasValue
            ? boardGenerator.TryGetPickupTargetWeightKg(target.targetX,target.targetZ,experimentPickupCount.Value,out csvTargetWeightKg,out pickupCount,out spawnIndex)
            : boardGenerator.TryGetPickupTargetWeightKg(target.targetX,target.targetZ,out csvTargetWeightKg,out pickupCount,out spawnIndex);
        if (!found) return false;

        pickupTargetWeightKg =
            Mathf.Max(0f, csvTargetWeightKg);
        NotifyPlanChanged();

        Debug.Log(
            $"CraneWorkLoadPlan: CSVPickupTarget, " +
            $"Crane={target.craneIndex + 1}, " +
            $"Point={target.pointIndex}, Spawn={spawnIndex}, " +
            $"PickupCount={pickupCount}, " +
            $"Weight={pickupTargetWeightKg:F1}kg",
            this
        );

        return true;
    }

    private void HandleWorkTargetChanged(
        CraneWorkTargetManager manager,
        CraneWorkTargetData target
    )
    {
        if (manager == workTargetManager &&
            target.isValid &&
            target.targetKind == CraneWorkTargetKind.Pickup)
        {
            RefreshPickupTargetFromCurrentTarget();
        }
    }

    private void ResolveReferences()
    {
        if (workTargetManager == null)
        {
            workTargetManager =
                GetComponent<CraneWorkTargetManager>();
        }

        if (workTargetManager == null)
        {
            workTargetManager =
                GetComponentInChildren<
                    CraneWorkTargetManager
                >(true);
        }

        if (craneInstance == null)
        {
            craneInstance = GetComponent<CraneInstance>();
        }

        if (craneInstance == null)
        {
            craneInstance = GetComponentInParent<CraneInstance>();
        }

        if (boardGenerator == null &&
            craneInstance != null)
        {
            boardGenerator = craneInstance.BoardGenerator;
        }

        if (boardGenerator == null)
        {
            boardGenerator =
                GetComponentInChildren<BoardGenerator>(true);
        }
    }

    public void SetPickupTargetWeightKg(float targetWeightKg)
    {
        pickupTargetWeightKg = Mathf.Max(0f, targetWeightKg);
        NotifyPlanChanged();
    }

    /// <summary>
    /// 配置する重量を設定します。全数配置の場合は、配置開始時の
    /// 保持重量と同じ値を指定します。
    /// </summary>
    public void SetPlannedReleaseWeightKg(float releaseWeightKg)
    {
        placementTargetMode = PlacementTargetMode.ReleaseWeight;
        plannedReleaseWeightKg = Mathf.Max(0f, releaseWeightKg);
        placementPlanPrepared = false;
        NotifyPlanChanged();
    }

    /// <summary>
    /// 配置後に保持しているべき重量を直接指定します。
    /// 全数配置の場合は0kgです。
    /// </summary>
    public void SetTargetRemainingWeightKg(float remainingWeightKg)
    {
        placementTargetMode = PlacementTargetMode.RemainingWeight;
        explicitRemainingWeightKg = Mathf.Max(0f, remainingWeightKg);
        placementPlanPrepared = false;
        NotifyPlanChanged();
    }

    /// <summary>
    /// PlaceまたはPlaceToTrack開始時の保持重量を記録し、
    /// 今回の残存目標重量を確定します。
    /// </summary>
    public bool PreparePlacementPlan(float currentAttachedWeightKg)
    {
        placementStartWeightKg = Mathf.Max(0f, currentAttachedWeightKg);

        if (experimentPlacementCount.HasValue)
        {
            ResolveReferences();
            LifMagSystem magnet=craneInstance!=null ? craneInstance.LifMagSystem : null;
            if(magnet==null || magnet.AttachedBoards.Count<experimentPlacementCount.Value) { placementPlanPrepared=false;return false; }
            float release=0;
            // Current control releases LastAttachedBoard first, so sum that same order.
            for(int i=0;i<experimentPlacementCount.Value;i++)
            {
                var info=magnet.AttachedBoards[magnet.AttachedBoards.Count-1-i].GetComponent<BoardInfo>();
                if(info==null || info.Weight<=0) { placementPlanPrepared=false;return false; }
                release+=info.Weight;
            }
            experimentRemainingBoardCount=magnet.AttachedBoards.Count-experimentPlacementCount.Value;
            plannedReleaseWeightKg=release;
            targetRemainingWeightKg=Mathf.Max(0,placementStartWeightKg-release);
            placementPlanPrepared=true;NotifyPlanChanged();return true;
        }
        if (forceZeroRemainingWeightOnPlacement)
        {
            targetRemainingWeightKg = 0f;
            placementPlanPrepared = true;
            NotifyPlanChanged();
            return true;
        }

        switch (placementTargetMode)
        {
            case PlacementTargetMode.ReleaseWeight:
                if (plannedReleaseWeightKg <= 0f)
                {
                    placementPlanPrepared = false;
                    targetRemainingWeightKg = 0f;
                    return false;
                }

                targetRemainingWeightKg = Mathf.Max(
                    0f,
                    placementStartWeightKg - plannedReleaseWeightKg
                );
                break;

            case PlacementTargetMode.RemainingWeight:
                if (explicitRemainingWeightKg >=
                    placementStartWeightKg)
                {
                    placementPlanPrepared = false;
                    targetRemainingWeightKg =
                        Mathf.Max(0f, explicitRemainingWeightKg);
                    return false;
                }

                targetRemainingWeightKg = Mathf.Max(
                    0f,
                    explicitRemainingWeightKg
                );
                break;
        }

        placementPlanPrepared = true;
        NotifyPlanChanged();
        return true;
    }

    public void ClearPlacementRuntimeState()
    {
        placementStartWeightKg = 0f;
        targetRemainingWeightKg = 0f;
        placementPlanPrepared = false;
        NotifyPlanChanged();
    }

    public bool TryGetPickupTargetWeightKg(out float targetWeightKg)
    {
        targetWeightKg = pickupTargetWeightKg;
        return HasPickupTarget;
    }

    public bool TryGetTargetRemainingWeightKg(
        out float remainingWeightKg
    )
    {
        remainingWeightKg = targetRemainingWeightKg;
        return placementPlanPrepared;
    }

    /// <summary>
    /// 操作画面の「目標重量」に表示する重量を返します。
    /// Move1/LiftUpでは吸着目標、Move2/Place系では今回配置する重量です。
    /// 単位はkgです。
    /// </summary>
    public bool TryGetDisplayTargetWeightKg(
        CraneStatusManager.WorkPhase phase,
        float currentAttachedWeightKg,
        out float displayTargetWeightKg
    )
    {
        displayTargetWeightKg = 0f;

        switch (phase)
        {
            case CraneStatusManager.WorkPhase.Move1:
            case CraneStatusManager.WorkPhase.LiftUp:
                if (!HasPickupTarget)
                {
                    return false;
                }

                displayTargetWeightKg = pickupTargetWeightKg;
                return true;

            case CraneStatusManager.WorkPhase.Move2:
            case CraneStatusManager.WorkPhase.Place:
            case CraneStatusManager.WorkPhase.PlaceToTrack:
                return TryGetPlacementDisplayTargetWeightKg(
                    currentAttachedWeightKg,
                    out displayTargetWeightKg
                );

            default:
                return false;
        }
    }

    private bool TryGetPlacementDisplayTargetWeightKg(
        float currentAttachedWeightKg,
        out float displayTargetWeightKg
    )
    {
        displayTargetWeightKg = 0f;

        if (experimentPlacementCount.HasValue && placementPlanPrepared)
        {
            displayTargetWeightKg=plannedReleaseWeightKg;
            return true;
        }
        if (forceZeroRemainingWeightOnPlacement)
        {
            // 配置時のUIも、配置後にリフマグへ残す目標重量を表示します。
            // 全数配置のため0kgです。
            return true;
        }

        if (placementTargetMode == PlacementTargetMode.ReleaseWeight)
        {
            if (plannedReleaseWeightKg <= 0f)
            {
                return false;
            }

            displayTargetWeightKg = plannedReleaseWeightKg;
            return true;
        }

        float placementStart = placementPlanPrepared
            ? placementStartWeightKg
            : Mathf.Max(0f, currentAttachedWeightKg);

        if (placementStart <= explicitRemainingWeightKg)
        {
            return false;
        }

        displayTargetWeightKg = Mathf.Max(
            0f,
            placementStart - explicitRemainingWeightKg
        );
        return displayTargetWeightKg > 0f;
    }

    private void NotifyPlanChanged()
    {
        PlanChanged?.Invoke(this);
    }

    private void OnValidate()
    {
        pickupTargetWeightKg = Mathf.Max(0f, pickupTargetWeightKg);
        plannedReleaseWeightKg = Mathf.Max(0f, plannedReleaseWeightKg);
        explicitRemainingWeightKg = Mathf.Max(
            0f,
            explicitRemainingWeightKg
        );

        if (Application.isPlaying)
        {
            NotifyPlanChanged();
        }
    }
}

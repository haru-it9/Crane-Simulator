using System;
using UnityEngine;

/// <summary>
/// クレーン1基分の現在目標を、DisplayModeに依存せず保持します。
/// 自動模式図は通常目標を登録し、実験モードは固定目標を登録します。
/// </summary>
[DisallowMultipleComponent]
public class CraneWorkTargetManager : MonoBehaviour
{
    [Header("クレーン識別")]
    [SerializeField]
    private CraneInstance craneInstance;

    [Tooltip(
        "CraneInstanceを取得できない場合だけ使用します。" +
        "Crane1なら0です。"
    )]
    [SerializeField]
    [Min(0)]
    private int fallbackCraneIndex;

    [Header("現在目標（実行時確認用）")]
    [SerializeField]
    private CraneWorkTargetData currentTarget;

    public int CraneIndex
    {
        get
        {
            if (craneInstance != null)
            {
                return Mathf.Max(0, craneInstance.CraneId - 1);
            }

            return Mathf.Max(0, fallbackCraneIndex);
        }
    }

    public bool HasTarget => currentTarget.isValid;
    public bool IsTargetFixed =>
        currentTarget.isValid && currentTarget.isFixed;
    public CraneWorkTargetData CurrentTarget => currentTarget;

    public event Action<
        CraneWorkTargetManager,
        CraneWorkTargetData
    > TargetChanged;

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    public bool TryGetTarget(out float targetX, out float targetZ)
    {
        targetX = currentTarget.targetX;
        targetZ = currentTarget.targetZ;
        return currentTarget.isValid;
    }

    /// <summary>
    /// 自動操業の模式図から目標を同期します。
    /// 実験条件などで固定中の場合は上書きしません。
    /// </summary>
    public bool TrySetAutomaticTarget(
        float targetX,
        float targetZ,
        int pointIndex,
        CraneWorkTargetKind targetKind
    )
    {
        if (IsTargetFixed)
        {
            return false;
        }

        SetTargetInternal(
            targetX,
            targetZ,
            pointIndex,
            targetKind,
            CraneWorkTargetSource.AutomaticSchematic,
            false
        );

        return true;
    }

    public bool SetFixedTargetFromPoint(
        int pointIndex,
        CraneWorkTargetXSelection xSelection,
        CraneWorkTargetKind targetKind,
        CraneWorkTargetSource source =
            CraneWorkTargetSource.ExperimentCondition
    )
    {
        if (!CraneWorkCoordinateUtility.TryCreatePointTarget(
                CraneIndex,
                pointIndex,
                xSelection,
                out float targetX,
                out float targetZ
            ))
        {
            Debug.LogWarning(
                $"{name}: Point{pointIndex}の目標座標を生成できません。",
                this
            );
            return false;
        }

        SetTargetInternal(
            targetX,
            targetZ,
            pointIndex,
            targetKind,
            source,
            true
        );

        return true;
    }

    public void SetFixedTarget(
        float targetX,
        float targetZ,
        int pointIndex,
        CraneWorkTargetKind targetKind,
        CraneWorkTargetSource source =
            CraneWorkTargetSource.Manual
    )
    {
        SetTargetInternal(
            targetX,
            targetZ,
            pointIndex,
            targetKind,
            source,
            true
        );
    }

    public void ReleaseFixedTarget(bool clearTarget)
    {
        if (!currentTarget.isValid)
        {
            return;
        }

        if (clearTarget)
        {
            ClearTarget();
            return;
        }

        currentTarget.isFixed = false;
        NotifyTargetChanged();
    }

    public void ClearTarget()
    {
        currentTarget = new CraneWorkTargetData
        {
            isValid = false,
            craneIndex = CraneIndex,
            pointIndex = -1,
            targetKind = CraneWorkTargetKind.Unknown,
            source = CraneWorkTargetSource.Unknown,
            isFixed = false
        };

        NotifyTargetChanged();
    }

    private void SetTargetInternal(
        float targetX,
        float targetZ,
        int pointIndex,
        CraneWorkTargetKind targetKind,
        CraneWorkTargetSource source,
        bool isFixed
    )
    {
        currentTarget = new CraneWorkTargetData
        {
            isValid = true,
            craneIndex = CraneIndex,
            pointIndex = pointIndex,
            targetX = targetX,
            targetZ = targetZ,
            targetKind = targetKind,
            source = source,
            isFixed = isFixed
        };

        NotifyTargetChanged();
    }

    private void NotifyTargetChanged()
    {
        TargetChanged?.Invoke(this, currentTarget);

        if (currentTarget.isValid)
        {
            Debug.Log(
                $"CraneWorkTarget: Crane={CraneIndex + 1}, " +
                $"Point={currentTarget.pointIndex}, " +
                $"X={currentTarget.targetX:F2}, " +
                $"Z={currentTarget.targetZ:F2}, " +
                $"Source={currentTarget.source}, " +
                $"Fixed={currentTarget.isFixed}",
                this
            );
        }
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
    }

    private void OnValidate()
    {
        fallbackCraneIndex = Mathf.Max(0, fallbackCraneIndex);
    }
}

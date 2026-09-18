using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CraneUnitの既存下降停止判定から通知する着床種別です。
/// Pickupはリフマグ下面、Placementは保持中の厚板下面の着床です。
/// </summary>
public enum CraneWorkTouchdownKind
{
    Pickup,
    Placement
}

/// <summary>
/// 実作業ステップの完了条件です。
/// 新しい判定を追加する場合は、この列挙値と
/// CraneWorkPhaseTracker.EvaluateCondition()を拡張します。
/// </summary>
public enum CraneWorkConditionType
{
    Always,
    PositionWithinTarget,
    BoardAttached,
    BoardNotAttached,
    HorizontalMovementObserved,
    LiftHeightFromAttachment,
    BoardReleasedAfterHeld,
    ReleasedBoardWithinTarget,
    ReleasedBoardStable,
    MinimumStepElapsedTime,

    // ここから下は詳細10ステップ版で追加した条件です。
    // 既存Profileのenum値を壊さないよう末尾へ追加しています。
    HorizontalSpeedBelow,
    VerticalSpeedBelow,
    TouchdownObserved,
    AttachedWeightWithinTarget,
    PlacementRemainingWeightWithinTarget,
    LiftMagClearanceFromTouchdown
}

[Serializable]
public class CraneWorkConditionDefinition
{
    [Tooltip("この条件で確認する状態です。")]
    public CraneWorkConditionType conditionType =
        CraneWorkConditionType.Always;

    [Tooltip(
        "条件の第1閾値です。座標ではX許容誤差、速度では上限、" +
        "重量では下側許容誤差[kg]、高さでは必要量[m]です。"
    )]
    [Min(0f)]
    public float threshold = 0.25f;

    [Tooltip(
        "条件の第2閾値です。座標ではZ許容誤差、" +
        "重量では上側許容誤差[kg]です。"
    )]
    [Min(0f)]
    public float secondaryThreshold = 0.25f;

    public CraneWorkConditionDefinition()
    {
    }

    public CraneWorkConditionDefinition(
        CraneWorkConditionType type,
        float firstThreshold = 0f,
        float secondThreshold = 0f
    )
    {
        conditionType = type;
        threshold = Mathf.Max(0f, firstThreshold);
        secondaryThreshold = Mathf.Max(0f, secondThreshold);
    }
}

[Serializable]
public class CraneWorkStepDefinition
{
    [Tooltip(
        "ログやCSVで使用する変更しない識別子です。" +
        "例: LiftUp.BoardAttached"
    )]
    public string stepId = "Step";

    [Tooltip("画面表示用の名称です。")]
    public string displayName = "作業ステップ";

    [Tooltip("この詳細ステップが属する既存の大フェーズです。")]
    public CraneStatusManager.WorkPhase majorPhase =
        CraneStatusManager.WorkPhase.Move1;

    [Tooltip(
        "同じ大フェーズ内では、Profileのリスト順に判定します。"
    )]
    public List<CraneWorkConditionDefinition> completionConditions =
        new List<CraneWorkConditionDefinition>();

    [Tooltip(
        "全条件成立後、この時間だけ成立状態が続いたら完了します。"
    )]
    [Min(0f)]
    public float requiredStableSeconds = 0.25f;
}

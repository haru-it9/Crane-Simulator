using System;
using System.Collections.Generic;
using UnityEngine;

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
    MinimumStepElapsedTime
}

[Serializable]
public class CraneWorkConditionDefinition
{
    [Tooltip("この条件で確認する状態です。")]
    public CraneWorkConditionType conditionType =
        CraneWorkConditionType.Always;

    [Tooltip(
        "条件の第1閾値です。座標判定ではX許容誤差、" +
        "移動・上昇・時間判定では必要量として使用します。"
    )]
    [Min(0f)]
    public float threshold = 0.25f;

    [Tooltip("座標判定で使用するZ許容誤差です。")]
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

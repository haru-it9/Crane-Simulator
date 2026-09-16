using System;

public enum TaskSwitchMethod
{
    ConfirmAfterDisplaySwitch,
    Countdown,
    PhaseBoundary
}

public enum TaskSwitchExperimentState
{
    Idle,
    OperatingSource,
    WaitingForConfirmation,
    CountingDown,
    WaitingForPhaseBoundary,
    OperatingTarget,
    Completed
}

/// <summary>
/// 作業切替実験で使用する1基分の条件です。
/// 座標・板・人・トレーラの生成条件は重複して保持せず、
/// CraneInterventionScenarioManagerの既存設定を使用します。
/// </summary>
[Serializable]
public class TaskSwitchCraneCondition
{
    [UnityEngine.Tooltip("Crane1なら0、Crane2なら1です。")]
    [UnityEngine.Min(0)]
    public int craneIndex;

    public string taskName = "Task";

    public CraneStatusManager.WorkPhase workPhase =
        CraneStatusManager.WorkPhase.Move1;

    public CraneStatusManager.ErrorType errorType =
        CraneStatusManager.ErrorType.None;

    [UnityEngine.Tooltip(
        "有効時は既存のCraneSchematicDisplayが持つ介入開始Zを使用します。" +
        "取得できない場合はInterventionScenarioManagerのCSV／範囲設定を使用します。"
    )]
    public bool useSchematicStartZ = true;
}

public struct TaskSwitchEventData
{
    public string eventName;
    public TaskSwitchMethod switchMethod;
    public TaskSwitchExperimentState state;
    public int sourceCraneIndex;
    public int targetCraneIndex;
    public CraneStatusManager.WorkPhase sourcePhase;
    public CraneStatusManager.WorkPhase targetPhase;
    public CraneStatusManager.ErrorType sourceErrorType;
    public CraneStatusManager.ErrorType targetErrorType;
    public float realtimeSinceExperimentStart;
    public string detail;
}

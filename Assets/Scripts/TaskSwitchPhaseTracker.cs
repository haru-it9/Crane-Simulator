using System;
using UnityEngine;

/// <summary>
/// 手動作業のフェーズ境界だけを通知します。
/// フェーズ定義は既存のCraneStatusManager.WorkPhaseを共用します。
/// </summary>
[DisallowMultipleComponent]
public class TaskSwitchPhaseTracker : MonoBehaviour
{
    [Header("識別情報")]
    [SerializeField]
    [Min(0)]
    private int craneIndex;

    [SerializeField]
    private string taskName = "Task";

    [Header("現在フェーズ")]
    [SerializeField]
    private CraneStatusManager.WorkPhase currentPhase =
        CraneStatusManager.WorkPhase.Move1;

    public int CraneIndex => craneIndex;
    public string TaskName => taskName;
    public CraneStatusManager.WorkPhase CurrentPhase => currentPhase;

    public event Action<
        TaskSwitchPhaseTracker,
        CraneStatusManager.WorkPhase,
        CraneStatusManager.WorkPhase
    > PhaseBoundaryReached;

    public void Configure(
        int newCraneIndex,
        string newTaskName,
        CraneStatusManager.WorkPhase startingPhase
    )
    {
        craneIndex = Mathf.Max(0, newCraneIndex);
        taskName = string.IsNullOrWhiteSpace(newTaskName)
            ? "Task"
            : newTaskName;
        currentPhase = startingPhase;
    }

    public void SetPhase(CraneStatusManager.WorkPhase phase)
    {
        currentPhase = phase;
    }

    /// <summary>
    /// 現在フェーズを完了して次へ進め、境界イベントを発行します。
    /// 実作業の完了判定または確認用Buttonから呼び出します。
    /// </summary>
    public void CompleteCurrentPhase()
    {
        CraneStatusManager.WorkPhase previousPhase = currentPhase;
        currentPhase = GetNextPhase(currentPhase);

        PhaseBoundaryReached?.Invoke(
            this,
            previousPhase,
            currentPhase
        );
    }

    private CraneStatusManager.WorkPhase GetNextPhase(
        CraneStatusManager.WorkPhase phase
    )
    {
        switch (phase)
        {
            case CraneStatusManager.WorkPhase.Move1:
                return CraneStatusManager.WorkPhase.LiftUp;
            case CraneStatusManager.WorkPhase.LiftUp:
                return CraneStatusManager.WorkPhase.Move2;
            case CraneStatusManager.WorkPhase.Move2:
                return CraneStatusManager.WorkPhase.Place;
            case CraneStatusManager.WorkPhase.Place:
                return CraneStatusManager.WorkPhase.PlaceToTrack;
            case CraneStatusManager.WorkPhase.PlaceToTrack:
            default:
                return CraneStatusManager.WorkPhase.Move1;
        }
    }

    private void OnValidate()
    {
        craneIndex = Mathf.Max(0, craneIndex);
    }
}

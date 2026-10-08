using System;
using System.Collections.Generic;
using UnityEngine;

public partial class TaskSwitchExperimentManager
{
    [Header("CSV作業条件（Source:サイクル / Target:切替番号）")]
    [Tooltip("role,index,pickupX,pickupZ,placementX,placementZ,pickupCount,placementCount。座標はワールドXZ[m]。")]
    [SerializeField] private TextAsset workConditionsCsv;
    [SerializeField] private TaskSwitchTargetTaskPattern targetTaskPattern = TaskSwitchTargetTaskPattern.Move1ToLiftUp;
    private TaskSwitchTargetTaskPattern activeTargetTaskPattern;
    private TaskSwitchWorkCondition activeTargetWorkCondition;
    private List<TaskSwitchWorkCondition> experimentWorkConditions = new List<TaskSwitchWorkCondition>();
    public string WorkConditionsCsvText => workConditionsCsv != null ? workConditionsCsv.text : "";
    public string ActiveTargetWorkConditionJson => activeTargetWorkCondition != null ? JsonUtility.ToJson(activeTargetWorkCondition) : "";
    public TaskSwitchTargetTaskPattern ActiveTargetTaskPattern => activeTargetTaskPattern;

    private TaskSwitchWorkCondition SourceWorkCondition(int cycle) => experimentWorkConditions.Find(c=>c.role=="Source" && c.index==cycle);
    private void SelectTargetCondition(int switchIndex)
    {
        var entry=scheduledSwitches.Find(e=>e.switchIndex==switchIndex);
        activeTargetTaskPattern=entry!=null ? entry.targetTaskPattern : targetTaskPattern;
        activeTargetWorkCondition=experimentWorkConditions.Find(c=>c.role=="Target" && c.index==switchIndex);
    }
    private bool LoadExperimentConditions()
    {
        try
        {
            var schedule=useSwitchScheduleCsv && switchScheduleCsv!=null
                ? TaskSwitchConditionCsv.ParseSchedule(switchScheduleCsv.text,targetTaskPattern,sourceTotalCycleCount)
                : new List<TaskSwitchScheduleRow>();
            var work=workConditionsCsv!=null ? TaskSwitchConditionCsv.ParseWork(workConditionsCsv.text,sourceTotalCycleCount) : new List<TaskSwitchWorkCondition>();
            if(workConditionsCsv!=null)
            {
                foreach(var e in schedule) if(!work.Exists(c=>c.role=="Target" && c.index==e.switchIndex)) throw new FormatException("Missing Target work condition: " + e.switchIndex);
            }
            else if(schedule.Exists(e=>e.targetTaskPattern==TaskSwitchTargetTaskPattern.Move2ToPlace) || targetTaskPattern==TaskSwitchTargetTaskPattern.Move2ToPlace)
                throw new FormatException("Move2ToPlace requires Work Conditions CSV to specify the preloaded boards");
            work.Sort((a,b)=>a.index.CompareTo(b.index));
            experimentWorkConditions=work;
            if(workConditionsCsv!=null) sourceCondition.workPhase=CraneStatusManager.WorkPhase.Move1;
            scheduledSwitches.Clear();
            foreach(var e in schedule) scheduledSwitches.Add(new ScheduledSwitchEntry { switchIndex=e.switchIndex,sourceCycle=e.sourceCycle,sourcePhase=e.sourcePhase,
                minimumDelaySeconds=e.minimumDelaySeconds,maximumDelaySeconds=e.maximumDelaySeconds,targetTaskPattern=e.targetTaskPattern });
            SelectTargetCondition(1);
            if(sourceCycleController!=null) sourceCycleController.SetExperimentConditions(work.FindAll(c=>c.role=="Source").ToArray());
            return true;
        }
        catch(Exception e) { Debug.LogError("TaskSwitch CSV: " + e.Message,this);return false; }
    }
    private static void ApplyCsvTarget(CraneWorkTargetManager manager,CraneStatusManager.WorkPhase phase,TaskSwitchWorkCondition condition)
    {
        bool pickup=phase==CraneStatusManager.WorkPhase.Move1 || phase==CraneStatusManager.WorkPhase.LiftUp;
        manager.SetFixedTarget(pickup?condition.pickupX:condition.placementX,pickup?condition.pickupZ:condition.placementZ,-1,
            pickup?CraneWorkTargetKind.Pickup:CraneWorkTargetKind.NormalPlacement,CraneWorkTargetSource.ExperimentCondition);
    }
    private bool PrepareCsvWorkCondition(TaskSwitchCraneCondition condition,CraneInstance crane)
    {
        var work=condition==sourceCondition ? SourceWorkCondition(1) : activeTargetWorkCondition;
        if(work==null) return true;
        var load=crane.GetComponentInChildren<CraneWorkLoadPlanManager>(true);
        var generator=crane.GetComponentInChildren<BoardGenerator>(true);
        if(generator==null) generator=interventionScenarioManager.GetBoardGeneratorForCrane(condition.craneIndex);
        if(load==null || generator==null) { Debug.LogError("CSV work requires LoadPlanManager and BoardGenerator",this);return false; }
        List<GameObject> boards;float weight;int spawn;
        if(!generator.TryGetPickupBoards(work.pickupX,work.pickupZ,work.pickupCount,out boards,out weight,out spawn))
        { Debug.LogError($"CSV work {work.role}:{work.index}: not enough boards at pickup ({work.pickupX},{work.pickupZ})",this);return false; }
        load.SetExperimentBoardCounts(work.pickupCount,work.placementCount);
        load.SetPickupTargetWeightKg(weight);
        if(condition==targetCondition && ActiveTargetTaskPattern==TaskSwitchTargetTaskPattern.Move2ToPlace)
        {
            if(crane.LifMagSystem==null) { Debug.LogError("Move2ToPlace requires LifMagSystem",this);return false; }
            // Use the real boards and their CSV weights, rather than resizing a synthetic single plate.
            if(!crane.LifMagSystem.TryPreloadTaskSwitchBoards(boards))
            { Debug.LogError("Move2ToPlace: magnet contact surface or pickup board geometry is missing",this);return false; }
            if(crane.LifMagSystem.AttachedBoards.Count!=boards.Count)
            { Debug.LogError("Move2ToPlace: pickup boards were not attached to the lifting magnet",this);return false; }
            for(int i=0;i<boards.Count;i++)
                if(crane.LifMagSystem.AttachedBoards[i]!=boards[i])
                { Debug.LogError("Move2ToPlace: attached boards differ from the CSV pickup boards",this);return false; }
            load.PreparePlacementPlan(crane.LifMagSystem.GetAttachedTotalWeightKgForDisplay());
        }
        EmitEvent("WorkConditionPrepared", $"Role={work.role};Index={work.index};PickupX={work.pickupX};PickupZ={work.pickupZ};PlacementX={work.placementX};PlacementZ={work.placementZ};PickupCount={work.pickupCount};PlacementCount={work.placementCount};PickupWeightKg={weight}");
        return true;
    }
    private void AcceptOperatorSwitch()
    {
        // The source continues until this click, then switches and starts the target with one confirmation.
        EmitEvent("OperatorSwitchConfirmationPressed");
        SwitchControlToTarget("OperatorInitiated");
        if(currentState==TaskSwitchExperimentState.WaitingForConfirmation) ConfirmTargetTask();
    }
    private void ClearExperimentWorkConditions()
    {
        if(sourceCycleController!=null) sourceCycleController.SetExperimentConditions(null);
        if(targetCycleController!=null) targetCycleController.SetExperimentConditions(null);
        foreach(int index in new[]{sourceCondition.craneIndex,targetCondition.craneIndex})
        {
            var crane=craneRegistry.GetCraneByRuntimeIndex(index);
            var load=crane!=null ? crane.GetComponentInChildren<CraneWorkLoadPlanManager>(true) : null;
            if(load!=null) load.ClearExperimentBoardCounts();
        }
    }
}

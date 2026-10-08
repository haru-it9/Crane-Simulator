using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

static class FlowTests
{
    static void Require(bool condition,string message) { if(!condition)throw new Exception(message); }
    static void Set(object o,string field,object value) {o.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,value);}
    static object Get(object o,string field) {return o.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);}
    static void Call(object o,string method) {o.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);}
    static void Throws(Action action) {try {action();}catch(FormatException) {return;}throw new Exception("invalid CSV accepted");}
    static readonly string Schedule="switchIndex,sourceCycle,sourcePhase,minimumDelaySeconds,maximumDelaySeconds,targetTaskPattern\n1,1,Move1,10,10,1\n2,2,Move2,10,10,Move2ToPlace\n";
    static string Work()
    {
        string text="role,index,pickupX,pickupZ,placementX,placementZ,pickupCount,placementCount\n";
        for(int i=5;i>=1;i--)text+="Source,"+i+",-4,"+i+",4,"+(i+10)+",2,2\n";
        return text+"Target,1,16,2,24,8,2,2\nTarget,2,16,4,24,6,3,1\n";
    }
    sealed class Rig
    {
        public TaskSwitchExperimentManager Manager=new TaskSwitchExperimentManager();
        public CraneOperationManager Op=new CraneOperationManager();
        public CraneRegistry Registry=new CraneRegistry {ActiveCraneCount=2,cranes=new CraneInstance[2]};
        public List<string> Events=new List<string>();
        public Rig(TaskSwitchMethod method)
        {
            for(int i=0;i<2;i++)
            {
                var c=new CraneInstance {CraneId=i+1,LifMagSystem=new LifMagSystem()};c.CraneUnit=new CraneUnit {magnet=c.LifMagSystem};
                var gen=new BoardGenerator();for(int b=1;b<=3;b++) {var board=new GameObject("board"+b);board.components[typeof(BoardInfo)]=new BoardInfo {Weight=b*100};gen.Boards.Add(board);}c.BoardGenerator=gen;
                var target=new CraneWorkTargetManager {CraneIndex=i};var tracker=new CraneWorkPhaseTracker();var load=new CraneWorkLoadPlanManager();var cycle=new CraneWorkCycleController();
                Set(load,"workTargetManager",target);Set(load,"craneInstance",c);Set(load,"boardGenerator",gen);
                Set(cycle,"phaseTracker",tracker);Set(cycle,"targetManager",target);Set(cycle,"loadPlanManager",load);Set(cycle,"craneInstance",c);
                c.components[typeof(BoardGenerator)]=gen;c.components[typeof(CraneWorkTargetManager)]=target;c.components[typeof(CraneWorkPhaseTracker)]=tracker;c.components[typeof(CraneWorkCycleController)]=cycle;c.components[typeof(CraneWorkLoadPlanManager)]=load;Registry.cranes[i]=c;
            }
            Set(Manager,"craneOperationManager",Op);Set(Manager,"craneRegistry",Registry);Set(Manager,"interventionScenarioManager",new CraneInterventionScenarioManager());
            Set(Manager,"switchScheduleCsv",new TextAsset(Schedule));Set(Manager,"workConditionsCsv",new TextAsset(Work()));Set(Manager,"switchMethod",method);
            Manager.ExperimentEventOccurred+=e=>Events.Add(e.eventName);
        }
        public CraneWorkCycleController Cycle(int i) => Registry.cranes[i].GetComponent<CraneWorkCycleController>();
        public CraneWorkPhaseTracker Tracker(int i) => Registry.cranes[i].GetComponent<CraneWorkPhaseTracker>();
        public CraneWorkTargetData Target(int i)=> Registry.cranes[i].GetComponent<CraneWorkTargetManager>().CurrentTarget;
        public void Complete(int i) {Tracker(i).Complete();if(Cycle(i).IsRunning && Cycle(i).HasPendingNextPhase && !Cycle(i).IsPaused) Cycle(i).ContinueAfterBoundary();}
        public void Attach(int i,int count) {var boards=Registry.cranes[i].BoardGenerator.Boards;Registry.cranes[i].LifMagSystem.AttachedBoards.Clear();for(int b=0;b<count;b++)Registry.cranes[i].LifMagSystem.AttachedBoards.Add(boards[b]);}
        public void Update() {Call(Manager,"Update");}
    }
    public static void Main()
    {
        CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
        var schedule=TaskSwitchConditionCsv.ParseSchedule(Schedule,TaskSwitchTargetTaskPattern.Move1ToLiftUp,5);
        Require(schedule[0].targetTaskPattern==TaskSwitchTargetTaskPattern.Move1ToLiftUp && schedule[1].targetTaskPattern==TaskSwitchTargetTaskPattern.Move2ToPlace,"pattern parsing");
        Require(TaskSwitchConditionCsv.ParseSchedule(Schedule.Substring(0,Schedule.IndexOf(",targetTaskPattern")).Replace("maximumDelaySeconds","maximumDelaySeconds")+"\n1,1,Move1,1.5,2.5",TaskSwitchTargetTaskPattern.Move1ToLiftUp,5)[0].minimumDelaySeconds==1.5f,"invariant/legacy CSV");
        Throws(()=>TaskSwitchConditionCsv.ParseSchedule(Schedule.Replace("10,10","NaN,10"),TaskSwitchTargetTaskPattern.Move1ToLiftUp,5));
        Throws(()=>TaskSwitchConditionCsv.ParseSchedule(Schedule.Replace("2,2,Move2","1,2,Move2"),TaskSwitchTargetTaskPattern.Move1ToLiftUp,5));
        Throws(()=>TaskSwitchConditionCsv.ParseSchedule(Schedule.Replace("Move2ToPlace","Unknown"),TaskSwitchTargetTaskPattern.Move1ToLiftUp,5));
        Throws(()=>TaskSwitchConditionCsv.ParseWork(Work().Replace("Source,3,","Source,2,"),5));
        Throws(()=>TaskSwitchConditionCsv.ParseWork(Work().Replace("2,2\n","2,3\n"),5));
        var r=new Rig(TaskSwitchMethod.OperatorInitiated);r.Manager.StartExperiment();
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.OperatingSource && r.Cycle(0).TotalCycleCount==5 && r.Target(0).targetZ==1,"source CSV sorting/5 cycles/start");
        r.Manager.RequestSwitch();Require(r.Manager.CurrentState==TaskSwitchExperimentState.WaitingForOperatorSwitch && r.Op.CurrentCraneIndex==0 && !r.Op.IsOperationInputLocked && !r.Cycle(0).IsPaused,"operator request interrupted source before confirmation");
        ExperimentPauseManager.IsPaused=true;r.Manager.ConfirmTargetTask();Require(r.Op.CurrentCraneIndex==0,"paused confirmation accepted");ExperimentPauseManager.IsPaused=false;
        Input.buttons["JoyStick2RedButton"]=true;r.Manager.ConfirmTargetTask();
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.OperatingTarget && r.Op.CurrentCraneIndex==1 && r.Cycle(0).IsPaused && r.Cycle(1).CurrentPhase==CraneStatusManager.WorkPhase.Move1 && r.Op.IsOperationInputLocked,"single operator confirmation did not start target safely");
        r.Update();Require(r.Op.IsOperationInputLocked,"held red button unlocked movement");Input.buttons.Clear();r.Update();Require(!r.Op.IsOperationInputLocked,"button release did not unlock");
        r.Complete(1);Require(r.Cycle(1).CurrentPhase==CraneStatusManager.WorkPhase.LiftUp && r.Target(1).targetX==16 && r.Target(1).targetZ==2,"Move1 to LiftUp changed CSV pickup target");
        r.Attach(1,2);r.Complete(1);Require(!r.Cycle(1).IsRunning && r.Cycle(1).CompletedCycleCount==1 && r.Manager.CurrentState==TaskSwitchExperimentState.WaitingForSourceConfirmation,"pickup segment ran Move2 or did not return");
        r.Manager.ConfirmTargetTask();Require(r.Manager.CurrentState==TaskSwitchExperimentState.OperatingSource && r.Target(0).targetZ==1 && r.Cycle(0).CurrentPhase==CraneStatusManager.WorkPhase.Move1,"source resume changed interrupted target/phase");r.Update();
        // Finish source cycle 1 and begin cycle 2. CSV pickup coordinates change only at the new cycle.
        r.Complete(0);r.Attach(0,2);r.Complete(0);Require(r.Target(0).targetX==4 && r.Target(0).targetZ==11,"source placement CSV not applied");r.Complete(0);r.Registry.cranes[0].LifMagSystem.AttachedBoards.Clear();r.Complete(0);
        Require(r.Cycle(0).CurrentCycleNumber==2 && r.Target(0).targetZ==2,"next source cycle CSV not applied");
        r.Manager.RequestSwitch();r.Manager.ConfirmTargetTask();
        Require(!((CraneInterventionScenarioManager)Get(r.Manager,"interventionScenarioManager")).LastCreateAttachedPlate,"Move2 CSV preload generated an extra synthetic plate");
        var targetMag=r.Registry.cranes[1].LifMagSystem;var load=r.Registry.cranes[1].GetComponent<CraneWorkLoadPlanManager>();
        Require(r.Cycle(1).CurrentPhase==CraneStatusManager.WorkPhase.Move2 && targetMag.AttachedBoards.Count==3 && targetMag.GetAttachedTotalWeightKgForDisplay()==600 && r.Target(1).targetX==24 && r.Target(1).targetZ==6,"Move2 preloaded real boards/placement coordinates failed");
        Require(load.IsPickupBoardCountSatisfied(3) && !load.IsPickupBoardCountSatisfied(2) && !load.IsPlacementBoardCountSatisfied(3) && load.IsPlacementBoardCountSatisfied(2),"CSV board count constraint missing");
        Require(load.TargetRemainingWeightKg==300 && load.PlannedReleaseWeightKg==300,"partial placement did not use last-attached board weight or bypass zero-remaining default");
        // Display the resulting held weight, including a valid 0kg target.
        float displayed;
        load.SetExperimentBoardCounts(3,2);Require(load.PreparePlacementPlan(600),"partial display plan failed");
        foreach(var phase in new[]{CraneStatusManager.WorkPhase.Move2,CraneStatusManager.WorkPhase.Place,CraneStatusManager.WorkPhase.PlaceToTrack})
            Require(load.TryGetDisplayTargetWeightKg(phase,100,out displayed) && displayed==100 && load.PlannedReleaseWeightKg==500,"placement UI displayed released weight or drifted during release");
        load.SetExperimentBoardCounts(1,1);targetMag.AttachedBoards.RemoveRange(1,2);
        Require(load.PreparePlacementPlan(100) && load.TryGetDisplayTargetWeightKg(CraneStatusManager.WorkPhase.Place,0,out displayed) && displayed==0,"one-board full placement did not display 0kg");
        targetMag.AttachedBoards.AddRange(r.Registry.cranes[1].BoardGenerator.Boards.GetRange(1,2));load.SetExperimentBoardCounts(3,1);load.PreparePlacementPlan(600);
        Require(load.TryGetDisplayTargetWeightKg(CraneStatusManager.WorkPhase.LiftUp,600,out displayed) && displayed==600,"pickup display changed");
        var legacyDisplay=new CraneWorkLoadPlanManager();Set(legacyDisplay,"forceZeroRemainingWeightOnPlacement",false);
        legacyDisplay.SetPlannedReleaseWeightKg(200);
        Require(legacyDisplay.TryGetDisplayTargetWeightKg(CraneStatusManager.WorkPhase.Move2,1000,out displayed) && displayed==800,"unprepared release-weight display did not subtract released weight");
        legacyDisplay.PreparePlacementPlan(1000);
        Require(legacyDisplay.TryGetDisplayTargetWeightKg(CraneStatusManager.WorkPhase.Place,800,out displayed) && displayed==800,"prepared release target was subtracted again");
        legacyDisplay.ClearPlacementRuntimeState();legacyDisplay.SetTargetRemainingWeightKg(600);
        Require(legacyDisplay.TryGetDisplayTargetWeightKg(CraneStatusManager.WorkPhase.Move2,1000,out displayed) && displayed==600,"explicit remaining-weight display showed released weight");
        r.Complete(1);Require(r.Cycle(1).CurrentPhase==CraneStatusManager.WorkPhase.Place && r.Target(1).targetZ==6,"Move2 to Place coordinate drift");
        targetMag.AttachedBoards.RemoveAt(2);r.Complete(1);Require(r.Manager.CurrentState==TaskSwitchExperimentState.WaitingForSourceConfirmation && !r.Cycle(1).IsRunning,"placement segment ran another cycle");r.Manager.ConfirmTargetTask();
        for(int cycle=2;cycle<=5;cycle++)
        {
            r.Complete(0);r.Attach(0,2);r.Complete(0);r.Complete(0);r.Registry.cranes[0].LifMagSystem.AttachedBoards.Clear();r.Complete(0);
        }
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.Completed && r.Cycle(0).CompletedCycleCount==5,"source ended before/after 5 cumulative cycles");
        Require(r.Events.FindAll(e=>e=="TargetWorkCycleCompleted").Count==2 && r.Manager.CompletedSwitchCount==2,"duplicate completion or lost switch counts");
        // Existing display-confirmation, phase-boundary and countdown modes must keep their confirmation stage.
        foreach(var method in new[]{TaskSwitchMethod.ConfirmAfterDisplaySwitch,TaskSwitchMethod.PhaseBoundary,TaskSwitchMethod.Countdown})
        {
            var legacy=new Rig(method);legacy.Manager.StartExperiment();legacy.Manager.RequestSwitch();
            if(method==TaskSwitchMethod.PhaseBoundary) legacy.Complete(0);
            if(method==TaskSwitchMethod.Countdown) {Time.deltaTime=6;legacy.Update();}
            Require(legacy.Manager.CurrentState==TaskSwitchExperimentState.WaitingForConfirmation,"existing switch method lost target confirmation: "+method);
            legacy.Manager.ConfirmTargetTask();Require(legacy.Cycle(1).CurrentPhase==CraneStatusManager.WorkPhase.Move1,"existing mode segment did not start");
        }
        var automatic=new Rig(TaskSwitchMethod.OperatorInitiated);Time.realtimeSinceStartupAsDouble=0;automatic.Manager.StartExperiment();Time.realtimeSinceStartupAsDouble=11;automatic.Update();
        Require(automatic.Manager.CurrentState==TaskSwitchExperimentState.WaitingForOperatorSwitch,"CSV request did not arm operator confirmation");
        Console.WriteLine("PASS: actual manager/cycle/load-plan integration, two segment patterns, real-board preload and partial placement weights, cumulative five source cycles, no target drift on return, operator source continuation/one-click/neutral guard, Pause, all legacy methods and automatic CSV request");
    }
}

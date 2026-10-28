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
    static void CheckSwitchMethodCsvPairs()
    {
        string[] fields={"confirmAfterDisplaySwitchCsv","countdownCsv","phaseBoundaryCsv","operatorInitiatedCsv","countdownOperatorInitiatedCsv","announcedPhaseBoundaryCsv","announcedPhaseBoundaryOperatorInitiatedCsv"};
        var rig=new Rig(TaskSwitchMethod.OperatorInitiated);
        // Start once without schedules: enabling method pairs on the next run must subscribe correctly.
        Set(rig.Manager,"useSwitchScheduleCsv",false);
        rig.Manager.StartExperiment();
        Require(rig.Target(0).targetZ==1,"common work CSV compatibility");
        rig.Manager.ExitExperimentMode();
        var pairs=new TaskSwitchMethodCsvPair[fields.Length];
        for(int i=0;i<fields.Length;i++)
        {
            int delay=20+i;
            pairs[i]=new TaskSwitchMethodCsvPair {
                switchScheduleCsv=new TextAsset(Schedule.Replace("10,10",delay+","+delay)) {name="schedule-"+i+".csv"},
                workConditionsCsv=new TextAsset(Work().Replace("Source,1,-4,1,","Source,1,-4,"+(30+i)+",")) {name="work-"+i+".csv"}
            };
            Set(rig.Manager,fields[i],pairs[i]);
        }
        Set(rig.Manager,"useSwitchMethodCsvPairs",true);
        var loaded=new List<TaskSwitchEventData>();
        rig.Manager.ExperimentEventOccurred+=e=>loaded.Add(e);
        for(int i=0;i<fields.Length;i++)
        {
            rig.Manager.SetSwitchMethod(i);
            Require(rig.Manager.SwitchScheduleCsvText==pairs[i].switchScheduleCsv.text && rig.Manager.WorkConditionsCsvText==pairs[i].workConditionsCsv.text,"selected method CSV preview: "+i);
            Time.realtimeSinceStartupAsDouble=0;
            rig.Manager.StartExperiment();
            Require(rig.Manager.CurrentState==TaskSwitchExperimentState.OperatingSource && rig.Target(0).targetZ==30+i,"selected method work CSV not applied: "+i);
            Require(loaded.Exists(e=>e.eventName=="SwitchScheduleLoaded" && e.detail.Contains("File=schedule-"+i+".csv;BySwitchMethod=True")) &&
                loaded.Exists(e=>e.eventName=="WorkConditionsLoaded" && e.detail.Contains("File=work-"+i+".csv;BySwitchMethod=True")),"applied CSV provenance missing: "+i);
            // Editing the Inspector table during a run must not change the logged input files.
            TextAsset scheduleFile=pairs[i].switchScheduleCsv,workFile=pairs[i].workConditionsCsv;
            pairs[i].switchScheduleCsv=new TextAsset("invalid");pairs[i].workConditionsCsv=null;
            Require(rig.Manager.SwitchScheduleCsvText==scheduleFile.text && rig.Manager.WorkConditionsCsvText==workFile.text,"active-run CSV references changed");
            Time.realtimeSinceStartupAsDouble=19+i;rig.Update();
            Require(rig.Manager.CurrentState==TaskSwitchExperimentState.OperatingSource,"schedule used common file delay: "+i);
            Time.realtimeSinceStartupAsDouble=21+i;rig.Update();
            Require(rig.Manager.CurrentState!=TaskSwitchExperimentState.OperatingSource && rig.Manager.CurrentSwitchIndex==1,"selected schedule ignored when common schedule disabled: "+i);
            pairs[i].switchScheduleCsv=scheduleFile;pairs[i].workConditionsCsv=workFile;
            rig.Manager.ExitExperimentMode();
        }
        // Both slots are required; valid common CSVs must not silently replace missing or invalid pairs.
        rig.Manager.SetSwitchMethod(3);
        for(int missing=0;missing<2;missing++)
        {
            TextAsset file=missing==0 ? pairs[3].switchScheduleCsv : pairs[3].workConditionsCsv;
            if(missing==0)pairs[3].switchScheduleCsv=null;else pairs[3].workConditionsCsv=null;
            int errors=Debug.Errors;rig.Manager.StartExperiment();
            Require(rig.Manager.CurrentState==TaskSwitchExperimentState.Idle && Debug.Errors==errors+1,"missing method CSV started experiment");
            if(missing==0)pairs[3].switchScheduleCsv=file;else pairs[3].workConditionsCsv=file;
        }
        var valid=pairs[3].workConditionsCsv;pairs[3].workConditionsCsv=new TextAsset("invalid");
        int before=Debug.Errors;rig.Manager.StartExperiment();
        Require(rig.Manager.CurrentState==TaskSwitchExperimentState.Idle && Debug.Errors==before+1,"invalid method CSV started experiment");
        pairs[3].workConditionsCsv=valid;
        Set(rig.Manager,"useSwitchMethodCsvPairs",false);Set(rig.Manager,"useSwitchScheduleCsv",true);
        Require(rig.Manager.SwitchScheduleCsvText==Schedule && rig.Manager.WorkConditionsCsvText==Work(),"common CSV selection not restored");
        Time.realtimeSinceStartupAsDouble=0;rig.Manager.StartExperiment();
        Require(rig.Target(0).targetZ==1,"common work CSV not restored");
        Time.realtimeSinceStartupAsDouble=11;rig.Update();
        Require(rig.Manager.CurrentState==TaskSwitchExperimentState.WaitingForOperatorSwitch,"common schedule not restored");
        Console.WriteLine("PASS: seven method CSV pairs, automatic schedule despite common toggle, restart selection, active-run snapshots, file provenance, missing/invalid pair rejection and common CSV compatibility");
    }
    static void CheckCognitiveSupportGoalPreview()
    {
        var r=new Rig(TaskSwitchMethod.ConfirmAfterDisplaySwitch);
        r.Manager.StartExperiment();
        float beforeX=r.Target(0).targetX,beforeZ=r.Target(0).targetZ,x,z;
        Require(r.Manager.TryGetCognitiveSupportTargetPosition(0,CraneStatusManager.WorkPhase.Move2,out x,out z) && x==4 && z==11,
            "Source pending placement preview must use its active CSV cycle");
        Require(r.Target(0).targetX==beforeX && r.Target(0).targetZ==beforeZ,
            "Support preview changed the current Source goal before confirmation");
        Require(r.Manager.TryGetCognitiveSupportTargetPosition(1,CraneStatusManager.WorkPhase.Move1,out x,out z) && x==16 && z==2,
            "Target pickup preview must use this switch condition");
        Require(r.Manager.TryGetCognitiveSupportTargetPosition(1,CraneStatusManager.WorkPhase.Place,out x,out z) && x==24 && z==8,
            "Target placement preview must use this switch condition");
        Require(!r.Manager.TryGetCognitiveSupportTargetPosition(10,CraneStatusManager.WorkPhase.Move1,out x,out z),
            "Unknown crane must not receive an arbitrary preview goal");
        float pickupWeight;
        var load=r.Registry.cranes[1].GetComponent<CraneWorkLoadPlanManager>();
        float oldPickupWeight=load.PickupTargetWeightKg;
        Require(r.Manager.TryGetCognitiveSupportPickupTargetWeightKg(1,out pickupWeight)&&pickupWeight==300,
            "Target pickup weight preview must sum the selected CSV board count");
        Require(load.PickupTargetWeightKg==oldPickupWeight&&r.Target(0).targetX==beforeX&&r.Target(0).targetZ==beforeZ,
            "Weight preview changed a load plan or live goal");
        Require(!r.Manager.TryGetCognitiveSupportPickupTargetWeightKg(10,out pickupWeight),
            "Unknown crane cannot receive an arbitrary pickup weight");
        var state=r.Manager.CurrentState;
        r.Manager.RecordCognitiveSupportEvent("Shown","Content=PhaseOnly;DurationSeconds=8");
        Require(r.Events.Contains("CognitiveSupportShown") && r.Manager.CurrentState==state,
            "Support event must be recorded without changing experiment state");
        r.Manager.ExitExperimentMode();
        Console.WriteLine("PASS: cognitive support CSV goal preview without goal mutation, active condition selection, and event recording");
    }
    static Rig HybridRig(TaskSwitchMethod method)
    {
        ExperimentPauseManager.IsPaused=false;ExperimentPauseManager.PauseOffsetSeconds=0;
        Time.realtimeSinceStartupAsDouble=0;Time.deltaTime=0;Input.buttons.Clear();
        var r=new Rig(method);
        var countdownText=new UnityEngine.UI.Text();countdownText.rectTransform.sizeDelta=new Vector2(300,300);
        Set(r.Manager,"countdownText",countdownText);
        var panel=new GameObject();panel.components[typeof(RectTransform)]=new RectTransform { sizeDelta=new Vector2(300,300),anchoredPosition=new Vector2(400,0) };
        Set(r.Manager,"countdownPanel",panel);
        Set(r.Manager,"confirmationButtonText",new UnityEngine.UI.Text());
        r.Manager.StartExperiment();return r;
    }
    static void ReturnFromPickup(Rig r)
    {
        Input.buttons.Clear();r.Update();r.Complete(1);r.Attach(1,2);r.Complete(1);
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.WaitingForSourceConfirmation,"hybrid target segment did not return");
        r.Manager.ConfirmTargetTask();r.Update();
    }
    static void CheckHybridSwitches()
    {
        Require((int)TaskSwitchMethod.ConfirmAfterDisplaySwitch==0 && (int)TaskSwitchMethod.OperatorInitiated==3 &&
            (int)TaskSwitchMethod.CountdownOperatorInitiated==4 && (int)TaskSwitchMethod.AnnouncedPhaseBoundary==5 &&
            (int)TaskSwitchMethod.AnnouncedPhaseBoundaryOperatorInitiated==6,"serialized method values changed");

        var r=HybridRig(TaskSwitchMethod.CountdownOperatorInitiated);
        Require(r.Manager.OperatorSwitchDeadlineSeconds==10 && r.Manager.CountdownSeconds==10,"hybrid deadline/default CSV duration");
        r.Manager.RequestSwitch();
        var label=(UnityEngine.UI.Text)Get(r.Manager,"countdownText");
        Require(r.Op.CurrentCraneIndex==0 && !r.Op.IsOperationInputLocked && !r.Cycle(0).IsPaused &&
            r.Manager.CurrentState==TaskSwitchExperimentState.CountingDown && label.text=="10" && !label.resizeTextForBestFit && label.fontSize==170,"deadline numeral size/source continuation");
        Require(((UnityEngine.UI.Text)Get(r.Manager,"confirmationButtonText")).text=="確認ボタンで切替","deadline early-switch hint missing");
        Time.realtimeSinceStartupAsDouble=9.999;Input.buttons["JoyStick2RedButton"]=true;r.Manager.ConfirmTargetTask();
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.OperatingTarget && r.Op.IsOperationInputLocked &&
            r.Events.Contains("OperatorSwitchConfirmationPressed") && !r.Events.Contains("OperatorDeadlineReached"),"early deadline choice/one-click");
        Require(!label.resizeTextForBestFit && label.resizeTextMaxSize==170,"notice changed original countdown style");
        ReturnFromPickup(r);Time.realtimeSinceStartupAsDouble=50;r.Update();
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.OperatingSource && !r.Events.Contains("OperatorDeadlineReached"),"old deadline survived early switch/return");

        r=HybridRig(TaskSwitchMethod.CountdownOperatorInitiated);r.Manager.RequestSwitch();
        Time.realtimeSinceStartupAsDouble=10;r.Update();r.Update();
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.WaitingForConfirmation && r.Op.CurrentCraneIndex==1 &&
            r.Op.IsOperationInputLocked && r.Events.FindAll(x=>x=="OperatorDeadlineReached").Count==1 && !r.Events.Contains("ConfirmationPressed"),"deadline fallback must switch once and retain confirmation");
        r.Manager.ConfirmTargetTask();Require(r.Manager.CurrentState==TaskSwitchExperimentState.OperatingTarget,"deadline target confirmation failed");

        r=HybridRig(TaskSwitchMethod.CountdownOperatorInitiated);r.Manager.RequestSwitch();
        Time.realtimeSinceStartupAsDouble=10;r.Manager.ConfirmTargetTask();
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.WaitingForConfirmation && !r.Events.Contains("OperatorSwitchConfirmationPressed"),"UI press exactly at deadline counted as early choice");
        r=HybridRig(TaskSwitchMethod.CountdownOperatorInitiated);r.Manager.SetOperatorSwitchDeadlineSeconds(0);r.Manager.RequestSwitch();
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.WaitingForConfirmation,"zero deadline did not switch immediately");

        r=HybridRig(TaskSwitchMethod.CountdownOperatorInitiated);r.Manager.RequestSwitch();Time.realtimeSinceStartupAsDouble=3;r.Update();
        ExperimentPauseManager.IsPaused=true;Time.realtimeSinceStartupAsDouble=103;r.Update();r.Manager.ConfirmTargetTask();
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.CountingDown && r.Manager.CountdownRemaining==7,"paused deadline/input progressed");
        ExperimentPauseManager.PauseOffsetSeconds=100;ExperimentPauseManager.IsPaused=false;r.Update();
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.CountingDown && r.Manager.CountdownRemaining==7,"pause duration consumed deadline");
        Time.realtimeSinceStartupAsDouble=110;r.Update();Require(r.Events.Contains("OperatorDeadlineReached"),"resumed deadline failed");

        foreach(var method in new[]{TaskSwitchMethod.AnnouncedPhaseBoundary,TaskSwitchMethod.AnnouncedPhaseBoundaryOperatorInitiated})
        {
            r=HybridRig(method);r.Complete(0);r.Attach(0,2); // request during LiftUp, not Inspector's initial Move1.
            r.Manager.RequestSwitch();label=(UnityEngine.UI.Text)Get(r.Manager,"countdownText");
            Require(r.Manager.CurrentState==TaskSwitchExperimentState.WaitingForPhaseBoundary && !r.Cycle(0).IsPaused &&
                label.text=="フェーズ\n終了後" && !label.resizeTextForBestFit && label.fontSize==80 && r.Events.Contains("PhaseBoundaryAdvanceNoticeShown"),"announced boundary notice/source continuation");
            var panelRect=((GameObject)Get(r.Manager,"countdownPanel")).GetComponent<RectTransform>();
            Require(label.rectTransform.sizeDelta.x==350 && panelRect.sizeDelta.x==350 && panelRect.anchoredPosition.x==425,"phase notice layout not widened");
            var hint=((UnityEngine.UI.Text)Get(r.Manager,"confirmationButtonText")).text;
            Require(method==TaskSwitchMethod.AnnouncedPhaseBoundary ? hint!="確認ボタンで切替" : hint=="確認ボタンで切替","phase early-switch hint policy mismatch");
            if(method==TaskSwitchMethod.AnnouncedPhaseBoundary)
            {
                r.Manager.ConfirmTargetTask();Require(r.Op.CurrentCraneIndex==0 && !r.Events.Contains("OperatorSwitchConfirmationPressed"),"boundary-only accepted early switch");
                Time.realtimeSinceStartupAsDouble=100;r.Update();Require(r.Op.CurrentCraneIndex==0,"announced boundary used a time deadline");
            }
            r.Complete(0);
            Require(r.Manager.CurrentState==TaskSwitchExperimentState.WaitingForConfirmation && r.Op.CurrentCraneIndex==1 &&
                r.Cycle(0).HasPendingNextPhase,"actual current phase boundary failed or advanced prematurely");
            Require(label.fontSize==170 && !label.resizeTextForBestFit && label.rectTransform.sizeDelta.x==300 &&
                panelRect.sizeDelta.x==300 && panelRect.anchoredPosition.x==400,"phase notice failed to restore numeral layout");
            r.Manager.ConfirmTargetTask();ReturnFromPickup(r);
            Require(r.Cycle(0).CurrentPhase==CraneStatusManager.WorkPhase.Move2 && !r.Cycle(0).IsWaitingAtBoundary,"boundary return did not continue next phase");
        }

        r=HybridRig(TaskSwitchMethod.AnnouncedPhaseBoundaryOperatorInitiated);r.Manager.RequestSwitch();
        Input.buttons["JoyStick2RedButton"]=true;r.Manager.ConfirmTargetTask();
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.OperatingTarget && r.Cycle(0).IsPaused &&
            !(bool)Get(r.Cycle(0),"holdAfterCurrentPhase"),"early boundary choice left future boundary hold armed");
        ReturnFromPickup(r);Require(r.Cycle(0).CurrentPhase==CraneStatusManager.WorkPhase.Move1,"early return skipped interrupted phase");
        r.Complete(0);Require(r.Cycle(0).CurrentPhase==CraneStatusManager.WorkPhase.LiftUp && !r.Cycle(0).IsWaitingAtBoundary,"source stranded at a later boundary after early choice");

        r=HybridRig(TaskSwitchMethod.AnnouncedPhaseBoundaryOperatorInitiated);
        r.Tracker(0).Complete(); // leave the next phase pending instead of Rig.Complete's auto continuation.
        r.Manager.RequestSwitch();Require(r.Manager.CurrentState==TaskSwitchExperimentState.WaitingForConfirmation,"already-completed phase request waited indefinitely");

        r=HybridRig(TaskSwitchMethod.AnnouncedPhaseBoundaryOperatorInitiated);r.Manager.RequestSwitch();r.Op.FailSelect=true;r.Manager.ConfirmTargetTask();
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.OperatingSource && !r.Cycle(0).IsPaused &&
            !(bool)Get(r.Cycle(0),"holdAfterCurrentPhase"),"early selection failure left source/hold stranded");

        r=HybridRig(TaskSwitchMethod.CountdownOperatorInitiated);r.Manager.RequestSwitch();r.Manager.CompleteExperiment();Time.realtimeSinceStartupAsDouble=30;r.Update();
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.Completed && r.Op.IsOperationInputLocked && !r.Events.Contains("OperatorDeadlineReached"),"completed experiment retained deadline");
        r.Manager.ExitExperimentMode();r.Manager.SetSwitchMethod(1);r.Manager.StartExperiment();r.Manager.RequestSwitch();
        Require(r.Manager.CountdownSeconds==5 && !((UnityEngine.UI.Text)Get(r.Manager,"countdownText")).resizeTextForBestFit,"legacy countdown setting/style changed after hybrid exit");
        Time.deltaTime=6;r.Update();Require(r.Manager.CurrentState==TaskSwitchExperimentState.WaitingForConfirmation,"legacy countdown no longer works after hybrid run");
        r=HybridRig(TaskSwitchMethod.CountdownOperatorInitiated);r.Manager.ExitExperimentMode();Set(r.Manager,"operatorSwitchDeadlineSeconds",float.NaN);r.Manager.StartExperiment();
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.Idle,"non-finite deadline accepted");
        ExperimentPauseManager.IsPaused=false;ExperimentPauseManager.PauseOffsetSeconds=0;Input.buttons.Clear();Time.deltaTime=0;
        Console.WriteLine("PASS: three hybrid policies, deadline boundaries/zero/Pause, notice UI/font restoration, actual phase completion, one-click early choice, source resume/hold cleanup, selection failure, completion/restart and enum compatibility");
    }
    static Rig SourceReturnReady(CraneStatusManager.WorkPhase phase)
    {
        var r=HybridRig(TaskSwitchMethod.AnnouncedPhaseBoundary);
        if(phase==CraneStatusManager.WorkPhase.Move2) {r.Complete(0);r.Attach(0,2);r.Complete(0);}
        r.Manager.RequestSwitch();r.Complete(0);r.Manager.ConfirmTargetTask();r.Update();
        r.Complete(1);r.Attach(1,2);r.Complete(1);
        Require(r.Manager.CurrentState==TaskSwitchExperimentState.WaitingForSourceConfirmation,"source return setup failed");
        return r;
    }
    static void CheckSourceReturnValidation()
    {
        foreach(var phase in new[]{CraneStatusManager.WorkPhase.Move1,CraneStatusManager.WorkPhase.Move2})
        {
            var r=SourceReturnReady(phase);var goal=r.Target(0);int completed=r.Cycle(0).CompletedCycleCount;
            r.Tracker(0).ReopenAlignmentOnBoundary=true;r.Manager.ConfirmTargetTask();
            Require(r.Manager.CurrentState==TaskSwitchExperimentState.OperatingSource && r.Cycle(0).CurrentPhase==phase &&
                !r.Cycle(0).IsPaused && !r.Cycle(0).HasPendingNextPhase && !r.Cycle(0).IsWaitingAtBoundary &&
                r.Tracker(0).IsMonitoring && !r.Tracker(0).IsMajorPhaseCompleted,"invalid boundary did not resume same movement alignment");
            Require(r.Target(0).targetX==goal.targetX && r.Target(0).targetZ==goal.targetZ && r.Cycle(0).CompletedCycleCount==completed &&
                r.Events.Contains("SourceMovementBoundaryReopened"),"reopening changed the goal/count or lost audit event");
            r.Update();r.Complete(0);
            Require(r.Cycle(0).CurrentPhase==(phase==CraneStatusManager.WorkPhase.Move1?CraneStatusManager.WorkPhase.LiftUp:CraneStatusManager.WorkPhase.Place),
                "realignment completion did not restore ordinary phase progress");
        }
        var failed=SourceReturnReady(CraneStatusManager.WorkPhase.Move1);failed.Tracker(0).BoundaryValidationSucceeds=false;
        failed.Manager.ConfirmTargetTask();failed.Update();
        Require(failed.Manager.CurrentState==TaskSwitchExperimentState.WaitingForSourceConfirmation && failed.Op.IsOperationInputLocked &&
            failed.Cycle(0).HasPendingNextPhase && failed.Cycle(0).IsPaused && failed.Events.Contains("SourceReturnResumeFailed") &&
            !failed.Events.Contains("SwitchCompleted") && !failed.Events.Contains("SourceInputUnlocked"),"failed validation unlocked source or completed switch");
        failed.Tracker(0).BoundaryValidationSucceeds=true;failed.Manager.ConfirmTargetTask();failed.Update();
        Require(failed.Manager.CurrentState==TaskSwitchExperimentState.OperatingSource && !failed.Op.IsOperationInputLocked &&
            failed.Cycle(0).CurrentPhase==CraneStatusManager.WorkPhase.LiftUp,"failed validation could not be retried");

        var legacy=SourceReturnReady(CraneStatusManager.WorkPhase.Move1);legacy.Cycle(0).StopCycle();Set(legacy.Manager,"sourceCycleController",null);
        legacy.Tracker(0).ReopenAlignmentOnBoundary=true;legacy.Manager.ConfirmTargetTask();
        Require(legacy.Manager.CurrentState==TaskSwitchExperimentState.OperatingSource && legacy.Tracker(0).CurrentMajorPhase==CraneStatusManager.WorkPhase.Move1 &&
            legacy.Tracker(0).IsMonitoring && legacy.Events.Contains("SourceMovementBoundaryReopened"),"tracker-only fallback skipped realignment");

        var held=SourceReturnReady(CraneStatusManager.WorkPhase.Move1);held.Op.MovementCommand=new Vector3(.2f,0,0);
        Input.buttons["JoyStick2RedButton"]=true;held.Manager.ConfirmTargetTask();held.Update();Input.buttons.Clear();held.Update();
        Require(held.Op.IsOperationInputLocked && !held.Events.Contains("SourceInputUnlocked") &&
            ((UnityEngine.UI.Text)Get(held.Manager,"confirmationButtonText")).text=="操作入力を中立へ","held return controls unlocked or lacked hint");
        held.Op.MovementCommand=new Vector3(0,1,0);held.Update();Require(held.Op.IsOperationInputLocked,"held vertical return input unlocked");
        held.Op.MovementCommand=Vector3.zero;held.Op.SpreadCommand=.3f;held.Update();Require(held.Op.IsOperationInputLocked,"held spread return input unlocked");
        held.Op.SpreadCommand=.01f;ExperimentPauseManager.IsPaused=true;held.Update();Require(held.Op.IsOperationInputLocked,"Pause unlocked return controls");
        ExperimentPauseManager.IsPaused=false;held.Update();held.Update();
        Require(!held.Op.IsOperationInputLocked && held.Events.FindAll(e=>e=="SourceInputUnlocked").Count==1,"neutral return controls did not unlock exactly once");
        Console.WriteLine("PASS: source return validation, Move1/Move2 fine-alignment restoration and goal/count preservation, failure lock/retry, tracker-only fallback and neutral movement/spread release gate with Pause/UI hint");
    }
    public static void Main()
    {
        CheckCognitiveSupportGoalPreview();
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
        CheckSwitchMethodCsvPairs();
        CheckHybridSwitches();
        CheckSourceReturnValidation();
        Console.WriteLine("PASS: actual manager/cycle/load-plan integration, two segment patterns, real-board preload and partial placement weights, cumulative five source cycles, no target drift on return, operator source continuation/one-click/neutral guard, Pause, all legacy methods and automatic CSV request");
    }
}

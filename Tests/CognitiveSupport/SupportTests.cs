using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
public static class SupportTests
{
    static int checks;
    static void Check(bool ok,string message) { checks++;if(!ok)throw new Exception(message); }
    static void Set(object o,string name,object value) { o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,value); }
    static void Call(object o,string name) { o.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null); }
    static CraneWorkStepDefinition Step(string id) { return CraneWorkPhaseProfile.CreateBuiltInDefaults().Find(s=>s.stepId==id); }
    static Vector3 Guidance(string id,float x,float z,bool contact=false,float y=-3,bool goal=true)
    { return TaskSwitchSupportGuidance.RequiredMovement(Step(id),Vector3.zero,goal,x,z,contact,true,y); }
    public static void Main()
    {
        Check(TaskSwitchSupportGuidance.PhaseLabel(CraneStatusManager.WorkPhase.Move1)=="移動","Move1 label");
        Check(TaskSwitchSupportGuidance.PhaseLabel(CraneStatusManager.WorkPhase.Move2)=="移動","Move2 label");
        Check(TaskSwitchSupportGuidance.PhaseLabel(CraneStatusManager.WorkPhase.LiftUp)=="吊り上げ","Lift label");
        Check(TaskSwitchSupportGuidance.PhaseLabel(CraneStatusManager.WorkPhase.Place)=="配置","Place label");
        Check(TaskSwitchSupportGuidance.PhaseLabel(CraneStatusManager.WorkPhase.PlaceToTrack)=="配置","Trailer label");
        var g=Guidance("Move1.PickupCoarseMove",2,-2);
        Check(g.x==1&&g.z==-1&&g.y==0,"Independent X/Z directions");
        g=Guidance("Move2.DestinationFineAlign",.05f,0);
        Check(g.x==0&&g.z==0,"At tolerance hides arrow");
        g=Guidance("Move2.DestinationFineAlign",-.1f,.1f);
        Check(g.x==-1&&g.z==1,"Fine alignment signs");
        g=Guidance("Move1.PickupCoarseMove",.2f,.2f);
        Check(g.x==0&&g.z==0,"Use the actual coarse-step tolerance");
        g=Guidance("Move1.PickupCoarseMove",2,2,goal:false);
        Check(g.x==0&&g.z==0,"Unknown goal gives no travel guidance");
        g=Guidance("Place.PlacementLowering",float.NaN,0);
        Check(g.x==0&&g.y==0&&g.z==0,"Non-finite goal cannot enable lowering");
        g=Guidance("Place.PlacementLowering",2,0);
        Check(g.x==1&&g.y==0,"Align before lowering, never simultaneous domains");
        Check(Guidance("LiftUp.PickupLowering",0,0).y==-1,"Pickup lowering");
        Check(Guidance("LiftUp.PickupLowering",0,0,true).y==0,"Touchdown hides downward arrow");
        Check(Guidance("LiftUp.LoadedRaising",0,0,y:-2).y==1,"Loaded raising");
        Check(Guidance("Place.PostPlacementRaising",0,0,y:-1.66f).y==0,"At height hides raising arrow");
        Check(Guidance("LiftUp.LoadAcquisition",0,0).y==0,"Acquiring weight is not movement");
        Check(Guidance("Place.LoadRelease",0,0).y==0,"Release weight is not movement");

        Check(TaskSwitchSupportGuidance.ChevronCount(.5f,.5f,3)==1,"Near distance: one chevron at boundary");
        Check(TaskSwitchSupportGuidance.ChevronCount(.5001f,.5f,3)==2,"Middle distance: two chevrons");
        Check(TaskSwitchSupportGuidance.ChevronCount(1f,.5f,3)==2,"Middle upper boundary");
        Check(TaskSwitchSupportGuidance.ChevronCount(1.0001f,.5f,3)==3,"Far distance: three chevrons");
        Check(TaskSwitchSupportGuidance.ChevronCount(float.MaxValue,.01f,3)==3,"Far distances cap without integer overflow");
        Check(TaskSwitchSupportGuidance.ChevronCount(float.NaN,.5f,3)==1&&
            TaskSwitchSupportGuidance.ChevronCount(-1,.5f,3)==1,"Unknown distances show direction only");
        var remaining=TaskSwitchSupportGuidance.RemainingTravelDistance(Step("Move1.PickupCoarseMove"),Vector3.zero,true,2,-2);
        Check(remaining.x==1.5f&&remaining.z==1.5f,"Travel distance measured to acceptable range");
        remaining=TaskSwitchSupportGuidance.RemainingTravelDistance(Step("Move1.PickupCoarseMove"),Vector3.zero,true,.2f,0);
        Check(remaining.x==0&&remaining.z==0,"Distances cannot become negative inside tolerance");

        var manager=new TaskSwitchExperimentManager();
        var tracker=new CraneWorkPhaseTracker { CurrentMajorPhase=CraneStatusManager.WorkPhase.Move1,CurrentStepDefinition=Step("Move1.PickupCoarseMove"),GoalX=2,GoalZ=-2 };
        var cycle=new CraneWorkCycleController();
        var crane=new CraneInstance { InformationTarget=new Transform(),CraneUnit=new CraneUnit() };
        crane.gameObject.Components[typeof(CraneWorkPhaseTracker)]=tracker;
        crane.gameObject.Components[typeof(CraneWorkCycleController)]=cycle;
        crane.gameObject.Components[typeof(CraneWorkTargetManager)]=new CraneWorkTargetManager();
        var operation=new CraneOperationManager { CurrentCraneInstance=crane };
        var support=new TaskSwitchCognitiveSupport();var root=new GameObject();var phase=new Text();
        var left=new RectTransform();var ry=new RectTransform();var rz=new RectTransform();
        var la=new TaskSwitchSupportArrow();var ya=new TaskSwitchSupportArrow();var za=new TaskSwitchSupportArrow();
        Set(support,"experimentManager",manager);Set(support,"operationManager",operation);
        Set(support,"supportRoot",root);Set(support,"phaseText",phase);
        Set(support,"leftXGroup",left);Set(support,"rightYGroup",ry);Set(support,"rightZGroup",rz);
        Set(support,"leftXArrow",la);Set(support,"rightYArrow",ya);Set(support,"rightZArrow",za);
        Call(support,"OnEnable");Check(!root.activeSelf,"Initially hidden");
        ExperimentPauseManager.ActiveRealtime=100;
        manager.Raise("SwitchRequested");Check(!support.IsVisible,"No support before actual display switch");
        manager.Raise("TargetDisplaySwitched");
        Check(support.IsVisible&&root.activeSelf,"Show at display switch before confirmation");
        Check(phase.text=="移動"&&left.gameObject.activeSelf&&rz.gameObject.activeSelf&&!ry.gameObject.activeSelf,"Both content with travel arrows");
        Check(la.rectTransform.localEulerAngles.z==0&&za.rectTransform.localEulerAngles.z==-90,"Controller mapping X positive up and Z negative right");
        Set(support,"supportContent",TaskSwitchSupportContent.PhaseOnly);Call(support,"LateUpdate");
        Check(phase.gameObject.activeSelf&&!left.gameObject.activeSelf&&!rz.gameObject.activeSelf,"Phase-only content");
        Set(support,"supportContent",TaskSwitchSupportContent.InputDirectionOnly);Call(support,"LateUpdate");
        Check(!phase.gameObject.activeSelf&&left.gameObject.activeSelf,"Direction-only content");
        Set(support,"positiveXPointsUp",false);Call(support,"LateUpdate");
        Check(la.rectTransform.localEulerAngles.z==180,"Configurable controller polarity");
        ExperimentPauseManager.ActiveRealtime=104;manager.CurrentState=TaskSwitchExperimentState.OperatingTarget;manager.Raise("TargetOperationStarted");
        tracker.CurrentMajorPhase=CraneStatusManager.WorkPhase.LiftUp;tracker.CurrentStepDefinition=null;
        Call(support,"LateUpdate");
        Check(!left.gameObject.activeSelf&&!ry.gameObject.activeSelf&&!rz.gameObject.activeSelf,
            "Completed phase must not replay its first lowering step while advancing");
        tracker.CurrentMajorPhase=CraneStatusManager.WorkPhase.Move1;tracker.CurrentStepDefinition=Step("Move1.PickupCoarseMove");
        ExperimentPauseManager.ActiveRealtime=108;Call(support,"LateUpdate");
        Check(!support.IsVisible&&!root.activeSelf,"Confirmation must not restart 8-second deadline");
        Check(manager.Recorded.Exists(s=>s.StartsWith("Shown:"))&&manager.Recorded.Exists(s=>s.Contains("Reason=Timeout")),"Presentation changes logged");
        ExperimentPauseManager.ActiveRealtime=200;manager.CurrentState=TaskSwitchExperimentState.WaitingForSourceConfirmation;
        cycle.HasPendingNextPhase=true;cycle.PendingNextPhase=CraneStatusManager.WorkPhase.Move2;
        manager.GoalX=-2;manager.GoalZ=2;manager.Raise("SourceDisplayRestored");
        Check(support.IsVisible,"Return restarts support window");
        Check(phase.text=="移動"&&la.rectTransform.localEulerAngles.z==0,"CSV preview uses pending Source phase goal, not stale displayed goal");
        manager.HasPreview=false;Call(support,"LateUpdate");
        Check(!left.gameObject.activeSelf&&!rz.gameObject.activeSelf,"Unknown future random target cannot use stale current target");
        ExperimentPauseManager.IsPaused=true;ExperimentPauseManager.ActiveRealtime=207;Call(support,"LateUpdate");
        Check(support.IsVisible,"Pause keeps presentation visible");
        ExperimentPauseManager.IsPaused=false;ExperimentPauseManager.ActiveRealtime=208;Call(support,"LateUpdate");
        Check(!support.IsVisible,"Uses active clock for deadline");
        manager.Raise("SourceDisplayRestored");manager.Raise("ExperimentCompleted");
        Check(!support.IsVisible,"Completion clears support");
        manager.Raise("TargetDisplaySwitched");Call(support,"OnDisable");manager.Raise("SourceDisplayRestored");
        Check(!support.IsVisible&&!root.activeSelf,"Disable hides and unsubscribes");
        var mesh=new VertexHelper();typeof(TaskSwitchSupportArrow).GetMethod("OnPopulateMesh",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(la,new object[]{mesh});
        Check(mesh.Vertices==7&&mesh.Triangles==3,"Arrow mesh has valid shaft and head");
        la.SetPresentation(TaskSwitchDirectionPresentation.DistanceChevrons,2,3);
        typeof(TaskSwitchSupportArrow).GetMethod("OnPopulateMesh",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(la,new object[]{mesh});
        Check(mesh.Vertices==12&&mesh.Triangles==8,"Two chevrons render independent valid glyphs");
        float glyphHeight=mesh.Points[1].y-mesh.Points[0].y;
        la.SetPresentation(TaskSwitchDirectionPresentation.DistanceChevrons,99,99);
        typeof(TaskSwitchSupportArrow).GetMethod("OnPopulateMesh",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(la,new object[]{mesh});
        Check(la.ChevronCount==3&&mesh.Vertices==18&&mesh.Triangles==12,"Rendering limited to three chevrons");
        Check(mesh.BadWinding==0&&Math.Abs(glyphHeight-(mesh.Points[1].y-mesh.Points[0].y))<.0001f,
            "Glyph size stays constant across counts and triangles face consistently");
        Set(support,"directionPresentation",TaskSwitchDirectionPresentation.DistanceChevrons);
        Set(support,"supportContent",TaskSwitchSupportContent.PhaseAndInputDirection);
        Set(support,"positiveXPointsUp",true);
        tracker.CurrentMajorPhase=CraneStatusManager.WorkPhase.Move1;tracker.CurrentStepDefinition=Step("Move1.PickupCoarseMove");
        manager.TargetCondition.workPhase=CraneStatusManager.WorkPhase.Move1;manager.HasPreview=true;manager.GoalX=2;manager.GoalZ=-2;
        Call(support,"OnEnable");manager.Raise("TargetDisplaySwitched");
        Check(support.IsVisible&&phase.gameObject.activeSelf&&la.ChevronCount==3&&za.ChevronCount==1,
            "New pattern available at switch with phase and per-axis distances");
        crane.InformationTarget.position=new Vector3(.7f,0,0);Call(support,"LateUpdate");
        Check(la.ChevronCount==3,"Approaching preserves the display-switch count");
        crane.InformationTarget.position=new Vector3(1.2f,0,0);Call(support,"LateUpdate");
        Check(la.ChevronCount==3,"Near target does not reduce the captured count");
        crane.InformationTarget.position=new Vector3(1.5f,0,0);Call(support,"LateUpdate");
        Check(!left.gameObject.activeSelf,"Reached axis still hides all chevrons");
        crane.InformationTarget.position=Vector3.zero;manager.GoalX=1.2f;manager.GoalZ=6;
        manager.Raise("SourceDisplayRestored");
        Check(la.ChevronCount==2&&za.ChevronCount==3,"Return captures a fresh per-axis count");
        manager.GoalX=.6f;Call(support,"LateUpdate");
        Check(la.ChevronCount==2,"Goal changes during a window cannot replace its captured count");
        manager.Raise("SourceOperationResumed");
        Check(la.ChevronCount==2&&za.ChevronCount==3,"Confirmation uses live guidance without recapturing counts");
        manager.TargetCondition.workPhase=CraneStatusManager.WorkPhase.LiftUp;
        tracker.CurrentMajorPhase=CraneStatusManager.WorkPhase.LiftUp;tracker.CurrentStepDefinition=Step("LiftUp.PickupLowering");
        crane.CraneUnit.LocalY=-3;manager.Raise("TargetDisplaySwitched");
        Check(ry.gameObject.activeSelf&&ya.ChevronCount==3,"Lowering count captured at display switch");
        int loweringQueries=crane.CraneUnit.LoweringQueries;
        crane.CraneUnit.LoweringDistance=.7f;Call(support,"LateUpdate");Check(ya.ChevronCount==3,"Lowering keeps its initial count at middle distance");
        crane.CraneUnit.LoweringDistance=.2f;Call(support,"LateUpdate");Check(ya.ChevronCount==3,"Lowering keeps its initial count near surface");
        crane.CraneUnit.DistanceKnown=false;Call(support,"LateUpdate");Check(ya.ChevronCount==3,"Later sensor changes cannot replace the snapshot");
        Check(crane.CraneUnit.LoweringQueries==loweringQueries,"Distance probe runs only at switch/return");
        crane.CraneUnit.DistanceKnown=true;crane.CraneUnit.IsDownwardMovementBlockedByTouchdown=true;
        Call(support,"LateUpdate");Check(!ry.gameObject.activeSelf,"Touchdown still hides the new pattern");
        crane.CraneUnit.IsDownwardMovementBlockedByTouchdown=false;
        tracker.CurrentStepDefinition=Step("LiftUp.LoadedRaising");manager.Raise("TargetOperationStarted");
        Check(ry.gameObject.activeSelf&&ya.ChevronCount==3,"Changing to raising does not replace the initial count");
        crane.CraneUnit.LocalY=-2.4f;Call(support,"LateUpdate");Check(ya.ChevronCount==3,"Raising progress does not replace the snapshot");
        cycle.HasPendingNextPhase=false;manager.Raise("SourceDisplayRestored");
        Check(ya.ChevronCount==2,"Return captures the current raising distance");
        int raisingQueries=crane.CraneUnit.RaisingQueries;
        crane.CraneUnit.LocalY=-1.8f;Call(support,"LateUpdate");Check(ya.ChevronCount==2,"Raising keeps the return-time count near target");
        Check(crane.CraneUnit.RaisingQueries==raisingQueries,"Raising distance is not queried during the window");
        manager.Raise("SourceDisplayRestored");Check(ya.ChevronCount==1,"Next return can capture a new near-distance count");
        crane.CraneUnit.DistanceKnown=false;manager.Raise("TargetDisplaySwitched");
        Check(ya.ChevronCount==1,"Unknown distance at switch captures one direction glyph");
        crane.CraneUnit.DistanceKnown=true;crane.CraneUnit.LoweringDistance=2;Call(support,"LateUpdate");
        Check(ya.ChevronCount==1,"Unknown-at-switch fallback remains fixed after later detection");
        Set(support,"supportContent",TaskSwitchSupportContent.PhaseOnly);Call(support,"LateUpdate");
        Check(!ry.gameObject.activeSelf&&phase.gameObject.activeSelf,"Chevron style respects phase-only condition");
        Check(manager.Recorded.Exists(s=>s.Contains("DirectionPresentation=DistanceChevrons")&&s.Contains("ChevronsY=2")),
            "Style and captured count changes across windows logged");
        Check(manager.Recorded.Exists(s=>s.Contains("ChevronCountAnchor=DisplaySwitch;ChevronCountsFixed=True")),
            "Logs identify the fixed-count snapshot policy");
        la.SetPresentation(TaskSwitchDirectionPresentation.SingleArrow,1,3);
        typeof(TaskSwitchSupportArrow).GetMethod("OnPopulateMesh",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(la,new object[]{mesh});
        Check(mesh.Vertices==7&&mesh.Triangles==3,"Switching back preserves the original arrow");
        Call(support,"OnDisable");
        Console.WriteLine("Cognitive support checks passed: "+checks);
    }
}

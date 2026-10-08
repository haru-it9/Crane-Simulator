using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

static class LifMagTests
{
    static LifMagCurrentControlIndicator indicator;
    static void Require(bool condition,string message) {if(!condition)throw new Exception(message);}
    static void Set(object o,string field,object value) {o.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,value);}
    static void Tick(LifMagSystem m,float amps) {Input.axes[m.CurrentSliderAxis]=amps/75f;typeof(LifMagSystem).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(m,null);if(indicator!=null)indicator.RefreshDisplay();}
    static object Get(object o,string field) {return o.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);}
    static void Near(float a,float b,string reason) {Require(Math.Abs(a-b)<.0001f,reason+": "+a+" != "+b);}
    static GameObject Board(string name,float weight) {
        var b=new GameObject(name);b.components[typeof(BoardInfo)]=new BoardInfo {Weight=weight};
        b.transform.localScale=new Vector3(4,.1f,2);
        b.components[typeof(Collider)]=new Collider {gameObject=b,LocalCenter=new Vector3(0,.2f,0)};
        b.components[typeof(Rigidbody)]=new Rigidbody();b.components[typeof(HoldBoardSensor)]=new HoldBoardSensor();return b;
    }
    static void Attached(GameObject b,CraneUnit crane) {
        Require(b.transform.parent==crane.mainLifMag && b.GetComponent<Rigidbody>().isKinematic && !b.GetComponent<Rigidbody>().useGravity,"preloaded plate became dynamic or unparented");
    }
    public static void Main()
    {
        var m=new LifMagSystem();var crane=new CraneUnit {lifMagSystem=m};var op=new CraneOperationManager {CurrentCrane=crane};
        crane.mainLifMag=m.transform;m.transform.localScale=new Vector3(.2f,1.2f,.18f);m.transform.position=new Vector3(16,12,4);
        var sensor=new MagnetSensor();Set(m,"magnetSensors",new[]{sensor});Set(m,"craneOperationManager",op);
        sensor.gameObject.transform.SetParent(m.transform,false);sensor.gameObject.transform.localPosition=new Vector3(0,-1.5f,0);
        var magnetCollider=new Collider {gameObject=sensor.gameObject,isTrigger=true,LocalSize=new Vector3(2,.2f,1)};
        sensor.components[typeof(Collider)]=magnetCollider;
        Set(m,"liftJudgementMode",LifMagSystem.LiftJudgementMode.CurrentSliderInputByWeight);
        var left=new GameObject("left",typeof(RectTransform)).AddComponent<LifMagCurrentButton>();
        var right=new GameObject("right",typeof(RectTransform)).AddComponent<LifMagCurrentButton>();
        right.gameObject.layer=5;
        var ui=new CraneOperationManager.OperationUiSet {lifMagCurrentButtons=new[]{left,right}};
        Set(op,"taskSwitchDisplayUiSet",ui);Set(op,"singleDisplayUiSet",ui);op.InitializeIndicatorsForTest();
        var created=UnityEngine.Object.Scene.FindAll(o=>o is LifMagCurrentControlIndicator);
        Require(created.Count==1,"current indicator was missing or duplicated on shared UI");indicator=(LifMagCurrentControlIndicator)created[0];
        var markerRect=indicator.gameObject.GetComponent<RectTransform>();
        Require(markerRect.parent==right.transform && markerRect.anchorMin.x==1 && markerRect.pivot.x==0 && markerRect.anchoredPosition.x==40,"indicator is not to the right of LifMagButton");
        var statusImage=(Image)Get(indicator,"stateImage");var statusText=(Text)Get(indicator,"stateText");
        Require(!statusImage.raycastTarget && !statusText.raycastTarget,"indicator intercepts clicks");
        Require(statusImage.gameObject.layer==5 && statusText.gameObject.layer==5,"indicator did not inherit UI layer");
        Require(!indicator.IsControlAvailable && statusText.text.EndsWith("不可"),"indicator allowed current before magnets are enabled");
        var scenario=new CraneInterventionScenarioManager();
        scenario.SetupInterventionState(crane,CraneStatusManager.WorkPhase.Move2,CraneStatusManager.ErrorType.None,1,null,false);
        Require(scenario.SyntheticPlateCount==0,"CSV Move2 scenario spawned a spare falling plate");
        scenario.SetupInterventionState(crane,CraneStatusManager.WorkPhase.Move2,CraneStatusManager.ErrorType.None,2);
        Require(scenario.SyntheticPlateCount==1,"legacy synthetic plate setup was changed");
        int released=0,accepted=0;m.TaskSwitchSafeCurrentHoldReleased+=(mag,a)=>released++;m.ElectricCurrentInputAccepted+=(mag,a)=>accepted++;
        var attachmentEvents=new System.Collections.Generic.List<string>();
        m.BoardAttachmentChanged+=(mag,eventName,detail)=>attachmentEvents.Add(eventName+":"+detail);
        // Move2 preload: all real plates remain attached, including the first one in the batch.
        var first=Board("first",1000);var second=Board("second",2000);
        first.transform.position=new Vector3(-4,1,2);second.transform.position=new Vector3(-4,.5f,2);
        second.transform.localScale=new Vector3(3,.2f,5);
        Require(m.TryPreloadTaskSwitchBoards(new[]{first,second}),"physical preload failed");
        var top=first.GetComponent<Collider>().bounds;var bottom=second.GetComponent<Collider>().bounds;
        Near(top.max.y,magnetCollider.bounds.min.y,"top plate is not at magnet underside");
        Near(bottom.max.y,top.min.y,"stack has a gap or overlap");
        Near(top.center.x,magnetCollider.bounds.center.x,"plate X alignment");Near(top.center.z,magnetCollider.bounds.center.z,"plate Z alignment");
        Near(top.size.y,.1f,"top thickness changed under scaled magnet");Near(bottom.size.y,.2f,"bottom thickness changed under scaled magnet");
        Near(first.transform.lossyScale.x,4,"plate width shrank");Near(second.transform.lossyScale.z,5,"plate length shrank");
        Require(!m.TryPreloadTaskSwitchBoards(new[]{first,new GameObject("no-geometry")}) && m.AttachedBoards.Count==2,"invalid geometry changed existing attachment");
        Require(m.AttachedBoards.Count==2 && m.CurrentAttachedWeightKg==3000,"preload lost a plate or CSV weight");
        Require(attachmentEvents.Exists(e=>e.StartsWith("TaskSwitchBoardsPreloaded:Count=2;")),"preload CSV event missing");
        Require(m.BeginTaskSwitchSafeCurrentHold() && m.TaskSwitchCurrentRearmCondition=="AtLeast" && m.TaskSwitchCurrentRearmThresholdAmpere==70,"holding gate wrong");
        Tick(m,0);Tick(m,69.99f);Attached(first,crane);Attached(second,crane);
        Require(m.IsTaskSwitchSafeCurrentHoldActive && m.CurrentElectricCurrentA==70 && released==0 && accepted==0,"holding gate applied low current");
        Require(!indicator.IsControlAvailable && statusImage.color.r==.55f && statusText.text.EndsWith("不可"),"holding gate indicator not gray/unavailable");
        ExperimentPauseManager.IsPaused=true;Tick(m,75);Require(m.IsTaskSwitchSafeCurrentHoldActive,"Pause released gate");ExperimentPauseManager.IsPaused=false;
        op.IsOperationInputLocked=true;Tick(m,75);Require(m.IsTaskSwitchSafeCurrentHoldActive,"confirmation lock released gate");op.IsOperationInputLocked=false;
        Tick(m,70);Require(!m.IsTaskSwitchSafeCurrentHoldActive && released==1 && accepted==1,"70A inclusive threshold did not rearm");
        Require(indicator.IsControlAvailable && statusImage.color.g==.85f && statusText.text.EndsWith("\n可"),"unlocked current indicator not green/available");
        ExperimentPauseManager.IsPaused=true;indicator.RefreshDisplay();Require(!indicator.IsControlAvailable && statusText.text.EndsWith("不可"),"Pause left indicator available");ExperimentPauseManager.IsPaused=false;
        op.IsOperationInputLocked=true;indicator.RefreshDisplay();Require(!indicator.IsControlAvailable,"confirmation lock indicator available");op.IsOperationInputLocked=false;
        op.CurrentCrane=new CraneUnit();indicator.RefreshDisplay();Require(!indicator.IsControlAvailable,"indicator retained old crane availability");op.CurrentCrane=crane;
        Attached(first,crane);Attached(second,crane);
        m.BeginTaskSwitchSafeCurrentHold();Tick(m,10);Require(m.IsTaskSwitchSafeCurrentHoldActive && m.CurrentAttachedWeightKg==3000,"holding return did not rearm or lost weight");Tick(m,75);
        var tracker=new CraneWorkPhaseTracker {IsMonitoring=true,CurrentMajorPhase=CraneStatusManager.WorkPhase.Move2};
        Set(m,"workPhaseTracker",tracker);Set(m,"craneUnit",crane);
        // Raised Move2 and Place before touchdown must allow current release for this batch.
        ExperimentPauseManager.IsPaused=true;Tick(m,15);Require(m.AttachedBoards.Count==2,"Pause detached preloaded plate");ExperimentPauseManager.IsPaused=false;
        Tick(m,15);Require(m.AttachedBoards.Count==1 && m.AttachedBoards[0]==first && m.CurrentAttachedWeightKg==1000,"current reduction did not release bottom plate during raised Move2");
        Require(second.transform.parent==null && !second.GetComponent<Rigidbody>().isKinematic && second.GetComponent<Rigidbody>().useGravity,"released plate stayed physically attached");
        Attached(first,crane);tracker.CurrentMajorPhase=CraneStatusManager.WorkPhase.Place;Tick(m,0);
        Require(!m.HasAttachedBoard && m.CurrentAttachedWeightKg==0 && first.transform.parent==null && first.GetComponent<Rigidbody>().useGravity,"zero current did not release remaining plate before touchdown");
        var drops=attachmentEvents.FindAll(e=>e.StartsWith("BoardDetachedInsufficientCurrent:"));
        Require(drops.Count==2 && drops[0].EndsWith(":second") && drops[1].EndsWith(":first"),"current release events or order missing");
        // Ordinary intervention plates keep the original release interlock.
        crane.SetInterventionBoardAttached(first,new Vector3(0,-1.565f,0),Vector3.zero);
        tracker.CurrentMajorPhase=CraneStatusManager.WorkPhase.Move2;Tick(m,75);Tick(m,0);
        Require(m.HasAttachedBoard,"current-release exception leaked into ordinary intervention plate");
        Set(m,"workPhaseTracker",null);
        // Empty return: even with a touching candidate, the old high slider cannot pick it up.
        crane.ClearInterventionBoardAttachment();m.SetLifMagCurrent(0,true);first.GetComponent<BoardInfo>().Weight=500;
        sensor.TouchingBoards.Add(first);Time.timeAsDouble=1;
        Require(m.BeginTaskSwitchSafeCurrentHold() && m.TaskSwitchCurrentRearmCondition=="AtMost" && m.TaskSwitchCurrentRearmThresholdAmpere==10,"empty gate wrong");
        Require(m.CurrentElectricCurrentA==10,"empty rearm did not immediately display 10A");
        int before=accepted;
        Tick(m,75);Tick(m,10.01f);Tick(m,float.NaN);
        Require(m.IsTaskSwitchSafeCurrentHoldActive && !m.HasAttachedBoard && m.CurrentElectricCurrentA==10 && accepted==before,"empty gate lost 10A display, accepted high/invalid current or picked up plate");
        Require(!indicator.IsControlAvailable && statusText.text.EndsWith("不可"),"10A wait indicator available");
        Tick(m,10);Require(!m.IsTaskSwitchSafeCurrentHoldActive && m.HasAttachedBoard && accepted==before+1,"10A inclusive threshold did not enable ordinary pickup");
        Require(indicator.IsControlAvailable,"10A release did not enable indicator");
        crane.ClearInterventionBoardAttachment();m.SetLifMagCurrent(0,true);sensor.TouchingBoards.Clear();m.BeginTaskSwitchSafeCurrentHold();Tick(m,0);
        Require(!m.IsTaskSwitchSafeCurrentHoldActive && m.CurrentElectricCurrentA==0,"0A empty input did not rearm");
        Set(m,"liftJudgementMode",LifMagSystem.LiftJudgementMode.CumulativeSliderInput);
        Require(!m.BeginTaskSwitchSafeCurrentHold(),"legacy accumulation mode was changed");
        Console.WriteLine("PASS: production scenario setup/LifMagSystem, underside alignment with offset pivots/nonuniform parent scale, multi-plate stack dimensions, staged current release without touchdown, ordinary plate interlock, 70A/10A rearm, Pause and confirmation lock");
    }
}

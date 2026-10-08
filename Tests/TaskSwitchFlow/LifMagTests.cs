using System;
using System.Reflection;
using UnityEngine;

static class LifMagTests
{
    static void Require(bool condition,string message) {if(!condition)throw new Exception(message);}
    static void Set(object o,string field,object value) {o.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,value);}
    static void Tick(LifMagSystem m,float amps) {Input.axes[m.CurrentSliderAxis]=amps/75f;typeof(LifMagSystem).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(m,null);}
    static GameObject Board(string name,float weight) {
        var b=new GameObject(name);b.components[typeof(BoardInfo)]=new BoardInfo {Weight=weight};
        b.components[typeof(Rigidbody)]=new Rigidbody();b.components[typeof(HoldBoardSensor)]=new HoldBoardSensor();return b;
    }
    static void Attached(GameObject b,CraneUnit crane) {
        Require(b.transform.parent==crane.mainLifMag && b.GetComponent<Rigidbody>().isKinematic && !b.GetComponent<Rigidbody>().useGravity,"preloaded plate became dynamic or unparented");
    }
    public static void Main()
    {
        var m=new LifMagSystem();var crane=new CraneUnit {lifMagSystem=m};var op=new CraneOperationManager {CurrentCrane=crane};
        var sensor=new MagnetSensor();Set(m,"magnetSensors",new[]{sensor});Set(m,"craneOperationManager",op);
        Set(m,"liftJudgementMode",LifMagSystem.LiftJudgementMode.CurrentSliderInputByWeight);
        var scenario=new CraneInterventionScenarioManager();
        scenario.SetupInterventionState(crane,CraneStatusManager.WorkPhase.Move2,CraneStatusManager.ErrorType.None,1,null,false);
        Require(scenario.SyntheticPlateCount==0,"CSV Move2 scenario spawned a spare falling plate");
        scenario.SetupInterventionState(crane,CraneStatusManager.WorkPhase.Move2,CraneStatusManager.ErrorType.None,2);
        Require(scenario.SyntheticPlateCount==1,"legacy synthetic plate setup was changed");
        int released=0,accepted=0;m.TaskSwitchSafeCurrentHoldReleased+=(mag,a)=>released++;m.ElectricCurrentInputAccepted+=(mag,a)=>accepted++;
        // Move2 preload: all real plates remain attached, including the first one in the batch.
        var first=Board("first",1000);var second=Board("second",2000);
        crane.ClearInterventionBoardAttachment();crane.SetInterventionBoardAttached(first,new Vector3(0,-.05f,0),Vector3.zero,true);
        crane.SetInterventionBoardAttached(second,new Vector3(0,-.15f,0),Vector3.zero,true);
        Require(m.AttachedBoards.Count==2 && m.CurrentAttachedWeightKg==3000,"preload lost a plate or CSV weight");
        Require(m.BeginTaskSwitchSafeCurrentHold() && m.TaskSwitchCurrentRearmCondition=="AtLeast" && m.TaskSwitchCurrentRearmThresholdAmpere==70,"holding gate wrong");
        Tick(m,0);Tick(m,69.99f);Attached(first,crane);Attached(second,crane);
        Require(m.IsTaskSwitchSafeCurrentHoldActive && m.CurrentElectricCurrentA==70 && released==0 && accepted==0,"holding gate applied low current");
        ExperimentPauseManager.IsPaused=true;Tick(m,75);Require(m.IsTaskSwitchSafeCurrentHoldActive,"Pause released gate");ExperimentPauseManager.IsPaused=false;
        op.IsOperationInputLocked=true;Tick(m,75);Require(m.IsTaskSwitchSafeCurrentHoldActive,"confirmation lock released gate");op.IsOperationInputLocked=false;
        Tick(m,70);Require(!m.IsTaskSwitchSafeCurrentHoldActive && released==1 && accepted==1,"70A inclusive threshold did not rearm");
        Attached(first,crane);Attached(second,crane);
        m.BeginTaskSwitchSafeCurrentHold();Tick(m,10);Require(m.IsTaskSwitchSafeCurrentHoldActive && m.CurrentAttachedWeightKg==3000,"holding return did not rearm or lost weight");Tick(m,75);
        // Empty return: even with a touching candidate, the old high slider cannot pick it up.
        crane.ClearInterventionBoardAttachment();m.SetLifMagCurrent(0,true);first.GetComponent<BoardInfo>().Weight=500;
        sensor.TouchingBoards.Add(first);Time.timeAsDouble=1;
        Require(m.BeginTaskSwitchSafeCurrentHold() && m.TaskSwitchCurrentRearmCondition=="AtMost" && m.TaskSwitchCurrentRearmThresholdAmpere==10,"empty gate wrong");
        int before=accepted;
        Tick(m,75);Tick(m,10.01f);Tick(m,float.NaN);
        Require(m.IsTaskSwitchSafeCurrentHoldActive && !m.HasAttachedBoard && m.CurrentElectricCurrentA==0 && accepted==before,"empty gate accepted high/invalid current or picked up plate");
        Tick(m,10);Require(!m.IsTaskSwitchSafeCurrentHoldActive && m.HasAttachedBoard && accepted==before+1,"10A inclusive threshold did not enable ordinary pickup");
        crane.ClearInterventionBoardAttachment();m.SetLifMagCurrent(0,true);sensor.TouchingBoards.Clear();m.BeginTaskSwitchSafeCurrentHold();Tick(m,0);
        Require(!m.IsTaskSwitchSafeCurrentHoldActive && m.CurrentElectricCurrentA==0,"0A empty input did not rearm");
        Set(m,"liftJudgementMode",LifMagSystem.LiftJudgementMode.CumulativeSliderInput);
        Require(!m.BeginTaskSwitchSafeCurrentHold(),"legacy accumulation mode was changed");
        Console.WriteLine("PASS: production scenario setup without spare plate, full LifMagSystem, real attachment Rigidbody/parenting, multi-plate preload, 70A/10A inclusive rearm, high-input pickup suppression, NaN, Pause and confirmation lock");
    }
}

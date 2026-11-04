using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

static class WeightTargetTests
{
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    static void Set(object o,string name,object value) { o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).SetValue(o,value); }
    static void Call(object o,string name) { o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null); }
    static void Main(string[] args)
    {
        for(int pattern=1;pattern<=6;pattern++)
        {
            var rows=TaskSwitchConditionCsv.ParseWork(File.ReadAllText(Path.Combine(args[0],"Assets/Resources/PilotWorkConditionsPattern"+pattern+".csv")),3);
            var generator=new BoardGenerator();
            for(int i=1;i<=3;i++) {var b=new GameObject();b.components[typeof(BoardInfo)]=new BoardInfo {Weight=i*1000};generator.Boards.Add(b);}
            var magnet=new LifMagSystem();
            var crane=new CraneInstance {CraneId=1,BoardGenerator=generator,LifMagSystem=magnet};
            var target=new CraneWorkTargetManager();var tracker=new CraneWorkPhaseTracker();var plan=new CraneWorkLoadPlanManager();
            Set(plan,"craneInstance",crane);Set(plan,"boardGenerator",generator);Set(plan,"workTargetManager",target);Call(plan,"OnEnable");
            var cycle=new CraneWorkCycleController();
            Set(cycle,"phaseTracker",tracker);Set(cycle,"targetManager",target);Set(cycle,"loadPlanManager",plan);Set(cycle,"craneInstance",crane);
            cycle.SetExperimentConditions(rows.FindAll(r=>r.role=="Source").ToArray());
            var text=new Text();var display=new TargetInformationDisplay();
            Set(display,"multiDisplayTexts",new TargetInformationDisplay.TargetTextSet {targetWeightText=text});
            var op=new CraneOperationManager {CurrentCrane=new CraneUnit {magnet=magnet}};
            Set(op,"targetInformationDisplay",display);Set(op,"selectedLoadPlanManager",plan);Set(op,"selectedWorkPhaseTracker",tracker);
            var current=new CraneInformationDisplay();
            Set(current,"workPhaseTracker",tracker);Set(current,"loadPlanManager",plan);Set(current,"lifMagSystem",magnet);
            Check(cycle.StartCycle(3,CraneStatusManager.WorkPhase.Move1),"cycle start");
            for(int index=0;index<3;index++)
            {
                var row=rows.Find(r=>r.role=="Source"&&r.index==index+1);
                float expected=row.pickupCount==1?1000:3000;
                Call(op,"LateUpdate");
                Check(cycle.CurrentPhase==CraneStatusManager.WorkPhase.Move1 && plan.PickupTargetWeightKg==expected,"next pickup used old counts/weight: "+pattern+"/"+index);
                Check(!plan.HasPlacementPlan && display.HasCurrentWeightTarget && display.CurrentTargetWeightTon==expected/1000,"placement zero leaked into next cycle");
                current.CurrentDisplayWeightTon=0;Call(current,"UpdateAchievementColors");Check(!current.CurrentWeightHighlighted,"empty pickup falsely green");
                for(int i=0;i<row.pickupCount;i++) magnet.AttachedBoards.Add(generator.Boards[i]);
                tracker.Complete();Check(cycle.ContinueAfterBoundary(),"start LiftUp");
                float saved=generator.Boards[0].GetComponent<BoardInfo>().Weight;
                generator.Boards[0].GetComponent<BoardInfo>().Weight=9000;
                Check(plan.RefreshPickupTargetFromCurrentTarget() && plan.PickupTargetWeightKg==expected,"pickup target drifted while lifting");
                generator.Boards[0].GetComponent<BoardInfo>().Weight=saved;
                tracker.Complete();Check(cycle.ContinueAfterBoundary(),"start Move2");
                tracker.Complete();Check(cycle.ContinueAfterBoundary(),"start Place");
                magnet.AttachedBoards.Clear();Call(op,"LateUpdate");
                current.CurrentDisplayWeightTon=0;Call(current,"UpdateAchievementColors");
                Check(display.HasCurrentWeightTarget && display.CurrentTargetWeightTon==0 && current.CurrentWeightHighlighted,"valid full-placement zero must be green");
                plan.ClearPlacementRuntimeState();Call(op,"LateUpdate");Call(current,"UpdateAchievementColors");
                Check(!display.HasCurrentWeightTarget && text.text=="-- t" && !current.CurrentWeightHighlighted,"unavailable target was displayed as valid zero");
                tracker.Complete();if(index<2) Check(cycle.ContinueAfterBoundary(),"next cycle");
            }
            Check(cycle.CompletedCycleCount==3 && !cycle.IsRunning,"three-cycle completion");
            // A failed explicit refresh must not retain a previous nonzero goal.
            tracker.CurrentMajorPhase=CraneStatusManager.WorkPhase.Move1;
            target.SetFixedTarget(-4,2,-1,CraneWorkTargetKind.Pickup);
            generator.Boards.Clear();Check(!plan.RefreshPickupTargetFromCurrentTarget(true) && !plan.HasPickupTarget,"missing stock retained old goal");
            Call(op,"LateUpdate");Check(!display.HasCurrentWeightTarget,"missing pickup showed a stale placement/pickup target");
        }
        Console.WriteLine("PASS: all 6 pilot CSVs across 3 cycles; 1/2/1 pickup counts, placement-zero cleanup, frozen pickup targets, production weight UI reconciliation, valid-zero green and invalid-target handling");
    }
}

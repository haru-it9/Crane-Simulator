using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
class LoggingTests
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Set(object o,string field,object v) { o.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v); }
    static void Tick(TaskSwitchExperimentCsvLogger l,double t) { Time.realtimeSinceStartupAsDouble=100000+t;Time.timeAsDouble=10000+t;Time.frameCount++; typeof(TaskSwitchExperimentCsvLogger).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(l,null); }
    public static int Main(string[] args)
    {
        string output=args[0];Directory.CreateDirectory(output);
        CultureInfo.CurrentCulture=new CultureInfo("fr-FR");
        foreach(string completion in new[]{"TargetMajorPhaseCompleted","TargetWorkCycleCompleted","TargetWorkCycleCompletedByPhase","TargetWorkCycleCompletionFallback"})
        {
            var timing=new TaskSwitchTimingSummary();
            Require(double.IsNaN(timing.Between("SwitchRequested","FirstEffectiveSourceInput")),"missing endpoints must be blank");
            timing.Mark("SwitchRequested",1);timing.Mark("SwitchRequested",99);
            timing.Mark(completion,3);timing.Mark("TargetWorkCycleCompleted",100);
            timing.Mark("SourceInputUnlocked",6);timing.Mark("FirstEffectiveSourceInput",7);
            Require(timing.At("SwitchRequested")==1 && timing.At("TargetCompleted")==3,"duplicate markers changed timing");
            Require(timing.Between("SourceInputUnlocked","FirstEffectiveSourceInput")==1,"latency wrong");
            Require(timing.Cells().Length==TaskSwitchTimingSummary.Header.Split(',').Length,"summary schema mismatch");
        }
        using(var f=new ExperimentCsvFile(Path.Combine(output,"escaping.csv"),"text,value,missing,flag"))
        { f.Write("参加者,\"名前\"\n改行",1.25,double.NaN,true); }
        Require(ExperimentCsvFile.Encode(double.PositiveInfinity)=="","invalid numeric value");
        bool rejected=false;try { using(var f=new ExperimentCsvFile(Path.Combine(output,"bad.csv"),"a,b")) f.Write(1); }catch(ArgumentException){rejected=true;}
        Require(rejected,"invalid row width accepted");
        var manager=new TaskSwitchExperimentManager();
        var registry=new CraneRegistry { cranes=new CraneInstance[2] };
        var op=new CraneOperationManager();
        for(int i=0;i<2;i++)
        {
            var c=new CraneInstance { LifMagSystem=new LifMagSystem() };
            c.LifMagSystem.AttachedBoards.Add(new GameObject { name="板,"+i });
            c.InformationTarget.position=new Vector3(4+i,-1,20+i);
            c.components[typeof(CraneWorkPhaseTracker)]=new CraneWorkPhaseTracker { targetX=4,targetZ=20 };
            c.components[typeof(CraneWorkCycleController)]=new CraneWorkCycleController();
            c.components[typeof(CraneWorkTargetManager)]=new CraneWorkTargetManager { targetX=4,targetZ=20 };
            c.components[typeof(CraneWorkLoadPlanManager)]=new CraneWorkLoadPlanManager();registry.cranes[i]=c;
            UnityEngine.Object.Scene.Add(c.GetComponent<CraneWorkPhaseTracker>());UnityEngine.Object.Scene.Add(c.LifMagSystem);
        }
        op.CurrentCraneInstance=registry.cranes[0];
        var logger=new TaskSwitchExperimentCsvLogger();Set(logger,"saveFolderPath",output);Set(logger,"taskSwitchExperimentManager",manager);
        Set(logger,"craneRegistry",registry);Set(logger,"craneOperationManager",op);Set(logger,"participantId","P01");Set(logger,"blockId","B01");
        Time.realtimeSinceStartupAsDouble=100000;Time.timeAsDouble=10000;
        logger.StartLogging("試行,1");Require(logger.IsLogging,"logger not running");
        manager.Emit("ExperimentPreparing");manager.Emit("ExperimentStarted");
        Tick(logger,1);manager.CurrentSwitchIndex=1;manager.Emit("SwitchRequested");
        Tick(logger,2);manager.Emit("SourceWorkSuspended");op.IsOperationInputLocked=true;
        registry.cranes[0].GetComponent<CraneWorkCycleController>().IsPaused=true;
        op.CurrentCraneIndex=1;op.CurrentCraneInstance=registry.cranes[1];manager.Emit("TargetDisplaySwitched");
        Tick(logger,3);manager.Emit("ConfirmationPressed");
        op.Accept(new Vector3(1,0,0)); // gated input must not count as first effective input
        Tick(logger,4);op.IsOperationInputLocked=false;op.requested=new Vector3(1,0,0);manager.Emit("TargetInputUnlocked");
        Tobii.Gaming.TobiiAPI.IsConnected=true;Tobii.Gaming.TobiiAPI.gaze.IsValid=true;
        Tobii.Gaming.TobiiAPI.gaze.Viewport=new Vector2(1.2f,-.1f);Tobii.Gaming.TobiiAPI.gaze.Screen=new Vector2(2400,-108);
        Tick(logger,5);op.Accept(new Vector3(1,0,0));
        registry.cranes[1].LifMagSystem.Current(70);registry.cranes[1].LifMagSystem.Drop();
        registry.cranes[1].GetComponent<CraneWorkPhaseTracker>().Invalidate();registry.cranes[1].GetComponent<CraneWorkCycleController>().Rollback();
        Tick(logger,6);manager.Emit("TargetWorkCycleCompletedByPhase");manager.Emit("TargetWorkCycleCompleted");
        registry.cranes[1].GetComponent<CraneWorkCycleController>().Complete();
        op.CurrentCraneIndex=0;op.CurrentCraneInstance=registry.cranes[0];op.IsOperationInputLocked=true;manager.Emit("SourceDisplayRestored");
        Tick(logger,7);manager.Emit("SourceReturnConfirmationPressed");manager.Emit("SourceOperationResumed");manager.Emit("SwitchCompleted");
        Tick(logger,8);op.IsOperationInputLocked=false;manager.Emit("SourceInputUnlocked");registry.cranes[0].GetComponent<CraneWorkCycleController>().IsPaused=false;
        Tick(logger,9);op.Accept(new Vector3(1,0,0));registry.cranes[0].GetComponent<CraneWorkPhaseTracker>().CompleteStep();
        PauseTestHarness.Set(true);Tick(logger,10);PauseTestHarness.Set(false);
        Tick(logger,11);manager.CurrentSwitchIndex=2;manager.Emit("SwitchRequested");
        Tick(logger,12);manager.Emit("SourceWorkSuspended");
        // Restart must close incomplete switch/cycle and isolate new run identity.
        manager.Emit("ExperimentPreparing");manager.Emit("ExperimentStarted");manager.CurrentSwitchIndex=1;manager.Emit("SwitchRequested");
        Tick(logger,13);manager.Emit("SwitchCompleted");
        string first=logger.SessionDirectory;logger.StopLogging();logger.StopLogging();
        logger.StartLogging("試行,1");string second=logger.SessionDirectory;logger.StopLogging();
        Require(first!=second,"repeated label overwrote session");
        string unavailable=Path.Combine(output,"not-a-directory");File.WriteAllText(unavailable,"test");
        Set(logger,"saveFolderPath",unavailable);logger.StartLogging("failed");
        Require(!logger.IsLogging && Debug.Errors==1,"failed startup must report failure and remain stopped");
        Set(logger,"saveFolderPath",output);Set(logger,"participantId","P01");
        Console.WriteLine("PASS: timing markers, missing intervals, culture/escaping, row widths, both-crane sampling, gating, drops, rollback, full-cycle completion, restart, incomplete rows, repeated labels");
        AuditorySubtaskTests.Run(output);
        PauseTests.Run(output);
        Console.WriteLine(first);return 0;
    }
}

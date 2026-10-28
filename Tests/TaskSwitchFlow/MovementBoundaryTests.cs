using System;
using System.Reflection;
using UnityEngine;

static class MovementBoundaryTests
{
    static int checks;
    static void Check(bool ok,string message) { checks++;if(!ok)throw new Exception(message); }
    static void Set(object o,string field,object value) { o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value); }
    static void Call(object o,string method) { o.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null); }
    sealed class Rig
    {
        public readonly CraneInstance Crane=new CraneInstance();
        public readonly CraneWorkPhaseTracker Tracker=new CraneWorkPhaseTracker();
        public readonly CraneOperationManager Op=new CraneOperationManager();
        public int Completions;
        public Rig(CraneStatusManager.WorkPhase phase)
        {
            Input.axes.Clear();Input.keys.Clear();UnityEngine.Object.Scene.Clear();ExperimentPauseManager.IsPaused=false;
            Crane.CraneUnit=new CraneUnit {Target=Crane.InformationTarget};
            Crane.InformationTarget.position=new Vector3(.02f,0,.02f);
            if(phase==CraneStatusManager.WorkPhase.Move2)Crane.LifMagSystem.AttachedBoards.Add(new GameObject());
            Set(Tracker,"craneInstance",Crane);Set(Tracker,"loadPlanManager",new CraneWorkLoadPlanManager());
            Set(Tracker,"useManualTargetWhenUnavailable",true);Set(Tracker,"manualTargetXZ",Vector2.zero);
            Op.CurrentCraneInstance=Crane;Set(Op,"selectedWorkPhaseTracker",Tracker);UnityEngine.Object.Scene.Add(Op);
            Tracker.MajorPhaseCompleted+=(t,p)=>Completions++;
            Check(Tracker.ConfigurePhase(phase,true),"configure movement phase");
        }
        public void Tick(float dt=.05f) { Time.deltaTime=Time.unscaledDeltaTime=dt;Time.frameCount++;Call(Tracker,"Update"); }
        public void Fine()
        {
            for(int i=0;i<8 && Tracker.CurrentStepId.Contains("CoarseMove");i++)Tick(.1f);
            Check(Tracker.CurrentStepId.Contains("FineAlign"),"coarse did not reach fine alignment");
        }
        public void Dispatch() { Call(Op,"HandleMovement"); }
    }
    static void BothMovementPhases()
    {
        foreach(var phase in new[]{CraneStatusManager.WorkPhase.Move1,CraneStatusManager.WorkPhase.Move2})
        {
            var r=new Rig(phase);r.Fine();
            r.Crane.InformationTarget.position=new Vector3(.049f,0,.049f);
            r.Tick();r.Tick(); // enter the 5cm range and latch position
            r.Crane.InformationTarget.position=new Vector3(.08f,0,.049f);
            for(int i=0;i<20;i++)r.Tick();
            Check(r.Completions==0 && r.Tracker.ConditionStableSeconds==0,"hysteresis enlarged final X tolerance");
            r.Crane.InformationTarget.position=new Vector3(.049f,0,.08f);
            for(int i=0;i<20;i++)r.Tick();
            Check(r.Completions==0,"hysteresis enlarged final Z tolerance");
            r.Crane.InformationTarget.position=new Vector3(.02f,0,.02f);
            Input.axes["x"]=.1f;
            for(int i=0;i<25;i++) {r.Dispatch();r.Tick();}
            Check(r.Completions==0 && r.Tracker.CurrentHorizontalSpeed<.05f,"slow held movement completed final alignment");
            Check(r.Op.LastAcceptedMovement.x!=0 && r.Crane.InformationTarget.position.x<.02f,"fine movement was blocked before completion");
            Input.axes["x"]=0;r.Tick();r.Tick();
            Check(r.Tracker.ConditionStableSeconds>0 && r.Completions==0,"neutral stability window missing");
            // A new input between physics dispatches must reset stability immediately.
            Input.axes["z"]=.1f;r.Tick();
            Check(r.Tracker.ConditionStableSeconds==0 && r.Completions==0,"fresh Z input did not reset stability");
            Input.axes["z"]=0;ExperimentPauseManager.IsPaused=true;r.Tick(20);
            Check(r.Completions==0 && r.Tracker.ConditionStableSeconds==0,"Pause advanced final alignment");
            ExperimentPauseManager.IsPaused=false;
            for(int i=0;i<5;i++)r.Tick();
            Check(r.Completions==1 && r.Tracker.IsMajorPhaseCompleted,"neutral in-range alignment did not complete once");
            var stopped=r.Crane.InformationTarget.position;
            Input.axes["x"]=Input.axes["z"]=.7f;
            for(int i=0;i<10;i++)r.Dispatch();
            Check(r.Crane.InformationTarget.position.x==stopped.x && r.Crane.InformationTarget.position.z==stopped.z,"late input moved completed source before switch/next phase");
            Check(r.Op.LastAcceptedMovement.x==0 && r.Op.LastAcceptedMovement.z==0,"logged accepted input did not reflect boundary hold");
            Input.axes.Clear();r.Tick();Check(r.Completions==1,"completion emitted twice");
            r.Tracker.ConfigurePhase(phase==CraneStatusManager.WorkPhase.Move1?CraneStatusManager.WorkPhase.LiftUp:CraneStatusManager.WorkPhase.Place,true);
            Input.axes["x"]=.7f;r.Dispatch();
            Check(r.Op.LastAcceptedMovement.x!=0,"horizontal hold persisted into next phase");
        }
    }
    static void InputAndProfileCompatibility()
    {
        var r=new Rig(CraneStatusManager.WorkPhase.Move1);r.Fine();
        Set(r.Op,"inputMode",CraneOperationManager.InputMode.Keyboard);Input.keys.Add(KeyCode.D);
        for(int i=0;i<8;i++)r.Tick();Check(r.Completions==0,"held keyboard input completed alignment");
        Input.keys.Clear();for(int i=0;i<5;i++)r.Tick();Check(r.Completions==1,"keyboard release did not complete alignment");

        r=new Rig(CraneStatusManager.WorkPhase.Move1);r.Fine();Input.axes["x"]=.01f;Input.axes["z"]=-.01f;
        for(int i=0;i<5;i++)r.Tick();Check(r.Completions==1,"joystick dead-zone jitter blocked completion");

        r=new Rig(CraneStatusManager.WorkPhase.Move1);r.Fine();OpToOtherCrane(r);Input.axes["x"]=.9f;
        for(int i=0;i<5;i++)r.Tick();Check(r.Completions==1,"other crane's controls blocked automatic completion");

        r=new Rig(CraneStatusManager.WorkPhase.Move1);r.Fine();
        var activeSteps=(System.Collections.Generic.List<CraneWorkStepDefinition>)typeof(CraneWorkPhaseTracker)
            .GetField("activeSteps",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(r.Tracker);
        activeSteps[activeSteps.Count-1].requiredStableSeconds=0;r.Crane.InformationTarget.position=new Vector3(1,0,1);
        r.Tick();Check(r.Completions==0,"zero stability accepted false position conditions");

        r=new Rig(CraneStatusManager.WorkPhase.Move1);r.Crane.InformationTarget.position=new Vector3(.49f,0,0);r.Tick(.1f);
        r.Crane.InformationTarget.position=new Vector3(.53f,0,0);for(int i=0;i<4;i++)r.Tick(.1f);
        Check(r.Tracker.CurrentStepId.Contains("FineAlign") && r.Completions==0,"coarse hysteresis behavior changed");

        r=new Rig(CraneStatusManager.WorkPhase.Move1);r.Fine();r.Crane.InformationTarget.position=new Vector3(.05f,0,.05f);
        for(int i=0;i<6;i++)r.Tick();Check(r.Completions==1,"exact profile tolerance rejected");
        Set(r.Op,"taskSwitchExperimentMode",false);Input.axes["x"]=.5f;r.Dispatch();
        Check(r.Op.LastAcceptedMovement.x!=0,"boundary hold leaked into ordinary operation mode");

        r=new Rig(CraneStatusManager.WorkPhase.Move1);r.Fine();
        var customSteps=(System.Collections.Generic.List<CraneWorkStepDefinition>)typeof(CraneWorkPhaseTracker)
            .GetField("activeSteps",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(r.Tracker);
        var position=customSteps[customSteps.Count-1].completionConditions.Find(c=>c.conditionType==CraneWorkConditionType.PositionWithinTarget);
        position.threshold=.025f;position.secondaryThreshold=.075f;
        r.Crane.InformationTarget.position=new Vector3(.03f,0,.07f);
        for(int i=0;i<8;i++)r.Tick();Check(r.Completions==0,"custom X tolerance ignored");
        r.Crane.InformationTarget.position=new Vector3(.024f,0,.07f);
        for(int i=0;i<6;i++)r.Tick();Check(r.Completions==1,"custom asymmetric X/Z tolerances ignored");

        r=new Rig(CraneStatusManager.WorkPhase.Move1);r.Fine();
        bool publishedWhileMonitoring=false;
        r.Tracker.MajorPhaseCompleted+=(t,p)=>{publishedWhileMonitoring=t.IsMonitoring;t.ConfigurePhase(CraneStatusManager.WorkPhase.LiftUp,true);};
        for(int i=0;i<5;i++)r.Tick();
        Check(!publishedWhileMonitoring,"completion published before tracker stopped");
        Check(r.Completions==1 && r.Tracker.IsMonitoring && r.Tracker.CurrentMajorPhase==CraneStatusManager.WorkPhase.LiftUp,"completion callback's new monitoring was overwritten");
    }
    static void OpToOtherCrane(Rig r) {r.Op.CurrentCraneInstance=new CraneInstance();}
    public static void Main()
    {
        BothMovementPhases();InputAndProfileCompatibility();
        Console.WriteLine("PASS: movement boundary checks="+checks+"; full production tracker/profile, real input mapping/dispatch, strict X/Z tolerances, slow held input, continuous neutral stability, late input hold, pause, keyboard/dead-zone compatibility and callback ordering");
    }
}

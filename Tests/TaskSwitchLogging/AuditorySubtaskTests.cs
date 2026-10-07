using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

static class AuditorySubtaskTests
{
    static double pausedWallOffset;
    static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
    static void Set(object o,string field,object value) { o.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,value); }
    static object Get(object o,string field) { return o.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o); }
    static void Call(object o,string method) { o.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null); }
    static void Advance(TaskSwitchExperimentManager manager,TaskSwitchExperimentCsvLogger logger,double dsp,float pedal)
    {
        AudioSettings.dspTime=dsp;Time.realtimeSinceStartupAsDouble=200000+dsp-1000+pausedWallOffset;Time.timeAsDouble=20000+dsp-1000;Time.frameCount++;
        Input.axes["TaskSwitchPedal"]=pedal;Call(manager,"UpdateAuditorySubtask");Call(logger,"LateUpdate");
    }
    public static void Run(string output)
    {
        var settings=new TaskSwitchAuditorySettings();
        Require(settings.Validate(48000)==null,"default settings invalid");
        settings.releaseThreshold=.5f;Require(settings.Validate(48000)!=null,"invalid hysteresis accepted");settings.releaseThreshold=.2f;
        settings.minimumIntervalSeconds=1;Require(settings.Validate(48000)!=null,"overlapping trials accepted");settings.minimumIntervalSeconds=3;
        var engine=new TaskSwitchAuditoryTrial(settings);
        var events=new List<TaskSwitchAuditoryEvent>();engine.EventOccurred+=events.Add;
        engine.Schedule(true,10,10010,0,10000,0);
        engine.Tick(1,10001,0);engine.Tick(2,10002,1);
        Require(events[events.Count-1].Outcome=="FalseAlarm" && events[events.Count-1].TrialIndex==0,"pre-onset input assigned to trial");
        engine.Tick(10,10010,1);engine.Tick(10.3,10010.3,-1);
        Require(engine.HasPendingTrial && events.Find(e=>e.EventType=="StimulusOnsetObserved").HeldAtOnset,"held/direct sign change produced response");
        engine.Tick(10.4,10010.4,0);engine.Tick(10.5,10010.5,1);
        Require(events[events.Count-1].Outcome=="Correct" && Math.Abs(events[events.Count-1].ReactionSeconds-.5)<.00001,"high positive RT incorrect");
        engine.Schedule(false,20,10020,11,10011,0);engine.Tick(19,10019,0);engine.Tick(20,10020,0);engine.Tick(20.5,10020.5,1);
        Require(events[events.Count-1].Outcome=="Incorrect" && events[events.Count-1].Correct==false,"wrong-side response accepted");
        engine.Schedule(false,30,10030,21,10021,0);engine.Tick(29,10029,0);engine.Tick(30.05,10030.05,-1);
        Require(events[events.Count-1].Outcome=="TooEarly" && !events[events.Count-1].Correct.HasValue,"early response classified correct");
        engine.Schedule(true,40,10040,31,10031,0);engine.Tick(39,10039,0);engine.Tick(42.1,10042.1,1);
        Require(events[events.Count-2].Outcome=="Miss" && double.IsNaN(events[events.Count-2].ReactionSeconds),"missing response became zero latency");
        Require(events[events.Count-1].Outcome=="LateResponse" && events[events.Count-1].TrialIndex==4,"late response association lost");
        engine.Schedule(false,50,10050,43,10043,0);engine.Cancel("Pause",44,10044,0);engine.Cancel("Again",45,10045,0);
        Require(events.FindAll(e=>e.EventType=="TrialFinished" && e.TrialIndex==5).Count==1 && events[events.Count-1].Outcome=="CancelledBeforeOnset","scheduled cancellation not idempotent");
        engine.Schedule(false,60,10060,51,10051,0);engine.Tick(59,10059,0);engine.Tick(60,10060,0);engine.Cancel("Pause",61,10061,0);
        Require(events[events.Count-1].Outcome=="Interrupted" && events[events.Count-1].Presented,"paused trial became miss");
        settings.invertPedalAxis=true;settings.highToneUsesPositivePedal=false;
        engine=new TaskSwitchAuditoryTrial(settings);TaskSwitchAuditoryEvent reversed=null;engine.EventOccurred+=e=>reversed=e;
        engine.Schedule(true,10,10,0,0,0);engine.Tick(9,9,0);engine.Tick(10.2,10.2,1);
        Require(reversed.Outcome=="Correct" && reversed.ResponseSign==-1,"inversion/mapping not applied");
        var frozen=new TaskSwitchAuditoryTrial(new TaskSwitchAuditorySettings());
        var frozenEvents=new List<TaskSwitchAuditoryEvent>();frozen.EventOccurred+=frozenEvents.Add;
        frozen.Schedule(true,10,10010,0,10000,0);frozen.Tick(7,10007,0);frozen.Pause(8,10008,0);
        frozen.Tick(8,10038,1);Require(frozen.HasPendingTrial && frozenEvents.FindAll(e=>e.EventType=="PedalPressed").Count==0,"paused input advanced a pending trial");
        frozen.Resume(8,10038,1);frozen.Tick(9,10039,0);frozen.Tick(10,10040,0);frozen.Tick(10.4,10040.4,1);
        var frozenResult=frozenEvents.FindLast(e=>e.EventType=="TrialFinished");
        Require(frozenResult.Outcome=="Correct" && frozenResult.EstimatedOnsetReal==10040 && frozenResult.PauseCount==1 && frozenResult.PausedSeconds==30,"scheduled onset was not shifted by wall-clock pause");
        Require(Math.Abs(frozenResult.ReactionSeconds-.4)<.00001 && Math.Abs(frozenResult.WallReactionSeconds-.4)<.00001,"pre-onset pause inflated reaction time");

        var manager=new TaskSwitchExperimentManager();var op=new CraneOperationManager { IsOperationInputLocked=true };
        var registry=new CraneRegistry { cranes=new[]{new CraneInstance(),new CraneInstance()} };
        for(int i=0;i<2;i++) registry.cranes[i].components[typeof(CraneWorkPhaseTracker)]=new CraneWorkPhaseTracker();
        op.CurrentCraneInstance=registry.cranes[0];
        Set(manager,"enableAuditorySubtask",true);Set(manager,"auditorySubtask",new TaskSwitchAuditorySettings { randomSeed=17 });
        var logger=new TaskSwitchExperimentCsvLogger();Set(logger,"saveFolderPath",output);Set(logger,"taskSwitchExperimentManager",manager);
        Set(logger,"craneRegistry",registry);Set(logger,"craneOperationManager",op);Set(logger,"participantId","P02");Set(logger,"blockId","auditory");
        Time.realtimeSinceStartupAsDouble=200000;Time.timeAsDouble=20000;AudioSettings.dspTime=1000;PauseTestHarness.Set(false);
        Input.axes["TaskSwitchPedal"]=0;logger.StartLogging("auditory-test");
        manager.Emit("ExperimentPreparing");manager.CurrentState=TaskSwitchExperimentState.OperatingSource;manager.Emit("ExperimentStarted");
        Call(manager,"SubscribeAuditoryPauseEvents");Call(manager,"StartAuditorySubtask");
        var audio=(AudioSource)Get(manager,"auditoryAudio");
        Require(audio!=null && audio.spatialBlend==0 && !audio.playOnAwake,"audio setup incorrect");
        var clip=(AudioClip)Get(manager,"auditoryHighClip");Require(clip.Samples.Length==9600 && clip.Samples[0]==0 && clip.Samples[9599]==0,"tone duration/fade incorrect");
        double onset=audio.Scheduled;Require(onset>=1003 && onset<=1005,"first onset interval wrong");
        var runtimeEvents=new List<TaskSwitchAuditoryEvent>();manager.AuditorySubtaskEventOccurred+=runtimeEvents.Add;
        Advance(manager,logger,onset-.1,0);Advance(manager,logger,onset+.02,0);
        int sign=runtimeEvents.Find(e=>e.EventType=="StimulusOnsetObserved").ExpectedSign;
        op.CurrentCraneIndex=1;op.CurrentCraneInstance=registry.cranes[1];manager.CurrentState=TaskSwitchExperimentState.OperatingTarget;manager.CurrentSwitchIndex=1;
        Advance(manager,logger,onset+.15,sign);
        Require(runtimeEvents.Find(e=>e.EventType=="TrialFinished").Outcome=="Correct","main-operation lock gated pedal");
        Require(audio.Scheduled==onset,"response truncated current tone");
        Advance(manager,logger,onset+.3,0);double secondOnset=audio.Scheduled;
        Require(secondOnset-onset>=3 && secondOnset-onset<=5,"stimulus interval drifted with response time");
        Advance(manager,logger,secondOnset+.02,0);
        PauseTestHarness.Set(true);Require(AudioListener.pause,"pause did not freeze audio");
        int plays=audio.Plays;pausedWallOffset=20;
        Advance(manager,logger,secondOnset+.02,sign);Require(audio.Plays==plays,"paused subtask issued tones");
        PauseTestHarness.Set(false);Require(audio.Scheduled==secondOnset && audio.Plays==plays,"resume replaced the suspended stimulus");
        sign=runtimeEvents.FindLast(e=>e.EventType=="StimulusOnsetObserved").ExpectedSign;
        Advance(manager,logger,secondOnset+.2,0);Advance(manager,logger,secondOnset+.52,sign);
        var resumed=runtimeEvents.FindLast(e=>e.EventType=="TrialFinished");
        Require(resumed.Outcome=="Correct" && resumed.PauseCount==1 && Math.Abs(resumed.PausedSeconds-20)<.00001 && Math.Abs(resumed.ReactionSeconds-.52)<.00001,"suspended trial/active reaction time not preserved");
        double thirdOnset=audio.Scheduled;
        Advance(manager,logger,thirdOnset-.1,0);Advance(manager,logger,thirdOnset+.01,0);Advance(manager,logger,thirdOnset+2.1,0);
        Require(runtimeEvents.FindAll(e=>e.EventType=="TrialFinished" && e.Outcome=="Miss").Count==1,"timeout missing/duplicated");
        // Recording stop finalizes the next scheduled trial before closing the file.
        logger.StopLogging();int afterStop=audio.Plays;Advance(manager,logger,thirdOnset+10,0);
        Require(!manager.AuditorySubtaskRunning && audio.Plays==afterStop,"logging stop restarted unrecorded subtask");
        Set(manager,"enableAuditorySubtask",false);logger.StartLogging("auditory-disabled");Advance(manager,logger,thirdOnset+11,1);logger.StopLogging();
        Require(audio.Plays==afterStop,"disabled subtask played audio");Call(manager,"DisposeAuditorySubtask");
        // Runtime checkbox changes cancel old trials and preserve unique trial/activation IDs.
        var toggle=new TaskSwitchExperimentManager { CurrentState=TaskSwitchExperimentState.OperatingSource };
        Set(toggle,"enableAuditorySubtask",true);var toggled=new List<TaskSwitchAuditoryEvent>();toggle.AuditorySubtaskEventOccurred+=toggled.Add;
        Call(toggle,"UpdateAuditorySubtask");Set(toggle,"enableAuditorySubtask",false);Call(toggle,"UpdateAuditorySubtask");
        Require(!toggle.AuditorySubtaskRunning && toggled.Find(e=>e.EventType=="TrialFinished").Detail=="InspectorDisabled","Inspector OFF failed");
        Set(toggle,"enableAuditorySubtask",true);Call(toggle,"UpdateAuditorySubtask");
        Require(toggle.AuditoryActivationIndex==2 && toggled.FindAll(e=>e.EventType=="StimulusScheduled")[1].TrialIndex==2,"toggle reused identifiers");
        Call(toggle,"DisposeAuditorySubtask");
        // Input/configuration failures suppress only the secondary task, without retrying every frame.
        var invalid=new TaskSwitchExperimentManager { CurrentState=TaskSwitchExperimentState.OperatingSource };
        Set(invalid,"enableAuditorySubtask",true);Set(invalid,"auditorySubtask",new TaskSwitchAuditorySettings { pedalAxisName="UnknownPedal" });
        Input.ErrorAxis="UnknownPedal";int errorsBefore=Debug.Errors;
        Call(invalid,"UpdateAuditorySubtask");Call(invalid,"UpdateAuditorySubtask");
        Require(!invalid.AuditorySubtaskRunning && invalid.CurrentState==TaskSwitchExperimentState.OperatingSource && Debug.Errors==errorsBefore+1,"input failure changed main state or repeated errors");
        Input.ErrorAxis=null;Call(invalid,"DisposeAuditorySubtask");
        Console.WriteLine("PASS: auditory correct/incorrect/early/miss/late, neutral hysteresis, held pedals, inversion, scheduling, audio envelope, crane-lock independence, immediate pause, resumed intervals, logging stop, disabled feature");
    }
}

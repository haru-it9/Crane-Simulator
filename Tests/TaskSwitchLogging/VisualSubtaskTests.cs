using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

static class VisualSubtaskTests
{
    static void Require(bool value,string message) { if (!value) throw new Exception(message); }
    static void Set(object o,string field,object value) { o.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,value); }
    static object Get(object o,string field) { return o.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o); }
    static void Call(object o,string method) { o.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null); }
    static double Due(TaskSwitchExperimentManager manager) { return (double)Get(manager,"visualScheduledClock")+ExperimentPauseManager.TotalPauseSeconds; }
    static void Advance(TaskSwitchExperimentManager manager,TaskSwitchExperimentCsvLogger logger,double real,float pedal)
    {
        Time.realtimeSinceStartupAsDouble=real;Time.frameCount++;
        Input.axes["TaskSwitchPedal"]=pedal;
        Call(manager,"UpdateSecondarySubtask");
        if (logger!=null) Call(logger,"LateUpdate");
    }
    static TaskSwitchExperimentManager Create(out Image left,out Image right)
    {
        var manager=new TaskSwitchExperimentManager();left=new Image();right=new Image();
        Set(manager,"visualLeftIndicator",left);Set(manager,"visualRightIndicator",right);
        Call(manager,"InitializeSecondaryTaskUi");
        Set(manager,"secondaryTaskMode",TaskSwitchSecondaryTaskMode.Visual);
        Set(manager,"visualSubtask",new TaskSwitchVisualSettings { randomSeed=12,minimumIntervalSeconds=3,maximumIntervalSeconds=3 });
        return manager;
    }
    public static void Run(string output)
    {
        Time.realtimeSinceStartupAsDouble=600000;Time.timeAsDouble=40000;PauseTestHarness.Set(false);
        // A visual task must not depend on an audio output device or an advancing DSP clock.
        AudioSettings.dspTime=0;AudioSettings.outputSampleRate=0;
        Image left,right;var manager=Create(out left,out right);
        Require(!left.gameObject.activeInHierarchy && !right.gameObject.activeInHierarchy,"inactive modality left markers visible");
        var events=new List<TaskSwitchAuditoryEvent>();manager.VisualSubtaskEventOccurred+=events.Add;
        var registry=new CraneRegistry { cranes=new[]{new CraneInstance(),new CraneInstance()} };
        for(int i=0;i<2;i++) registry.cranes[i].components[typeof(CraneWorkPhaseTracker)]=new CraneWorkPhaseTracker();
        var op=new CraneOperationManager { IsOperationInputLocked=true,CurrentCraneInstance=registry.cranes[0] };
        var logger=new TaskSwitchExperimentCsvLogger();Set(logger,"saveFolderPath",output);Set(logger,"taskSwitchExperimentManager",manager);
        Set(logger,"craneRegistry",registry);Set(logger,"craneOperationManager",op);Set(logger,"participantId","P04");Set(logger,"blockId","visual");
        Input.axes["TaskSwitchPedal"]=0;logger.StartLogging("visual-test");
        manager.Emit("ExperimentPreparing");manager.CurrentState=TaskSwitchExperimentState.OperatingSource;manager.Emit("ExperimentStarted");
        Call(manager,"SubscribeSecondaryPauseEvents");Call(manager,"StartSecondarySubtask");
        Require(manager.VisualSubtaskRunning && !manager.AuditorySubtaskRunning && Get(manager,"auditoryAudio")==null,"visual task played audio");
        Require(!left.raycastTarget && !right.raycastTarget && left.color.b==1 && right.color.b==1,"initial blue/noninteractive UI incorrect");
        double firstDue=Due(manager);Require(firstDue==600003,"first visual interval incorrect");
        Advance(manager,logger,firstDue-.1,0);
        Advance(manager,logger,firstDue+2.7,0); // Frame stall: start RT at the actual red frame.
        double onset=Time.realtimeSinceStartupAsDouble;
        var shown=events.FindLast(e=>e.EventType=="StimulusOnsetObserved");int sign=shown.ExpectedSign;
        Require(manager.VisualLeftRed!=manager.VisualRightRed && shown.Presented,"exactly one marker must turn red");
        Require((sign==1 && right.color.r==1 && left.color.b==1) || (sign==-1 && left.color.r==1 && right.color.b==1),"side/pedal correspondence incorrect");
        op.CurrentCraneIndex=1;op.CurrentCraneInstance=registry.cranes[1];manager.CurrentState=TaskSwitchExperimentState.OperatingTarget;manager.CurrentSwitchIndex=1;
        Advance(manager,logger,onset+.24,sign);
        var result=events.FindLast(e=>e.EventType=="TrialFinished");
        Require(result.Outcome=="Correct" && Math.Abs(result.ReactionSeconds-.24)<.00001,"RT started at scheduled rather than actual UI onset");
        Require(!manager.VisualLeftRed && !manager.VisualRightRed && left.color.b==1 && right.color.b==1,"response failed to restore blue");
        Require(Math.Abs(Due(manager)-onset-.24-3)<.00001,"next red was not scheduled from the return to blue");

        double secondDue=Due(manager);Advance(manager,logger,secondDue-.1,0);Advance(manager,logger,secondDue,0);
        sign=events.FindLast(e=>e.EventType=="StimulusOnsetObserved").ExpectedSign;
        Advance(manager,logger,secondDue+.15,-sign);
        Advance(manager,logger,secondDue+.2,0);PauseTestHarness.Set(true);
        bool wasLeft=manager.VisualLeftRed;int finished=events.FindAll(e=>e.EventType=="TrialFinished").Count;
        Advance(manager,logger,secondDue+10.2,sign);
        Require(manager.VisualLeftRed==wasLeft && manager.VisualLeftRed!=manager.VisualRightRed && events.FindAll(e=>e.EventType=="TrialFinished").Count==finished,"Pause changed colors or finalized trial");
        PauseTestHarness.Set(false);Advance(manager,logger,secondDue+10.2,sign);
        Require(events.FindAll(e=>e.EventType=="TrialFinished").Count==finished,"held paused pedal became resumed response");
        Advance(manager,logger,secondDue+10.3,0);Advance(manager,logger,secondDue+10.5,sign);
        result=events.FindLast(e=>e.EventType=="TrialFinished");
        Require(result.Outcome=="Correct" && result.PauseCount==1 && Math.Abs(result.PausedSeconds-10)<.00001 &&
            Math.Abs(result.ReactionSeconds-.5)<.00001 && Math.Abs(result.WallReactionSeconds-10.5)<.00001,"visual pause included stopped time in RT");
        Require(result.FirstResponseCorrect==false && result.ResponseCount==2 && Math.Abs(result.FirstReactionSeconds-.15)<.00001 &&
            Math.Abs(result.FirstWallReactionSeconds-.15)<.00001,"pause or correction changed the earlier first response");

        double due=Due(manager);Advance(manager,logger,due-.1,0);Advance(manager,logger,due,0);
        sign=events.FindLast(e=>e.EventType=="StimulusOnsetObserved").ExpectedSign;
        finished=events.FindAll(e=>e.EventType=="TrialFinished").Count;
        Advance(manager,logger,due+12,0);
        Require(manager.VisualLeftRed!=manager.VisualRightRed && events.FindAll(e=>e.EventType=="TrialFinished").Count==finished &&
            Math.Abs(Due(manager)-due)<.00001,"no response timed out or changed the pending stimulus");
        Advance(manager,logger,due+12.2,sign);
        Require(Math.Abs(events.FindLast(e=>e.EventType=="TrialFinished").ReactionSeconds-12.2)<.00001 &&
            Math.Abs(Due(manager)-due-12.2-3)<.00001,"slow correct response did not start a full blue interval");
        due=Due(manager);Advance(manager,logger,due-.1,0);Advance(manager,logger,due,0);
        sign=events.FindLast(e=>e.EventType=="StimulusOnsetObserved").ExpectedSign;finished=events.FindAll(e=>e.EventType=="TrialFinished").Count;
        Advance(manager,logger,due+.3,-sign);Advance(manager,logger,due+.4,0);Advance(manager,logger,due+.5,-sign);
        Advance(manager,logger,due+.55,-sign);
        Require(manager.VisualLeftRed!=manager.VisualRightRed && events.FindAll(e=>e.EventType=="TrialFinished").Count==finished &&
            Math.Abs(Due(manager)-due)<.00001,"incorrect response cleared red or changed side/onset");
        Advance(manager,logger,due+.6,0);Advance(manager,logger,due+.7,sign);
        result=events.FindLast(e=>e.EventType=="TrialFinished");
        Require(result.Outcome=="Correct" && result.ResponseCount==3 && result.IncorrectResponseCount==2 &&
            result.FirstResponseOutcome=="Incorrect" && result.FirstResponseCorrect==false &&
            Math.Abs(result.FirstReactionSeconds-.3)<.00001 && Math.Abs(result.ReactionSeconds-.7)<.00001,"correction overwrote first error or double-counted held input");
        due=Due(manager);Advance(manager,logger,due-.1,0);Advance(manager,logger,due,0);
        sign=events.FindLast(e=>e.EventType=="StimulusOnsetObserved").ExpectedSign;
        Advance(manager,logger,due+.02,-sign);Require(manager.VisualLeftRed!=manager.VisualRightRed,"early wrong-side response cleared red");
        Advance(manager,logger,due+.04,0);Advance(manager,logger,due+.07,sign);
        result=events.FindLast(e=>e.EventType=="TrialFinished");
        Require(result.Outcome=="TooEarly" && result.ResponseCount==2 && result.TooEarlyResponseCount==2 &&
            result.IncorrectResponseCount==1 && !manager.VisualLeftRed && !manager.VisualRightRed,"fast correct-side press did not clear red or lost early markers");

        due=Due(manager);Advance(manager,logger,due-.3,0);Advance(manager,logger,due-.2,1);Advance(manager,logger,due,1);
        shown=events.FindLast(e=>e.EventType=="StimulusOnsetObserved");sign=shown.ExpectedSign;
        Require(shown.HeldAtOnset,"held pedal at visual onset not marked");
        finished=events.FindAll(e=>e.EventType=="TrialFinished").Count;Advance(manager,logger,due+.1,-1);
        Require(events.FindAll(e=>e.EventType=="TrialFinished").Count==finished,"direct sign reversal rearmed pedal");
        Advance(manager,logger,due+.2,0);Advance(manager,logger,due+.4,sign);

        // Pause before the planned color change must preserve remaining wait, not paint a missed stimulus.
        due=Due(manager);Advance(manager,logger,due-.5,0);PauseTestHarness.Set(true);
        Advance(manager,logger,due+6.5,1);Require(!manager.VisualLeftRed && !manager.VisualRightRed,"pre-onset pause painted red");
        PauseTestHarness.Set(false);Advance(manager,logger,due+6.5,0);
        Require(Math.Abs(Due(manager)-due-7)<.00001,"planned wait was not shifted across pause");
        Advance(manager,logger,due+7,0);sign=events.FindLast(e=>e.EventType=="StimulusOnsetObserved").ExpectedSign;
        Advance(manager,logger,due+7.3,sign);
        result=events.FindLast(e=>e.EventType=="TrialFinished");
        Require(result.PauseCount==1 && Math.Abs(result.PausedSeconds-7)<.00001 && Math.Abs(result.ReactionSeconds-.3)<.00001 &&
            Math.Abs(result.WallReactionSeconds-.3)<.00001,"pre-onset pause inflated visual response time");
        // A cancelled red trial keeps its first wrong response without inventing a successful RT.
        due=Due(manager);Advance(manager,logger,due-.1,0);Advance(manager,logger,due,0);
        sign=events.FindLast(e=>e.EventType=="StimulusOnsetObserved").ExpectedSign;Advance(manager,logger,due+.3,-sign);
        manager.GetType().GetMethod("StopSecondarySubtask",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(manager,new object[]{"TestInterruption"});
        result=events.FindLast(e=>e.EventType=="TrialFinished");
        Require(result.Outcome=="Interrupted" && result.FirstResponseOutcome=="Incorrect" && result.ResponseCount==1 &&
            double.IsNaN(result.ReactionSeconds),"interrupted retry lost first response or invented completion RT");
        Advance(manager,logger,due+.5,0);
        logger.StopLogging();double stopped=Time.realtimeSinceStartupAsDouble;Advance(manager,logger,stopped+10,0);
        Require(!manager.VisualSubtaskRunning && !left.gameObject.activeInHierarchy && !right.gameObject.activeInHierarchy,"recording stop restarted unlogged visual trials");
        Require(events.FindLast(e=>e.EventType=="TrialFinished").Outcome=="CancelledBeforeOnset","recording stop lost planned trial");
        Call(manager,"DisposeSecondarySubtask");

        // Switch modalities while a red stimulus is awaiting an answer, then select None.
        AudioSettings.outputSampleRate=48000;Image l2,r2;var toggle=Create(out l2,out r2);toggle.CurrentState=TaskSwitchExperimentState.OperatingSource;
        var toggleEvents=new List<TaskSwitchAuditoryEvent>();toggle.VisualSubtaskEventOccurred+=toggleEvents.Add;
        Advance(toggle,null,stopped+20,0);Advance(toggle,null,Due(toggle),0);
        Set(toggle,"secondaryTaskMode",TaskSwitchSecondaryTaskMode.Auditory);Advance(toggle,null,Time.realtimeSinceStartupAsDouble+.2,0);
        Require(toggle.AuditorySubtaskRunning && !toggle.VisualSubtaskRunning && !l2.gameObject.activeInHierarchy &&
            toggleEvents.FindLast(e=>e.EventType=="TrialFinished").Outcome=="Interrupted","modality switch left old task active");
        Set(toggle,"secondaryTaskMode",TaskSwitchSecondaryTaskMode.None);Advance(toggle,null,Time.realtimeSinceStartupAsDouble+.2,0);
        Require(!toggle.SecondarySubtaskRunning,"None left secondary task running");Call(toggle,"DisposeSecondarySubtask");

        // Default settings use a random 2-5 second gap after blue, independently choosing either side.
        var defaults=new TaskSwitchVisualSettings { randomSeed=23 };
        Require(defaults.Validate()==null && defaults.minimumIntervalSeconds==2 && defaults.maximumIntervalSeconds==5,"2-5 second defaults are invalid");
        Image l3,r3;var random=Create(out l3,out r3);Set(random,"visualSubtask",defaults);random.CurrentState=TaskSwitchExperimentState.OperatingSource;
        var randomEvents=new List<TaskSwitchAuditoryEvent>();random.VisualSubtaskEventOccurred+=randomEvents.Add;
        var gaps=new HashSet<int>();var sides=new HashSet<int>();bool repeatedSide=false;int previousSide=0;
        Advance(random,null,Time.realtimeSinceStartupAsDouble+20,0);
        for (int i=0;i<20;i++)
        {
            var scheduled=randomEvents.FindLast(e=>e.EventType=="StimulusScheduled");
            double gap=scheduled.PlannedOnsetClock-scheduled.DspTime;
            Require(gap>=2 && gap<=5,"gap measured from blue is outside 2-5 seconds");gaps.Add((int)(gap*1000));
            sign=scheduled.ExpectedSign;sides.Add(sign);repeatedSide|=previousSide==sign;previousSide=sign;
            due=Due(random);Advance(random,null,due-.1,0);Advance(random,null,due,0);Advance(random,null,due+.3,sign);
            Require(!random.VisualLeftRed && !random.VisualRightRed && Due(random)-Time.realtimeSinceStartupAsDouble>=2 &&
                Due(random)-Time.realtimeSinceStartupAsDouble<=5,"successful response did not begin the full random blue wait");
        }
        Require(gaps.Count>1 && sides.Count==2 && repeatedSide,"random sequence was fixed or strictly alternating");
        Call(random,"DisposeSecondarySubtask");

        var legacy=new TaskSwitchExperimentManager();Set(legacy,"enableAuditorySubtask",true);Call(legacy,"InitializeSecondaryTaskUi");
        Require(legacy.SecondaryTaskMode==TaskSwitchSecondaryTaskMode.Auditory,"legacy enabled scene was silently disabled");
        Set(legacy,"secondaryTaskMode",TaskSwitchSecondaryTaskMode.None);Require(!legacy.AuditorySubtaskEnabled,"legacy migration prevents selecting None");
        var invalid=new TaskSwitchExperimentManager { CurrentState=TaskSwitchExperimentState.OperatingTarget };
        Set(invalid,"secondaryTaskMode",TaskSwitchSecondaryTaskMode.Visual);int errors=Debug.Errors;
        Call(invalid,"UpdateSecondarySubtask");Call(invalid,"UpdateSecondarySubtask");
        Require(!invalid.VisualSubtaskRunning && invalid.CurrentState==TaskSwitchExperimentState.OperatingTarget && Debug.Errors==errors+1,"missing UI repeatedly errored or changed main work");
        Call(invalid,"DisposeSecondarySubtask");
        Console.WriteLine("PASS: visual red until correct side, no timeout, repeated errors and first-response preservation, early/held input, random 2-5 seconds from blue, random sides, actual-frame RT, DSP independence, main lock, pause before/after onset, recording stop and modality selection");
    }
}

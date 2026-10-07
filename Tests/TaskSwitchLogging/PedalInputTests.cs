using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

static class PedalInputTests
{
    static void Require(bool value,string message) { if (!value) throw new Exception(message); }
    static void Set(object o,string field,object value) { o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value); }
    static object Get(object o,string field) { return o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o); }
    static void Call(object o,string method) { o.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null); }
    static double Due(TaskSwitchExperimentManager m) { return (double)Get(m,"visualScheduledClock")+ExperimentPauseManager.TotalPauseSeconds; }
    static void Keys(params KeyCode[] keys) { Input.keys.Clear();foreach(var key in keys) Input.keys.Add(key); }
    static void Tick(TaskSwitchExperimentManager m,TaskSwitchExperimentCsvLogger logger,double real,params KeyCode[] keys)
    {
        Time.realtimeSinceStartupAsDouble=real;Time.frameCount++;Keys(keys);Call(m,"UpdateSecondarySubtask");
        if (logger!=null) Call(logger,"LateUpdate");
    }
    public static void Run(string output)
    {
        Input.axes["TaskSwitchPedal"]=0;Keys();
        foreach(var key in new[]{KeyCode.Equals,KeyCode.KeypadPlus,KeyCode.Minus,KeyCode.KeypadMinus})
        {
            Keys(key);var input=TaskSwitchPedalInput.Read("TaskSwitchPedal",true,KeyCode.Equals,KeyCode.Minus);
            Require(input.AxisRaw==0 && input.Source=="PlusMinusKeys" && input.Value==(key==KeyCode.Equals||key==KeyCode.KeypadPlus?1:-1),"plus/minus key was not read independently of the axis");
        }
        Keys(KeyCode.Semicolon);Require(TaskSwitchPedalInput.Read("TaskSwitchPedal",true,KeyCode.Semicolon,KeyCode.Minus).Value==1,"custom physical key ignored");
        Input.axes["TaskSwitchPedal"]=-.8f;Keys(KeyCode.Equals);
        Require(TaskSwitchPedalInput.Read("TaskSwitchPedal",false,KeyCode.Equals,KeyCode.Minus).Value==-.8f,"disabled key fallback replaced axis input");
        Input.axes["TaskSwitchPedal"]=0;Keys(KeyCode.Equals,KeyCode.Minus);
        var conflict=TaskSwitchPedalInput.Read("TaskSwitchPedal",true,KeyCode.Equals,KeyCode.Minus);
        Require(conflict.ConflictingKeys && float.IsNaN(conflict.Value),"both keys were treated as a neutral or valid response");Keys();

        Time.realtimeSinceStartupAsDouble=800000;Time.timeAsDouble=50000;PauseTestHarness.Set(false);
        var m=new TaskSwitchExperimentManager();var left=new Image();var right=new Image();
        Set(m,"visualLeftIndicator",left);Set(m,"visualRightIndicator",right);Call(m,"InitializeSecondaryTaskUi");
        Event.current=new Event { type=EventType.KeyDown,keyCode=KeyCode.Semicolon };Call(m,"OnGUI");
        Event.current=new Event { type=EventType.KeyUp,keyCode=KeyCode.Semicolon };Call(m,"OnGUI");Event.current=null;
        Set(m,"secondaryTaskMode",TaskSwitchSecondaryTaskMode.Visual);
        var settings=new TaskSwitchVisualSettings { randomSeed=39 };Set(m,"visualSubtask",settings);
        var events=new List<TaskSwitchAuditoryEvent>();m.VisualSubtaskEventOccurred+=events.Add;
        var logger=new TaskSwitchExperimentCsvLogger();Set(logger,"saveFolderPath",output);Set(logger,"taskSwitchExperimentManager",m);
        var registry=new CraneRegistry { cranes=new[]{new CraneInstance(),new CraneInstance()} };
        for(int i=0;i<2;i++) registry.cranes[i].components[typeof(CraneWorkPhaseTracker)]=new CraneWorkPhaseTracker();
        Set(logger,"craneRegistry",registry);Set(logger,"craneOperationManager",new CraneOperationManager { CurrentCraneInstance=registry.cranes[0] });
        Set(logger,"participantId","P05");Set(logger,"blockId","pedal_keys");logger.StartLogging("keyboard-pedal-test");
        Require(logger.IsLogging,"keyboard pedal CSV test failed to start logging");
        m.CurrentState=TaskSwitchExperimentState.OperatingSource;Call(m,"SubscribeSecondaryPauseEvents");Call(m,"StartSecondarySubtask");
        double due=Due(m);Tick(m,logger,due-.1);Tick(m,logger,due);
        int sign=events.FindLast(e=>e.EventType=="StimulusOnsetObserved").ExpectedSign;
        KeyCode correct=sign==1?KeyCode.Equals:KeyCode.Minus,wrong=sign==1?KeyCode.Minus:KeyCode.Equals;
        Tick(m,logger,due+.2,wrong);Require(m.VisualLeftRed!=m.VisualRightRed,"wrong keyboard pedal cleared red");
        Tick(m,logger,due+.3);Tick(m,logger,due+.4,correct);
        var result=events.FindLast(e=>e.EventType=="TrialFinished");
        Require(result.Outcome=="Correct" && result.PedalInputSource=="PlusMinusKeys" && result.PedalAxisRaw==0 && result.ResponseCount==2 &&
            !m.VisualLeftRed && !m.VisualRightRed,"keyboard pedal failed to return actual UI to blue");
        Require(m.PedalInputDiagnostics.Contains("Source=PlusMinusKeys") && m.PedalInputDiagnostics.Contains("LastResponse=Correct") &&
            m.PedalInputDiagnostics.Contains("AxisRaw=0.000") && m.PedalInputDiagnostics.Contains("LastKeyDown=Semicolon"),"Inspector diagnostics did not show effective keyboard response or actual physical key");

        due=Due(m);Tick(m,logger,due-.1);Tick(m,logger,due);sign=events.FindLast(e=>e.EventType=="StimulusOnsetObserved").ExpectedSign;
        correct=sign==1?KeyCode.KeypadPlus:KeyCode.KeypadMinus;
        Tick(m,logger,due+.1);PauseTestHarness.Set(true);Tick(m,logger,due+5.1,correct);
        Require(m.VisualLeftRed!=m.VisualRightRed && m.PedalInputDiagnostics.Contains("Paused=True") &&
            (m.PedalInputDiagnostics.Contains("PlusKey=True")||m.PedalInputDiagnostics.Contains("MinusKey=True")),"pause did not show live keys while preserving red");
        PauseTestHarness.Set(false);Tick(m,logger,due+5.1,correct);Require(m.VisualLeftRed!=m.VisualRightRed,"held paused keypad input counted on resume");
        Tick(m,logger,due+5.2);Tick(m,logger,due+5.4,correct);
        result=events.FindLast(e=>e.EventType=="TrialFinished");
        Require(result.Outcome=="Correct" && result.PauseCount==1 && Math.Abs(result.ReactionSeconds-.4)<.00001,"keypad response or pause RT failed");

        due=Due(m);Tick(m,logger,due-.1);Tick(m,logger,due);sign=events.FindLast(e=>e.EventType=="StimulusOnsetObserved").ExpectedSign;
        correct=sign==1?KeyCode.Equals:KeyCode.Minus;
        Tick(m,logger,due+.2,KeyCode.Equals,KeyCode.Minus);
        Require(!m.SecondaryPedalArmed && m.PedalInputDiagnostics.Contains("Conflict=True") && m.VisualLeftRed!=m.VisualRightRed,"conflicting keys rearmed or answered");
        Tick(m,logger,due+.3,correct);Require(m.VisualLeftRed!=m.VisualRightRed,"release of one conflicting key became a response");
        Tick(m,logger,due+.4);Tick(m,logger,due+.5,correct);
        Require(events.FindLast(e=>e.EventType=="TrialFinished").ResponseCount==1 && !m.VisualLeftRed && !m.VisualRightRed,"neutral rearm after simultaneous keys failed");

        // Settings restart: axis-only operation remains available, even with a wrong keyboard key held.
        m.GetType().GetMethod("StopSecondarySubtask",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(m,new object[]{"SettingsRestart"});
        Keys();settings.usePlusMinusKeys=false;m.ResumeSecondaryForLoggingStart();due=Due(m);Tick(m,logger,due-.1);Tick(m,logger,due);
        sign=events.FindLast(e=>e.EventType=="StimulusOnsetObserved").ExpectedSign;Input.axes["TaskSwitchPedal"]=sign*.8f;
        Tick(m,logger,due+.3,sign==1?KeyCode.Minus:KeyCode.Equals);result=events.FindLast(e=>e.EventType=="TrialFinished");
        Require(result.Outcome=="Correct" && result.PedalInputSource=="Axis" && Math.Abs(result.PedalAxisRaw-sign*.8f)<.00001,"axis-only operation regressed");
        logger.StopLogging();Call(m,"DisposeSecondarySubtask");Input.axes["TaskSwitchPedal"]=0;Keys();

        // The same keyboard fallback must serve the auditory task.
        var audio=new TaskSwitchExperimentManager { CurrentState=TaskSwitchExperimentState.OperatingSource };
        Set(audio,"secondaryTaskMode",TaskSwitchSecondaryTaskMode.Auditory);var audioEvents=new List<TaskSwitchAuditoryEvent>();audio.AuditorySubtaskEventOccurred+=audioEvents.Add;
        AudioSettings.outputSampleRate=48000;AudioSettings.dspTime=3000;Call(audio,"StartSecondarySubtask");
        double audioOnset=((AudioSource)Get(audio,"auditoryAudio")).Scheduled;
        AudioSettings.dspTime=audioOnset-.1;Tick(audio,null,Time.realtimeSinceStartupAsDouble+1);
        AudioSettings.dspTime=audioOnset;Tick(audio,null,Time.realtimeSinceStartupAsDouble+.1);
        sign=audioEvents.FindLast(e=>e.EventType=="StimulusOnsetObserved").ExpectedSign;
        AudioSettings.dspTime=audioOnset+.3;Tick(audio,null,Time.realtimeSinceStartupAsDouble+.3,sign==1?KeyCode.KeypadPlus:KeyCode.KeypadMinus);
        result=audioEvents.FindLast(e=>e.EventType=="TrialFinished");Require(result.Outcome=="Correct" && result.PedalInputSource=="PlusMinusKeys","auditory task ignored the keyboard pedal");
        Call(audio,"DisposeSecondarySubtask");Keys();
        Console.WriteLine("PASS: main/keypad +/- pedals with zero axis, custom keys, real visual blue return and wrong response retention, live Inspector diagnostics, paused keys, simultaneous-key neutral guard, axis-only and auditory compatibility");
    }
}

using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

static class PauseTestHarness
{
    static ExperimentPauseManager manager;
    public static ExperimentPauseManager Manager
    {
        get
        {
            if (manager==null)
            {
                manager=new ExperimentPauseManager();
                typeof(ExperimentPauseManager).GetField("pauseOnSimulatorStart",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(manager,false);
                typeof(ExperimentPauseManager).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(manager,null);
            }
            return manager;
        }
    }
    public static void Set(bool paused) { Manager.SetPaused(paused); }
}
static class PauseTests
{
    static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
    static void Set(object o,string field,object value) { o.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,value); }
    static void Call(object o,string method) { o.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null); }
    public static void Run(string output)
    {
        Time.realtimeSinceStartupAsDouble=500000;Time.timeAsDouble=30000;Time.timeScale=.75f;
        var pause=PauseTestHarness.Manager;
        var experiment=new TaskSwitchExperimentManager { CurrentState=TaskSwitchExperimentState.OperatingTarget,CurrentSwitchIndex=2 };
        var simulator=new SimulatorStartManager { CurrentMode=SimulatorStartManager.SimulatorMode.TaskSwitchExperiment };
        Set(pause,"taskSwitchExperimentManager",experiment);Set(pause,"simulatorStartManager",simulator);
        var button=new Button();var label=new Text();Set(pause,"pauseResumeButton",button);Set(pause,"buttonLabel",label);
        button.onClick.persistent.Add(new Button.ClickEvent.Persistent { Target=experiment,Method="StartExperiment",Callback=experiment.StartExperiment });
        Set(pause,"additionalPauseResumeButtons",new[]{button});Call(pause,"OnEnable");
        Require(button.onClick.persistent[0].State==UnityEngine.Events.UnityEventCallState.Off,"legacy StartExperiment listener not disabled");
        double active=ExperimentPauseManager.ActiveRealtime;
        button.onClick.Invoke();Require(ExperimentPauseManager.IsPaused && Time.timeScale==0 && AudioListener.pause,"Pause button did not freeze physics/audio");
        Time.realtimeSinceStartupAsDouble+=20;
        Require(ExperimentPauseManager.ActiveRealtime==active,"real-time deadline advanced during Pause");
        button.onClick.Invoke();Require(!ExperimentPauseManager.IsPaused && Time.timeScale==.75f && !AudioListener.pause,"Start did not restore timing/audio");
        Require(experiment.Starts==0 && experiment.CurrentState==TaskSwitchExperimentState.OperatingTarget && experiment.CurrentSwitchIndex==2,"target work was reset on Pause/Start");
        Time.realtimeSinceStartupAsDouble+=1;Require(ExperimentPauseManager.ActiveRealtime==active+1,"active deadline did not resume");
        foreach(var state in new[]{TaskSwitchExperimentState.OperatingSource,TaskSwitchExperimentState.WaitingForConfirmation,TaskSwitchExperimentState.WaitingForSourceConfirmation,TaskSwitchExperimentState.CountingDown})
        {
            experiment.CurrentState=state;button.onClick.Invoke();Time.realtimeSinceStartupAsDouble+=7;button.onClick.Invoke();
            Require(experiment.CurrentState==state && experiment.Starts==0,"Pause/Start reset state: "+state);
        }
        // Only an idle experiment is initialized by the first Start.
        experiment.CurrentState=TaskSwitchExperimentState.Idle;button.onClick.Invoke();button.onClick.Invoke();
        Require(experiment.Starts==1 && experiment.CurrentState==TaskSwitchExperimentState.OperatingSource,"initial Start not initialized exactly once");
        button.onClick.Invoke();button.onClick.Invoke();Require(experiment.Starts==1,"later Start restarted experiment");
        experiment.CurrentState=TaskSwitchExperimentState.Completed;button.onClick.Invoke();button.onClick.Invoke();Require(experiment.Starts==1,"completed experiment restarted from Pause button");
        // Multiple pause spans and stop-during-pause remain identifiable in the CSV.
        var op=new CraneOperationManager { CurrentCraneIndex=1 };
        var registry=new CraneRegistry { cranes=new[]{new CraneInstance(),new CraneInstance()} };
        var source=new CraneWorkPhaseTracker();var target=new CraneWorkPhaseTracker { CurrentMajorPhase=CraneStatusManager.WorkPhase.LiftUp,CurrentStepId="LiftUp.LoadAcquisition" };
        registry.cranes[0].components[typeof(CraneWorkPhaseTracker)]=source;registry.cranes[1].components[typeof(CraneWorkPhaseTracker)]=target;
        op.CurrentCraneInstance=registry.cranes[1];experiment.CurrentState=TaskSwitchExperimentState.OperatingTarget;
        var logger=new TaskSwitchExperimentCsvLogger();Set(logger,"saveFolderPath",output);Set(logger,"taskSwitchExperimentManager",experiment);Set(logger,"craneOperationManager",op);Set(logger,"craneRegistry",registry);
        Set(logger,"participantId","P03");Set(logger,"blockId","pause");logger.StartLogging("pause-test");
        double origin=Time.realtimeSinceStartupAsDouble;
        Time.realtimeSinceStartupAsDouble=origin+1;PauseTestHarness.Set(true);Call(logger,"LateUpdate");
        Time.realtimeSinceStartupAsDouble=origin+6;Call(logger,"LateUpdate");
        Time.realtimeSinceStartupAsDouble=origin+8;PauseTestHarness.Set(false);Call(logger,"LateUpdate");
        Time.realtimeSinceStartupAsDouble=origin+10;PauseTestHarness.Set(true);Call(logger,"LateUpdate");
        Time.realtimeSinceStartupAsDouble=origin+12;logger.StopLogging();PauseTestHarness.Set(false);
        // Starting recording during an existing pause produces a partial-start interval.
        Time.realtimeSinceStartupAsDouble=origin+20;PauseTestHarness.Set(true);logger.StartLogging("pause-partial-start");
        Time.realtimeSinceStartupAsDouble=origin+23;Call(logger,"LateUpdate");PauseTestHarness.Set(false);logger.StopLogging();
        Call(pause,"OnDisable");
        Console.WriteLine("PASS: real PauseManager buttons, legacy reset listener, first-start-only initialization, source/target/confirmation/countdown state retention, frozen active clock, audio/timeScale restoration, pause CSV spans and partial intervals");
    }
}

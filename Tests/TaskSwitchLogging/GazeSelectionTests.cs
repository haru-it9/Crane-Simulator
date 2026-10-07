using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Tobii.Gaming;

static class GazeSelectionTests
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static object Invoke(object target, string name, params object[] args)
    {
        return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }
    public static void Run(string output)
    {
        var bounds = new TobiiGameViewSelection.Bounds { Left=914, Top=-1088, Right=2834, Bottom=-8 };
        var other = new TobiiGameViewSelection.Bounds { Left=-1006, Top=-1080, Right=914, Bottom=0 };
        var first = new TobiiGameViewSelection.Window { Handle=(IntPtr)1, Monitor=@"\\.\DISPLAY3", MonitorBounds=other, Focused=true };
        var second = new TobiiGameViewSelection.Window { Handle=(IntPtr)2, Monitor=@"\\.\DISPLAY2", MonitorBounds=bounds };
        var third = new TobiiGameViewSelection.Window { Handle=(IntPtr)3, Monitor=@"\\.\DISPLAY2", MonitorBounds=bounds };
        var windows = new List<TobiiGameViewSelection.Window> { first, third, second };
        Require(TobiiGameViewSelection.Choose(@"\\.\DISPLAY2",bounds,windows,IntPtr.Zero)==second.Handle,"wrong monitor chosen by enumeration order/focus");
        Require(TobiiGameViewSelection.Choose(@"\\.\display2",bounds,windows,third.Handle)==third.Handle,"existing matching view not retained");
        second.Focused=true;
        Require(TobiiGameViewSelection.Choose(@"\\.\DISPLAY2",bounds,windows,third.Handle)==second.Handle,"focused matching view not preferred");
        second.Focused=false;
        Require(TobiiGameViewSelection.Choose(@"\\.\DISPLAY2",other,windows,IntPtr.Zero)==IntPtr.Zero,"stale monitor geometry accepted");
        Require(TobiiGameViewSelection.Choose("",bounds,windows,IntPtr.Zero)==IntPtr.Zero,"unconfigured monitor accepted");
        Require(TobiiGameViewSelection.Choose(@"\\.\DISPLAY2",new TobiiGameViewSelection.Bounds(),windows,IntPtr.Zero)==IntPtr.Zero,"zero display rectangle accepted");
        Require(TobiiGameViewSelection.Choose(@"\\.\DISPLAY2",bounds,new List<TobiiGameViewSelection.Window>{first},second.Handle)==IntPtr.Zero,"missing game view reused old handle");

        bool available=false, connected=true;
        int frame=10, changed=10;
        double age=0;
        TobiiAPI.IsConnected=true;
        TobiiAPI.gaze=new GazePoint { IsValid=true, Viewport=new Vector2(.25f,.75f), Screen=new Vector2(480,810) };
        TobiiTrackedGameView.TestRead = () => TobiiGameViewSelection.CanRecord(available,connected,true,frame,changed,age)
            ? TobiiAPI.GetGazePoint() : GazePoint.Invalid;
        string directory=Path.Combine(output,"gaze_selection");Directory.CreateDirectory(directory);
        var normal = new TobiiGazeCsvLogger { saveFolderPath=directory };
        normal.StartLogging("selection");
        var task = new TaskSwitchExperimentCsvLogger();
        string header="is_connected,app_focused,is_valid,game_screen_x,game_screen_y,clamped_game_screen_x,clamped_game_screen_y,viewport_x,viewport_y,raw_screen_x,raw_screen_y,screen_width,screen_height,aoi,aoi_layout_json";
        using (var file=new ExperimentCsvFile(Path.Combine(directory,"selection_task.csv"),header))
        {
            typeof(TaskSwitchExperimentCsvLogger).GetField("gazeFile",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(task,file);
            Action sample = () => { Invoke(normal,"WriteGazeLine");Invoke(task,"WriteGaze",new object[]{new object[0]}); };
            sample(); // No matching view, although the old SDK point is still valid.
            available=true;sample(); // Rebinding frame must also be blank.
            frame++;sample(); // First subsequent fresh point can be recorded.
            available=false;frame++;sample(); // View closed or moved off the tracked monitor.
            available=true;connected=false;frame++;sample();
            connected=true;age=.6;frame++;sample(); // Stopped/stale stream.
            age=double.NaN;frame++;sample();
        }
        normal.StopLogging();
        var ordinary = File.ReadAllLines(Path.Combine(directory,"selection_gaze.csv"));
        var taskRows = File.ReadAllLines(Path.Combine(directory,"selection_task.csv"));
        for(int i=1;i<ordinary.Length;i++)
        {
            var n=ordinary[i].Split(',');var t=taskRows[i].Split(',');
            bool expected=i==3;
            Require(n[4]==(expected?"1":"0") && t[2]==(expected?"1":"0"),"selection gate CSV validity incorrect");
            if(!expected)
            {
                for(int j=5;j<=12;j++) Require(n[j]=="","ordinary CSV kept previous coordinates");
                for(int j=3;j<=10;j++) Require(t[j]=="","Task Switch CSV kept previous coordinates");
            }
        }
        Require(TobiiGameViewSelection.CanRecord(true,true,true,11,10,.1),"fresh point rejected");
        Require(!TobiiGameViewSelection.CanRecord(true,true,true,11,10,-.1),"future timestamp accepted");
        Require(!TobiiGameViewSelection.CanRecord(true,true,true,11,10,.5),"stale boundary accepted");
        TobiiAPI.IsConnected=false;TobiiAPI.gaze=new GazePoint();
        TobiiTrackedGameView.TestRead=null;
        Console.WriteLine("PASS: tracked-monitor selection, wrong-monitor focus, retained/focused views, missing/zero/stale display bounds, rebind/disconnect/stale gaze blank in both real CSV loggers");
    }
}

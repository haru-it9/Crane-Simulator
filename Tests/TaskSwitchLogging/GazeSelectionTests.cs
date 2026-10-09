using System;
using System.Collections.Generic;
using System.Globalization;
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
    static GazePoint Fresh(float x = .25f, float y = .75f)
    {
        return new GazePoint { IsValid=true, Timestamp=Time.unscaledTime,
            Viewport=new Vector2(x,y), Screen=new Vector2(x*Screen.width,y*Screen.height) };
    }
    public static void Run(string output)
    {
        // Exercise the production reader against SDK doubles, without the test hook
        // or a selector component. Both real CSV writers must share EyeTrack's rules.
        TobiiTrackedGameView.TestRead=null;
        Time.realtimeSinceStartupAsDouble=10;
        Time.frameCount=0;
        TobiiAPI.IsConnected=true;
        string directory=Path.Combine(output,"gaze_selection");Directory.CreateDirectory(directory);
        var normal = new TobiiGazeCsvLogger { saveFolderPath=directory };
        normal.StartLogging("selection");
        var task = new TaskSwitchExperimentCsvLogger();
        var expected = new List<bool>();
        string header="is_connected,app_focused,is_valid,game_screen_x,game_screen_y,clamped_game_screen_x,clamped_game_screen_y,viewport_x,viewport_y,raw_screen_x,raw_screen_y,screen_width,screen_height,aoi,aoi_layout_json";
        using (var file=new ExperimentCsvFile(Path.Combine(directory,"selection_task.csv"),header))
        {
            typeof(TaskSwitchExperimentCsvLogger).GetField("gazeFile",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(task,file);
            Action<GazePoint,bool> sample = (point,usable) =>
            {
                TobiiAPI.gaze=point;
                Require(TobiiTrackedGameView.GetGazePoint().IsValid==usable,"SDK sample eligibility differs from EyeTrack");
                Invoke(normal,"WriteGazeLine");Invoke(task,"WriteGaze",new object[]{new object[0]});
                expected.Add(usable);
            };
            sample(Fresh(),true); // Fresh data works immediately, without a window-binding frame.
            sample(Fresh(),true); // Multiple consumers in one frame do not discard the sample.
            sample(GazePoint.Invalid,false);
            var stale=Fresh();stale.Timestamp-=.5f;sample(stale,false);
            var old=Fresh();old.Timestamp-=1f;sample(old,false);
            var nan=Fresh();nan.Screen=new Vector2(float.NaN,810);sample(nan,false);
            var infinity=Fresh();infinity.Screen=new Vector2(480,float.PositiveInfinity);sample(infinity,false);
            var noTimestamp=Fresh();noTimestamp.Timestamp=float.NaN;sample(noTimestamp,false);
            sample(Fresh(.75f,-.25f),true); // Preserve real off-screen coordinates; no clamping in the reader.
            TobiiAPI.IsConnected=false;
            sample(Fresh(),true); // Match EyeTrack: connection metadata is recorded, not an extra veto.
            TobiiAPI.IsConnected=true;
            Time.frameCount++;Time.realtimeSinceStartupAsDouble=12;
            sample(Fresh(),true); // Acquisition recovers after missing/stale data.
        }
        normal.StopLogging();
        var ordinary = File.ReadAllLines(Path.Combine(directory,"selection_gaze.csv"));
        var taskRows = File.ReadAllLines(Path.Combine(directory,"selection_task.csv"));
        Require(ordinary.Length==expected.Count+1 && taskRows.Length==ordinary.Length,"gaze CSV row count changed");
        for(int i=1;i<ordinary.Length;i++)
        {
            var n=ordinary[i].Split(',');var t=taskRows[i].Split(',');
            Require(n.Length==15,"ordinary gaze CSV schema changed");
            Require(n[4]==(expected[i-1]?"1":"0") && t[2]==(expected[i-1]?"1":"0"),"SDK-direct CSV validity incorrect");
            if(!expected[i-1])
            {
                for(int j=5;j<=12;j++) Require(n[j]=="","ordinary CSV kept missing/stale/nonfinite coordinates");
                for(int j=3;j<=10;j++) Require(t[j]=="","Task Switch CSV kept missing/stale/nonfinite coordinates");
            }
            else
            {
                Require(float.Parse(n[5],CultureInfo.InvariantCulture)==float.Parse(t[3],CultureInfo.InvariantCulture) &&
                    float.Parse(n[6],CultureInfo.InvariantCulture)==float.Parse(t[4],CultureInfo.InvariantCulture),
                    "CSV loggers disagree on raw game coordinates");
            }
        }
        var outside=ordinary[9].Split(',');
        Require(float.Parse(outside[6],CultureInfo.InvariantCulture)==-270,"raw off-screen coordinate was altered");
        Require(float.Parse(outside[8],CultureInfo.InvariantCulture)==0,"existing clamped coordinate column changed");
        TobiiAPI.IsConnected=false;TobiiAPI.gaze=new GazePoint();
        TobiiTrackedGameView.TestRead=null;
        Console.WriteLine("PASS: SDK-direct fresh/repeated/recovery samples, stale/NaN/infinite data blank in both real CSV loggers, raw off-screen coordinates and CSV schema preserved");
    }
}

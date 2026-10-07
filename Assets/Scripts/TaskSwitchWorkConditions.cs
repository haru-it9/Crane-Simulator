using System;
using System.Collections.Generic;
using System.Globalization;

public enum TaskSwitchTargetTaskPattern { Move1ToLiftUp, Move2ToPlace }

[Serializable]
public class TaskSwitchWorkCondition
{
    public string role;
    public int index;
    public float pickupX, pickupZ, placementX, placementZ;
    public int pickupCount, placementCount;
}

public sealed class TaskSwitchScheduleRow
{
    public int switchIndex, sourceCycle;
    public CraneStatusManager.WorkPhase sourcePhase;
    public float minimumDelaySeconds, maximumDelaySeconds;
    public TaskSwitchTargetTaskPattern targetTaskPattern;
}

// Parse the entire file before changing experiment state. Invalid rows never silently disappear.
public static class TaskSwitchConditionCsv
{
    static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    static List<Dictionary<string,string>> Rows(string text)
    {
        var rows = new List<Dictionary<string,string>>();
        string[] header = null;
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith("#")) continue;
            string[] cells = line.Split(',');
            if (header == null) { header = cells; continue; }
            if (cells.Length != header.Length) throw new FormatException("CSV column count differs from header: " + line);
            var row = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            for (int i=0;i<header.Length;i++) row.Add(header[i].Trim(),cells[i].Trim());
            rows.Add(row);
        }
        return rows;
    }
    static string Cell(Dictionary<string,string> row,string key) { string v; if (!row.TryGetValue(key,out v)) throw new FormatException("Missing CSV column: " + key); return v; }
    static int Positive(Dictionary<string,string> row,string key) { int v; if (!int.TryParse(Cell(row,key),NumberStyles.Integer,Culture,out v) || v<1) throw new FormatException("Invalid " + key);return v; }
    static float Number(Dictionary<string,string> row,string key) { float v; if (!float.TryParse(Cell(row,key),NumberStyles.Float,Culture,out v) || float.IsNaN(v) || float.IsInfinity(v)) throw new FormatException("Invalid " + key);return v; }
    public static List<TaskSwitchScheduleRow> ParseSchedule(string text,TaskSwitchTargetTaskPattern fallback,int sourceCycles)
    {
        var result = new List<TaskSwitchScheduleRow>();
        foreach (var row in Rows(text))
        {
            CraneStatusManager.WorkPhase phase;
            if (!Enum.TryParse(Cell(row,"sourcePhase"),true,out phase) || !Enum.IsDefined(typeof(CraneStatusManager.WorkPhase),phase)) throw new FormatException("Invalid sourcePhase");
            var pattern = fallback;string value;
            if (row.TryGetValue("targetTaskPattern",out value) && value.Length>0)
            {
                if (value=="1") pattern=TaskSwitchTargetTaskPattern.Move1ToLiftUp;
                else if (value=="2") pattern=TaskSwitchTargetTaskPattern.Move2ToPlace;
                else if (!Enum.TryParse(value,true,out pattern) || !Enum.IsDefined(typeof(TaskSwitchTargetTaskPattern),pattern)) throw new FormatException("Invalid targetTaskPattern");
            }
            var entry=new TaskSwitchScheduleRow { switchIndex=Positive(row,"switchIndex"),sourceCycle=Positive(row,"sourceCycle"),sourcePhase=phase,
                minimumDelaySeconds=Number(row,"minimumDelaySeconds"),maximumDelaySeconds=Number(row,"maximumDelaySeconds"),targetTaskPattern=pattern };
            if (entry.sourceCycle>sourceCycles || entry.minimumDelaySeconds<0 || entry.maximumDelaySeconds<entry.minimumDelaySeconds) throw new FormatException("Invalid switch timing/cycle");
            result.Add(entry);
        }
        result.Sort((a,b)=>a.switchIndex.CompareTo(b.switchIndex));
        for(int i=0;i<result.Count;i++) if(result[i].switchIndex!=i+1) throw new FormatException("switchIndex must be unique and consecutive from 1");
        return result;
    }
    public static List<TaskSwitchWorkCondition> ParseWork(string text,int sourceCycles)
    {
        var result=new List<TaskSwitchWorkCondition>();var keys=new HashSet<string>();
        foreach(var row in Rows(text))
        {
            string role=Cell(row,"role");
            if (role.Equals("Source",StringComparison.OrdinalIgnoreCase)) role="Source";
            else if (role.Equals("Target",StringComparison.OrdinalIgnoreCase)) role="Target";
            else throw new FormatException("role must be Source or Target");
            var entry=new TaskSwitchWorkCondition { role=role,index=Positive(row,"index"),pickupX=Number(row,"pickupX"),pickupZ=Number(row,"pickupZ"),
                placementX=Number(row,"placementX"),placementZ=Number(row,"placementZ"),pickupCount=Positive(row,"pickupCount"),placementCount=Positive(row,"placementCount") };
            if(entry.placementCount>entry.pickupCount) throw new FormatException("placementCount exceeds pickupCount");
            if(role=="Source" && (entry.index>sourceCycles || (entry.index<sourceCycles && entry.placementCount!=entry.pickupCount)))
                throw new FormatException("Source cycles before the last must place all picked boards before the next Move1");
            if(!keys.Add(role+":"+entry.index)) throw new FormatException("Duplicate work condition: " + role + ":" + entry.index);
            result.Add(entry);
        }
        for(int i=1;i<=sourceCycles;i++) if(!keys.Contains("Source:"+i)) throw new FormatException("Missing Source condition: " + i);
        return result;
    }
    public static CraneStatusManager.WorkPhase StartPhase(TaskSwitchTargetTaskPattern pattern) => pattern==TaskSwitchTargetTaskPattern.Move1ToLiftUp ? CraneStatusManager.WorkPhase.Move1 : CraneStatusManager.WorkPhase.Move2;
    public static CraneStatusManager.WorkPhase EndPhase(TaskSwitchTargetTaskPattern pattern) => pattern==TaskSwitchTargetTaskPattern.Move1ToLiftUp ? CraneStatusManager.WorkPhase.LiftUp : CraneStatusManager.WorkPhase.Place;
}

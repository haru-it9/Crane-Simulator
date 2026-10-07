using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>UTF-8 CSV with a fixed schema. Empty cells mean unavailable, never zero.</summary>
public sealed class ExperimentCsvFile : IDisposable
{
    private readonly StreamWriter writer;
    private readonly int columnCount;
    public ExperimentCsvFile(string path, string header)
    {
        columnCount = header.Split(',').Length;
        writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine(header);
    }
    public void Write(params object[] cells)
    {
        if (cells.Length != columnCount)
            throw new ArgumentException("CSV column count does not match its header");
        string[] encoded = new string[cells.Length];
        for (int i = 0; i < cells.Length; i++) encoded[i] = Encode(cells[i]);
        writer.WriteLine(string.Join(",", encoded));
    }
    public static string Encode(object value)
    {
        if (value == null) return "";
        if (value is bool) return (bool)value ? "1" : "0";
        if (value is float && (float.IsNaN((float)value) || float.IsInfinity((float)value))) return "";
        if (value is double && (double.IsNaN((double)value) || double.IsInfinity((double)value))) return "";
        string s = value is float || value is double
            ? Convert.ToDouble(value).ToString("F6", CultureInfo.InvariantCulture)
            : Convert.ToString(value, CultureInfo.InvariantCulture);
        return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
            ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
    }
    public void Flush() { writer.Flush(); }
    public void Dispose() { writer.Dispose(); }
}

/// <summary>Idempotent markers; intervals are blank unless both endpoints exist.</summary>
public sealed class TaskSwitchTimingSummary
{
    private readonly System.Collections.Generic.Dictionary<string, double> markers =
        new System.Collections.Generic.Dictionary<string, double>();
    public bool LogicalCompletion { get { return markers.ContainsKey("SwitchCompleted"); } }
    public bool SourceInput { get { return markers.ContainsKey("FirstEffectiveSourceInput"); } }
    public void Mark(string name, double seconds)
    {
        if (!markers.ContainsKey(name)) markers.Add(name, seconds);
        if (name == "TargetMajorPhaseCompleted" || name == "TargetWorkCycleCompleted" ||
            name == "TargetWorkCycleCompletedByPhase" || name == "TargetWorkCycleCompletionFallback")
            Mark("TargetCompleted", seconds);
    }
    public double At(string name) { double value; return markers.TryGetValue(name, out value) ? value : double.NaN; }
    public double Between(string start, string end)
    {
        double a = At(start), b = At(end);
        return double.IsNaN(a) || double.IsNaN(b) || b < a ? double.NaN : b - a;
    }
    public static readonly string Header =
        "request_s,source_suspended_s,target_display_s,target_confirm_s,target_unlock_s," +
        "first_target_input_s,target_completed_s,source_display_s,source_confirm_s,source_unlock_s," +
        "first_source_input_s,logical_completion_s,request_to_suspend_s,request_to_target_input_s," +
        "suspend_to_source_input_s,source_display_to_confirm_s,source_confirm_to_unlock_s," +
        "source_unlock_to_input_s,target_confirm_to_unlock_s,target_unlock_to_input_s";
    public object[] Cells()
    {
        return new object[] { At("SwitchRequested"), At("SourceWorkSuspended"), At("TargetDisplaySwitched"),
            At("ConfirmationPressed"), At("TargetInputUnlocked"), At("FirstEffectiveTargetInput"), At("TargetCompleted"),
            At("SourceDisplayRestored"), At("SourceReturnConfirmationPressed"), At("SourceInputUnlocked"),
            At("FirstEffectiveSourceInput"), At("SwitchCompleted"), Between("SwitchRequested", "SourceWorkSuspended"),
            Between("SwitchRequested", "FirstEffectiveTargetInput"), Between("SourceWorkSuspended", "FirstEffectiveSourceInput"),
            Between("SourceDisplayRestored", "SourceReturnConfirmationPressed"), Between("SourceReturnConfirmationPressed", "SourceInputUnlocked"),
            Between("SourceInputUnlocked", "FirstEffectiveSourceInput"), Between("ConfirmationPressed", "TargetInputUnlocked"),
            Between("TargetInputUnlocked", "FirstEffectiveTargetInput") };
    }
}

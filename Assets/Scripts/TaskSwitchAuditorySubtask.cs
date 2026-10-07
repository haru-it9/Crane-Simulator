using System;
using UnityEngine;

[Serializable]
public class TaskSwitchAuditorySettings
{
    [Tooltip("右＋ / 左−、中立0の専用Input Manager軸。")]
    public string pedalAxisName = "TaskSwitchPedal";
    public bool invertPedalAxis = false;
    public bool highToneUsesPositivePedal = true;
    [UnityEngine.Range(0.01f, 1f)] public float pressThreshold = 0.5f;
    [UnityEngine.Range(0f, 0.99f)] public float releaseThreshold = 0.2f;
    [Min(100f)] public float lowToneHz = 500f;
    [Min(100f)] public float highToneHz = 1000f;
    [Min(0.02f)] public float toneDurationSeconds = 0.2f;
    [UnityEngine.Range(0f, 1f)] public float volume = 0.2f;
    [Min(0.1f)] public float minimumIntervalSeconds = 3f;
    [Min(0.1f)] public float maximumIntervalSeconds = 5f;
    [Min(0.1f)] public float responseTimeoutSeconds = 2f;
    [Min(0f)] public float minimumValidReactionSeconds = 0.1f;
    [Tooltip("0は実行ごとに生成。それ以外は音種・間隔の乱数シードを固定。")]
    public int randomSeed = 0;

    public string Validate(int sampleRate)
    {
        if (string.IsNullOrWhiteSpace(pedalAxisName)) return "Pedal Axis Name is empty";
        if (!(releaseThreshold >= 0 && releaseThreshold < pressThreshold && pressThreshold <= 1)) return "Require 0 <= release < press <= 1";
        if (!(lowToneHz >= 100 && highToneHz > lowToneHz && highToneHz < sampleRate / 2.0)) return "Require 100 <= low < high < sampleRate / 2";
        if (!(toneDurationSeconds >= 0.02 && toneDurationSeconds <= responseTimeoutSeconds && volume > 0 && volume <= 1)) return "Invalid tone duration or volume";
        if (!(minimumValidReactionSeconds >= 0 && minimumValidReactionSeconds < responseTimeoutSeconds)) return "Invalid reaction time limits";
        if (!(minimumIntervalSeconds > responseTimeoutSeconds + 0.1 && maximumIntervalSeconds >= minimumIntervalSeconds)) return "Intervals must exceed response timeout + 0.1 s";
        return null;
    }
}

// Times are explicit: DSP is the audio clock, RealTime is the frame-observed common log clock.
public class TaskSwitchAuditoryEvent
{
    public string EventType, Tone = "", Outcome = "", Detail = "";
    public int TrialIndex, ExpectedSign, ResponseSign;
    public double RealTime, DspTime, ScheduledDsp = double.NaN, EstimatedOnsetReal = double.NaN;
    public double ObservedOnsetReal = double.NaN, ResponseReal = double.NaN, ReactionSeconds = double.NaN;
    public double PausedSeconds, WallReactionSeconds = double.NaN;
    public int PauseCount;
    public float RawPedal;
    public bool Presented, HeldAtOnset;
    public bool? Correct;
}

// Pure state machine: a pedal must return to neutral before another press is accepted.
public sealed class TaskSwitchAuditoryTrial
{
    private readonly TaskSwitchAuditorySettings settings;
    private int sequence, trialIndex, expected;
    private string tone;
    private double onsetDsp, onsetReal, observedReal = double.NaN;
    private bool pending, presented, held, armed;
    private bool paused;
    private double pauseStartedReal, pausedSeconds;
    private int pauseCount;
    public bool HasPendingTrial => pending;
    public bool PedalArmed => armed;
    public int LastTrialIndex => sequence;
    public event Action<TaskSwitchAuditoryEvent> EventOccurred;

    public TaskSwitchAuditoryTrial(TaskSwitchAuditorySettings settings, int firstIndex = 0)
    {
        this.settings = settings;
        sequence = firstIndex;
    }
    public void Schedule(bool high, double scheduledDsp, double estimatedReal, double dsp, double real, float raw)
    {
        if (pending) throw new InvalidOperationException("An auditory trial is already pending");
        trialIndex = ++sequence;
        tone = high ? "High" : "Low";
        expected = (high == settings.highToneUsesPositivePedal) ? 1 : -1;
        onsetDsp = scheduledDsp; onsetReal = estimatedReal;
        observedReal = double.NaN; held = presented = false; pending = true;
        pausedSeconds = 0; pauseCount = 0;
        Emit("StimulusScheduled", dsp, real, raw);
    }
    public void Tick(double dsp, double real, float raw)
    {
        if (paused) return;
        float effective = settings.invertPedalAxis ? -raw : raw;
        int sign = effective >= settings.pressThreshold ? 1 : effective <= -settings.pressThreshold ? -1 : 0;
        bool edge = armed && sign != 0;
        ObserveOnset(dsp, real, raw, effective);
        if (Math.Abs(effective) <= settings.releaseThreshold) armed = true;
        else if (sign != 0) armed = false;
        bool expired = pending && presented && dsp > onsetDsp + settings.responseTimeoutSeconds;
        if (expired) Finish("Miss", dsp, real, raw);
        if (!edge) return;
        if (!pending || !presented)
        {
            // False alarms cannot become a response to the next tone.
            Emit("PedalPressed", dsp, real, raw, sign, expired ? "LateResponse" : "FalseAlarm", null,
                expired ? dsp - onsetDsp : double.NaN, expired);
            return;
        }
        double rt = Math.Max(0, dsp - onsetDsp);
        string outcome = rt < settings.minimumValidReactionSeconds ? "TooEarly" : sign == expected ? "Correct" : "Incorrect";
        bool? correct = outcome == "TooEarly" ? (bool?)null : sign == expected;
        Emit("PedalPressed", dsp, real, raw, sign, outcome, correct, rt);
        Finish(outcome, dsp, real, raw, sign, correct, rt);
    }
    public void Cancel(string reason, double dsp, double real, float raw)
    {
        if (!pending) return;
        ObserveOnset(dsp, real, raw, settings.invertPedalAxis ? -raw : raw);
        Finish(presented ? "Interrupted" : "CancelledBeforeOnset", dsp, real, raw, detail: reason);
    }
    public void RequireNeutral() { armed = false; }
    public void Pause(double dsp, double real, float raw)
    {
        if (paused) return;
        ObserveOnset(dsp, real, raw, settings.invertPedalAxis ? -raw : raw);
        paused = true; pauseStartedReal = real;
        if (pending) { pauseCount++; Emit("TrialPaused", dsp, real, raw); }
    }
    public void Resume(double dsp, double real, float raw)
    {
        if (!paused) return;
        double duration = Math.Max(0, real - pauseStartedReal);
        paused = false;
        if (pending)
        {
            pausedSeconds += duration;
            // The DSP clock/scheduled audio is frozen by AudioListener.pause.
            if (!presented) onsetReal += duration;
            Emit("TrialResumed", dsp, real, raw);
        }
        RequireNeutral(); // Inputs made while paused cannot become a response on the first resumed frame.
    }
    private void ObserveOnset(double dsp, double real, float raw, float effective)
    {
        if (!pending || presented || dsp < onsetDsp) return;
        presented = true; observedReal = real;
        held = Math.Abs(effective) >= settings.pressThreshold && !armed;
        Emit("StimulusOnsetObserved", dsp, real, raw);
    }
    private void Finish(string outcome, double dsp, double real, float raw, int sign = 0, bool? correct = null, double rt = double.NaN, string detail = "")
    {
        Emit("TrialFinished", dsp, real, raw, sign, outcome, correct, rt, true, detail);
        pending = false;
    }
    private void Emit(string type, double dsp, double real, float raw, int sign = 0, string outcome = "", bool? correct = null, double rt = double.NaN, bool associated = true, string detail = "")
    {
        EventOccurred?.Invoke(new TaskSwitchAuditoryEvent {
            EventType = type, TrialIndex = associated ? trialIndex : 0, Tone = associated ? tone : "",
            ExpectedSign = associated ? expected : 0, ResponseSign = sign, Outcome = outcome, Correct = correct,
            RealTime = real, DspTime = dsp, RawPedal = raw, ReactionSeconds = rt,
            ScheduledDsp = associated ? onsetDsp : double.NaN, EstimatedOnsetReal = associated ? onsetReal : double.NaN,
            ObservedOnsetReal = associated ? observedReal : double.NaN, ResponseReal = sign != 0 ? real : double.NaN,
            Presented = associated && presented, HeldAtOnset = associated && held, Detail = detail,
            PauseCount = associated ? pauseCount : 0,
            PausedSeconds = associated ? pausedSeconds + (paused ? Math.Max(0, real - pauseStartedReal) : 0) : 0,
            WallReactionSeconds = associated && sign != 0 && presented ? real - onsetReal : double.NaN
        });
    }
}

using System;
using UnityEngine;

public enum TaskSwitchSecondaryTaskMode { None = 0, Auditory = 1, Visual = 2 }

[Serializable]
public class TaskSwitchVisualSettings
{
    public const float ResponseTimeoutSeconds = 2f;
    [Tooltip("右＋ / 左−、中立0の専用Input Manager軸。右の赤＝＋、左の赤＝−。")]
    public string pedalAxisName = "TaskSwitchPedal";
    [Tooltip("＋／−キーを送る足ペダルを直接受け取る。設定した軸入力も継続。")]
    public bool usePlusMinusKeys = true;
    [Tooltip("右＋の物理キー。通常はEquals。テンキー＋も受理。")]
    public KeyCode positivePedalKey = KeyCode.Equals;
    [Tooltip("左−の物理キー。通常はMinus。テンキー−も受理。")]
    public KeyCode negativePedalKey = KeyCode.Minus;
    public bool invertPedalAxis = false;
    [UnityEngine.Range(0.01f, 1f)] public float pressThreshold = 0.5f;
    [UnityEngine.Range(0f, 0.99f)] public float releaseThreshold = 0.2f;
    [Tooltip("青に戻ってから次の赤までのランダム待ち時間。赤は正しい側の押下、または提示から2秒で青に戻ります。")]
    [UnityEngine.Range(2f, 10f)] public float minimumIntervalSeconds = 5f;
    [UnityEngine.Range(2f, 10f)] public float maximumIntervalSeconds = 10f;
    [Min(0f)] public float minimumValidReactionSeconds = 0.1f;
    [Tooltip("0は有効化ごとに生成。それ以外は左右・提示間隔のシードを固定。")]
    public int randomSeed = 0;

    public TaskSwitchAuditorySettings PedalSettings()
    {
        return new TaskSwitchAuditorySettings {
            pedalAxisName = pedalAxisName, invertPedalAxis = invertPedalAxis,
            usePlusMinusKeys = usePlusMinusKeys, positivePedalKey = positivePedalKey, negativePedalKey = negativePedalKey,
            pressThreshold = pressThreshold, releaseThreshold = releaseThreshold,
            minimumIntervalSeconds = minimumIntervalSeconds, maximumIntervalSeconds = maximumIntervalSeconds,
            responseTimeoutSeconds = ResponseTimeoutSeconds,
            minimumValidReactionSeconds = minimumValidReactionSeconds,
            randomSeed = randomSeed
        };
    }
    public string Validate()
    {
        string error = PedalSettings().ValidatePedalAndTiming(false);
        if (error != null) return error;
        if (minimumValidReactionSeconds >= ResponseTimeoutSeconds) return "Minimum reaction time must be less than the 2-second response deadline";
        return minimumIntervalSeconds >= 2f && maximumIntervalSeconds <= 10f ? null : "Visual blue-to-red intervals must be within 2 to 10 seconds";
    }
}

using System;
using UnityEngine;

public enum TaskSwitchSecondaryTaskMode { None = 0, Auditory = 1, Visual = 2 }

[Serializable]
public class TaskSwitchVisualSettings
{
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
    [Tooltip("青に戻ってから次の赤までのランダム待ち時間。正しい側の押下までは赤を保持し、時間制限なし。")]
    [UnityEngine.Range(2f, 5f)] public float minimumIntervalSeconds = 2f;
    [UnityEngine.Range(2f, 5f)] public float maximumIntervalSeconds = 5f;
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
            minimumValidReactionSeconds = minimumValidReactionSeconds,
            randomSeed = randomSeed
        };
    }
    public string Validate()
    {
        string error = PedalSettings().ValidatePedalAndTiming(false);
        if (error != null) return error;
        return minimumIntervalSeconds >= 2f && maximumIntervalSeconds <= 5f ? null : "Visual blue-to-red intervals must be within 2 to 5 seconds";
    }
}

using System;
using UnityEngine;

public enum TaskSwitchSecondaryTaskMode { None = 0, Auditory = 1, Visual = 2 }

[Serializable]
public class TaskSwitchVisualSettings
{
    [Tooltip("右＋ / 左−、中立0の専用Input Manager軸。右の赤＝＋、左の赤＝−。")]
    public string pedalAxisName = "TaskSwitchPedal";
    public bool invertPedalAxis = false;
    [UnityEngine.Range(0.01f, 1f)] public float pressThreshold = 0.5f;
    [UnityEngine.Range(0f, 0.99f)] public float releaseThreshold = 0.2f;
    [Min(0.1f)] public float minimumIntervalSeconds = 3f;
    [Min(0.1f)] public float maximumIntervalSeconds = 5f;
    [Min(0.1f)] public float responseTimeoutSeconds = 2f;
    [Min(0f)] public float minimumValidReactionSeconds = 0.1f;
    [Tooltip("0は有効化ごとに生成。それ以外は左右・提示間隔のシードを固定。")]
    public int randomSeed = 0;

    public TaskSwitchAuditorySettings PedalSettings()
    {
        return new TaskSwitchAuditorySettings {
            pedalAxisName = pedalAxisName, invertPedalAxis = invertPedalAxis,
            pressThreshold = pressThreshold, releaseThreshold = releaseThreshold,
            minimumIntervalSeconds = minimumIntervalSeconds, maximumIntervalSeconds = maximumIntervalSeconds,
            responseTimeoutSeconds = responseTimeoutSeconds, minimumValidReactionSeconds = minimumValidReactionSeconds,
            randomSeed = randomSeed
        };
    }
}

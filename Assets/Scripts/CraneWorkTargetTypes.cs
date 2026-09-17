using System;
using UnityEngine;

public enum CraneWorkTargetKind
{
    Unknown,
    Pickup,
    NormalPlacement,
    Trailer
}

public enum CraneWorkTargetSource
{
    Unknown,
    AutomaticSchematic,
    ExperimentCondition,
    Manual
}

public enum CraneWorkTargetXSelection
{
    Random,
    First,
    Second
}

[Serializable]
public struct CraneWorkTargetData
{
    public bool isValid;
    public int craneIndex;
    public int pointIndex;
    public float targetX;
    public float targetZ;
    public CraneWorkTargetKind targetKind;
    public CraneWorkTargetSource source;
    public bool isFixed;
}

/// <summary>
/// 既存のCraneSchematicDisplayで使用してきたPoint座標変換を、
/// UI・実験モード・作業判定から共用できる形にまとめます。
/// </summary>
public static class CraneWorkCoordinateUtility
{
    private static readonly float[] BasePointTargetZValues =
    {
        19f, 10f, 8f, 6f, 4f, 2f, 0f,
        -2f, -4f, -6f, -8f, -10f, -15f
    };

    public const int CranesPerGroup = 6;
    public const float CraneXInterval = 20f;
    public const float CraneGroupZInterval = 200f;
    public const float FirstXCandidate = -4f;
    public const float SecondXCandidate = 4f;

    public static int PointCount => BasePointTargetZValues.Length;

    public static bool TryGetBasePointZ(
        int pointIndex,
        out float basePointZ
    )
    {
        if (pointIndex < 0 || pointIndex >= PointCount)
        {
            basePointZ = 0f;
            return false;
        }

        basePointZ = BasePointTargetZValues[pointIndex];
        return true;
    }

    public static float GetCraneGroupZOffset(int craneIndex)
    {
        int validCraneIndex = Mathf.Max(0, craneIndex);
        int craneGroupIndex = validCraneIndex / CranesPerGroup;
        return craneGroupIndex * CraneGroupZInterval;
    }

    public static bool TryCreatePointTarget(
        int craneIndex,
        int pointIndex,
        CraneWorkTargetXSelection xSelection,
        out float targetX,
        out float targetZ
    )
    {
        targetX = 0f;
        targetZ = 0f;

        if (craneIndex < 0 ||
            !TryGetBasePointZ(pointIndex, out float basePointZ))
        {
            return false;
        }

        int craneGroupIndex = craneIndex / CranesPerGroup;
        int craneIndexWithinGroup = craneIndex % CranesPerGroup;

        float selectedBaseX;

        switch (xSelection)
        {
            case CraneWorkTargetXSelection.First:
                selectedBaseX = FirstXCandidate;
                break;
            case CraneWorkTargetXSelection.Second:
                selectedBaseX = SecondXCandidate;
                break;
            case CraneWorkTargetXSelection.Random:
            default:
                selectedBaseX = UnityEngine.Random.Range(0, 2) == 0
                    ? FirstXCandidate
                    : SecondXCandidate;
                break;
        }

        targetX =
            selectedBaseX +
            craneIndexWithinGroup * CraneXInterval;

        targetZ =
            basePointZ +
            craneGroupIndex * CraneGroupZInterval;

        return true;
    }
}

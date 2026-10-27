using UnityEngine;

/// <summary>Side-effect-free desired movement calculation, in CraneUnit command axes.</summary>
public static class TaskSwitchSupportGuidance
{
    /// <summary>Pickup cue only: 10 A per target tonne, with an inclusive +10 A allowance.</summary>
    public static string PickupCurrentGuidance(float targetWeightKg, float currentAmpere)
    {
        if (!IsFinite(targetWeightKg) || targetWeightKg <= 0f ||
            !IsFinite(currentAmpere) || currentAmpere < 0f)
            return "CurrentTargetUnavailable";
        float minimumAmpere = targetWeightKg / 1000f * 10f;
        if (currentAmpere < minimumAmpere) return "IncreaseCurrent";
        if (currentAmpere > minimumAmpere + 10f) return "DecreaseCurrent";
        return "ReadyToRaise";
    }

    /// <summary>One glyph per distance interval, capped; unknown distance shows direction only.</summary>
    public static int ChevronCount(float remainingDistance, float distancePerChevron, int maximum)
    {
        maximum = Mathf.Clamp(maximum, 1, 3);
        if (!IsFinite(remainingDistance) || remainingDistance < 0f) return 1;
        if (!IsFinite(distancePerChevron) || distancePerChevron <= 0f) distancePerChevron = 1f;
        // Clamp before converting to int so very large distances cannot overflow.
        return Mathf.Max(1, Mathf.CeilToInt(Mathf.Min(maximum, remainingDistance / distancePerChevron)));
    }

    public static Vector3 RemainingTravelDistance(CraneWorkStepDefinition step,
        Vector3 current, bool hasGoal, float goalX, float goalZ)
    {
        if (step == null || step.completionConditions == null || !hasGoal ||
            !IsFinite(goalX) || !IsFinite(goalZ) || !IsFinite(current.x) || !IsFinite(current.z))
            return Vector3.zero;
        foreach (CraneWorkConditionDefinition condition in step.completionConditions)
        {
            if (condition == null || condition.conditionType != CraneWorkConditionType.PositionWithinTarget)
                continue;
            return new Vector3(
                Mathf.Max(0f, Mathf.Abs(goalX - current.x) - Mathf.Max(0f, condition.threshold)), 0f,
                Mathf.Max(0f, Mathf.Abs(goalZ - current.z) - Mathf.Max(0f, condition.secondaryThreshold)));
        }
        return Vector3.zero;
    }

    public static bool TryGetRaisingTarget(CraneWorkStepDefinition step, out float targetLocalY)
    {
        targetLocalY = 0f;
        if (step == null || step.completionConditions == null) return false;
        foreach (CraneWorkConditionDefinition condition in step.completionConditions)
        {
            if (condition == null || condition.conditionType != CraneWorkConditionType.MainLifMagLocalYAtLeast ||
                !IsFinite(condition.threshold)) continue;
            targetLocalY = condition.threshold;
            return true;
        }
        return false;
    }

    public static string PhaseLabel(CraneStatusManager.WorkPhase phase)
    {
        switch (phase)
        {
            case CraneStatusManager.WorkPhase.Move1:
            case CraneStatusManager.WorkPhase.Move2: return "移動";
            case CraneStatusManager.WorkPhase.LiftUp: return "吊り上げ";
            case CraneStatusManager.WorkPhase.Place:
            case CraneStatusManager.WorkPhase.PlaceToTrack: return "配置";
            default: return string.Empty;
        }
    }

    public static Vector3 RequiredMovement(
        CraneWorkStepDefinition step, Vector3 current,
        bool hasGoal, float goalX, float goalZ,
        bool loweringBlocked, bool hasLocalY, float localY)
    {
        if (step == null || step.completionConditions == null) return Vector3.zero;
        CraneWorkConditionDefinition position = null, height = null;
        bool touchdown = false;
        foreach (CraneWorkConditionDefinition condition in step.completionConditions)
        {
            if (condition == null) continue;
            if (condition.conditionType == CraneWorkConditionType.PositionWithinTarget)
                position = condition;
            if (condition.conditionType == CraneWorkConditionType.MainLifMagLocalYAtLeast)
                height = condition;
            if (condition.conditionType == CraneWorkConditionType.TouchdownObserved)
                touchdown = true;
        }

        // Positional alignment precedes lowering. Never recommend travel and lift together.
        if (position != null)
        {
            if (!hasGoal || !IsFinite(goalX) || !IsFinite(goalZ) ||
                !IsFinite(current.x) || !IsFinite(current.z)) return Vector3.zero;
            Vector3 movement = new Vector3(
                Direction(goalX - current.x, position.threshold), 0f,
                Direction(goalZ - current.z, position.secondaryThreshold));
            if (movement.x != 0f || movement.z != 0f) return movement;
        }

        if (touchdown && !loweringBlocked) return new Vector3(0f, -1f, 0f);
        if (height != null && hasLocalY && IsFinite(localY) &&
            localY < height.threshold) return new Vector3(0f, 1f, 0f);

        // Weight acquisition/release, speed/stability waits and completed axes need no movement.
        return Vector3.zero;
    }

    private static int Direction(float error, float tolerance)
    {
        if (!IsFinite(error) || !IsFinite(tolerance)) return 0;
        if (Mathf.Abs(error) <= Mathf.Max(0f, tolerance)) return 0;
        return error > 0f ? 1 : -1;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

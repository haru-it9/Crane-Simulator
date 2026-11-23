using System;
using UnityEngine;

public partial class TaskSwitchExperimentManager
{
    private int previousHorizontalAxes = -1;

    private const float PilotTimingStartLocalY = -1.25f;

    private bool SetPilotTimingStartPose(TaskSwitchCraneCondition condition, CraneInstance crane)
    {
        bool source = condition == sourceCondition;
        TaskSwitchWorkCondition work = source ? SourceWorkCondition(1) : activeTargetWorkCondition;
        if (work == null) return false;

        bool startsWithMovement = source || !PilotLiftOnly;
        float x = startsWithMovement ? (work.pickupX + work.placementX) * 0.5f : work.pickupX;
        float z = startsWithMovement ? work.placementZ : work.pickupZ;
        if (!crane.CraneUnit.TryAlignInterventionPickup(crane.InformationTarget, x, z, PilotTimingStartLocalY))
        {
            Debug.LogError($"予備検証の開始姿勢を設定できません。Role={work.role};Index={work.index};X={x};Z={z}", this);
            return false;
        }

        EmitEvent("PilotTimingStartPose", FormattableString.Invariant(
            $"Role={work.role};Index={work.index};X={x};Z={z};LocalY={PilotTimingStartLocalY}"));
        return true;
    }

    private bool IsWaitingForPilotTimingBoundary =>
        currentState == TaskSwitchExperimentState.WaitingForPhaseBoundary &&
        (switchMethod == TaskSwitchMethod.DetailedBoundaryOperatorInitiated ||
         switchMethod == TaskSwitchMethod.SingleAxisBoundary);

    private void StartPilotTimingBoundary()
    {
        previousHorizontalAxes = ReadSourceHorizontalAxes();
        bool singleAxisPolicy = switchMethod == TaskSwitchMethod.SingleAxisBoundary;
        if (singleAxisPolicy && SourceNeedsSingleAxis(previousHorizontalAxes))
        {
            EmitEvent("PilotSingleAxisAtRequest", $"Step={sourceWorkPhaseTracker.CurrentStepId};HorizontalAxes={previousHorizontalAxes}");
            StartOperatorDeadlineSwitch();
            return;
        }

        if (pilotSourceExtensionCompleted)
        {
            SwitchAtPilotSourceEnd();
            return;
        }

        if (!singleAxisPolicy)
            sourceCycleController.RequestHoldAfterCurrentPhase();

        advanceNoticeActive = true;
        advanceNoticeAllowsEarlySwitch = !singleAxisPolicy;
        SetState(TaskSwitchExperimentState.WaitingForPhaseBoundary);
        ShowAdvanceNoticeText("作業の区切りで\n切替");
        SetPanelActive(countdownPanel, true);
        SetPanelActive(confirmationPanel, advanceNoticeAllowsEarlySwitch);
        EmitEvent("PhaseBoundaryAdvanceNoticeShown", "AllowEarlySwitch=" + advanceNoticeAllowsEarlySwitch);
        if (advanceNoticeAllowsEarlySwitch)
            EmitEvent("OperatorSwitchAvailable", "UntilDetailedBoundary");
    }

    private void SwitchAtPilotSourceEnd()
    {
        EmitEvent("SwitchTriggerSelected", "Trigger=SourceWorkEnded;Policy=" + switchMethod);
        SwitchControlToTarget("SourceWorkEnded");
    }

    private bool TryCompleteHorizontalAxisBoundary()
    {
        if (!IsWaitingForPilotTimingBoundary || sourceWorkPhaseTracker == null ||
            !sourceWorkPhaseTracker.IsMonitoring) return false;

        int axes = ReadSourceHorizontalAxes();
        bool reached = previousHorizontalAxes == 2 && axes == 1;
        previousHorizontalAxes = axes;
        if (!reached) return false;

        CompletePhaseBoundarySwitch($"HorizontalAxes:2->1;Step={sourceWorkPhaseTracker.CurrentStepId}");
        return true;
    }

    private void HandleSourceDetailBoundary(CraneWorkPhaseTracker tracker, string previousStep, string nextStep)
    {
        if (!IsWaitingForPilotTimingBoundary || tracker != sourceWorkPhaseTracker ||
            ExperimentPauseManager.IsPaused) return;

        bool startsCurrentControl = nextStep == "LiftUp.LoadAcquisition" ||
            nextStep == "Place.LoadRelease" || nextStep == "PlaceToTrack.LoadRelease";
        if (IsVerticalStep(nextStep) ||
            (switchMethod == TaskSwitchMethod.DetailedBoundaryOperatorInitiated && startsCurrentControl))
        {
            CompletePhaseBoundarySwitch($"Detail:{previousStep}->{nextStep}");
        }
    }

    private bool SourceNeedsSingleAxis(int horizontalAxes) =>
        sourceWorkPhaseTracker != null && sourceWorkPhaseTracker.IsMonitoring &&
        (horizontalAxes == 1 || IsVerticalStep(sourceWorkPhaseTracker.CurrentStepId));

    private int ReadSourceHorizontalAxes()
    {
        if (sourceWorkPhaseTracker == null || !sourceWorkPhaseTracker.IsMonitoring) return -1;
        CraneStatusManager.WorkPhase phase = sourceWorkPhaseTracker.CurrentMajorPhase;
        if (phase != CraneStatusManager.WorkPhase.Move1 && phase != CraneStatusManager.WorkPhase.Move2) return -1;

        Transform position = craneOperationManager.CurrentCraneInstance?.InformationTarget;
        if (position == null || !sourceWorkPhaseTracker.TryGetTargetPosition(out float targetX, out float targetZ))
            return -1;

        float tolerance = craneOperationManager.CurrentInformationDisplay?.FineAlignTolerance ?? 0.05f;
        return (Mathf.Abs(position.position.x - targetX) > tolerance ? 1 : 0) +
            (Mathf.Abs(position.position.z - targetZ) > tolerance ? 1 : 0);
    }

    private static bool IsVerticalStep(string step) =>
        step == "LiftUp.PickupLowering" || step == "LiftUp.LoadedRaising" ||
        step == "Place.PlacementLowering" || step == "Place.PostPlacementRaising" ||
        step == "PlaceToTrack.PlacementLowering" || step == "PlaceToTrack.PostPlacementRaising";
}

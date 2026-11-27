using System;
using UnityEngine;

public partial class TaskSwitchExperimentManager
{
    private int previousHorizontalAxes = -1;

    private const float PilotTimingStartLocalY = -1.56f;
    private CraneOperationManager mainStudyMovementInputSource;
    private string mainStudyDeferredSwitchDetail;
    private bool mainStudyDeferredOperatorConfirmation;

    private bool MainStudyUsesMovementAnchor => IsMainStudy &&
        (switchMethod == TaskSwitchMethod.Immediate || switchMethod == TaskSwitchMethod.CountdownOperatorInitiated ||
         switchMethod == TaskSwitchMethod.OperatorInitiated ||
         switchMethod == TaskSwitchMethod.SingleAxisBoundary);

    private int MainStudyPlannedAxes(int window)
    {
        if (switchMethod == TaskSwitchMethod.Immediate || switchMethod == TaskSwitchMethod.CountdownOperatorInitiated) return 2;
        if (switchMethod != TaskSwitchMethod.SingleAxisBoundary) return -1;
        return window == 1 || window == 2 || window == 5 ? 1 : 2;
    }

    private float GetMainStudyMovementDelay(int window, int longImmediateWindow, float fraction)
    {
        if (switchMethod == TaskSwitchMethod.Immediate)
            return window == longImmediateWindow ? 7f : Mathf.Lerp(2f, 3f, fraction);
        if (switchMethod == TaskSwitchMethod.CountdownOperatorInitiated)
            return window == FindLongestMainStudyMovementWindow() ? 6f : Mathf.Lerp(2.5f, 4f, fraction);
        if (switchMethod == TaskSwitchMethod.OperatorInitiated)
            return Mathf.Lerp(2f, 10f, fraction);
        return MainStudyPlannedAxes(window) == 1 ? Mathf.Lerp(1f, 2f, fraction) : Mathf.Lerp(1.5f, 2f, fraction);
    }

    private int FindLongestMainStudyMovementWindow()
    {
        int longestWindow = -1;
        float longestTravel = -1f;
        for (int cycle = 1; cycle <= PilotRequiredSourceCycles; cycle++)
        {
            var work = SourceWorkCondition(cycle);
            if (work == null) continue;
            var previous = SourceWorkCondition(cycle - 1);
            float move1 = Mathf.Abs(work.pickupZ - (previous != null ? previous.placementZ : work.placementZ));
            float move2 = Mathf.Abs(work.placementZ - work.pickupZ);
            if (move1 > longestTravel) { longestTravel = move1; longestWindow = (cycle - 1) * 2; }
            if (move2 > longestTravel) { longestTravel = move2; longestWindow = (cycle - 1) * 2 + 1; }
        }
        return longestWindow;
    }

    private void SubscribeMainStudyMovementInput()
    {
        if (mainStudyMovementInputSource == craneOperationManager) return;
        UnsubscribeMainStudyMovementInput();
        mainStudyMovementInputSource = craneOperationManager;
        if (mainStudyMovementInputSource != null)
            mainStudyMovementInputSource.MovementInputAccepted += HandleMainStudyMovementInput;
    }

    private void UnsubscribeMainStudyMovementInput()
    {
        if (mainStudyMovementInputSource != null)
            mainStudyMovementInputSource.MovementInputAccepted -= HandleMainStudyMovementInput;
        mainStudyMovementInputSource = null;
    }

    private void HandleMainStudyMovementInput(CraneOperationManager manager, Vector3 movement, float spread)
    {
        if (!MainStudyUsesMovementAnchor || !CanRequestSwitch ||
            manager.CurrentCraneIndex != sourceCondition.craneIndex || sourceCycleController == null ||
            sourceWorkPhaseTracker == null || !sourceWorkPhaseTracker.IsMonitoring ||
            sourceWorkPhaseTracker.IsMajorPhaseCompleted || (movement.x == 0f && movement.z == 0f)) return;

        var phase = sourceWorkPhaseTracker.CurrentMajorPhase;
        if (phase != CraneStatusManager.WorkPhase.Move1 && phase != CraneStatusManager.WorkPhase.Move2) return;
        int cycle = sourceCycleController.CurrentCycleNumber;
        int window = (cycle - 1) * 2 + (phase == CraneStatusManager.WorkPhase.Move1 ? 0 : 1);
        if (window != completedSwitchCount || pilotRequestWindowStarts[window] >= 0) return;

        if (switchMethod == TaskSwitchMethod.SingleAxisBoundary)
        {
            int plannedAxes = MainStudyPlannedAxes(window);
            if (ReadSourceHorizontalAxes() != plannedAxes ||
                (plannedAxes == 1 && movement.x != 0f && movement.z != 0f)) return;
        }

        RecordPilotRequestAnchor(window, cycle, "AcceptedHorizontalMovement:" + sourceWorkPhaseTracker.CurrentStepId);
    }

    private bool UpdateMainStudyDeferredSwitch()
    {
        if (mainStudyDeferredSwitchDetail == null) return false;
        if (IsCurrentControlStep(sourceWorkPhaseTracker?.CurrentStepId)) return true;

        string detail = mainStudyDeferredSwitchDetail;
        bool confirm = mainStudyDeferredOperatorConfirmation;
        mainStudyDeferredSwitchDetail = null;
        mainStudyDeferredOperatorConfirmation = false;
        SwitchControlToTarget(detail);
        if (confirm && currentState == TaskSwitchExperimentState.WaitingForConfirmation) ConfirmTargetTask();
        return true;
    }

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
        if (IsMainStudy && mainStudyHeldSlot == completedSwitchCount)
        {
            EmitEvent("SwitchTriggerSelected", "Trigger=WindowEnd;Policy=" + switchMethod);
            SwitchControlToTarget("MainStudyWindowEnd");
            return;
        }

        bool singleAxisPolicy = switchMethod == TaskSwitchMethod.SingleAxisBoundary;
        previousHorizontalAxes = ReadSourceHorizontalAxes(requireNeutralInput: true);
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
        advanceNoticeAllowsEarlySwitch = true;
        SetState(TaskSwitchExperimentState.WaitingForPhaseBoundary);
        ShowAdvanceNoticeText(singleAxisPolicy ? "横行or走行\n完了後に切替" : "作業の区切りで\n切替", singleAxisPolicy);
        SetPanelActive(countdownPanel, true);
        SetPanelActive(confirmationPanel, true);
        EmitEvent("PhaseBoundaryAdvanceNoticeShown", "AllowEarlySwitch=True");
        EmitEvent("OperatorSwitchAvailable", singleAxisPolicy ? "UntilSingleAxisBoundary" : "UntilDetailedBoundary");
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

        int axes = ReadSourceHorizontalAxes(requireNeutralInput: true);
        bool reached = previousHorizontalAxes == 2 && axes >= 0 && axes <= 1;
        previousHorizontalAxes = axes;
        if (!reached) return false;

        CompletePhaseBoundarySwitch($"HorizontalAxes:2->{axes};Step={sourceWorkPhaseTracker.CurrentStepId}");
        return true;
    }

    private void HandleSourceDetailBoundary(CraneWorkPhaseTracker tracker, string previousStep, string nextStep)
    {
        if (!IsWaitingForPilotTimingBoundary || tracker != sourceWorkPhaseTracker ||
            ExperimentPauseManager.IsPaused) return;

        bool detailedBoundaryPolicy = switchMethod == TaskSwitchMethod.DetailedBoundaryOperatorInitiated;
        if (detailedBoundaryPolicy && IsCurrentControlStep(previousStep) && IsVerticalStep(nextStep))
            return;

        if (IsVerticalStep(nextStep) || (detailedBoundaryPolicy && IsCurrentControlStep(nextStep)))
        {
            CompletePhaseBoundarySwitch($"Detail:{previousStep}->{nextStep}");
        }
    }

    private bool SourceNeedsSingleAxis(int horizontalAxes) =>
        sourceWorkPhaseTracker != null && sourceWorkPhaseTracker.IsMonitoring &&
        (horizontalAxes == 1 || IsVerticalStep(sourceWorkPhaseTracker.CurrentStepId));

    private int ReadSourceHorizontalAxes(bool requireNeutralInput = false)
    {
        if (sourceWorkPhaseTracker == null || !sourceWorkPhaseTracker.IsMonitoring) return -1;
        CraneStatusManager.WorkPhase phase = sourceWorkPhaseTracker.CurrentMajorPhase;
        if (phase != CraneStatusManager.WorkPhase.Move1 && phase != CraneStatusManager.WorkPhase.Move2) return -1;

        Transform position = craneOperationManager.CurrentCraneInstance?.InformationTarget;
        if (position == null || !sourceWorkPhaseTracker.TryGetTargetPosition(out float targetX, out float targetZ))
            return -1;

        float tolerance = craneOperationManager.CurrentInformationDisplay?.FineAlignTolerance ?? 0.05f;
        // Read the dead-zone-filtered command used by final movement alignment.
        Vector3 command = requireNeutralInput ? craneOperationManager.ReadMovementCommand() : Vector3.zero;
        return (Mathf.Abs(position.position.x - targetX) > tolerance || command.x != 0f ? 1 : 0) +
            (Mathf.Abs(position.position.z - targetZ) > tolerance || command.z != 0f ? 1 : 0);
    }

    private static bool IsCurrentControlStep(string step) =>
        step == "LiftUp.LoadAcquisition" || step == "Place.LoadRelease" || step == "PlaceToTrack.LoadRelease";

    private static bool IsVerticalStep(string step) =>
        step == "LiftUp.PickupLowering" || step == "LiftUp.LoadedRaising" ||
        step == "Place.PlacementLowering" || step == "Place.PostPlacementRaising" ||
        step == "PlaceToTrack.PlacementLowering" || step == "PlaceToTrack.PostPlacementRaising";
}

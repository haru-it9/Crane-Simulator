#!/usr/bin/env bash
set -eu
root="$(cd "$(dirname "$0")/../.." && pwd)"
output="$(mktemp -d)"
trap 'rm -rf "$output"' EXIT
"${MCS:-mcs}" -out:"$output/test.exe" "$root/Assets/Scripts/TaskSwitchExperimentTypes.cs" \
    "$root/Assets/Scripts/TaskSwitchWorkConditions.cs" "$root/Assets/Scripts/TaskSwitchExperimentManager.cs" \
    "$root/Assets/Scripts/TaskSwitchExperimentManager.WorkConditions.cs" "$root/Assets/Scripts/CraneWorkTargetTypes.cs" \
    "$root/Assets/Scripts/CraneWorkCycleController.cs" "$root/Assets/Scripts/CraneWorkLoadPlanManager.cs" \
    "$root/Tests/TaskSwitchFlow/UnityFlowStubs.cs" "$root/Tests/TaskSwitchFlow/FlowTests.cs"
"${MONO:-mono}" "$output/test.exe"
python3 "$root/Tests/TaskSwitchFlow/prepare_board_selection_test.py" "$output"
"${MCS:-mcs}" -out:"$output/boards.exe" "$output/BoardUnityStubs.cs" "$output/BoardSelectionProduction.cs" "$root/Tests/TaskSwitchFlow/BoardSelectionTests.cs"
"${MONO:-mono}" "$output/boards.exe"
python3 "$root/Tests/TaskSwitchFlow/prepare_lifmag_test.py" "$output"
"${MCS:-mcs}" -out:"$output/lifmag.exe" "$output/LifMagUnityStubs.cs" "$output/CraneAttachmentProduction.cs" "$output/ScenarioSetupProduction.cs" "$root/Assets/Scripts/LifMagSystem.cs" "$root/Tests/TaskSwitchFlow/LifMagTests.cs"
"${MONO:-mono}" "$output/lifmag.exe"
python3 "$root/Tests/TaskSwitchFlow/check_templates.py"

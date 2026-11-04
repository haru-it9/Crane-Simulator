#!/usr/bin/env bash
set -eu
root="$(cd "$(dirname "$0")/../.." && pwd)"
output="$(mktemp -d)"
trap 'rm -rf "$output"' EXIT
python3 "$root/Tests/TaskSwitchFlow/prepare_weight_display_test.py" "$output"
"${MCS:-mcs}" -out:"$output/weights.exe" \
    "$root/Assets/Scripts/TaskSwitchExperimentTypes.cs" "$root/Assets/Scripts/TaskSwitchWorkConditions.cs" \
    "$root/Assets/Scripts/CraneWorkTargetTypes.cs" "$root/Assets/Scripts/CraneWorkCycleController.cs" \
    "$root/Assets/Scripts/CraneWorkLoadPlanManager.cs" "$root/Assets/Scripts/TargetInformationDisplay.cs" \
    "$output/WeightUiStubs.cs" "$output/WeightUiProduction.cs" "$root/Tests/TaskSwitchFlow/WeightTargetTests.cs"
"${MONO:-mono}" "$output/weights.exe" "$root"

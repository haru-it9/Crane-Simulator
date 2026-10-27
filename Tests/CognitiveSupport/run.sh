#!/usr/bin/env bash
set -eu
root="$(cd "$(dirname "$0")/../.." && pwd)"
output="$(mktemp -d)"
trap 'rm -rf "$output"' EXIT
python3 "$root/Tests/CognitiveSupport/prepare_profile_test.py" "$output"
"${MCS:-mcs}" -out:"$output/support.exe" \
    "$root/Assets/Scripts/CraneWorkPhaseTypes.cs" \
    "$root/Assets/Scripts/CraneWorkPhaseProfile.cs" \
    "$root/Assets/Scripts/TaskSwitchExperimentTypes.cs" \
    "$root/Assets/Scripts/TaskSwitchSupportGuidance.cs" \
    "$root/Assets/Scripts/TaskSwitchSupportArrow.cs" \
    "$root/Assets/Scripts/TaskSwitchCognitiveSupport.cs" \
    "$root/Assets/Scripts/LifMagAccumValueText.cs" \
    "$output/ProfileLookupProduction.cs" \
    "$root/Tests/CognitiveSupport/SupportStubs.cs" \
    "$root/Tests/CognitiveSupport/SupportTests.cs"
"${MONO:-mono}" "$output/support.exe"
python3 "$root/Tests/CognitiveSupport/prepare_distance_test.py" "$output"
"${MCS:-mcs}" -out:"$output/distances.exe" "$output/DistanceProduction.cs" "$root/Tests/CognitiveSupport/DistanceProbeTests.cs"
"${MONO:-mono}" "$output/distances.exe"
python3 "$root/Tests/CognitiveSupport/check_scene.py"

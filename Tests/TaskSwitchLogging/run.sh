#!/usr/bin/env bash
set -eu
root="$(cd "$(dirname "$0")/../.." && pwd)"
output="$(mktemp -d)"
trap 'rm -rf "$output"' EXIT
"${MCS:-mcs}" -out:"$output/test.exe" "$root/Assets/Scripts/ExperimentCsvFile.cs" \
    "$root/Assets/Scripts/TaskSwitchExperimentTypes.cs" "$root/Assets/Scripts/TaskSwitchExperimentCsvLogger.cs" \
    "$root/Assets/Scripts/TaskSwitchExperimentCsvLogger.Session.cs" \
    "$root/Assets/Scripts/TaskSwitchAuditorySubtask.cs" \
    "$root/Assets/Scripts/TaskSwitchExperimentManager.AuditorySubtask.cs" \
    "$root/Assets/Scripts/TaskSwitchExperimentCsvLogger.AuditorySubtask.cs" \
    "$root/Assets/Scripts/ExperimentPauseManager.cs" "$root/Assets/Scripts/TaskSwitchExperimentCsvLogger.Pause.cs" \
    "$root/Assets/Scripts/TobiiDebugCheck.cs" \
    "$root/Tests/TaskSwitchLogging/PauseTests.cs" \
    "$root/Tests/TaskSwitchLogging/AuditorySubtaskTests.cs" \
    "$root/Tests/TaskSwitchLogging/UnityStubs.cs" "$root/Tests/TaskSwitchLogging/LoggingTests.cs"
"${MONO:-mono}" "$output/test.exe" "$output/data"
python3 "$root/Tests/TaskSwitchLogging/check_csv.py" "$output/data"

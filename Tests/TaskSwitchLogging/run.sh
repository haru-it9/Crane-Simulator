#!/usr/bin/env bash
set -eu
root="$(cd "$(dirname "$0")/../.." && pwd)"
output="$(mktemp -d)"
trap 'rm -rf "$output"' EXIT
"${MCS:-mcs}" -out:"$output/test.exe" "$root/Assets/Scripts/ExperimentCsvFile.cs" \
    "$root/Assets/Scripts/TaskSwitchExperimentTypes.cs" "$root/Assets/Scripts/TaskSwitchExperimentCsvLogger.cs" \
    "$root/Assets/Scripts/TaskSwitchExperimentCsvLogger.Session.cs" \
    "$root/Tests/TaskSwitchLogging/UnityStubs.cs" "$root/Tests/TaskSwitchLogging/LoggingTests.cs"
"${MONO:-mono}" "$output/test.exe" "$output/data"
python3 "$root/Tests/TaskSwitchLogging/check_csv.py" "$output/data"

#!/usr/bin/env bash
# Re-applies the Quest project setup headlessly: Android target, OpenXR + Meta XR
# feature set, URP, passthrough scene, then Meta's Project Setup Tool "Fix All".
# Safe to re-run after a package upgrade.
#
# Usage: scripts/setup-quest-project.sh
# Log: Builds/logs/setup-quest-project.log
# Report of remaining Project Setup Tool items: Builds/logs/project-setup-report.json
set -euo pipefail

# shellcheck source-path=SCRIPTDIR source=lib/unity-common.sh
source "$(dirname "$0")/lib/unity-common.sh"

log_file="$LOG_DIR/setup-quest-project.log"
report_file="$LOG_DIR/project-setup-report.json"

require_unity_editor
require_project_closed
mkdir -p "$LOG_DIR"

echo "Applying Quest project setup (log: $log_file)"
status=0
# No -quit: the setup method exits itself after the asynchronous Fix All finishes.
"$UNITY_EDITOR" -batchmode -nographics \
    -projectPath "$(to_native_path "$PROJECT_DIR")" \
    -buildTarget Android \
    -executeMethod VrFencing.EditorTools.QuestProjectSetup.ApplyInBatchmode \
    -reportFile "$(to_native_path "$report_file")" \
    -logFile "$(to_native_path "$log_file")" || status=$?

if [[ $status -ne 0 ]]; then
    echo "error: setup failed (exit $status). See $log_file" >&2
    explain_licence_failure "$log_file"
    exit 1
fi
echo "Done. Project Setup Tool report: $report_file"

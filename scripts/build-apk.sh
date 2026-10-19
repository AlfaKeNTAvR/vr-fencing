#!/usr/bin/env bash
# Builds a Development APK of the Unity project headlessly.
#
# Usage: scripts/build-apk.sh [output.apk]
# Default output: Builds/Android/vr-fencing-dev.apk (gitignored).
# Log: Builds/logs/build-apk.log. Exit code is non-zero when the build fails.
set -euo pipefail

# shellcheck source-path=SCRIPTDIR source=lib/unity-common.sh
source "$(dirname "$0")/lib/unity-common.sh"

apk_path="${1:-$REPO_ROOT/Builds/Android/vr-fencing-dev.apk}"
log_file="$LOG_DIR/build-apk.log"

require_unity_editor
require_project_closed
mkdir -p "$(dirname "$apk_path")" "$LOG_DIR"

echo "Building $apk_path (log: $log_file)"
status=0
"$UNITY_EDITOR" -batchmode -nographics -quit \
    -projectPath "$(to_native_path "$PROJECT_DIR")" \
    -buildTarget Android \
    -executeMethod VrFencing.EditorTools.BuildAndroid.BuildDevelopmentApk \
    -apkPath "$(to_native_path "$apk_path")" \
    -logFile "$(to_native_path "$log_file")" || status=$?

if [[ $status -ne 0 || ! -f "$apk_path" ]]; then
    echo "error: build failed (exit $status). Last errors:" >&2
    grep -n -e "error CS" -e "BuildFailedException" -e "\[BuildAndroid\]" -e "Error building" "$log_file" | tail -n 20 >&2 || true
    explain_licence_failure "$log_file"
    exit 1
fi

grep "\[BuildAndroid\]" "$log_file" | tail -n 1
echo "APK ready: $apk_path"

#!/usr/bin/env bash
# Runs the Unity Edit-mode tests headlessly and writes NUnit XML results.
#
# Usage: scripts/run-editmode-tests.sh [results.xml]
# Default results: Builds/test-results/editmode.xml (gitignored).
# Log: Builds/logs/editmode-tests.log. Exit code: 0 all passed, non-zero otherwise.
set -euo pipefail

# shellcheck source-path=SCRIPTDIR source=lib/unity-common.sh
source "$(dirname "$0")/lib/unity-common.sh"

results_file="${1:-$REPO_ROOT/Builds/test-results/editmode.xml}"
log_file="$LOG_DIR/editmode-tests.log"

require_unity_editor
require_project_closed
mkdir -p "$(dirname "$results_file")" "$LOG_DIR"
rm -f "$results_file"

echo "Running Edit-mode tests (log: $log_file)"
status=0
# No -quit here: -runTests exits on its own once the run finishes.
run_unity -batchmode -nographics \
    -projectPath "$(to_native_path "$PROJECT_DIR")" \
    -buildTarget Android \
    -runTests -testPlatform EditMode \
    -testResults "$(to_native_path "$results_file")" \
    -logFile "$(to_native_path "$log_file")" || status=$?

if [[ ! -f "$results_file" ]]; then
    echo "error: no test results written (exit $status). See $log_file" >&2
    grep -n -e "error CS" "$log_file" | tail -n 20 >&2 || true
    explain_licence_failure "$log_file"
    exit 1
fi

# Summary line from the NUnit XML root element.
grep -o '<test-run [^>]*' "$results_file" | grep -o -e 'total="[0-9]*"' -e 'passed="[0-9]*"' -e 'failed="[0-9]*"' -e 'skipped="[0-9]*"' | tr '\n' ' '
echo
echo "Results: $results_file"
exit "$status"

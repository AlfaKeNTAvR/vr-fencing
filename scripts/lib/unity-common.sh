#!/usr/bin/env bash
# Shared helpers for the batchmode Unity scripts. Source it, do not run it.
#
# Override the editor with UNITY_EDITOR=/path/to/Unity.exe if it is not at the
# default Unity Hub location.

UNITY_VERSION="6000.0.85f1"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT_DIR="$REPO_ROOT/unity"
# shellcheck disable=SC2034 # used by the scripts that source this file
LOG_DIR="$REPO_ROOT/Builds/logs"
UNITY_EDITOR="${UNITY_EDITOR:-/c/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity.exe}"

# Unity.exe is a native Windows program: hand it Windows-style paths.
to_native_path() {
    if command -v cygpath > /dev/null 2>&1; then
        cygpath -w "$1"
    else
        printf '%s\n' "$1"
    fi
}

require_unity_editor() {
    if [[ ! -x "$UNITY_EDITOR" ]]; then
        echo "error: Unity editor not found at '$UNITY_EDITOR' (set UNITY_EDITOR)." >&2
        exit 2
    fi
}

# Batchmode cannot open a project that an Editor window already has open.
require_project_closed() {
    if [[ -f "$PROJECT_DIR/Temp/UnityLockfile" ]] && ! rm -f "$PROJECT_DIR/Temp/UnityLockfile" 2> /dev/null; then
        echo "error: the Unity project is open in an Editor. Close it, or use the Editor MCP instead." >&2
        exit 3
    fi
}

# Prints the licensing hint when the log shows a licence failure.
explain_licence_failure() {
    local log_file="$1"
    if grep -q -i -e "No valid Unity Editor license" -e "License is not active" "$log_file" 2> /dev/null; then
        echo "hint: Unity licence not active. Sign in to Unity Hub and activate a Personal licence." >&2
    fi
}

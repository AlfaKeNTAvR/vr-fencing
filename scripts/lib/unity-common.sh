#!/usr/bin/env bash
# Shared helpers for the batchmode Unity scripts. Source it, do not run it.
#
# Override the editor with UNITY_EDITOR=/path/to/Unity.exe if it is not at the
# default Unity Hub location. Launch Unity through run_unity: it waits for the
# shared Unity lock (one Unity process across all worktrees) and adds the
# Accelerator import cache flags when the Accelerator is listening.
# Overrides: UNITY_CACHE_SERVER=off or host:port, UNITY_LOCK_DIR=/some/dir.

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

# --- Unity Accelerator (local import cache) -----------------------------------
# The Accelerator runs as a Windows service on 0.0.0.0:10080. When it is not
# listening, Unity imports from scratch as before.
# UNITY_CACHE_SERVER=off skips it; UNITY_CACHE_SERVER=host:port picks another one.
UNITY_CACHE_SERVER="${UNITY_CACHE_SERVER:-127.0.0.1:10080}"
CACHE_PROBE_TIMEOUT_S=2

# Prints the cache server flags one per line, or nothing when it is unreachable.
print_cache_server_args() {
    if [[ "$UNITY_CACHE_SERVER" == "off" ]]; then
        echo "Import cache: off (UNITY_CACHE_SERVER=off)" >&2
        return 0
    fi
    local host="${UNITY_CACHE_SERVER%:*}" port="${UNITY_CACHE_SERVER##*:}"
    # shellcheck disable=SC2016 # $1/$2 expand in the child shell
    if ! timeout "$CACHE_PROBE_TIMEOUT_S" bash -c 'exec 3<> "/dev/tcp/$1/$2"' _ "$host" "$port" 2> /dev/null; then
        echo "Import cache: Accelerator not reachable at $UNITY_CACHE_SERVER, importing without it" >&2
        return 0
    fi
    echo "Import cache: Accelerator at $UNITY_CACHE_SERVER" >&2
    printf '%s\n' \
        -EnableCacheServer \
        -cacheServerEndpoint "$UNITY_CACHE_SERVER" \
        -cacheServerEnableDownload true \
        -cacheServerEnableUpload true \
        -cacheServerWaitForUploadCompletion
}

# --- Shared Unity lock ----------------------------------------------------------
# The laptop has RAM for one Unity process. Every batchmode script takes this
# lock, shared by all worktrees because it lives in the main repo's git dir.
# The owner file names the holder; a holder whose processes are gone is stale.
UNITY_LOCK_POLL_S=5
UNITY_LOCK_REPORT_EVERY_S=60
UNITY_LOCK_STARTUP_GRACE_S=30

locate_unity_lock_dir() {
    local common_dir
    if common_dir="$(git -C "$REPO_ROOT" rev-parse --path-format=absolute --git-common-dir 2> /dev/null)"; then
        printf '%s/vr-fencing-unity.lock\n' "$common_dir"
    else
        printf '%s/vr-fencing/unity.lock\n' "${LOCALAPPDATA:-$HOME/.cache}"
    fi
}
UNITY_LOCK_DIR="${UNITY_LOCK_DIR:-$(locate_unity_lock_dir)}"
UNITY_LOCK_HELD=0
UNITY_CHILD_PID=""

# Windows PID of a Git Bash process, or the plain PID elsewhere.
to_windows_pid() {
    local pid="$1"
    if [[ -r "/proc/$pid/winpid" ]]; then
        cat "/proc/$pid/winpid"
    else
        printf '%s\n' "$pid"
    fi
}

# True when a process with this PID and image name (e.g. bash.exe) still runs.
# The image name guards against Windows reusing a dead holder's PID.
is_process_alive() {
    local pid="$1" image="$2"
    [[ -n "$pid" ]] || return 1
    if command -v tasklist > /dev/null 2>&1; then
        tasklist //FI "PID eq $pid" //NH //FO CSV 2> /dev/null | grep -q -i -F "\"$image\",\"$pid\""
    else
        kill -0 "$pid" 2> /dev/null
    fi
}

# Usage: read_lock_field <field> [lock dir]
read_lock_field() {
    sed -n "s/^$1=//p" "${2:-$UNITY_LOCK_DIR}/owner" 2> /dev/null | head -n 1
}

# Usage: is_lock_holder_alive [lock dir]
is_lock_holder_alive() {
    is_process_alive "$(read_lock_field shell_winpid "$@")" bash.exe ||
        is_process_alive "$(read_lock_field unity_winpid "$@")" Unity.exe
}

describe_lock_holder() {
    printf '%s (worktree %s, since %s, shell PID %s)\n' \
        "$(read_lock_field script)" "$(read_lock_field worktree)" \
        "$(read_lock_field started)" "$(read_lock_field shell_winpid)"
}

write_lock_owner() {
    local unity_winpid="$1"
    {
        printf 'shell_winpid=%s\n' "$(to_windows_pid "$$")"
        printf 'unity_winpid=%s\n' "$unity_winpid"
        printf 'script=%s\n' "$(basename "$0")"
        printf 'worktree=%s\n' "$REPO_ROOT"
        printf 'started=%s\n' "$UNITY_LOCK_STARTED"
    } > "$UNITY_LOCK_DIR/owner.tmp"
    mv -f "$UNITY_LOCK_DIR/owner.tmp" "$UNITY_LOCK_DIR/owner"
}

# Moves a stale lock aside. The rename is atomic, so only one waiter wins; the
# loser just retries mkdir. If another waiter replaced the stale lock with a live
# one in the meantime, the live one is put back.
clear_stale_lock() {
    local aside="$UNITY_LOCK_DIR.stale.$$"
    mv "$UNITY_LOCK_DIR" "$aside" 2> /dev/null || return 0
    if [[ -f "$aside/owner" ]] && is_lock_holder_alive "$aside"; then
        mv "$aside" "$UNITY_LOCK_DIR" 2> /dev/null ||
            echo "warning: moved a live Unity lock aside by mistake ($aside); two Unity processes may run" >&2
        return 0
    fi
    echo "Unity lock: holder is gone, removed the stale lock" >&2
    rm -rf "$aside"
}

# Is the lock older than the grace period? Covers a holder that died between
# mkdir and writing the owner file.
is_lock_past_grace() {
    local created now
    created="$(stat -c %Y "$UNITY_LOCK_DIR" 2> /dev/null)" || return 1
    now="$(date +%s)"
    ((now - created > UNITY_LOCK_STARTUP_GRACE_S))
}

acquire_unity_lock() {
    local waited_s=0
    mkdir -p "$(dirname "$UNITY_LOCK_DIR")"
    until mkdir "$UNITY_LOCK_DIR" 2> /dev/null; do
        if [[ -f "$UNITY_LOCK_DIR/owner" ]]; then
            if ! is_lock_holder_alive; then
                clear_stale_lock
                continue
            fi
        elif is_lock_past_grace; then
            clear_stale_lock
            continue
        fi
        if ((waited_s % UNITY_LOCK_REPORT_EVERY_S == 0)); then
            echo "Unity lock: waiting ${waited_s}s for $(describe_lock_holder)" >&2
        fi
        sleep "$UNITY_LOCK_POLL_S"
        waited_s=$((waited_s + UNITY_LOCK_POLL_S))
    done
    UNITY_LOCK_HELD=1
    UNITY_LOCK_STARTED="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    trap release_unity_lock EXIT
    trap 'exit 130' INT TERM
    write_lock_owner ""
    echo "Unity lock: acquired ($UNITY_LOCK_DIR)" >&2
}

# Stops a still running Unity child (Ctrl+C, script error) and frees the lock.
release_unity_lock() {
    if [[ -n "$UNITY_CHILD_PID" ]] && kill -0 "$UNITY_CHILD_PID" 2> /dev/null; then
        echo "Stopping Unity (PID $(to_windows_pid "$UNITY_CHILD_PID"))" >&2
        kill "$UNITY_CHILD_PID" 2> /dev/null || true
        wait "$UNITY_CHILD_PID" 2> /dev/null || true
    fi
    UNITY_CHILD_PID=""
    if ((UNITY_LOCK_HELD)); then
        rm -rf "$UNITY_LOCK_DIR"
        UNITY_LOCK_HELD=0
    fi
}

# Runs the Unity editor with the given arguments under the shared lock, adding
# the Accelerator flags when it is reachable. Returns Unity's exit code.
run_unity() {
    local -a cache_args
    local status=0
    acquire_unity_lock
    mapfile -t cache_args < <(print_cache_server_args)
    "$UNITY_EDITOR" "${cache_args[@]}" "$@" &
    UNITY_CHILD_PID=$!
    write_lock_owner "$(to_windows_pid "$UNITY_CHILD_PID")"
    wait "$UNITY_CHILD_PID" || status=$?
    UNITY_CHILD_PID=""
    release_unity_lock
    return "$status"
}

# Prints the licensing hint when the log shows a licence failure.
explain_licence_failure() {
    local log_file="$1"
    if grep -q -i -e "No valid Unity Editor license" -e "License is not active" "$log_file" 2> /dev/null; then
        echo "hint: Unity licence not active. Sign in to Unity Hub and activate a Personal licence." >&2
    fi
}

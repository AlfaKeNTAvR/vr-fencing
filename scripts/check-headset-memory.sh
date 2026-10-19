#!/usr/bin/env bash
# Checks the app's GPU and RAM use on the Quest against a budget: fresh launch,
# then N scripted sleep/wake cycles (PER-94, after the PER-93 leak).
#
# NEVER run this while someone (Nikita) is wearing or testing the headset: it
# fakes the proximity sensor, restarts the app and puts the headset to sleep.
# The script refuses to start when the headset is awake (a worn headset is
# always awake); put it on the desk, wait until it sleeps, then run. --force
# skips that check, for a headset you know is not on anyone's head.
#
# Steps: connect over Wi-Fi adb, fake "worn" (vrpowermanager prox_close) and
# wake the headset, optionally install an APK, force-stop and launch the app,
# wait, record. Then per cycle: KEYCODE_SLEEP, wait, KEYCODE_WAKEUP, wait,
# record. A last KEYCODE_SLEEP makes the app log the state after the last wake.
# The proximity sensor is always handed back (automation_disable) on exit,
# error or Ctrl+C, so the headset ends asleep on the desk.
#
# Recorded each time: GL mtrack (GPU memory), total PSS and swap PSS of the app
# (dumpsys meminfo), system MemAvailable (/proc/meminfo). From the app's
# "[Life] gpu <moment> ... xrTex N" lines (LifecycleLogBehaviour): the count of
# Unity "XR Texture" eye buffers at each logged moment.
#
# Budget (all overridable through the environment):
#   MEMCHECK_MAX_FRESH_GL_MB=1024     GL mtrack after the fresh launch
#   MEMCHECK_MAX_WAKE_GROWTH_PCT=10   growth of GL mtrack per wake, in percent
#   MEMCHECK_MIN_WAKE_GROWTH_MB=50    ... or this many MB, whichever is larger
#   MEMCHECK_EXPECTED_XR_TEXTURES=3   XR Texture count at every logged moment
#   and no "ANR in <app>" in logcat -b system during the run.
#
# Prints one "PASS" or "FAIL: <reasons>" line and appends a row to
# Logs/memory-checks.csv (gitignored). Raw dumps go to Logs/memory-checks/<time>/.
#
# Usage: scripts/check-headset-memory.sh [--apk PATH] [--wakes N] [--force]
#        scripts/check-headset-memory.sh --self-test
#   --apk PATH   install this APK first (adb install -r -g, no streaming)
#   --wakes N    sleep/wake cycles, default 2 (or MEMCHECK_WAKES)
#   --force      run even though the headset is awake (see above)
#   --self-test  run the parsers and the budget on scripts/fixtures/headset-memory, no headset
# Environment: QUEST_SERIAL (default 192.168.1.67:5555), ADB (default: Unity's adb),
#   MEMCHECK_LAUNCH_WAIT_S=30, MEMCHECK_SLEEP_S=10, MEMCHECK_WAKE_WAIT_S=30.
# Exit codes: 0 pass, 1 budget failed, 2 setup error, 4 refused (headset awake).
set -euo pipefail

# shellcheck source-path=SCRIPTDIR source=lib/unity-common.sh
source "$(dirname "$0")/lib/unity-common.sh"

# Device paths like /sdcard must reach adb.exe untouched by Git Bash.
export MSYS_NO_PATHCONV=1

PACKAGE="com.alfakentavr.vrfencing"
SERIAL="${QUEST_SERIAL:-192.168.1.67:5555}"
ADB="${ADB:-/c/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe}"
# Self-test inputs. gpu-log-leak.txt and gpu-log-fixed.txt are real [Life] lines
# from the PER-93 runs before and after the fix (PR 38); the meminfo, power and
# system log files are hand-written in the Android output format.
FIXTURE_DIR="$REPO_ROOT/scripts/fixtures/headset-memory"
CSV_FILE="$REPO_ROOT/Logs/memory-checks.csv"
CSV_HEADER="timestamp,git_commit,apk,serial,wakes,result,gl_mtrack_mb,total_pss_mb,swap_pss_mb,mem_available_mb,xr_texture_counts,anr_count,reasons"

MAX_FRESH_GL_MB="${MEMCHECK_MAX_FRESH_GL_MB:-1024}"
MAX_WAKE_GROWTH_PCT="${MEMCHECK_MAX_WAKE_GROWTH_PCT:-10}"
MIN_WAKE_GROWTH_MB="${MEMCHECK_MIN_WAKE_GROWTH_MB:-50}"
EXPECTED_XR_TEXTURES="${MEMCHECK_EXPECTED_XR_TEXTURES:-3}"
LAUNCH_WAIT_S="${MEMCHECK_LAUNCH_WAIT_S:-30}"
SLEEP_S="${MEMCHECK_SLEEP_S:-10}"
WAKE_WAIT_S="${MEMCHECK_WAKE_WAIT_S:-30}"
# After the last KEYCODE_SLEEP, time for the app to log its before-pause line.
FINAL_PAUSE_WAIT_S=5

# --- Parsers: read one dump on stdin, print one value (empty when missing) ----

# "GL mtrack" row of dumpsys meminfo: first column is its PSS in KB.
parse_gl_mtrack_kb() {
    awk '{ sub(/\r$/, "") } $1 == "GL" && $2 == "mtrack" { print $3; exit }'
}

parse_total_pss_kb() {
    awk '{ sub(/\r$/, "") } match($0, /TOTAL PSS: *[0-9]+/) { value = substr($0, RSTART, RLENGTH); gsub(/[^0-9]/, "", value); print value; exit }'
}

# "TOTAL SWAP PSS:" on Android 10+, "TOTAL SWAP (KB):" on older releases.
parse_swap_pss_kb() {
    awk '{ sub(/\r$/, "") } match($0, /TOTAL SWAP[^:]*: *[0-9]+/) { value = substr($0, RSTART, RLENGTH); sub(/^[^:]*:/, "", value); gsub(/[^0-9]/, "", value); print value; exit }'
}

# /proc/meminfo MemAvailable, in KB.
parse_mem_available_kb() {
    awk '{ sub(/\r$/, "") } $1 == "MemAvailable:" { print $2; exit }'
}

# Space-separated XR Texture counts of every "[Life] gpu <moment> frame ..."
# line, in log order. Other lines that mention xrTex (xr-cleanup) are ignored.
parse_xr_texture_counts() {
    awk '{ sub(/\r$/, "") }
        /\[Life\] gpu [a-z-]+ frame / {
            for (i = 1; i < NF; i++) if ($i == "xrTex") { printf "%s%s", separator, $(i + 1); separator = " " }
        }
        END { print "" }'
}

# Number of "ANR in <package>" lines in a system log (not other packages).
count_app_anrs() {
    awk -v needle="ANR in $PACKAGE" '{ sub(/\r$/, "") }
        (start = index($0, needle)) > 0 {
            next_char = substr($0, start + length(needle), 1)
            if (next_char !~ /[A-Za-z0-9_.]/) count++
        }
        END { print count + 0 }'
}

# mWakefulness from dumpsys power: Awake, Asleep, Dozing or Dreaming.
parse_wakefulness() {
    awk -F= '{ sub(/\r$/, "") } /mWakefulness=/ { gsub(/[[:space:]]/, "", $2); print $2; exit }'
}

# KB to whole MB; empty stays empty.
kb_to_mb() {
    if [[ -z "$1" ]]; then
        echo ""
    else
        awk -v kb="$1" 'BEGIN { printf "%.0f\n", kb / 1024 }'
    fi
}

# --- Budget -------------------------------------------------------------------

# Usage: evaluate_budget "<GL MB fresh wake1 ...>" "<XR Texture counts>" <ANR count>
# Prints one reason per line; no output means the run is within budget.
# A missing GL value is written as NA.
evaluate_budget() {
    local gl_values="$1" xr_counts="$2" anr_count="$3"
    awk -v gl="$gl_values" -v max_fresh="$MAX_FRESH_GL_MB" -v pct="$MAX_WAKE_GROWTH_PCT" \
        -v min_growth="$MIN_WAKE_GROWTH_MB" 'BEGIN {
        n = split(gl, values, " ")
        if (n == 0 || values[1] == "NA") { print "fresh GL mtrack not measured (app not running?)"; exit }
        if (values[1] + 0 > max_fresh + 0) printf "fresh GL mtrack %d MB > %d MB\n", values[1], max_fresh
        for (i = 2; i <= n; i++) {
            if (values[i] == "NA") { printf "wake %d: GL mtrack not measured (app not running?)\n", i - 1; continue }
            if (values[i - 1] == "NA") continue
            allowance = values[i - 1] * pct / 100
            if (allowance < min_growth) allowance = min_growth
            growth = values[i] - values[i - 1]
            if (growth > allowance) printf "wake %d: GL mtrack +%d MB (%d to %d) > +%d MB\n", i - 1, growth, values[i - 1], values[i], allowance
        }
    }'
    if [[ -z "$xr_counts" ]]; then
        echo "no [Life] gpu lines in the Unity log (app not running, or a build without LifecycleLogBehaviour?)"
    else
        local count
        for count in $xr_counts; do
            if [[ "$count" != "$EXPECTED_XR_TEXTURES" ]]; then
                echo "XR Texture counts $xr_counts, expected $EXPECTED_XR_TEXTURES each time"
                break
            fi
        done
    fi
    if [[ "$anr_count" != "0" ]]; then
        echo "$anr_count ANR(s) for $PACKAGE in logcat -b system"
    fi
}

# --- Self-test (no headset) ---------------------------------------------------

SELF_TEST_FAILURES=0

expect_equal() {
    local name="$1" expected="$2" actual="$3"
    if [[ "$expected" == "$actual" ]]; then
        echo "ok    $name"
    else
        echo "FAIL  $name: expected '$expected', got '$actual'"
        SELF_TEST_FAILURES=$((SELF_TEST_FAILURES + 1))
    fi
}

# Joins evaluate_budget's reasons into one line, for comparison.
budget_reasons() {
    evaluate_budget "$@" | paste -sd '|' -
}

run_self_test() {
    local fixtures="$FIXTURE_DIR"
    expect_equal "GL mtrack KB" "44012" "$(parse_gl_mtrack_kb < "$fixtures/dumpsys-meminfo.txt")"
    expect_equal "total PSS KB" "327744" "$(parse_total_pss_kb < "$fixtures/dumpsys-meminfo.txt")"
    expect_equal "swap PSS KB" "4140" "$(parse_swap_pss_kb < "$fixtures/dumpsys-meminfo.txt")"
    expect_equal "MemAvailable KB" "2950000" "$(parse_mem_available_kb < "$fixtures/proc-meminfo.txt")"
    expect_equal "KB to MB" "43" "$(kb_to_mb 44012)"
    expect_equal "GL mtrack missing" "" "$(parse_gl_mtrack_kb <<< "No process found for: $PACKAGE")"
    expect_equal "XR Textures, leak log (PER-93 baseline)" "3 3 6 9" "$(parse_xr_texture_counts < "$fixtures/gpu-log-leak.txt")"
    expect_equal "XR Textures, fixed log (PR 38), xr-cleanup lines ignored" "3 3 3" "$(parse_xr_texture_counts < "$fixtures/gpu-log-fixed.txt")"
    expect_equal "ANR count, app ANR plus a system UI ANR" "1" "$(count_app_anrs < "$fixtures/logcat-system-anr.txt")"
    expect_equal "ANR count, other packages only" "0" "$(count_app_anrs < "$fixtures/logcat-system-clean.txt")"
    expect_equal "wakefulness awake" "Awake" "$(parse_wakefulness < "$fixtures/dumpsys-power-awake.txt")"
    expect_equal "wakefulness asleep" "Asleep" "$(parse_wakefulness < "$fixtures/dumpsys-power-asleep.txt")"

    expect_equal "budget: fixed build passes" "" "$(budget_reasons "43 41 41" "3 3 3" 0)"
    expect_equal "budget: +45 MB is under the 50 MB floor" "" "$(budget_reasons "400 445" "3 3" 0)"
    expect_equal "budget: +10% of a large value passes" "" "$(budget_reasons "900 990" "3 3" 0)"
    expect_equal "budget: fresh over 1 GB fails" "fresh GL mtrack 1679 MB > 1024 MB" "$(budget_reasons "1679" "3" 0)"
    expect_equal "budget: PER-93 leak fails per wake and on XR Textures" \
        "wake 1: GL mtrack +630 MB (660 to 1290) > +66 MB|wake 2: GL mtrack +630 MB (1290 to 1920) > +129 MB|XR Texture counts 3 3 6 9, expected 3 each time" \
        "$(budget_reasons "660 1290 1920" "$(parse_xr_texture_counts < "$fixtures/gpu-log-leak.txt")" 0)"
    expect_equal "budget: app ANR fails" "1 ANR(s) for $PACKAGE in logcat -b system" \
        "$(budget_reasons "43 41" "3 3" "$(count_app_anrs < "$fixtures/logcat-system-anr.txt")")"
    expect_equal "budget: no gpu lines fails" "no [Life] gpu lines in the Unity log (app not running, or a build without LifecycleLogBehaviour?)" \
        "$(budget_reasons "43" "" 0)"
    expect_equal "budget: app gone after a wake fails" "wake 1: GL mtrack not measured (app not running?)" \
        "$(budget_reasons "43 NA" "3" 0)"

    if ((SELF_TEST_FAILURES > 0)); then
        echo "self-test: $SELF_TEST_FAILURES failed"
        return 1
    fi
    echo "self-test: all passed"
}

# --- Headset run --------------------------------------------------------------

run_adb() {
    "$ADB" -s "$SERIAL" "$@"
}

PROXIMITY_FAKED=0

# Hands the proximity sensor back to the hardware. Runs on every exit.
restore_proximity_sensor() {
    if ((PROXIMITY_FAKED == 1)); then
        if run_adb shell am broadcast -a com.oculus.vrpowermanager.automation_disable > /dev/null 2>&1; then
            echo "Proximity sensor handed back (automation_disable)."
        else
            echo "WARNING: could not hand the proximity sensor back. Run: \"$ADB\" -s $SERIAL shell am broadcast -a com.oculus.vrpowermanager.automation_disable" >&2
        fi
        PROXIMITY_FAKED=0
    fi
}

fake_worn() {
    PROXIMITY_FAKED=1
    run_adb shell am broadcast -a com.oculus.vrpowermanager.prox_close > /dev/null
    run_adb shell input keyevent KEYCODE_WAKEUP
}

connect_headset() {
    if [[ ! -x "$ADB" ]]; then
        echo "error: adb not found at '$ADB' (set ADB)." >&2
        exit 2
    fi
    "$ADB" connect "$SERIAL" > /dev/null || true
    local state
    state="$(run_adb get-state 2> /dev/null | tr -d '\r' || true)"
    if [[ "$state" != "device" ]]; then
        echo "error: headset $SERIAL is not reachable over adb (state '${state:-none}'). Is it on, on Wi-Fi, and authorized?" >&2
        exit 2
    fi
}

# A worn headset is always awake. An asleep one is on the desk (or off).
refuse_if_maybe_worn() {
    local force="$1" wakefulness
    wakefulness="$(run_adb shell dumpsys power | parse_wakefulness)"
    echo "Headset power state: ${wakefulness:-unknown}"
    if [[ "$wakefulness" == "Asleep" || "$wakefulness" == "Dozing" ]]; then
        return 0
    fi
    if ((force == 1)); then
        echo "WARNING: headset is ${wakefulness:-in an unknown state}; --force given, running anyway."
        return 0
    fi
    echo "REFUSED: the headset is ${wakefulness:-in an unknown state}, so someone may be wearing it." >&2
    echo "Never run this while Nikita is wearing or testing the headset. Put it on the desk, wait until" >&2
    echo "it sleeps, and run again. Use --force only if you know nobody has it on." >&2
    exit 4
}

# Usage: record_memory <label>. Appends MB values to the GL/PSS/SWAP/AVAILABLE lists.
record_memory() {
    local label="$1" meminfo_file="$RUN_DIR/meminfo-$1.txt" proc_file="$RUN_DIR/proc-meminfo-$1.txt"
    run_adb shell dumpsys meminfo "$PACKAGE" > "$meminfo_file" 2>&1 || true
    run_adb shell cat /proc/meminfo > "$proc_file" 2>&1 || true
    local gl pss swap available
    gl="$(kb_to_mb "$(parse_gl_mtrack_kb < "$meminfo_file")")"
    pss="$(kb_to_mb "$(parse_total_pss_kb < "$meminfo_file")")"
    swap="$(kb_to_mb "$(parse_swap_pss_kb < "$meminfo_file")")"
    available="$(kb_to_mb "$(parse_mem_available_kb < "$proc_file")")"
    GL_VALUES+=("${gl:-NA}")
    PSS_VALUES+=("${pss:-NA}")
    SWAP_VALUES+=("${swap:-NA}")
    AVAILABLE_VALUES+=("${available:-NA}")
    echo "$label: GL mtrack ${gl:-NA} MB, total PSS ${pss:-NA} MB, swap PSS ${swap:-NA} MB, system MemAvailable ${available:-NA} MB"
}

sleep_wake_cycle() {
    local wake="$1"
    run_adb shell input keyevent KEYCODE_SLEEP
    sleep "$SLEEP_S"
    run_adb shell input keyevent KEYCODE_WAKEUP
    run_adb shell am broadcast -a com.oculus.vrpowermanager.prox_close > /dev/null
    sleep "$WAKE_WAIT_S"
    local wakefulness
    wakefulness="$(run_adb shell dumpsys power | parse_wakefulness)"
    if [[ "$wakefulness" != "Awake" ]]; then
        echo "WARNING: wake $wake: headset is ${wakefulness:-unknown}, not Awake."
    fi
    record_memory "wake-$wake"
}

join_with() {
    local separator="$1"
    shift
    local IFS="$separator"
    echo "$*"
}

append_csv_row() {
    local timestamp="$1" apk="$2" wakes="$3" result="$4" xr_counts="$5" anr_count="$6" reasons="$7"
    local commit
    # git.exe is native: with MSYS_NO_PATHCONV it needs a Windows path.
    commit="$(git -C "$(to_native_path "$REPO_ROOT")" rev-parse --short HEAD 2> /dev/null || echo unknown)"
    mkdir -p "$(dirname "$CSV_FILE")"
    if [[ ! -s "$CSV_FILE" ]]; then
        echo "$CSV_HEADER" > "$CSV_FILE"
    fi
    printf '%s,%s,"%s",%s,%s,%s,%s,%s,%s,%s,%s,%s,"%s"\n' \
        "$timestamp" "$commit" "${apk//\"/\'}" "$SERIAL" "$wakes" "$result" \
        "$(join_with ';' "${GL_VALUES[@]}")" "$(join_with ';' "${PSS_VALUES[@]}")" \
        "$(join_with ';' "${SWAP_VALUES[@]}")" "$(join_with ';' "${AVAILABLE_VALUES[@]}")" \
        "${xr_counts// /;}" "$anr_count" "${reasons//\"/\'}" >> "$CSV_FILE"
}

run_check() {
    local apk="$1" wakes="$2" force="$3"
    if [[ -n "$apk" && ! -f "$apk" ]]; then
        echo "error: APK '$apk' not found." >&2
        exit 2
    fi
    local timestamp
    timestamp="$(date +%Y-%m-%dT%H:%M:%S)"
    RUN_DIR="$REPO_ROOT/Logs/memory-checks/${timestamp//:/-}"
    mkdir -p "$RUN_DIR"
    GL_VALUES=()
    PSS_VALUES=()
    SWAP_VALUES=()
    AVAILABLE_VALUES=()

    connect_headset
    refuse_if_maybe_worn "$force"

    trap restore_proximity_sensor EXIT
    trap 'exit 130' INT TERM
    set -o errtrace
    trap 'echo "error: a step failed (line $LINENO); no CSV row written." >&2; trap - ERR' ERR
    fake_worn

    if [[ -n "$apk" ]]; then
        echo "Installing $apk"
        run_adb install --no-streaming -r -g "$(to_native_path "$apk")"
    fi

    # Device clock, so the log reads below cover this run only.
    local start_epoch
    start_epoch="$(run_adb shell date +%s | tr -d '\r')"

    run_adb shell am force-stop "$PACKAGE"
    run_adb shell monkey -p "$PACKAGE" -c android.intent.category.LAUNCHER 1 > /dev/null 2>&1
    echo "Launched $PACKAGE; waiting ${LAUNCH_WAIT_S} s."
    sleep "$LAUNCH_WAIT_S"
    record_memory "fresh"

    local wake
    for ((wake = 1; wake <= wakes; wake++)); do
        sleep_wake_cycle "$wake"
    done

    # The app logs "[Life] gpu before-pause" on this last pause: the state after the last wake.
    run_adb shell input keyevent KEYCODE_SLEEP
    sleep "$FINAL_PAUSE_WAIT_S"

    run_adb logcat -d -T "$start_epoch.000" -s Unity > "$RUN_DIR/unity.log" 2>&1 || true
    run_adb logcat -d -b system -T "$start_epoch.000" > "$RUN_DIR/system.log" 2>&1 || true
    restore_proximity_sensor

    local xr_counts anr_count reasons result
    xr_counts="$(parse_xr_texture_counts < "$RUN_DIR/unity.log")"
    anr_count="$(count_app_anrs < "$RUN_DIR/system.log")"
    reasons="$(evaluate_budget "${GL_VALUES[*]}" "$xr_counts" "$anr_count" | paste -sd ';' -)"
    reasons="${reasons//;/; }"
    if [[ -z "$reasons" ]]; then
        result="PASS"
    else
        result="FAIL"
    fi
    append_csv_row "$timestamp" "$apk" "$wakes" "$result" "$xr_counts" "$anr_count" "$reasons"

    local summary="GL mtrack MB ${GL_VALUES[*]}, XR Textures ${xr_counts:-none}, ANRs $anr_count"
    echo "Logs: $RUN_DIR; row appended to $CSV_FILE"
    if [[ "$result" == "PASS" ]]; then
        echo "PASS: $summary"
    else
        echo "FAIL: $reasons ($summary)"
        return 1
    fi
}

main() {
    local apk="" wakes="${MEMCHECK_WAKES:-2}" force=0
    while (($# > 0)); do
        case "$1" in
            --self-test)
                run_self_test
                return
                ;;
            --apk)
                apk="${2:?--apk needs a path}"
                shift
                ;;
            --wakes)
                wakes="${2:?--wakes needs a number}"
                shift
                ;;
            --force) force=1 ;;
            -h | --help)
                sed -n '2,/^set -euo/p' "${BASH_SOURCE[0]}" | sed -e '$d' -e 's/^# \{0,1\}//'
                return
                ;;
            *)
                echo "error: unknown argument '$1' (see --help)." >&2
                exit 2
                ;;
        esac
        shift
    done
    if [[ ! "$wakes" =~ ^[0-9]+$ ]]; then
        echo "error: --wakes must be a whole number, got '$wakes'." >&2
        exit 2
    fi
    run_check "$apk" "$wakes" "$force"
}

main "$@"

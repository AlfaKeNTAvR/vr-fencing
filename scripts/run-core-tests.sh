#!/usr/bin/env bash
# Runs the Edit-mode tests that need no Unity, with dotnet test, in seconds.
#
# Compiles the plain C# Core assemblies (asmdefs with "noEngineReferences": true)
# and every Edit-mode test file that uses no Unity type, then runs them with NUnit.
# A test file is left out when it uses UnityEngine, UnityEditor, TMPro, Oculus or
# Meta, or names a type declared outside the Core folders (the Unity-side
# assemblies and Assets/Editor), or names a helper declared in a test file that
# was itself left out. Unity Edit-mode tests (scripts/run-editmode-tests.sh)
# stay the merge gate: they run everything.
#
# Usage: scripts/run-core-tests.sh [--list] [extra dotnet test args...]
#   --list   print which test files are in and out (with the reason), then stop.
# Needs the .NET SDK (scoop install dotnet-sdk). Does not take the Unity lock.
# Results: Builds/test-results/core-tests.trx (gitignored).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ASSETS_DIR="$REPO_ROOT/unity/Assets"
TEST_DIR="$ASSETS_DIR/Tests/EditMode"
PROJECT_FILE="$REPO_ROOT/tools/CoreTests/CoreTests.csproj"
GENERATED_DIR="$REPO_ROOT/Builds/core-tests"
SOURCES_PROPS="$GENERATED_DIR/Sources.props"
RESULTS_DIR="$REPO_ROOT/Builds/test-results"
UNITY_RESULTS="$RESULTS_DIR/editmode.xml"
# Namespaces that only exist inside Unity.
UNITY_USING_PATTERN='^[[:space:]]*using[[:space:]]+(static[[:space:]]+)?([A-Za-z0-9_]+[[:space:]]*=[[:space:]]*)?(UnityEngine|UnityEditor|TMPro|Oculus|Meta)\b|\b(UnityEngine|UnityEditor)\.'
TYPE_DECLARATION_PATTERN='\b(class|struct|enum|interface|record)[[:space:]]+[A-Z][A-Za-z0-9_]*'
# Test files that compile without Unity but only make sense inside it, with why.
# Path relative to Tests/EditMode.
declare -A MANUAL_EXCLUSIONS=(
    [ModuleLayoutTests.cs]="checks the Unity assembly layout (asmdefs loaded in the Editor)"
)

list_only=0
if [[ "${1:-}" == "--list" ]]; then
    list_only=1
    shift
fi

# Unity.exe-style tools want Windows paths; MSBuild accepts forward slashes.
to_msbuild_path() {
    if command -v cygpath > /dev/null 2>&1; then
        cygpath -m "$1"
    else
        printf '%s\n' "$1"
    fi
}

# Prefers the scoop SDK: an older x86 runtime may come first on PATH.
locate_dotnet() {
    local scoop_dotnet="$HOME/scoop/apps/dotnet-sdk/current/dotnet.exe"
    if [[ -n "${DOTNET:-}" ]]; then
        printf '%s\n' "$DOTNET"
    elif [[ -x "$scoop_dotnet" ]]; then
        printf '%s\n' "$scoop_dotnet"
    elif type -P dotnet > /dev/null; then
        type -P dotnet
    else
        echo "error: .NET SDK not found. Install it with 'scoop install dotnet-sdk' (see docs/dev-loop.md)." >&2
        exit 2
    fi
}

# Prints the type names declared in the given C# files, one per line. Line
# comments are dropped first, so prose like "class in the menu" does not count.
# One pipeline for all files: forks are slow in Git Bash.
list_declared_types() {
    sed 's|//.*$||' "$@" | { grep -o -E "$TYPE_DECLARATION_PATTERN" || true; } | awk '{print $2}' | sort -u
}

# --- Find the Core folders and the Unity-only types ------------------------------
core_dirs=()
while IFS= read -r asmdef; do
    if tr -d '[:space:]' < "$asmdef" | grep -q '"noEngineReferences":true'; then
        core_dirs+=("$(dirname "$asmdef")")
    fi
done < <(find "$ASSETS_DIR" -name '*.asmdef' -not -path "$TEST_DIR/*" | sort)
if [[ ${#core_dirs[@]} -eq 0 ]]; then
    echo "error: no Core asmdefs (noEngineReferences) found under $ASSETS_DIR" >&2
    exit 1
fi

core_sources=()
unity_sources=()
while IFS= read -r source; do
    is_core=0
    for dir in "${core_dirs[@]}"; do
        if [[ "$source" == "$dir"/* ]]; then
            is_core=1
            break
        fi
    done
    if ((is_core)); then
        core_sources+=("$source")
    else
        unity_sources+=("$source")
    fi
done < <(find "$ASSETS_DIR" -name '*.cs' -not -path "$TEST_DIR/*" | sort)

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT
unavailable_types_file="$work_dir/unavailable-types.txt"
list_declared_types "${unity_sources[@]}" > "$unavailable_types_file"

# --- Pick the test files ---------------------------------------------------------
# Matching runs on comment-free copies, so a type named in a comment does not count.
stripped_tests="$work_dir/tests"
cp -r "$TEST_DIR" "$stripped_tests"
find "$stripped_tests" -name '*.cs' -exec sed -i 's|//.*$||' {} +

mapfile -t test_files < <(find "$TEST_DIR" -name '*.cs' | sort)
declare -A exclusion_reason=()
newly_excluded=()
for file in "${test_files[@]}"; do
    reason="${MANUAL_EXCLUSIONS[${file#"$TEST_DIR"/}]:-}"
    if [[ -n "$reason" ]]; then
        exclusion_reason[$file]="$reason"
        newly_excluded+=("$file")
    fi
done

# Repeats until stable: leaving a file out makes its helper types unavailable too.
while true; do
    if [[ ${#newly_excluded[@]} -gt 0 ]]; then
        list_declared_types "${newly_excluded[@]}" >> "$unavailable_types_file"
    fi
    candidates=()
    for file in "${test_files[@]}"; do
        if [[ -z "${exclusion_reason[$file]:-}" ]]; then
            candidates+=("$stripped_tests/${file#"$TEST_DIR"/}")
        fi
    done
    [[ ${#candidates[@]} -gt 0 ]] || break
    newly_excluded=()
    while IFS= read -r stripped; do
        file="$TEST_DIR/${stripped#"$stripped_tests"/}"
        exclusion_reason[$file]="uses a Unity namespace"
        newly_excluded+=("$file")
    done < <(grep -l -E "$UNITY_USING_PATTERN" "${candidates[@]}" || true)
    while IFS= read -r stripped; do
        file="$TEST_DIR/${stripped#"$stripped_tests"/}"
        if [[ -z "${exclusion_reason[$file]:-}" ]]; then
            used="$(grep -o -w -F -f "$unavailable_types_file" "$stripped" | sort -u | head -n 3 | tr '\n' ' ')"
            exclusion_reason[$file]="uses ${used% }"
            newly_excluded+=("$file")
        fi
    done < <(grep -l -w -F -f "$unavailable_types_file" "${candidates[@]}" || true)
    [[ ${#newly_excluded[@]} -gt 0 ]] || break
done

included_tests=()
for file in "${test_files[@]}"; do
    if [[ -z "${exclusion_reason[$file]:-}" ]]; then
        included_tests+=("$file")
    fi
done

if ((list_only)); then
    for file in "${test_files[@]}"; do
        relative="${file#"$REPO_ROOT"/}"
        if [[ -n "${exclusion_reason[$file]:-}" ]]; then
            printf 'out  %s (%s)\n' "$relative" "${exclusion_reason[$file]}"
        else
            printf 'in   %s\n' "$relative"
        fi
    done
    echo "${#included_tests[@]} of ${#test_files[@]} test files run without Unity; ${#core_sources[@]} Core sources."
    exit 0
fi

# --- Generate the source list and run ------------------------------------------
mkdir -p "$GENERATED_DIR" "$RESULTS_DIR"
{
    echo '<Project>'
    echo '  <!-- Generated by scripts/run-core-tests.sh. Do not edit. -->'
    echo '  <ItemGroup>'
    for file in "${core_sources[@]}" "${included_tests[@]}"; do
        printf '    <Compile Include="%s" />\n' "$(to_msbuild_path "$file")"
    done
    echo '  </ItemGroup>'
    echo '</Project>'
} > "$SOURCES_PROPS"

dotnet_exe="$(locate_dotnet)"
dotnet_dir="$(dirname "$dotnet_exe")"
DOTNET_ROOT="$(to_msbuild_path "$dotnet_dir")"
sdk_version="$("$dotnet_exe" --version)"
MSBuildSDKsPath="$DOTNET_ROOT/sdk/$sdk_version/Sdks"
export DOTNET_ROOT MSBuildSDKsPath
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

echo "Core tests: ${#core_sources[@]} Core sources, ${#included_tests[@]} of ${#test_files[@]} test files (scripts/run-core-tests.sh --list shows which)"
status=0
"$dotnet_exe" test "$(to_msbuild_path "$PROJECT_FILE")" \
    --logger "trx;LogFileName=$(to_msbuild_path "$RESULTS_DIR/core-tests.trx")" \
    --logger "console;verbosity=minimal" \
    "$@" || status=$?

if [[ -f "$UNITY_RESULTS" ]]; then
    unity_total="$(grep -o '<test-run [^>]*' "$UNITY_RESULTS" | grep -o 'total="[0-9]*"' | grep -o '[0-9]*' || true)"
    if [[ -n "$unity_total" ]]; then
        echo "For comparison: the last Unity Edit-mode run had $unity_total tests ($UNITY_RESULTS)."
    fi
fi
exit "$status"

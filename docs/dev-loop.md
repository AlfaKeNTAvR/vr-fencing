# Dev loop: build, test, install, and the agent tools

How the Unity project in `unity/` gets built, tested and put on the Quest 3, by a person or by Claude. Background and the reasons behind the choices: `docs/research/agentic-dev-tooling.md` and ADR 0001.

## What is in the project

- Unity 6000.0.85f1, Android target, IL2CPP, ARM64, Vulkan, Linear colour, URP (`Assets/Settings/URP-Quest.asset`: no HDR, 4x MSAA).
- XR: Unity OpenXR plugin with Meta's "Meta XR" feature group, for Android and for Windows (Link / Meta XR Simulator). No XR Interaction Toolkit.
- Packages, pinned in `unity/Packages/manifest.json`:

| Package | Version | Source |
|---|---|---|
| Unity OpenXR Plugin `com.unity.xr.openxr` | 1.18.0 | Unity registry |
| Meta XR Core SDK `com.meta.xr.sdk.core` | 207.0.1 | Unity registry (Meta publishes there) |
| Meta MR Utility Kit `com.meta.xr.mrutilitykit` | 207.0.1 | Unity registry |
| Meta XR Haptics SDK `com.meta.xr.sdk.haptics` | 207.0.1 | Unity registry |
| Meta XR Movement SDK `com.meta.xr.sdk.movement` | 207.0.0 | git tag `v207.0.0` of `oculus-samples/Unity-Movement` (Meta's official distribution, not on the registry) |
| URP `com.unity.render-pipelines.universal` | 17.0.4 | built into the editor |
| Test Framework `com.unity.test-framework` | 1.6.0 | built into the editor |
| Newtonsoft Json `com.unity.nuget.newtonsoft-json` | 3.2.2 | Unity registry (JSON for the Core assemblies; already a dependency of MR Utility Kit and MCP for Unity, now pinned directly) |
| MCP for Unity `com.coplaydev.unity-mcp` | 10.3.0 | git tag `v10.3.0` of `CoplayDev/unity-mcp` |
| Netcode for GameObjects `com.unity.netcode.gameobjects` | 2.13.3 | Unity registry (PER-54; not 3.x, which needs Unity 6000.7) |
| Multiplayer Services `com.unity.services.multiplayer` | 2.3.3 | Unity registry (PER-54: Sessions, Relay, Lobby and Authentication in one; never add the standalone Lobby or Relay packages, they clash) |

- Scene: `Assets/Scenes/Passthrough.unity`, the only build scene, kept up to date by `PassthroughSceneBuilder` (menu "VR Fencing > Update passthrough scene", or batchmode `-executeMethod VrFencing.EditorTools.PassthroughSceneBuilder.UpdatePassthroughScene`). Meta's camera rig with passthrough as the background, and the Strip with its En Garde Line transform (the Strip frame). The Dummy sits under the En Garde Line at lunge distance, so it moves with the Strip; it reads the Fencer's height and hand from the settings file at start. Its body is the Movement SDK's Humanoid rig (`Armature.fbx`, referenced from the package by the scene), posed in en garde on the same joints as its Target Zones and drawn translucent; the zones stay capsules on top, and Touches are judged on the Core capsules, never on the mesh (PER-78). Until a Strip is placed or restored, the En Garde Line is at the scene origin. A `Practice` object holds the Drill runner, the session recorder (rep log), the body tracking (IOBT, the only component that starts and stops it), the arm tracking (arm pose for the rep log), the footwork tracking (head) and the Fencer recorder; the `Menu` object (`MenuBehaviour`) builds the panels and the off-hand pointer at runtime and is what starts, pauses and cancels Drills. The `Partner` object holds Netcode's NetworkManager, its UnityTransport and the `PartnerSessionBehaviour` that hosts or joins a second Fencer (see "Connecting a Partner"). The camera rig's OVRManager asks for the body tracking permission at launch.
- Code: one folder per module from ADR 0002 under `Assets/VrFencing/<Module>/`, each with a `Core` assembly (`VrFencing.<Module>.Core`, plain C# with `noEngineReferences`, Edit-mode tested) and a `Unity` assembly (`VrFencing.<Module>.Unity`, thin MonoBehaviours). Core code uses `System.Numerics` vectors; `VrFencing.Settings.PoseConversions` converts to and from Unity types. Edit-mode tests live in `Assets/Tests/EditMode/` and reference every Core assembly.
- Android package id: `com.alfakentavr.vrfencing`. Entry point: the plain Activity, not GameActivity (see "Sleep/wake and the `[Life]` log").

## The scripts (repo root, bash, run from Git Bash)

The three Unity scripts drive Unity without opening its window. **They fail if the project is already open in the Unity Editor**: close the Editor first, or ask Claude to use the Unity MCP instead. They take the shared Unity lock and use the import cache when it is running (see "Dev machine setup"). Output goes to `Builds/` (gitignored).

| Script | What it does | Output |
|---|---|---|
| `scripts/run-core-tests.sh` | Runs the Edit-mode tests that need no Unity, with `dotnet test`, in seconds. No Unity, no lock | `Builds/test-results/core-tests.trx`, pass/fail counts printed, non-zero exit on failure |
| `scripts/run-editmode-tests.sh` | Runs all Edit-mode tests in Unity (the merge gate) | `Builds/test-results/editmode.xml` (NUnit XML), pass/fail counts printed, non-zero exit on failure |
| `scripts/build-apk.sh` | Builds a Development APK | `Builds/Android/vr-fencing-dev.apk` |
| `scripts/setup-quest-project.sh` | Re-applies the Quest settings and runs Meta's Project Setup Tool "Fix All" (except downloading the XR Simulator installer) | `Builds/logs/project-setup-report.json` lists what is still open |

Logs for each run: `Builds/logs/*.log`. Run the setup script again after upgrading Meta packages. If Unity is not at the default Hub path, set `UNITY_EDITOR=/path/to/Unity.exe`.

A Development Build is used on purpose: Meta XR Operator (the agent's "hands and eyes" inside the running app) is only included in Development Builds.

## Dev machine setup

Two to three coding agents work in parallel, each in its own git worktree, and take turns for Unity. Two user-level installs (PER-26, 2026-10-19) make that cheap.

### The two installs

1. **.NET SDK** (10.0.402), user-level through scoop: `scoop install dotnet-sdk`. It lands in `~\scoop\apps\dotnet-sdk\current`, and scoop sets the user variables `DOTNET_ROOT` and `MSBuildSDKsPath`. An older x86 runtime in `C:\Program Files (x86)\dotnet` can stay; `scripts/run-core-tests.sh` picks the scoop SDK first anyway (or `DOTNET=/path/to/dotnet.exe`). Used only for the fast Core tests.
2. **Unity Accelerator** v1.5.137, the local import cache: the Windows x64 installer from [Unity's download page](https://docs.unity3d.com/Manual/accelerator-download.html) (`unity-accelerator-v1.5.137+g4eddaf6a-windows-x64-installer.exe`). It needs admin rights, so a person runs it once. Installer choices: the defaults, plus security "allow insecure access with no encryption" (it is only used from this laptop), a dashboard admin login (kept in a password manager, never in the repo or a ticket), and **deny** the Windows Firewall prompt so only this laptop can reach it. It runs as a Windows service listening on `0.0.0.0:10080` (the installer reports it as `<machine name>:10080`); the scripts use `127.0.0.1:10080`. A fresh worktree then imports its ~13 GB `Library` from the cache in minutes instead of 1 to 3 hours.

To reproduce on a new machine: do both installs as above, open a new Git Bash (so the new PATH and variables are seen), and check:

```bash
~/scoop/apps/dotnet-sdk/current/dotnet.exe --version     # 10.0.402 or later
timeout 2 bash -c 'exec 3<> /dev/tcp/127.0.0.1/10080' && echo "Accelerator listening"
scripts/run-core-tests.sh                                 # passes in under a minute
```

### The import cache

The Unity scripts probe `127.0.0.1:10080` (2 s at most) before starting Unity. When the Accelerator answers they pass `-EnableCacheServer -cacheServerEndpoint 127.0.0.1:10080 -cacheServerEnableDownload true -cacheServerEnableUpload true -cacheServerWaitForUploadCompletion`, so imports download from the cache and new imports are uploaded before Unity quits. When it does not answer they print "Accelerator not reachable" and import without it, as before. The flags live in the scripts, not in the project's Editor settings, so `QuestProjectSetup` cannot undo them. `UNITY_CACHE_SERVER=off` turns it off for one run; `UNITY_CACHE_SERVER=host:port` points elsewhere.

### The Unity lock

The laptop has RAM for one Unity process. Every Unity script takes a shared lock before it starts Unity and frees it when Unity exits (also on Ctrl+C or an error, which stops that Unity). The lock is the folder `vr-fencing-unity.lock` in the main repo's git directory (`git rev-parse --git-common-dir`, so every worktree sees the same one); its `owner` file holds the script, worktree, start time and the Windows PIDs of the script's shell and of Unity. A second script waits and prints who holds the lock once a minute:

```
Unity lock: waiting 60s for build-apk.sh (worktree /c/.../worktrees/agent-x, since 2026-10-19T19:19:15Z, shell PID 59328)
```

A lock whose shell and Unity are both gone (a crashed or killed holder) is stale: the next script removes it and goes on. Remove it by hand only if no Unity is running: `rm -rf "$(git rev-parse --git-common-dir)/vr-fencing-unity.lock"`. The lock covers the scripts only, not a Unity Editor window opened by hand. `UNITY_LOCK_DIR` overrides the location.

### The fast Core tests

`scripts/run-core-tests.sh` compiles the Core assemblies (every asmdef with `noEngineReferences`) and the Edit-mode test files that need no Unity into one test project, `tools/CoreTests/CoreTests.csproj` (outside `unity/Assets`, so Unity never imports it), and runs them with `dotnet test`: about 15 s, 546 tests as of PER-85 (the Unity run has those plus the Unity-side ones). It uses Newtonsoft.Json 13.0.2 from NuGet (the version Unity's `com.unity.nuget.newtonsoft-json` 3.2.2 ships), NUnit 3 and C# 9, as Unity does. The first run downloads those packages from nuget.org into the user's NuGet cache.

A test file is left out automatically when it uses `UnityEngine`, `UnityEditor`, `TMPro`, `Oculus` or `Meta`, names a type declared in a Unity assembly or `Assets/Editor`, or names a helper from a test file that was left out. `ModuleLayoutTests.cs` is left out by name (it checks the assemblies the Editor loads); add others to `MANUAL_EXCLUSIONS` in the script. `scripts/run-core-tests.sh --list` prints which files run and why the others do not. Extra arguments go to `dotnet test`, for example `scripts/run-core-tests.sh --filter FullyQualifiedName~Blade`.

Use it while iterating; `scripts/run-editmode-tests.sh` in Unity stays the gate before a merge.

### Rules for parallel agents

- One ticket per worktree, on its own branch.
- Avoid running tickets at the same time that touch the same files.
- Iterate with `scripts/run-core-tests.sh`; it needs no lock and runs in any number of worktrees at once.
- Only one Unity process at a time: always go through the scripts (they take the lock), and do not open the Editor while an agent runs one.
- Rebase onto the latest `main` before the Unity gate (Edit-mode tests and the APK build), so the gate tests what will be merged.
- Merge one branch at a time; the next one rebases and passes the gate again.

## Installing on the headset

Use Unity's own adb (nothing to install):

```bash
ADB="/c/Program Files/Unity/Hub/Editor/6000.0.85f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
"$ADB" devices                                   # headset must show as "device", not "unauthorized"
"$ADB" install -r Builds/Android/vr-fencing-dev.apk
"$ADB" shell monkey -p com.alfakentavr.vrfencing 1   # launch
"$ADB" logcat -s Unity                            # app logs
```

Or the same through `metavr` (`npx -y metavr@1.8.1 app install Builds/Android/vr-fencing-dev.apk`, `... app launch com.alfakentavr.vrfencing`, `... capture screenshot -o shot.png`).

For Meta XR Operator on the headset (after install, once per headset reboot):

```bash
"$ADB" shell setprop debug.oculus.experimentalEnabled 1
"$ADB" forward tcp:8720 tcp:8720
"$ADB" shell setprop debug.meta_xr_operator.request_capture_permission 1
```

## Pulling the session logs

The Session module writes two JSON Lines files (one JSON object per line) to `Application.persistentDataPath`, which on the Quest is `/sdcard/Android/data/com.alfakentavr.vrfencing/files/`:

- `session-reps.jsonl`: one line per judged Rep (timestamp, session, Drill, Rep index, Target Zone, result, reaction time, tip speed, distance from the zone center, shrink tag, flag button, IOBT arm pose or null). Drills 4 to 6 add `dummyDistance`, a compound's `secondTargetZone` and `secondTouchSeconds`, a footwork call's `footworkCall` (with `targetZone` null), and `footwork`, the footwork read from the head (see "The Drills on the headset"); lines written before these fields existed read back with them null.
- `session-history.jsonl`: one line per finished Drill run with its results (success %, averages, hit map per Target Zone), used for the last-10-sessions trend.

The `armPose` object (Tracking module, IOBT upper body) holds the blade arm's `shoulder`, `elbow` and `wrist` poses at judging time in the Strip frame (metres; +x the Fencer's right, +y up, +z toward the Dummy; any joint not tracked is null), plus:

| Field | Meaning |
|---|---|
| `fidelity` | `High` (IOBT) or `Low` (the headset's quiet fallback from head and controllers only) |
| `armExtendedBeforeLunge` | `true`/`false`: was the arm extended when the lunge started; null if no lunge was found or the arm was not tracked then |
| `elbowAngleAtLungeStartDegrees` | elbow angle at the lunge start, 180 = straight |
| `lungeStartSecondsBeforeJudging` | how long before the Rep was judged the lunge started |

`armPose` is null when body tracking was not running (permission not granted, not supported, or stopped). Rules and thresholds, all in `Assets/VrFencing/Tracking/Core/`: the lunge is the fastest forward burst of the head along the Strip since the Rep's cue, reaching at least 0.9 m/s; it starts at the last moment before that peak when the head was moving forward slower than 0.2 m/s (`LungeStart.cs`). The arm counts as extended with the elbow at 150 degrees or straighter and the wrist forward of the shoulder by at least 0.6 of the arm's length (`ArmExtension.cs`). Lines written before these fields existed read back with them null.

On the headset, every judged Rep also prints a `[Tracking] Rep judged: ...` line to `adb logcat -s Unity`, and body tracking start, fidelity changes (as the runtime reports them) and missing permission are logged with the same tag. Body tracking (`BodyTrackingBehaviour`, Full Body: 84 joints, legs included, since PER-79) runs whenever the app is active and stops at once while the app is paused (headset asleep), has lost focus (system menu, lock screen), or the headset is off the head. It restarts only from the frame loop, never from a pause or focus callback, once the app is resumed and the runtime has reported VR focus, input focus, the headset connected and the user wearing it continuously for 1.5 s (`BodyTrackingLifecycle.DefaultSettleSeconds`); the same settle applies to the first start after launch (PER-71: restarting inside the wake callbacks, under the lock screen, left a black frozen view). It logs `[Tracking] Body tracking stopped (<reason>).` and `[Tracking] Starting body tracking (runtime ready for 1.5 s since <last event>).`, plus `[Tracking] OVRPlugin.<call> took <n> ms (ok|failed).` around every start, fidelity request and stop. The app asks for the body tracking permission at first launch (Meta's dialog); if it was denied, grant it with `"$ADB" shell pm grant com.alfakentavr.vrfencing com.oculus.permission.BODY_TRACKING` (or in the headset's app permissions) and restart the app.

The name is not `reps.jsonl` on purpose: an older throwaway prototype writes that name with a different schema. The newest Rep can lag by one rest period, since it is held back so the flag button can still mark it.

```bash
"$ADB" pull /sdcard/Android/data/com.alfakentavr.vrfencing/files/session-reps.jsonl Builds/
"$ADB" pull /sdcard/Android/data/com.alfakentavr.vrfencing/files/session-history.jsonl Builds/
```

`metavr files pull` works too, with the same device paths.

## Recording the Fencer and replaying it in the Editor

The Practice object's Fencer recorder (`FencerRecorderBehaviour`, PER-79) records the Fencer so the PC Editor can replay them as a moving opponent (decided in PER-55):

1. On the headset, press the off-hand thumbstick (left for a right-handed Fencer) to start; a red `REC m:ss` shows in the upper left of the view. Press it again to stop. It stops by itself after 10 minutes (about 105 MB). Place the Strip first: poses are recorded in the Strip frame (without a Strip, in the room around the scene origin).
2. Each recording is one file, `fencer-<UTC yyyyMMdd-HHmmss>.fencer`, in `/sdcard/Android/data/com.alfakentavr.vrfencing/files/recordings/`. The log prints `[Recorder] Recording the Fencer ... to <path>` and `[Recorder] Recording stopped (<reason>): <frames> frames, <s> s, <MB> MB, <path>.`
3. Pull them into the repo's gitignored `Recordings/`:

```bash
"$ADB" shell ls -l /sdcard/Android/data/com.alfakentavr.vrfencing/files/recordings/
mkdir -p Recordings
"$ADB" pull /sdcard/Android/data/com.alfakentavr.vrfencing/files/recordings/fencer-20261019-213005.fencer Recordings/
```

4. In the Editor: "VR Fencing > Replay a Fencer recording...", pick the file, press Play. It opens a scratch scene (not the passthrough scene, so the replay never reaches the Quest build) with a `Fencer Replay` object at the world origin, which stands for the recorded En Garde Line. The Scene view draws the skeleton (green at high fidelity, yellow at low), the head and the sword controller. Loop, speed (slow motion below 1), start time and the En Garde Line transform are on the component.

What is in a recording (`FencerRecordingFile` in `Assets/VrFencing/Tracking/Core/`): the 8 bytes `VRFENCER`, a JSON header (format version, joint set, Fencer height, sword hand, Strip length, app version, UTC start), then one fixed-size binary frame per rendered frame (72 Hz): time, head, sword-hand controller anchor and the 84 Full Body joints, in the Strip frame, with a validity bit per pose and the body fidelity. About 2.4 KB per frame, 10.5 MB per minute. The body's rest pose (bind pose) is stored when body tracking first gives one and again whenever the runtime re-measures the Fencer. Poses are recorded true, never mirrored for a left-handed Fencer. A file cut short by a killed app keeps every complete frame. While body tracking is stopped (asleep, unfocused, off the head) the frames carry only the head and controller; a pause shows as a jump in time, and the replay holds still across it instead of sliding.

The replay (`FencerReplayProvider`) is a Movement SDK `ISourceDataProvider`: a `CharacterRetargeter` on the same GameObject retargets it onto a rigged body exactly as it would a headset's `MetaSourceDataProvider`, and its `Sword Controller` child follows the recorded controller for the blade. Until the rigged body exists (MP1) nothing is attached, so only the gizmos show. The Core round-trip tests use a synthetic clip, `Assets/Tests/EditMode/Tracking/Fixtures/synthetic-lunge.fencer`, written by `FencerRecordingTestData`; regenerate it with `scripts/run-core-tests.sh --filter "FullyQualifiedName~WriteTheFixtureClip"` after an intended change to that synthetic lunge.

## Sleep/wake and the `[Life]` log

The Android entry point is the plain Activity (`UnityPlayerActivity`), not GameActivity (PER-71): Unity issue UUM-139694 is an intermittent ANR in GameActivity's pause/resume on Quest OpenXR, fixed only in Unity 6000.6. `QuestProjectSetup` sets it, rewrites the manifest's activity to match, and keeps Meta's Project Setup Tool from switching it back (its "Always specify single GameActivity application entry" task is excluded from Fix All, and the setup script fails if the entry point changed anyway). The build log and the OpenXR validation still warn about GameActivity; that is expected. Revisit when the project moves to a Unity with the fix.

The `LifecycleLogBehaviour` on the camera rig writes sleep/wake diagnostics with a `[Life]` tag (`adb logcat -s Unity`):

| Line | Meaning |
|---|---|
| `[Life] frame <n> t <s> s vrFocus 1 inputFocus 1 hmd 1 user 1` | Heartbeat, once per second of realtime while frames run. A gap means the frame loop was not running (paused, or frozen); a gap after a resume with no later heartbeat is the freeze |
| `[Life] OnApplicationPause(true/false) at frame <n>, t <s> s.` | Unity pause/resume; also `OnApplicationFocus(...)`, `OVRManager.InputFocusLost/Acquired`, `VrFocusLost/Acquired`, `HMDUnmounted/Mounted`, `HMDLost/Acquired` |
| `[Life] On resume: passthrough initialized, passthrough layer enabled.` | Passthrough state right after `OnApplicationPause(false)` (`pending`, `failed` or `not initialized` otherwise) |
| `[Life] passthroughLayerResumed at frame <n>, t <s> s.` | The passthrough layer was shown again after a restart |

A healthy wake reads: `OnApplicationPause(false)`, `On resume: passthrough initialized`, focus events, heartbeats with all four flags at 1, `passthroughLayerResumed`, then 1.5 s later `[Tracking] Starting body tracking (...)` and the `OVRPlugin.StartBodyTracking2 took ... ms` line.

```bash
"$ADB" logcat -s Unity | grep -e "\[Life\]" -e "\[Tracking\]"
```

## Checking memory on the headset

`scripts/check-headset-memory.sh` (PER-94) measures the app's GPU and RAM use after a fresh launch and after scripted sleep/wake cycles, and checks it against a budget, so a leak like PER-93 (one set of eye buffers, about 200 MB, leaked on every wake) shows up as a FAIL line instead of lag on the headset.

**Never run it while Nikita is wearing or testing the headset.** It fakes the proximity sensor, restarts the app and puts the headset to sleep and wakes it. It refuses to start while the headset is awake (`dumpsys power` says `mWakefulness=Awake`), because a worn headset is always awake: put the headset on the desk, wait until it sleeps, then run. `--force` skips that check; use it only when you know nobody has the headset on. Whatever happens (error, FAIL, Ctrl+C), the script hands the proximity sensor back to the hardware (`automation_disable`), so the headset ends asleep on the desk.

```bash
scripts/check-headset-memory.sh                      # app already installed, 2 sleep/wake cycles
scripts/check-headset-memory.sh --apk Builds/Android/vr-fencing-dev.apk --wakes 3
scripts/check-headset-memory.sh --self-test          # parsers and budget on fixtures, no headset
```

Steps: connect (`QUEST_SERIAL`, default `192.168.1.67:5555`, with Unity's adb or `ADB`), fake worn (`am broadcast -a com.oculus.vrpowermanager.prox_close`) and `KEYCODE_WAKEUP`, install the APK if given, force-stop and launch the app, wait 30 s, record. Each cycle: `KEYCODE_SLEEP`, 10 s, `KEYCODE_WAKEUP` plus `prox_close`, 30 s, record. A last `KEYCODE_SLEEP` makes the app log its state after the last wake. Waits: `MEMCHECK_LAUNCH_WAIT_S`, `MEMCHECK_SLEEP_S`, `MEMCHECK_WAKE_WAIT_S`.

Recorded each time: GL mtrack (GPU memory), total PSS and swap PSS of the app (`dumpsys meminfo`), and the system's `MemAvailable` (`/proc/meminfo`). From the Unity log of the run: the XR Texture count of every `[Life] gpu <moment> ... xrTex N` line. From `logcat -b system`: ANRs of the app.

| Check | Default | Override |
|---|---|---|
| GL mtrack after the fresh launch | at most 1024 MB (about 44 MB since PR 38) | `MEMCHECK_MAX_FRESH_GL_MB` |
| GL mtrack growth per wake | at most 10% of the previous value or 50 MB, whichever is larger | `MEMCHECK_MAX_WAKE_GROWTH_PCT`, `MEMCHECK_MIN_WAKE_GROWTH_MB` |
| XR Texture count at every logged moment | 3 | `MEMCHECK_EXPECTED_XR_TEXTURES` |
| ANR for the app in `logcat -b system` during the run | none | |

Output: one `PASS: ...` or `FAIL: <reasons> (...)` line (exit 0 or 1; 4 when it refused, 2 on a setup error), and one row appended to `Logs/memory-checks.csv` (gitignored): timestamp, git commit of the checkout (not necessarily what is installed), APK path if given, and the numbers, with the per-moment values separated by `;`. The raw dumps and logs of each run are in `Logs/memory-checks/<time>/`. A run that stops on an error writes no row.

It runs **on demand, not as a merge gate**: it needs the headset free and asleep, which an agent cannot know, and it takes about 2 minutes. Run it after installing a build that touches rendering, XR, passthrough or the pause/resume path, and compare the CSV rows across builds. The `[Life] gpu` lines come from `LifecycleLogBehaviour`; a build without them fails the check with "no [Life] gpu lines".

## The Strip on the headset

On launch the app looks for a Strip saved in this room (a Meta spatial anchor at its En Garde Line) and asks to place one if none is found. The off-hand controller (left for a right-handed Fencer, right for a left-handed one) drives it:

| Button | Does |
|---|---|
| Trigger | Placement: first press at the En Garde Line (over the front foot, standing en garde), second at the far end. Outside placement it is the menu pointer's select |
| Y (left) / B (right) | Re-place the Strip; press again to cancel and keep the old one |
| X (left) / A (right) | Cycle the off-strip tolerance 0 / 0.2 / 0.4 m, shown in front of you and saved to Settings |

The Strip extends behind the En Garde Line by the en garde stance (0.30 of the Fencer's height) plus the rear Warning Zone, so about 1 m of free floor is needed behind the first point. Saved Strips are listed in `strip-anchors.json` next to the settings file (`/sdcard/Android/data/com.alfakentavr.vrfencing/files/`), newest first, at most 10 rooms; each launch tries all of them and uses the newest anchor the headset finds in the room. Anchor save and restore steps are logged with a `[Strip]` tag (`adb logcat -s Unity`). Anchors need the anchor support and `USE_ANCHOR_API` permission the project already enables (`Assets/Oculus/OculusProjectConfig.asset`, `Assets/Plugins/Android/AndroidManifest.xml`).

## The Blade on the headset

The épée sits on the sword-hand controller (right by default; the Sword hand Setting moves it and mirrors its yaw): a 90 cm blade of triangular section tapering from 11 mm to 4 mm (flat face up on the grip), a 13.5 cm bell guard, a grip and pommel behind it (about 18 cm from the guard's front to the pommel, so about 1.08 m overall, within the FIE's 90 cm blade and 1.10 m weapon), and a red point. It uses no buttons. Detection and drawing live in the Blade module (`Assets/VrFencing/Blade/`); the scene's `Blade` object (`BladeBehaviour`) is made by `PassthroughSceneBuilder`.

**Grip** (PER-52, computed in PER-51; Setting "Grip"). Where the épée sits on the controller is computed from geometry, not set by feel. In the OVRCameraRig controller anchor's frame (+z forward, +y the thumb's side, origin about at the thumb web), the Touch Plus handle rises about 60 degrees above forward, and a neutral-wrist grasp holds a handle at 105 to 115 degrees to the forearm, so the forearm line, which every grip continues with the blade, is about 10 degrees below the anchor's forward axis. Each grip puts the front of the guard on the blade's line, a distance ahead of the thumb web, and cants the blade:

| Grip | Guard ahead | Pitch (up +) | Yaw (right +, right hand) |
|---|---|---|---|
| Center (default) | 0 | -10 deg | 0 |
| Pistol (Visconti, Belgian) | 5.5 cm | -12 deg | -3 deg |
| French | 7.5 cm | -13 deg | -4 deg |
| French posted | 13 cm | -13 deg | -4 deg |

The left hand mirrors the yaw and the guard's sideways offset. The "Blade pitch" and "Blade yaw" Settings show the chosen grip's angles in use and step them by 1 degree; a step saves that grip's own angle (each grip keeps its own). The old feel-test default (pitch +10, yaw -10, from PER-22) is no longer used: a settings file saved before PER-52 loads on the computed angles.

**Thrust calibration** (Settings, Blade group, "Calibrate grip"). The grip page shows the chosen grip's computed angles, the Fencer's saved ones, the old feel-test angles and the ones in use, with Calibrate, "Reset to computed" and Back. Calibrate puts the panels and the laser away and shows a dot 2 m from the shoulder (estimated 20 cm below the eyes and 18 cm to the sword side): thrust at it with a straight arm, so the shoulder, hand and point line up, and hold still. The dot turns green while the arm points at it (hand at least 40 cm from the shoulder and within 12 degrees of the shoulder's line to the dot); a hold of 1 s with the controller within 2 cm and 3 degrees takes the thrust. Three dots (straight ahead; 15 degrees inward and 8 up; 15 degrees outward and 10 down). The app then fits the pitch and yaw that put the real, drooped point on the line from the guard to each dot, averages them, saves them for the grip, and says how far apart the single thrusts were (over 8 degrees: try again). The off-hand trigger cancels without changing anything. The `[Blade] Grip calibration` log line prints the fit, its spread and the computed angles.

**Blade droop** (PER-52). A real épée hangs: its point sits 3 to 8 cm below the forte's line. The blade's set (Setting "Blade set (curve)": the gap under the middle on a flat surface, default 0.7 cm, FIE limit 1 cm) curves it toward the grip's palm side and drops the point 4 times that; gravity sags it by 2 cm (Blade flex Stiff), 3 cm (Normal) or 4.5 cm (Soft) with the blade level, less as it points up or down. Detection uses the line from the guard to that real point, so a Touch lands where the drawn point is; the drawn blade hangs in that curve and leaves the guard along the forte. Pushed straight in, the blade bends further the way it hangs (the set plus the sag), so with the thumb up it bows down as in PER-50, and with the wrist rolled it bows between the palm side and the floor.

Every frame the blade (guard front to the real point) is moved into the Strip frame and swept against the Dummy's Target Zone capsules, so a 15 m/s point cannot pass through a zone between frames. When the point enters a zone from outside every zone it is judged once:

| Event | When | Goes to | Feedback |
|---|---|---|---|
| Touch | Moving into the surface at least the minimum speed, approach and blade both within the rule's angles, and pressed (Touch pressure, below) | Drill runner as point-first, when pressed | Haptic (default Heavy), the zone flashes white, the point flashes green |
| Glance | The point reached the zone but failed the Touch test, or left without being pressed | Not reported (only the point scores) | Light haptic tick, the point flashes blue |
| Flat contact | The blade body (all but the last 3 cm) crosses a zone while the point is in none | Drill runner as flat | Haptic (default Double tap), at most once per 0.15 s |

The approach is the point's motion over the last 40 ms before it entered (stretched back up to 100 ms until the point has moved 15 mm), and the blade direction is averaged over the same 40 ms. A single 72 Hz frame was too short: at 2 m/s it is only 28 mm, so one noisy frame made fast point-first thrusts read as sideways (PER-22).

| Touch rule | Min speed into the surface | Approach within | Blade within | Point radius, blade radius, point length |
|---|---|---|---|---|
| Loose (default) | 0.02 m/s | 70 degrees | 75 degrees | 4 mm, 6 mm, 3 cm |
| Strict | 0.05 m/s | 40 degrees | 45 degrees | same |

**Touch pressure** (PER-50, Setting "Touch pressure"). A real épée scores only with at least 750 g (about 7.4 N) on the point. With "Realistic (750 g)" (the default) a point-first arrival scores only once pressed: at once if it arrives at 0.5 m/s or faster into the surface (the impact presses it), else when the straight point has been pushed 5 mm past the surface; a point that leaves before that was a light brush and reads as a glance. The Touch is reported, and its reaction time taken, when the pressure is reached; the first contact's time stays on the event (`FirstContactSeconds`) and the `[Blade]` log line prints "pressed N ms after contact". "On contact" scores at the first contact, as before. The estimate behind the numbers (`TouchRules` in the Blade Core): the FIE flexibility test (4.5 to 7 cm of tip bend under 200 g, clamped 70 cm from the point) gives the blade a bending stiffness of about 4 N m^2, so a blade clamped at the guard and pinned at the point buckles at about 100 N; at 7.4 N it has not bent at all, and the 750 g is taken by the point's own spring (about 1 mm of travel). Tracked hands jitter by about 3.5 mm, so 5 mm of push stands for it. On impact, the épée's mass at the point (about 0.15 kg) stopped by the point's spring in about 10 ms gives 7.4 N at 0.5 m/s.

Haptics play on the sword hand through the Meta Haptics SDK (OVRInput vibration does nothing under OpenXR). Strength rises with tip speed from the "Haptic strength when slow" floor (default 40%) to full at "Full haptic strength at" (default 2.0 m/s), on top of each event's pattern (Off, Tick, Crisp, Heavy, Double tap). The blade drawn whips on fast moves (PER-50): its point lags and overshoots on a change of direction, traces a larger circle than the hand on a circle parry, and rings a few decaying cycles after a sudden stop, up to 7 cm; detection never uses the flex, only the line from the guard to the resting point. It is the blade's first bending mode, a spring driven by the point's acceleration (inertia), set by the "Blade flex" Setting:

| Blade flex | First bending mode | Damping ratio | 15 cm point circle at 2 Hz / 3 Hz |
|---|---|---|---|
| Stiff | 11 Hz | 0.2 | about 0.6 cm / 1.3 cm |
| Normal (default, about a real épée) | 8 Hz | 0.1 (about four visible cycles) | about 1.3 cm / 3 cm |
| Soft (livelier than a real blade) | 6 Hz | 0.07 | about 2 cm / 5 cm |

A held blade stays still (PER-48): the point is smoothed over 20 ms (position and velocity), accelerations under 4 m/s^2 are ignored, and nothing drives the flex unless the point has moved faster than 0.2 m/s lately (full from 0.45 m/s; the recent top speed fades over 0.15 s, so a fast move still rings through its stop). Tracking jitter (about 7 mm at the point), tremor and slow aiming bend it less than 1 mm.

The drawn blade never passes through the Dummy or the floor (any capsule, Target Zone or not, and the floor plane; `BladeObstacle`, `BladeContactBend`, `BladeSideContact`, `BladeArc`, `DrawnShape` and `DrawnBlade` in the Blade Core, generic obstacles so a blade can join them later). It is always one smooth curve of the blade's full length, never folds and never hooks at the point (PER-50): the pressed blade is the large-deflection elastica of a slender rod clamped at the guard (it leaves the guard along the hand's blade) and pinned at the point (the point stays on the surface and is free to turn). The only load is the force on the point, so the curvature is zero at the point and largest near the guard and mid-blade (`BladeArc`: the pendulum form of the elastica, solved by Newton's method from the last frame's shape or a small table of shapes; about 0.08 ms per frame in the editor). It bends in the plane of the hand's blade and the line to the point, and bulges to the side the hand's blade points to from that line, so it curves over toward the point; pushed straight in, it bulges to its set, toward the grip's palm side (away from the thumb on top of the grip), turning with the wrist, or the way the point was moving sideways as it landed. A side that leans into the surface the point lands on (more than 30 degrees: a shallow press on the floor, a glancing one on the Dummy) is mirrored out of it, so the blade starts on its free side and never bulges into the floor (PER-69). The side holds through a press and changes only when the point is more than 3 cm past the hand's line on the bulge's side and that side does not lean into the surface. A blocked side never snaps the blade over to the other one: the drawn hilt lags instead, and the side only changes over while the blade is nearly straight (bulge under 1 cm); a blocked natural side also never turns the bend's plane, which used to flicker with the wrist rolled 90 degrees. It never bends more sharply than the elastica of a 20 cm straight push (about a 20 cm radius), and on a surface met at a slant (more than 20 degrees off its normal) the point never curls back past perpendicular to it (the old floor hook): it slides, or the drawn hilt lags. The hand is not stopped; the drawn blade is:

| Situation | Drawn blade | Haptic |
|---|---|---|
| The point meets a body (found along its whole path since the last frame, so a fast thrust cannot tunnel) | The point pins on that near-side surface | The event haptic (Touch, glance) as before |
| The hand pushes on, up to 20 cm past the surface straight in | The blade bows in one big smooth arc and leaves the guard along the hand (about 21 cm of bulge at 15 cm of straight push, 24 cm at 20 cm) | A buzz every 0.06 s, from 25% as the point lands to 100% at 20 cm of push |
| The hand asks for more bend than the blade has (pushed further, front or side, or turned more than 40 degrees off the line to the point), or no elastica on either side reaches the point outside every body | The drawn hilt stops following: it takes the reachable pose closest to the hand on the way to the blade lined up on the point at 20 cm of push, or holds the last shape; a faint ghost hilt shows where the hand is. It follows again once the hand is back within reach | Full-strength buzz |
| The blade's push on the point leans out of the friction cone round the surface normal (friction 0.35 on the floor, about 19 degrees; 0.7 on the Dummy's jacket, about 35 degrees). The push is along the bent blade's load line, not the hand | The point slides over the surface, never through it, to where the push is back on the cone's edge, at most as fast as the hand moves (or 1 cm a frame). A steep press (about 70 degrees or more into the floor, straight into the Dummy) pins; a shallow one skids along with the hand, keeping its bend; a turned or sideways hand makes it slide sideways | Buzz continues |
| The hand pulls back until the blade no longer pushes on the point | The point is dragged after the hand along the surface (a blade cannot pull) until it comes out | Buzz continues |
| The hand comes back out or goes around (the straight point is outside every body with a clear path to the pin) | The point lets go and the blade springs back ringing a few decaying cycles (the Blade flex Setting's mode) | Stops |
| The blade's side (not the point) is pushed or swept into a round body (the Dummy) | The blade bends round it as a cantilever (PER-69): clamped in the hand, pushed square at the contact, bent between the guard and the contact and straight past it to a free point that still whips with the flex; the contact sits where the bent blade runs along the surface and moves as the push grows. The hilt stays in the hand. Not a Touch, no pin | Flat-contact haptic as before |
| The side is pushed further than the blade can bend round, or swept through a thin part in one frame | The whole drawn blade stops at the body's near side and lags the hand (ghost hilt), until the hand goes back or around | Full buzz while it lags |
| The hand takes the side back off the body, or the contact (within 12 cm of the point) slides off the body's end | The blade lets go and springs back from where it was bent, ringing out the same way | Stops |

Each release logs `[Blade] Point released: CameOut` (or `BodyMoved` when the Dummy changes distance), and the drawn blade starting or stopping to lag the hand, or its side starting or stopping to press a body, logs a line both ways.

The floor (Strip frame y = 0, the tracking floor; the room's y = 0 before a Strip is placed) is an obstacle under the same rule (PER-49): the point pins on it, the blade bends, lags past the most bend and slides along it (the floor is slippery: a shallow press skids, PER-69), and the whole drawn blade stays above it. Floor contacts are not Touches, never reach the Drill runner and play only the press buzz.

Every event prints a `[Blade]` line to `adb logcat -s Unity`: event, zone, tip speed, approach and blade angles, distance from the zone center, and the rule. For a flat contact the approach is the motion of the blade point nearest the contact and the blade angle is against the surface normal (about 90 degrees for a true flat); no angle is ever NaN.

## The menus on the headset

World-space panels float about 1.1 m in front of the Fencer, a little below eye level; they follow the head's position and turn with it once the Fencer looks more than 40 degrees away. A laser from the off-hand controller points (tilted down from the controller's forward axis by the "Menu laser tilt" Setting, default 15 degrees; Meta's SDK has no separate aim pose for controllers under OpenXR, and its controller rotation already follows the aim pose's), its trigger presses (hold it on a settings step button to repeat). Everything mirrors with the sword hand setting. Placing the Strip hides the panels, since placement uses the same trigger.

| Screen | Holds |
|---|---|
| Home | The Drill picker, easy to hard; Drills that don't fit the Strip are greyed out with "Needs a N m Strip". Start (off while in a Partner session), Partner, Settings, History, Re-place Strip, and the off-strip tolerance (same as X / A) |
| Partner | Host (shows the Join Code), Join (a large keypad), the Bout / Paired Practice switch (Host only), Leave, Back, the app version; see "Connecting a Partner" |
| Running (during a Drill) | Small panel low on the off-hand side: Drill and Rep, the cue or why it is paused, Pause/Resume, Cancel, Flag Rep |
| Results (after each Drill) | Success %, average reaction time and tip speed, the hit map per Target Zone, the last-10-sessions trend; Run again, History, Done |
| History | Every Drill with saved runs (shortened runs listed apart), its newest run and its trend |
| Settings | Opens on seven groups (PER-70, Armed Dummy added in PER-100); pick one to see its Settings with - and +, saved at once; height and hand rebuild the Dummy and the Strip. Back goes from a group to the groups, and from the groups to Home; opening Settings from Home always starts on the groups |
| Grip (from Settings, Blade group) | The chosen grip's computed, saved, feel-test and in-use pitch and yaw; Calibrate (the thrust calibration), Reset to computed, Back (to the Blade group) |

The Settings groups, in order (`SettingGroups` in the UI Core; an Edit-mode test fails if a Setting is in no group or in two, so a new Setting must be added to one):

| Group | Settings |
|---|---|
| You | Sword hand, Your height, Menu laser tilt |
| Drills | Reps per Drill, Rest between Reps, Reaction window, Compound second Touch window, Distance choice window, Footwork call window, Shrink Drills to fit the Strip |
| Strip | Warning Zone on a full piste, Warning Zone at least, Off-strip tolerance, Shrink Drills to fit the Strip, and "Bout double window (host)" (PER-82): 50 to 150 ms in 25 ms steps, used when this Fencer hosts a bout (here because Touch is full) |
| Touch | Touch rule, Touch pressure, and the Dummy's look (PER-102): Show Target Zones, Zone opacity, Zone thickness (of scoring), Dummy body opacity. The look only changes how the Dummy is drawn, never how Touches are judged; hidden zones still show while lit or flashing. |
| Blade | Grip, Blade pitch, Blade yaw, Blade set (curve), Blade flex, and the "Calibrate grip" button (opens the grip page) |
| Haptics | Haptic Touch, flat contact, glance, strength when slow, full strength at, and the Armed Dummy's clash (PER-99): Haptic: blade clash, Clash strength when light. Full (7), so the Armed Dummy's hit buzz has no Setting |
| Armed Dummy | Parry rule (moved here from Touch in PER-100), and each Parry Drill's Riposte switch: "Riposte: Parry 6/4 slow", "Riposte: Parry 6/4 fast", "Riposte: Parry 8/7", "Riposte: Dummy lunges" (On (within 1 s) by default; Off (parry only) runs the Drill as a parry-only warm-up) |

A group holds at most 7 Settings, so each fits on one page. Folding the panels with the Menu button keeps the open group.

The Menu button on the left controller (three lines; `OVRInput.RawButton.Start`) folds every panel away (Home, Settings, Results, History and the Running panel, which otherwise sits in view during a Drill) and opens them again (PER-50). It is the left controller's whichever the sword hand (the right controller's matching button is the system's), and is none of the Strip's X, Y, A or B buttons or a trigger. The laser is off while the panels are folded. The folded state is remembered between sessions (Unity PlayerPrefs, key `VrFencing.MenuCollapsed`). When a Drill ends with results, the panels open on the Results even if folded (without changing the remembered state); the Menu button folds them again.

Pause from the menu holds the Drill until Resume, even back on the Strip; leaving the Strip pauses it as before. Development builds also list a test Drill after the real ones, "Test: needs a full piste", always greyed out, to see a greyed-out row.

Auto-pause (PER-106): when the headset goes to sleep (`OnApplicationPause(true)`), the app loses focus (the Meta menu: `OnApplicationFocus(false)`, `OVRManager.InputFocusLost` or `VrFocusLost`) or the headset comes off (`HMDUnmounted`, `HMDLost`), a running Drill pauses exactly as if the Fencer pressed Pause, and the panels open (even if folded, without changing the remembered state) so Resume is on view on return. Resume then cues the interrupted Rep again with a full window, as after any pause; the interrupted Rep is not judged or logged, and time away never counts toward a reaction time. A start still showing its task line is dropped instead (no Rep has run). Without this, the Meta menu left Unity's frames and game time running, so the open Rep and the ones after it timed out one by one; a real sleep stops the frames, so it only added up to 0.33 s (Maximum Allowed Timestep), but the Rep went on mid-window on waking. In the Editor, `OnApplicationFocus(false)` (clicking another window) is ignored. Solo Drills cannot run in a Partner session, so this never meets the Partner's sleep handling (PER-84). Log lines (`adb logcat -s Unity`): `[Drills] Auto-paused Drill '<id>' at Rep <n>, state <state> (headset asleep|app lost focus, system menu|headset off); waiting for Resume.`, `[Drills] Auto-pause: the start of Drill '<id>' is dropped (<why>).`, and on the buttons `[Drills] Pause pressed: ...` and `[Drills] Resume pressed: Drill '<id>' goes on, Rep <n>.`

## The Drills on the headset

The Drills are data in `DrillCatalog.CreateDefault` (`Assets/VrFencing/Drills/Core/DrillRegistry.cs`); the runner and the menus need nothing per Drill. When a run starts, the Practice object's `DrillRunnerBehaviour` stands the Dummy at the Drill's Dummy Distance (scaled to the Fencer's height) and logs `[Drills] <name> started: Dummy at <preset> distance, <m> m.`; when the run ends or is cancelled the Dummy goes back to lunge distance. A named-zone cue shows the zone's name above the Dummy's head (and on the Running panel); a lit-zone cue lights the zone. In Drills 1 to 3 every Rep cues one of all seven Target Zones at random, and succeeds on a point-first Touch on that zone within the window; another zone, a flat contact or the window running out fails it. In every Drill the window opens with the cue, so the logged reaction time runs from the cue (name shown, zone lit, Dummy moved or action called) to the Touch, or to the first head movement in Drill 6.

| Drill (picker order) | Dummy Distance | Cue | Window | Strip needed (end to end, run-back included) |
|---|---|---|---|---|
| 1. Point control | Extension | Named zone | 3.0 s | 1.0 m (every Strip) |
| 2. Lunge to target | Lunge | Named zone | 3.0 s | 2.5 m, no shortened variant |
| 3. Reaction: lit target | Lunge | Lit zone, dark when the window ends | Reaction window Setting (1.5 s) | 2.5 m; with "shrink drills to fit", 1.0 m at extension distance (Reps tagged shrunk) |
| 4. Distance choice | Extension, lunge or step-lunge, at random per Rep | The Dummy moves to the Rep's distance and its body lights | Distance-choice Setting (2.0 s) | 3.5 m, no shortened variant |
| 5. Compound sequence | Lunge | First zone lit, both named above the Dummy ("Hand, then thigh"); the first Touch lights the second | 3.0 s to the first Touch, then the compound Setting (1.0 s) from it to the second | 3.0 m; with "shrink drills to fit", 1.0 m at extension distance |
| 6. Footwork calls | Step-lunge (out of the way) | The action in capitals above the Dummy: ADVANCE, RETREAT, LUNGE or CHECK | Footwork-call Setting (2.0 s), judged when it ends | 3.0 m, no shortened variant |
| 7. Parry 6/4: slow | Extension (0.75 m further back for a thrust at the hand, 0.6 m at the forearm) | The Armed Dummy's thrust at the hand, forearm or chest, slow | 8 s for the attack, then 1 s from the Parry for the Riposte | 1.0 m (every Strip) |
| 8. Parry 6/4: medium, real | As Drill 7 | As Drill 7, medium and real speed in turn | As Drill 7 | 1.0 m |
| 9. Parry 8/7 | Extension | The thrust at the thigh, slow and medium in turn | As Drill 7 | 1.0 m |
| 10. Beat, then thrust | Lunge | The Armed Dummy's blade held in line at the chest | 5 s to Beat, then 1 s from the Beat to Touch | 2.5 m |
| 11. Stop hit to the arm | Extension | The thrust at the chest or thigh, slow and medium in turn | 8 s; the Stop Hit must land before the point, and 50 ms clear of it | 1.0 m |
| 12. Dummy lunges | Lunge (the Dummy lunges to extension distance) | A lunging thrust at the chest or thigh, medium and real in turn | As Drill 7 | 1.0 m |

Reps per Drill and the rest between Reps are the Settings (10 and 2 s by default); in Drill 2 the rest is the time to recover.

Rules for Drills 4 to 6 (`DrillRunner`, `FootworkJudging` in `Assets/VrFencing/Drills/Core/`):

- **Distance choice:** a point-first Touch on the body in time, with footwork read up to the Touch that suits the distance: no step at extension distance, a lunge at lunge distance, a lunge or a step-lunge at step-lunge distance (the Dummy is out of reach with less, so a long lunge that lands counts). Anything else fails as `WrongFootwork`. Without a footwork reading (no head tracking) the Touch alone decides. The Dummy moves on each cue, after the rest, and logs `[Drills] Rep <n>: Dummy at <preset> distance, <m> m.`; return to the En Garde Line during the rest, since the distance is measured from it.
- **Compound sequence:** the first zone's Touch must come within 3.0 s and the second zone's within the compound window of it. A wrong zone or a flat contact at either step fails the Rep; the second zone touched first is a wrong zone. The logged reaction time runs from the cue to the first Touch, `secondTouchSeconds` from the first Touch to the second contact.
- **Footwork calls:** no blade (contacts are ignored). When the window ends, the footwork read since the cue must answer the call (a lunge call also accepts what reads as a step-lunge); no movement is a timeout, another action `WrongFootwork`. Calls are random, but once the judged advances outnumber the retreats by 2 no advance is called, and once the retreats outnumber the advances by 1 no retreat, so the Fencer stays on a 3 m Strip.

The footwork is read from the head alone (`FootworkClassifier` in `Assets/VrFencing/Tracking/Core/`, fed every frame by the Practice object's `FootworkTrackingBehaviour` while Drill 4 or 6 runs), in the Strip frame from where the head was at the cue. Distances scale with the Fencer's height (H); the numbers below are for 1.65 m. Checked in order:

| Read as | When |
|---|---|
| Lunge | Forward head speed reached 0.9 m/s (the lunge-start threshold, `LungeStart.cs`) and the head got 0.25 H (41 cm) forward; a recovery afterwards does not change it |
| Step-lunge | As a lunge, and the head got 0.5 H (82 cm) forward |
| Retreat | The head got 0.08 H (13 cm) back, and at least as far back as forward |
| Check | The head got 0.04 H (7 cm) forward and came back at least half of that |
| Advance | The head got 0.08 H (13 cm) forward and stayed |
| None | Anything less |

The onset (reaction time of a footwork call) is when the head first moved 0.02 H (3 cm) from its place. Every judged Rep of Drills 4 and 6 logs the reading in the rep log's `footwork` object (`action`, `onsetSeconds`, `forwardTravelMeters`, `backwardTravelMeters`, `peakForwardSpeedMetersPerSecond`, `headDropMeters`) and prints `[Tracking] Footwork: ...` to `adb logcat -s Unity`; tune the thresholds from those. The results' hit map lists Drill 6 per call ("Advance: Done 2 of 3") and leaves out the tip speed.

**Parry Drills 7 to 9 (Armed Dummy, PER-100).** Each Drill's `AttackScript` (`DrillCatalog`) names the targets the Armed Dummy thrusts at (one at random per Rep: hand, forearm or chest for 6/4; the thigh for 8/7) and its speeds (in turn by Rep: slow; medium, real; slow, medium). The runner puts both in the Rep's cue (`RepCue.AttackTarget`, `AttackSpeed`) and the Blade's Armed Dummy plans the thrust from them after its random 1 to 3 s delay. The attack is the cue (`CueType.ArmedDummyAttack`): nothing is named or lit, the Running panel says "Parry, then riposte" (or "Parry the thrust" with the Riposte off), then "Riposte now!" after the Parry, and the Riposte is a point-first Touch on any Target Zone. For a thrust at the arm the Dummy stands further back than its Drill's distance on that Rep's cue (`AttackAim.GetStandBackMeters`, `ArmedDummyTuning.HandAttackStandBackMeters` 0.75 m and `ForearmAttackStandBackMeters` 0.6 m, Nikita's call in PER-100), since from extension distance a straight wrist and a 90 cm blade overshoot the hand by 20 to 50 cm. A Rep fails on a hit on the Fencer or no Parry (`NotParried`), no Riposte within 1 s (`NoRiposte`), a flat Riposte or the strict rule. The rep log adds `attackTarget` and `attackSpeed`; the results add "Parries: 6 x3, 4 x2" (deflections per line), "Hits on you: 2 (body tracking 1, headset 1)" (hits per source) and a hit map per target ("At your hand: Done 2 of 3"). Logcat: `[ArmedDummy] Rep <n>: thrust at the <target>, <speed> (...), Dummy at <m> m (<m> m back), ...`.

**Drills 10 to 12 (Armed Dummy, PER-101).**
- **Beat, then thrust** (`SuccessRule.BeatThenTouch`, `CueType.BladeInLine`): on each cue the Dummy stands at lunge distance with its arm straight and its blade in line at the Fencer's chest. A clash on its blade is a Beat once it knocks the point at least 15 cm off its line (`BeatJudge.OutOfLineMeters`, Blade Core): the larger of the clash's kick peak and the measured tip offset, watched while the blades touch and 0.15 s after. Then a point-first Touch anywhere within 1 s (`DrillDefinition.BeatTouchWindowSeconds`) succeeds. A point-first Touch before the Beat fails as `NoBeat` (a flat sweep while beating is ignored), no Touch in time as `NoTouchAfterBeat`, no Beat in 5 s as `Timeout`. The rep log adds `beatOutOfLineMeters` and `touchAfterBeatSeconds`; the results add "Beats: 8 of 10 Reps" and "After the Beat: Touched 6 of 8".
- **Stop hit to the arm** (`SuccessRule.StopHit`): the Dummy thrusts at the chest or thigh; once its wind-up starts (`DrillRunner.ReportAttackUnderWay`), a point-first Touch on its hand, wrist, forearm or upper arm is the Stop Hit. It succeeds once 50 ms pass without the Dummy's point landing (`StopHitDoubleWindowSeconds`); a landing within them is a `Double`, a landing before the Stop Hit `HitFirst`, an attack ending short without one `NoStopHit`, a Touch elsewhere `WrongZone`. Blade contact does not parry here: the thrust carries on. A point landing after the Rep was judged is locked out (no flash, no buzz). The results leave out the Parries line.
- **Dummy lunges** (`AttackScript.Lunges`): from lunge distance the whole Dummy moves forward with the thrust, by a Fencer's lunge (0.6 of its height, `ArmedDummyAttack.GetLungeMeters`) on the thrust's own profile, holds, and comes back in the recovery (`GetLungeOffsetMeters`); the speed level is the point's speed over the floor. Judged as Drills 7 to 9 (Parry, then Riposte anywhere within 1 s, with the Riposte switch "Riposte: Dummy lunges").

**The moving Dummy: distance keeper (PER-105).** A Drill may opt in to a Dummy that keeps its Dummy Distance (`DrillDefinition.DummyDifficulties`, one difficulty per Rep in turn); only the development Drill "Test: Dummy keeps distance" does so far, and every other Drill, the Armed Dummy's included, keeps its Dummy standing still (the definition refuses a keeper on an Armed Dummy or distance-choice Drill until PER-44 stage 2 merges them). It is Drill 2 (a named, lit zone, point-first Touch) at lunge distance with a 6 s window, on a 3.0 m Strip, at Easy, Medium and Hard in turn by Rep.

- **Brain** (`DistanceKeeper` in the Dummy Core): every frame it takes the Fencer's front foot along the Strip (the headset, `StartMark.HeadBehindFrontFootPerHeight` behind it) and outputs an intent, where the Dummy's front foot should stand. While the front foot stays within 6 cm (`DistanceKeeperTuning.ToleranceMeters`) of where it last set the distance, nothing moves (sway, an arm extension). Past that, a reaction starts; after its delay the target follows the Fencer as seen that delay ago, so the Dummy copies an advance, a retreat or a check one reaction late, and holds again once it has caught up.
- **Difficulty** (`DummyDifficulty`, `DistanceKeeperTuning`): the reaction delay plus an even jitter either side of it, drawn per reaction. Easy 600 +/- 120 ms, Medium 400 +/- 80 ms, Hard 250 +/- 50 ms (PER-44's starting values; tune on the headset). The rep log adds `dummyDifficulty`.
- **Body and legs** (`DummyFootwork`, PER-110): the Dummy steps to the target in real advances and retreats, at most 0.18 H (33 cm at 1.8 m, `FootworkTuning.MaxStridePerHeight`) per step, so a longer move is several steps. A full step takes 0.45 s, a short adjustment 0.25 s, each varied by up to 5% (seeded random). The body moves on a minimum-jerk curve (no jolt at start or stop); on an advance the front foot swings first and the back foot follows, on a retreat the reverse, each foot lifted about 5 cm (0.03 H), the front foot toe up and the back foot heel up in the swing. A planted foot never slides. The knees come from analytic two-bone IK in the Core (`TwoBoneIk`), bending as in en garde; the hips, torso, head and arms ride rigidly with the body, so the head stays level as in fencing footwork. A step is finished before the next one starts, even when the Fencer turns back meanwhile. The Target Zones follow the Core pose: the body, arm and head zones with the body (`DummyBehaviour.SetDistanceMeters`), the thigh and foot zones with the legs (`DummyBehaviour.SetLegs`, `DummyLayout.PoseLegs`), at under 5 cm a frame, so the Blade keeps sweeping them. Every number is in `FootworkTuning`, to tune against the filmed coach session (PER-108). Drill 12's Dummy lunge still slides the whole Dummy with its feet in en garde.
- **Strip ends** (`DummyStripLimits`): its back heel stays on the far end and its front toe never comes behind the Fencer's En Garde Line. Cornered at the far end it stays put and the Fencer closes the distance.
- **Logcat:** `adb logcat -s Unity | grep '\[Dummy\]'` prints the distance held and the limits at the start, each Rep's difficulty, and every reaction: `Fencer moved (front foot at <m> m): reacting in <ms> ms`, `Reacted: stepping toward <m> m`, `Holding at <m> m (... gap <m> m)`.

**Parry judging (Armed Dummy, PER-97).** A Drill with the Parry rule (`SuccessRule.Parry`: Drills 7 to 9 and the development Drill "Test: Armed Dummy parry feel") arms the Dummy. Each Rep it thrusts once, and a blade-on-blade contact during the thrust or its hold is a Parry only once it turns the Dummy's point off the Fencer's body (`ParryJudge` in the Blade Core):

- **Deflection:** the larger of the swing's tip offset (kicked, pushed or pressed aside) and the peak the clash's kick will reach, square to the thrust. It is watched while the blades touch and for 0.15 s after the clash (`ParryJudge.WatchSeconds`).
- **Off the body:** the rest of the thrust, shifted by the deflection and carried 25 cm past its aim (`FollowThroughMeters`), must miss every capsule of the Fencer's body: the same body the hits are judged on (below). Only with no headset pose at all does it fall back to the stand-in at the En Garde Line (`AttackAim.GetFencerBodyOnStrip`).
- **Line:** low (8, 7) when the Fencer's blade points more than 15 degrees down at the clash (`LowLinePitchDegrees`), else high (6, 4); outside (6, 8) when the point was turned toward the sword-hand side, inside (4, 7) across the body. Left-handed mirrors.
- **Parry rule** Setting (Armed Dummy group): Loose (default) takes any deflecting contact; Strict also needs the Fencer's forte (guard half) on the Dummy's foible (point half). A deflecting contact that strict rejects still turns the thrust aside, but the Rep fails as `NotForteOnFoible`.
- **Rep:** contacts with the Dummy before the Parry are ignored. An unparried thrust fails the Rep as `NotParried`, either when its point lands on the Fencer (see hits, below) or when it ends its hold without reaching them. With the Drill's Riposte on (`DrillDefinition.RequiresRiposte`), a point-first Touch on the cued zone must land within 1 s of the Parry (`RiposteWindowSeconds`), else `NoRiposte`; with it off, the Parry is the success. The rep log adds `parryLine` (`Six`, `Four`, `Eight`, `Seven`) and `riposteSeconds`.
- **Logcat:** `adb logcat -s Unity | grep ArmedDummy` prints each clash, then `[ArmedDummy] Parry <line> ... counts.` or `does not count.`, or `[ArmedDummy] Hit on the Fencer: <part>, judged by <source> ...`, or `[ArmedDummy] Thrust ended short of the Fencer: not parried, no hit.` A line `[ArmedDummy] Hits on the Fencer judged by <source> (body tracking fidelity ...)` prints whenever the source changes.

**Hits on the Fencer (Armed Dummy, PER-98).** While the Dummy's thrust or hold is unparried, its point's path each frame (the real point, swung and pushed, swept from the last frame so a fast point cannot tunnel) is tested against the Fencer's body (`FencerTarget` in the Partner Core, next to `FencerBody`, because Partner.Core already depends on Blade.Core), rebuilt every frame:

- **IOBT** (`Iobt`) when body tracking reports high fidelity and gives every joint of the 14 body capsules (`FencerBodyShape`, the same capsules a Partner's blade bends against).
- **Headset capsules** (`HeadsetCapsules`) otherwise (fidelity Low, tracking stopped, a joint missing): the en garde stand-in (`AttackAim.GetFencerStanceOnStrip`) slid along the floor so its head is under the headset's head center (5% of height behind the eyes, `HeadCenterBehindEyesPerHeight`), heights kept, plus a sword arm from its shoulder to the live guard.
- **Aims:** chest and thigh thrusts aim 3 cm inside the front of that body's torso or front thigh (`AimDepthMeters`), so they follow where the Fencer really stands; hand and forearm still follow the blade.
- **On a hit:** the thrust stops where the point met the body, holds 0.3 s, then recovers (`ArmedDummyAttack.ReportLanded`); a red flash fills the view for 0.25 s (`HitFlash`) and the Armed Dummy's hit buzz plays on both controllers (`BladeHaptics.ArmedDummyHit`, PER-99: a hard sharp jab, then a low rumble that fades, 0.28 s; unlike a Touch, glance, flat contact, clash or the bout's being-touched buzz, and fixed, with no Setting); the Rep fails as `NotParried`. The rep log adds `hitSource` (`Iobt`, `HeadsetCapsules`) and `hitPart` (`Head`, `Torso`, `SwordArm`, `SwordHand`, `BackArm`, `FrontThigh`, `FrontLowerLeg`, `BackLeg`); both are null when the thrust did not land.

**Clash haptics (Armed Dummy, PER-99).** Each blade-on-blade contact (`BladeClashDetector`, one per contact: the blades must part 3 cm before the next) plays the clash buzz on the sword hand (`ClashHaptics` in the Blade Core), at most one every 0.12 s (`ClashBuzzGate.MinIntervalSeconds`), so blades that bounce or rattle never machine-gun it. Its strength rises in a straight line with the relative blade speed square to the Dummy's blade, from the "Clash strength when light" Setting (default 35%) to full at 2 m/s (`FullStrengthSpeedMetersPerSecond`). The "Haptic: blade clash" Setting picks the pattern; each is a sharp hit that rings down, unlike the Touch, glance and flat contact:

| Pattern | Clash buzz |
|---|---|
| Tick | A lighter hit and a short ring, 35 ms |
| Crisp (default, the prototype's PER-95 buzz) | A sharp high hit ringing down, 85 ms |
| Heavy | The same hit with a longer, lower ring, 130 ms |
| Double tap | Two rings close together, like a rattle, 110 ms |

The logcat clash line ends with `buzz <strength>` or `buzz skipped (rate limit)`. The defaults stay the prototype's until its feel test (PER-95) picks others.

All world-space text is TextMeshPro (SDF shader, drawn in both eyes under single-pass instanced stereo). Its essentials live in `Assets/TextMesh Pro/`; `VrFencing.EditorTools.TextMeshProSetup.ImportEssentialsIfMissing` re-imports them in batchmode (run without `-quit`, it exits itself). To check the panels' layout without the headset, `VrFencing.EditorTools.MenuSnapshots.RenderAll` (menu "VR Fencing > Render menu snapshots", batchmode without `-nographics`) renders every screen with sample data to `Builds/menu-snapshots/`.

## Connecting a Partner

The Partner page (PER-80, decided in PER-58; module `Assets/VrFencing/Partner/`) connects two Fencers through Unity's Multiplayer Services: Unity Services with an anonymous sign-in, a private 2-player session on Relay, and Netcode for GameObjects. The project is linked to the Unity Cloud project "VR Fencing Trainer" (project ID `fc12ae47-8dcf-4350-9d81-f13c0d13dc3c`, organization `2474804760271`, PER-60) in `ProjectSettings.asset`; `QuestProjectSetup` writes the link again on every run, and an Edit-mode test checks it. Relay and Lobby are switched on in the Unity Dashboard; no payment method was needed so far (usage is about 58 MB per bout-hour, PER-54).

- **Host** (Home > Partner > Host) signs in, creates the session and shows its 6-character Join Code in large letters, to read out over voice. The session stays up for 5 minutes when the Partner leaves or drops, with the same code (see "Drops, rejoin and session logging").
- **Join** opens a large keypad with the 20 characters a Sessions code can hold (`6789BCDFGHJKLMNPQRTW`: no vowels, no 0, O, 1 or I), plus Delete, Clear, Cancel; Join is on once 6 characters are typed.
- **Mode** (Host only, once the session is up): Bout or Paired Practice; the Partner sees the Host's choice, and the switch works any time without reconnecting (see "The bout flow and Paired Practice"). The Opponent shows in both modes.
- **Leave** closes the session; the Host leaving ends it for both.
- While in a session (from Host or Join until Leave) solo Drills cannot start ("Leave the Partner session to run solo Drills" on Home) and the Dummy is hidden, so the Blade has nothing to touch.
- **Version check:** the joining side sends its app version (Player Settings > Version, `Application.version`) with its connection request, and the Host refuses a different one; both pages say "Different app versions: host X, partner Y". Bump the Version whenever what goes over the network changes.

Every step logs a `[Partner]` line (`adb logcat -s Unity`): sign-in, the code, the partner connecting, refusals with both versions, leaving.

### The Opponent (body and blade sync, PER-81)

While connected, the Partner object's `OpponentLinkBehaviour` streams this Fencer to the other app and its `OpponentViewBehaviour` draws the other Fencer as the Opponent (decided in PER-59, PER-57). Rules and numbers are in `Assets/VrFencing/Partner/Core/`; the Unity side only moves bytes and draws.

| Stream | Rate | Delivery | Holds |
|---|---|---|---|
| Info | on change and every 1 s | reliable | this Fencer's placed Strip (whole length, rear end), height, sword hand |
| Hand | 72 per second | unreliable | the sword-hand controller pose with velocity and angular velocity (over the last 40 ms), and the grip, its angles in use, set and flex |
| Body | 30 per second | unreliable | the rig's 19 joints (the Dummy's skeleton joints plus the back hand's end) in millimeters, the headset's turn, the body fidelity |

All poses go true and unmirrored in the sender's own Strip frame (the room origin without a Strip). They travel as Netcode named messages (`VrFencing.Opponent`), with no network objects; each message starts with its kind and a format version, and unreadable ones are dropped and counted (`[Opponent] Dropped ...`). This sends the rig's joints (about 4 KB/s) instead of Movement SDK's `NetworkCharacterRetargeter` stream, since the rig is posed on joints (PER-78).

- **Shared Strip** (`BoutStrip`): the shorter of the two placed Strips, run-backs included, anchored at each Fencer's own rear end. The En Garde Lines are 4 m apart about the bout centre, shrunk on a short Strip so each Fencer keeps at least 1 m to their rear end. The receiver turns the partner's poses 180 degrees about the bout centre: a turn, not a mirror. While connected, the Strip's two drawn En Garde Lines move to the bout's (PER-83); its ends, centre line and Warning Zones stay each Fencer's own. The bout's sizes are logged (`[Opponent] Partner streams arriving: ... Bout Strip ... m, En Garde gap ... m`).
- **Smoothing** (`HandStream`, `BodyStream`): the partner's clock is the smallest send-to-arrival gap seen. The hand is Hermite-interpolated 40 ms behind the partner's now, then predicted 50 ms ahead along its velocity, at most 15 cm and 30 degrees: view only, never judged. The body is interpolated 100 ms behind, with no prediction; its sword wrist and hand are carried onto the drawn hilt so they stay on the blade. A hand silent for 0.5 s hides the blade; a body silent for 0.5 s stays as a ghost.
- **Body** (`OpponentViewBehaviour`): the Dummy's rig (`Armature.fbx`) in the Dummy's look at the "Dummy body opacity" Setting, built for the partner's height and hand. The generated legs always fade (to 50% at high fidelity, 30% at low); the torso and arms always show (the model's arms are one mesh with the sword arm). The whole body drops to 25% while it is not coming (asleep, unfocused, link lost).
- **Blade**: built from the partner's hand and grip exactly as the Fencer's own (`BladePose`, `BladeDroop`, `DrawnBlade`), so it hangs, whips and bends the same. It bends against this Fencer's body (capsules from this Fencer's tracked joints, Dummy-sized), the floor and this Fencer's drawn blade. This Fencer's blade bends on the Opponent's body the same way. The drawn blades are cosmetic; Touches on the Opponent are judged by the bout (below).
- **Version:** Player Settings > Version is 1.1 from this slice; 1.0 builds are refused (1.2 from the judge, PER-82).

### The judge and the scoring box (PER-82)

While connected, the Partner object's `BoutBehaviour` judges Touches between the two Fencers (rules decided in PER-56, feedback in PER-74). The rules and numbers are plain C# in `Assets/VrFencing/Partner/Core/` (`BoutJudge`, `ClockSync`, `BoutLink`, `OpponentTouchSensor`, `ScoringBox`); the Unity side moves bytes, plays haptics and draws. It runs in both modes; the bout flow decides when Touches count (below).

- **Detection, attacker side:** each headset tests its own blade (the guard to the drooped point, built from the sword-hand controller and grip settings exactly as the Blade does) against the Opponent's body as drawn there (the 14 body capsules, `FencerBodyShape`), with the Dummy's `TouchDetector`: this Fencer's Touch rule, the Host's Touch pressure. A Touch is claimed with the time the point was pressed; a glance plays the glance haptic and leaves a faint blue mark for 1 s (only the attacker sees it); a flat contact plays its haptic. Neither ever lights. Blade on blade is cosmetic only, as in PER-81. The zone names in the logs: head and torso "Body", shins with the feet "Foot".
- **Off the Strip:** a Touch made with both feet (the ankles, else the toes, else the head) off the side or past the rear end of the attacker's own placed Strip, past the off-strip tolerance, is claimed as off-Strip and never lights. Without a placed Strip (the PC Editor's replay) nothing is off.
- **Clock:** both apps send a ping every 0.5 s and answer the other's at once (`ClockSync`). Of the last 16 round trips, the quickest sets the other app's clock offset (its error is at most half that round trip); the median is the ping shown. The Partner puts its claims on the Host's clock with it; a Touch before the first answer is not claimed (logged). All on `Time.realtimeSinceStartupAsDouble`, the streams' clock.
- **Judge (Host only, `BoutJudge`):** the first Touch wins; the other Fencer's Touch within the double window of it (either way) makes a double. It lights the double window plus 70 ms after the earliest Touch (120 ms at the default 50 ms window), so the other claim is normally in by then; if one still arrives within the window (and not stale) after a single lit, the light becomes a double. Claims older than 200 ms on arrival, or more than 200 ms ahead of the Host's clock, are refused; so is a second Touch by the same Fencer in one phrase. Touches count only inside the phrase: in a Bout from "Allez!" until a halt or the phrase's light (nothing counts again until the next "Allez!"); in Paired Practice the next Touch counts right after a light, and off-Strip Touches light too (PER-83).
- **Rules:** the Host's Touch pressure and double window (Settings > Strip > "Bout double window (host)", 50 to 150 ms) are the bout's; the Host sends them on change and every second, and the Partner uses them instead of its own.
- **Scoring box:** floats 2.3 m above the bout Strip's centre, facing this Fencer, 0.7 m wide. In each headset this Fencer's lamp is green on their left, the opponent's red on their right; both light for a double; lit 2 s. "Ping N ms" is written under it.
- **Being touched:** when the opponent's lamp lights (their Touch or a double), both controllers play a distinct short buzz (`BladeHaptics.BeingTouched`: a low rumble, a gap, a sharp pulse, 0.2 s). The attacker's own Touch plays the usual Touch haptic and point flash at once, before the light.

| Message (`VrFencing.Bout`) | Direction | Delivery | Holds |
|---|---|---|---|
| Ping, Pong | both ways, 2 per second | unreliable | the sender's time; the echo and the answering app's time |
| Claim | Partner to Host | reliable | a sequence number, the attacker, the zone, the pressed time on the Host's clock, off-Strip |
| Light | Host to Partner | reliable | its number, single (Host or Partner) or double, the zones |
| Rules | Host to Partner | reliable, on change and every 1 s | the Touch pressure, the double window |

Log tag `[Bout]` (`adb logcat -s Unity`): which side judges, the rules, every Touch, glance or flat contact on the opponent, every claim and the judge's verdict on the Host (`Accepted`, `MadeDouble`, `Stale`, `FromTheFuture`, `OffStrip`, `LockedOut`, `AlreadyClaimed`), and every light. The PC Editor's Partner window shows the ping, the rules in force and the last light.

**Version:** Player Settings > Version is 1.2 from this slice; 1.1 builds are refused (1.3 from the bout flow, PER-83).

### The bout flow and Paired Practice (PER-83)

The Host runs the bout flow (`BoutFlow`, plain C# beside the judge, decided in PER-56, PER-57, PER-58, PER-72 and PER-74) and sends it with the Paired Practice tally; each headset reads its own Fencer's stance, plays the calls and draws the text. Rules and numbers are in `Assets/VrFencing/Partner/Core/` (`BoutFlow`, `FencerStance`, `BoutCall`, `BoutAnnouncer`, `PairedPracticeTally`).

- **Stance** (`FencerStance`, read by each headset on its own Strip with its own off-strip tolerance): behind the bout En Garde Line when the front-most foot point (ankles and toes; the head with no foot tracked) is no more than 10 cm past it; off the Strip as for the Touch rule (both feet off a side, or past the rear end). The Partner sends its stance 10 times a second; the Host treats one older than 0.5 s as unknown (never ready).
- **Bout:** when both Fencers stand behind their lines and on their Strips for 1 s, both headsets call "En garde!", "Prets?" 1 s later and "Allez!" 2 s after the start (voice and large text above the scoring box). The phrase opens on "Allez!". The Partner times the call on the synced clock, so "Allez!" sounds together on both headsets (to the clock's error), not a one-way trip late. A call seen more than 0.5 s late (a rejoin) is written, not spoken.
- **Halts** ("Halte!" with the reason under it, for 2 s): a light ("Your Touch", "Opponent's Touch", "Double"); a Fencer off their own Strip ("You left the Strip (side)", "(rear end)"); corps-a-corps, the two bodies' chests (else hips, else heads) closer than 0.5 m across the floor, measured on the Host. A Touch made before the halt still lights. After the halt the line under the box says "Go back behind your En Garde Line" or "Waiting for your opponent to go back" until the next call.
- **Paired Practice:** no calls, no halts, no lock-out; off-Strip Touches light. Under the box: both tallies ("You: 3 Touches, 1 double, 2 glances" and the opponent's), with "You are off the Strip" above them while off. A double counts for both and not as a Touch; a glance counts for its attacker. Bodies within 0.5 m fade the whole opponent to 30%. Either Fencer resets the tally (Partner page > Reset tally, or the Editor window's button).
- **Session log:** a tally that ends (reset, switched to a Bout, session ended or the partner not back within 5 minutes) is saved, if not empty, as one JSON line in `session-paired-practice.jsonl` beside the session history, from that headset's side: session id, tally number, time, role, why it ended, `you` and `opponent` counts. A drop or a sleep saves it so far (`Interrupted`, PER-84, below). Pull it like the other logs.
- **Voice** (PER-75): four LoquaxFR clips from Wikimedia Commons in `Assets/VrFencing/Partner/Unity/Resources/BoutCalls/`, trimmed and normalized (see `CREDITS.md`; the attribution also shows at the foot of the Partner page). A missing clip plays a tone instead (lower for "Halte!").

| Message (`VrFencing.Bout`, PER-83) | Direction | Delivery | Holds |
|---|---|---|---|
| Stance | Partner to Host, 10 per second | unreliable | behind the line, off the Strip (side or rear end) |
| Flow | Host to Partner, on change and every 1 s | reliable | the mode, phase and its count, the last halt (reason, who, which way, what lit), the call's start on the Host's clock, both readiness flags, both tallies |
| Glance, Tally reset | Partner to Host | reliable | nothing more |

Log lines (`[Bout]`): every phase change ("Both behind their En Garde Lines: En garde! Prets? Allez!", "Allez: the phrase is on.", "Halte! <reason>.", "Waiting for both Fencers ..."), mode changes, tally resets and each saved tally. The PC Editor's Partner window adds a "Flow" line, the prompt or tally, and a Reset tally button.

**Version:** Player Settings > Version is 1.3 from this slice; 1.2 builds are refused.

### Drops, rejoin and session logging (PER-84)

Decided in PER-73. The rules and numbers are plain C# in `Assets/VrFencing/Partner/Core/` (`PartnerSession`, `OpponentLink`, `BoutLink`, `TallyNumbering`); `PartnerSessionBehaviour`, `OpponentViewBehaviour` and `BoutBehaviour` act on them. Nothing new goes over the network (the "host left" reason is Netcode's own disconnect reason), so the Version stays 1.3.

- **Lag spike:** no hand or body message for 0.5 s (`OpponentLink.SignalLostSeconds`): the opponent holds its last pose, faded to 25% (as in PER-81), its blade is hidden, and an amber "(!) No signal" sign floats 35 cm above its head, turned to this Fencer. No Touch is judged on a faded opponent; claims older than 200 ms are refused by the judge as before.
- **Partner drops or sleeps:** the Host's page shows the code with "Waiting for partner: rejoin with this code within 4:59", counting down 5 minutes (`PartnerSession.RejoinWindowSeconds`). A rejoin with the same code in time carries the session on: same mode, same Paired Practice tally (the Bout starts again from the En Garde Lines). After 5 minutes the Host ends the session ("Partner did not rejoin within 5 min. Session ended") and hosts again for a new code. A Partner who presses Leave counts the same: the Host cannot tell a Leave from a drop.
- **Host leaves or sleeps:** the session ends for both, with no host migration. On Leave the Host tells the Partner first ("The host left", Netcode's disconnect reason); the Partner then goes back to Home (solo practice) with "Host left. Back to solo practice". If the connection just ends (the Host slept, or either network dropped), the Partner goes to Home with "Host left or the link dropped. Rejoin: Partner > Join (5 min)", and Partner > Join opens the keypad with the code already typed: one press rejoins if the Host is still waiting. If a rejoin by code is refused because the dropped Partner is still listed in the session, the app reconnects to that session instead.
- **Sleep:** a headset asleep for 10 s or more (`PartnerSession.SleepEndsSessionSeconds`, about Relay's idle timeout; measured on the wall clock) ends its session on waking: the Host's with "Session ended while your headset slept. Host again", the Partner's with the code kept to rejoin. A shorter blink keeps the session if the connection survived. A failed transport ends the session too.
- **Session log:** each headset writes its own Paired Practice tally up to the drop: when the connection drops, and when the headset goes to sleep, the tally so far is saved as `"end": "Interrupted"` (the app may never wake). The Host keeps counting from it after a rejoin; if the Partner does not come back, it is saved again as `Disconnected` and reset. Every line now has a `tallyNumber` (schema 2; 0 on older lines): the records of one tally share it within a `sessionId`, and the last one holds the whole tally. A tally that ends for good (reset, switched to a Bout, disconnected) moves on to the next number. The Partner knows a rejoin carried its tally on when the first tally it hears is not empty (nothing can be counted while it is away).

Log lines: `[Partner]` "Partner dropped or left ...; the session stays up with the same code for 300 s.", "Partner rejoined (client N); the session carries on.", "The partner did not rejoin within 300 s; ending the session.", "Lost the host (HostLeft|LostHost): <reason>.", "Woke after 10 s or more asleep; the session ends.", "Told the partner the host left.", "Session ended (<why>)."; `[Opponent]` "No data from the partner for 0.5 s: holding the last pose, faded, connection icon on." and "Partner data back after N s without it."; `[Bout]` "The connection to the partner dropped: the Paired Practice tally so far is saved and kept for a rejoin.", "The partner rejoined: the bout goes on, Paired Practice tally kept (...)", "Paired Practice tally N saved so far (Interrupted): ...". `[UI] Back to solo practice: <message>` on the Partner. The `[Life]` lines (above) show the sleep itself.

**In the PC Editor** (Partner window, while connected): "Simulate a lag spike (2 s without data)" stops sending the replay's body and hand for 2 s, so the headset shows the connection icon and the faded last pose; "Simulate a dropped connection" cuts this app's connection without leaving the session. As the Partner, the window then keeps the code in "Join code" for a rejoin; as the Host, the session ends for both.

**The PC Editor as the Opponent:** open "VR Fencing > Replay a Fencer recording...", press Play, open "VR Fencing > Partner session" and Join or Host as above. The window says "Sending: the Fencer replay in this scene", and the recording's Fencer goes out as the Partner (the recording's Strip length stands in for a placed Strip). The headset's Fencer comes back and is drawn in the Editor's Game and Scene views, on the far side of the replay's Strip at the world origin. One Editor cannot be both sides: two apps are needed (a headset and the Editor, or two Editors from two clones).

**The PC Editor as the Partner:** press Play (any scene; the window makes a Partner object if the scene has none), open "VR Fencing > Partner session", type the headset's code and press Join, or press Host and type the Editor's code on the headset. "Pretend version" sends another version, to see a refused join without a second build. From the command line, `-partnerJoin FGR78N` or `-partnerHost` does the same at start.

**Online tests** (need the internet, so outside the Edit-mode gate): `Assets/Tests/PlayMode/PartnerOnlineTests.cs` signs in, creates a Relay session, checks its code fits the keypad and leaves it, and checks a join with a code that has no session ends with the reason. Run them in batchmode with `-runTests -testPlatform PlayMode -testCategory Online`. A host-and-join needs two apps (one signed-in player per app), so it is tested between the headset and the Editor.

## The MCP servers (`.mcp.json`)

MCP servers are plug-ins that give Claude extra tools. They are configured for this repo only, in `.mcp.json` at the repo root; Claude Code asks once whether to trust them.

- **UnityMCP** (CoplayDev "MCP for Unity"): lets Claude drive the open Unity Editor: create and edit scenes and GameObjects, edit scripts, read the console, run tests, enter Play mode, build, take screenshots. The Unity package starts a small Python server (through `uvx`, pinned to 10.3.0) and Claude talks to it at `http://localhost:8080/mcp`. It only works while the Editor is open and the server is started (Window > MCP for Unity > Start Server, or turn on "Auto-start on load" under Advanced). Do not press its "Configure" button for Claude Code: `.mcp.json` already does that, and the button rewrites Claude's config.
- **metavr** (Meta VR CLI, run through `npx`, pinned to 1.8.1, nothing installed globally): device and app control on the Quest (install, launch, logcat, screenshots, files, performance traces), plus Meta documentation search. Needs the headset connected and USB debugging allowed.
- **Meta XR Operator** is not in `.mcp.json`: it is an MCP server inside the running app (port 8720) in the Editor's Play mode, in the Meta XR Simulator, or on the headset through the `adb forward` above. It reads poses, scene and screen, and can move the controllers and press buttons for scripted tests.

When Meta's `agentic-tools` Claude Code plugin is installed, it brings its own `metavr` server (unpinned). Keep one of the two: either remove `metavr` from `.mcp.json`, or disable the plugin's copy.

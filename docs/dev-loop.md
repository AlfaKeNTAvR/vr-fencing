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

- Scene: `Assets/Scenes/Passthrough.unity`, the only build scene, kept up to date by `PassthroughSceneBuilder` (menu "VR Fencing > Update passthrough scene", or batchmode `-executeMethod VrFencing.EditorTools.PassthroughSceneBuilder.UpdatePassthroughScene`). Meta's camera rig with passthrough as the background, and the Strip with its En Garde Line transform (the Strip frame). The Dummy sits under the En Garde Line at lunge distance, so it moves with the Strip; it reads the Fencer's height and hand from the settings file at start. Until a Strip is placed or restored, the En Garde Line is at the scene origin.
- Code: one folder per module from ADR 0002 under `Assets/VrFencing/<Module>/`, each with a `Core` assembly (`VrFencing.<Module>.Core`, plain C# with `noEngineReferences`, Edit-mode tested) and a `Unity` assembly (`VrFencing.<Module>.Unity`, thin MonoBehaviours). Core code uses `System.Numerics` vectors; `VrFencing.Settings.PoseConversions` converts to and from Unity types. Edit-mode tests live in `Assets/Tests/EditMode/` and reference every Core assembly.
- Android package id: `com.alfakentavr.vrfencing`.

## The scripts (repo root, bash, run from Git Bash)

All three drive Unity without opening its window. **They fail if the project is already open in the Unity Editor**: close the Editor first, or ask Claude to use the Unity MCP instead. Output goes to `Builds/` (gitignored).

| Script | What it does | Output |
|---|---|---|
| `scripts/run-editmode-tests.sh` | Runs the Edit-mode tests | `Builds/test-results/editmode.xml` (NUnit XML), pass/fail counts printed, non-zero exit on failure |
| `scripts/build-apk.sh` | Builds a Development APK | `Builds/Android/vr-fencing-dev.apk` |
| `scripts/setup-quest-project.sh` | Re-applies the Quest settings and runs Meta's Project Setup Tool "Fix All" (except downloading the XR Simulator installer) | `Builds/logs/project-setup-report.json` lists what is still open |

Logs for each run: `Builds/logs/*.log`. Run the setup script again after upgrading Meta packages. If Unity is not at the default Hub path, set `UNITY_EDITOR=/path/to/Unity.exe`.

A Development Build is used on purpose: Meta XR Operator (the agent's "hands and eyes" inside the running app) is only included in Development Builds.

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

- `session-reps.jsonl`: one line per judged Rep (timestamp, session, Drill, Rep index, Target Zone, result, reaction time, tip speed, distance from the zone center, shrink tag, flag button, IOBT arm pose or null).
- `session-history.jsonl`: one line per finished Drill run with its results (success %, averages, hit map per Target Zone), used for the last-10-sessions trend.

The name is not `reps.jsonl` on purpose: an older throwaway prototype writes that name with a different schema. The newest Rep can lag by one rest period, since it is held back so the flag button can still mark it.

```bash
"$ADB" pull /sdcard/Android/data/com.alfakentavr.vrfencing/files/session-reps.jsonl Builds/
"$ADB" pull /sdcard/Android/data/com.alfakentavr.vrfencing/files/session-history.jsonl Builds/
```

`metavr files pull` works too, with the same device paths.

## The Strip on the headset

On launch the app looks for a Strip saved in this room (a Meta spatial anchor at its En Garde Line) and asks to place one if none is found. The off-hand controller (left for a right-handed Fencer, right for a left-handed one) drives it:

| Button | Does |
|---|---|
| Trigger | Placement: first press at the En Garde Line (over the front foot, standing en garde), second at the far end |
| Y (left) / B (right) | Re-place the Strip; press again to cancel and keep the old one |
| X (left) / A (right) | Cycle the off-strip tolerance 0 / 0.2 / 0.4 m, shown in front of you and saved to Settings |

The Strip extends behind the En Garde Line by the en garde stance (0.30 of the Fencer's height) plus the rear Warning Zone, so about 1 m of free floor is needed behind the first point. Saved Strips are listed in `strip-anchors.json` next to the settings file (`/sdcard/Android/data/com.alfakentavr.vrfencing/files/`), newest first, at most 10 rooms; each launch tries all of them and uses the newest anchor the headset finds in the room. Anchor save and restore steps are logged with a `[Strip]` tag (`adb logcat -s Unity`). Anchors need the anchor support and `USE_ANCHOR_API` permission the project already enables (`Assets/Oculus/OculusProjectConfig.asset`, `Assets/Plugins/Android/AndroidManifest.xml`).

## The MCP servers (`.mcp.json`)

MCP servers are plug-ins that give Claude extra tools. They are configured for this repo only, in `.mcp.json` at the repo root; Claude Code asks once whether to trust them.

- **UnityMCP** (CoplayDev "MCP for Unity"): lets Claude drive the open Unity Editor: create and edit scenes and GameObjects, edit scripts, read the console, run tests, enter Play mode, build, take screenshots. The Unity package starts a small Python server (through `uvx`, pinned to 10.3.0) and Claude talks to it at `http://localhost:8080/mcp`. It only works while the Editor is open and the server is started (Window > MCP for Unity > Start Server, or turn on "Auto-start on load" under Advanced). Do not press its "Configure" button for Claude Code: `.mcp.json` already does that, and the button rewrites Claude's config.
- **metavr** (Meta VR CLI, run through `npx`, pinned to 1.8.1, nothing installed globally): device and app control on the Quest (install, launch, logcat, screenshots, files, performance traces), plus Meta documentation search. Needs the headset connected and USB debugging allowed.
- **Meta XR Operator** is not in `.mcp.json`: it is an MCP server inside the running app (port 8720) in the Editor's Play mode, in the Meta XR Simulator, or on the headset through the `adb forward` above. It reads poses, scene and screen, and can move the controllers and press buttons for scripted tests.

When Meta's `agentic-tools` Claude Code plugin is installed, it brings its own `metavr` server (unpinned). Keep one of the two: either remove `metavr` from `.mcp.json`, or disable the plugin's copy.

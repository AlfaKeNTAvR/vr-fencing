# Agentic dev tooling: how Claude builds, tests and validates the Quest app, and what Nikita tests by hand

Ticket: PER-25. Researched 2026-10-18.

## Answer

- **Meta now ships first-party agent tooling, and it is the backbone.** The **Meta VR CLI (`metavr`, formerly `hzdb`)** is a CLI plus MCP server (~40 tools) that wraps adb: install/launch/stop apps, logcat, headset screenshots, file push/pull, proximity sensor and boundary toggles, Perfetto perf traces, Meta docs search, and control of the Meta XR Simulator. It installs into Claude Code as a plugin (`meta-quest/agentic-tools`, Apache-2.0) together with ~37 Quest skills (Unity code review, MRUK, Movement SDK, perf debugging, "verify first" against current Meta docs) [1][2][3][4].
- **Meta XR Operator (experimental, in Meta XR Core SDK v207+) lets the agent "put the headset on".** It is an OpenXR API layer that runs an MCP server inside the running app (port 8720; in Editor/Link/Simulator on Windows, and on Quest in Development Builds via `adb forward`). The agent can read head/controller poses, scene graph, UI and composited passthrough+virtual images, and **set controller poses and button/trigger input**, so it can drive a scripted lunge into a target on a real headset. Head pose cannot be overridden on-device [5][6].
- **Unity Editor control: use a community MCP server, keep official Unity MCP as an option.** CoplayDev/unity-mcp (formerly justinpbarnett, MIT, ~15k stars, pushed 2026-10-17) is the most mature: scenes, GameObjects, scripts, assets, console, run tests, play mode, build, screenshots. IvanMurzak/Unity-MCP (Apache-2.0, ~4.5k stars) and CoderGamester/mcp-unity (MIT, ~1.9k stars, Unity 6+) are alternatives. Unity's official MCP (in the AI Assistant package) is still **open beta** and needs a Unity Cloud project plus a paid AI subscription (Personal: $10/month after a 14-day trial, per third-party reports) [7][8][9][10][11][12][13].
- **Headless builds and tests work from the command line:** `Unity.exe -batchmode -projectPath ... -executeMethod Build.Android -buildTarget Android -logFile - -quit` for APKs, and `-runTests -testPlatform EditMode|PlayMode -testResults results.xml` for Unity Test Framework (NUnit XML). Batchmode cannot open a project the Editor already has open, so pick per run: build through the Editor MCP, or close the Editor first [14][15].
- **Without a headset an agent can verify logic, not feel.** Edit-mode tests cover the swept tip-touch math, rules, scoring and drill state machines; Input System `InputTestFixture` and Play-mode tests drive simulated input; Meta XR Simulator (standalone OpenXR runtime, Windows, Quest 3 profile, synthetic rooms with passthrough and scene data, VRS record/replay) plus Meta XR Operator lets the agent run the app with scripted controller poses and screenshot it [16][17][18][19]. It cannot verify real tracking under fast lunges, latency, haptic feel, passthrough comfort, blade-to-hand alignment, IOBT quality or thermals.
- **On the real headset the agent can do almost everything itself once USB debugging is authorised:** install, launch, logcat, screenshots, pulling log files, OVR Metrics Tool CSV capture via adb broadcasts, Perfetto traces, disable proximity sensor and boundary for bench testing, and drive inputs through XR Operator [1][3][6][20]. Video: scrcpy works with Quest 3 per user reports; passthrough may come out black in adb/scrcpy captures (unverified for current Horizon OS) [21][22].
- **Nikita's part is a short, structured "wear it" pass:** blade feel and alignment, tracking during real lunges, haptic timing/strength, false/missed touches as felt, comfort and safety. Report through an in-app rep log (JSONL written to the app's persistent data folder, which the agent pulls with `metavr files pull`), a one-button "flag this rep" on the controller, and a 5-item checklist answered in a text file or Linear comment.
- **Setup only Nikita can do:** Meta developer account/org, Developer Mode in the Meta Horizon phone app, accepting "Allow USB debugging" in the headset (once per PC), Unity Hub sign-in and licence activation, approving MCP connections in the Unity Editor, the one-time screen-capture consent dialog in the headset for XR Operator, and physical safety (clear space) whenever the boundary is disabled [3][6][14].

## 1. Agent control of the Unity Editor

| Option | License / cost | Activity (2026-10-18) | Windows | What it can do | Notes |
|---|---|---|---|---|---|
| **Unity MCP (official, `com.unity.ai.assistant`)** | Proprietary; needs Unity Cloud project + Unity AI subscription or trial; open beta since May 2026 | Beta, "may change or be discontinued" | Yes (relay binaries for Win/macOS/Linux) | Scene hierarchy, GameObjects, component read/write, C# script create/edit, console logs, build settings inspection, custom C# tools. Test runs and screenshots not mentioned in the launch post | Personal plan: 1 concurrent MCP connection, subscription required (third-party pricing summaries; forum complaints about credits) [7][8][9] |
| **CoplayDev/unity-mcp** ("MCP for Unity", formerly justinpbarnett/unity-mcp) | MIT, free | ~15.1k stars, last push 2026-10-17, v10.3.0 (2026-10-04) | Yes | 50 tool entry points: scenes, GameObjects, scripts, assets, run tests, profile, build, camera screenshots, menu items | Unity 2021.3 to 6.x, Python 3.10+ via `uv`; auto-configures Claude Code [10][11] |
| **IvanMurzak/Unity-MCP** ("AI Game Developer") | Apache-2.0 | ~4.5k stars, last push 2026-10-18, v1.2.0 | Yes (win-x64 server exe) | Console logs, play mode control, `tests-run` (Edit/Play mode), screenshots of camera / Game view / Scene view, custom tools, can also run inside a built game | Ships its own server binary; Docker option [12] |
| **CoderGamester/mcp-unity** | MIT | ~1.9k stars, last push 2026-10-11, v1.5.0 | Yes | `run_tests`, `get_console_logs`, play-mode set/step, scene and GameObject edits, menu items | Unity 6+, Node.js; disabled in batchmode unless opted in [13] |
| **Unity command line** | Included | Stable | Yes | Batch builds via `-executeMethod`, tests via `-runTests` | No Editor UI needed; project must not be open in another Editor [14][15] |

Recommendation: CoplayDev/unity-mcp for interactive Editor work (free, most active, has tests/build/screenshots), plus plain batchmode scripts for repeatable builds and test runs so the loop does not depend on any MCP server. Revisit official Unity MCP when it leaves beta and if the subscription is acceptable.

Meta's own Unity helpers that an agent can lean on: `metavr unity editors|projects` lists installed editors and opens/closes projects [2]; skills `hz-unity-code-review`, `hz-unity-project-analyzer`, `hz-unity-meta-mixed-reality-utility-kit`, `hz-unity-meta-movement-sdk-retargeting`, `hz-unity-sdk-upgrade`, `hz-perfetto-debug` [4]. Building Blocks are added through the Meta XR SDK window (Add Block), with dependencies and Project Setup Tool fixes applied automatically; no scripting API for adding blocks is documented, so an agent would add them through an Editor MCP menu/GameObject call or Nikita clicks them once (unverified which works) [23].

## 2. Meta-side tooling

- **Meta VR CLI (`metavr`, alias `hzdb`)**: Rust CLI, standalone installer for Windows (`iwr -useb https://developers.meta.com/horizon/install-cli/windows/ | iex`) or `npx -y metavr`. Command groups: `adb`, `app` (install/launch/stop/clear/foreground), `capture screenshot` (methods `metacam` default or `screencap`), `device` (list, info, battery, controllers, `configure-testing setup` = stay awake / no animations, `health-check`, `proximity --disable`, `boundary --disable`), `docs`, `files` (ls/pull/push), `input key`, `log`, `perf` (Perfetto), `tapedeck`, `ui` (2D UI automation), `unity`, `xroperator`, `xrsim`. `metavr mcp install claude-code` registers the MCP server [2][3].
- **MCP tools exposed** (docs page lists): `search_doc`, `fetch_meta_quest_doc`, `get_device_logcat`, `stream_device_logcat`, `take_screenshot`, Perfetto analysis tools, `meta-assets-search`; the skills describe newer names `vr_docs_search`, `device`, `app`, `files`, `run` (catch-all for any subcommand). Tool names are in flux. No auth needed for most tools [1][4].
- **agentic-tools plugin**: `claude plugin marketplace add meta-quest/agentic-tools` then `claude plugin install agentic-tools@meta-quest`. Bundles the MCP config and skills; 221 stars, Apache-2.0, last push 2026-09-24 [1][4].
- **Meta XR Operator**: see Answer. Requirements: Unity 6000.0+, Meta XR Core SDK v207+, OpenXR Plugin 1.17.0+. On Quest: Development Build, Internet permission, `com.oculus.experimental.enabled` manifest feature + `adb shell setprop debug.oculus.experimentalEnabled 1` (Horizon OS v205+), `adb forward tcp:8720 tcp:8720`; debug props and forwards do not survive reboot. Screen capture needs one-time MediaProjection consent in the headset. Experimental: "Avoid depending on it in production apps" [5][6]. `metavr xroperator detect|call` probes and calls its tools, e.g. `openxr_set_controller_input` [2].
- **Tapedeck**: OpenXR layer that records an app's OpenXR call stream (head/controller poses) to VRS on a real Quest and replays it into a fresh launch "as if a human were wearing the headset". Install/record/replay need a userdebug (rootable) build; an `apk-bundled` mode without root is described but `bundle-apk` reads as not yet shipped. Treat as unavailable on a retail Quest 3 for now [2].
- **Meta Quest Developer Hub (MQDH)**: Windows/macOS desktop app: device setup, casting, screenshots and video recording, file manager, logs, perf analyzer, proximity/boundary toggles, bundles the MCP server (v6.2.1+). No separate MQDH CLI is documented; `metavr` is the CLI [1][24].
- **Meta XR Simulator (standalone)**: OpenXR runtime on Windows/macOS, Quest 3 default profile, keyboard/mouse/Xbox input or real Quest controllers via data forwarding, synthetic rooms with passthrough, scene data, anchors and environment depth, VRS record/replay, multiplayer. Scriptable via `metavr xrsim` (activate runtime, device profile, input source on/off, env, compositor layers, MP4 record). `xrsim input` toggles sources but does not set poses; scripted poses come from Meta XR Operator. Haptics and body tracking support not stated (unverified) [2][18][19].
- **Immersive Debugger**: in-headset console log panel, Inspector to watch and tweak `[DebugMember]` fields/actions (sliders for tuning thresholds), Development Build not required since v77. Its experimental **AI Assistant** (Core SDK v207+, needs XR Operator) lets Nikita talk to Claude Code from inside the headset to inspect/change the live scene [25][26].

## 3. Automated testing without a headset

| Layer | Tool | Verifies | Runs headless / CI? |
|---|---|---|---|
| Pure logic | Unity Test Framework Edit-mode tests [15] | Swept tip-touch math, touch validity rules, timing windows, drill state machine, rep-log format | Yes, `-batchmode -runTests -testPlatform EditMode` |
| Input → behaviour | Play-mode tests + Input System `InputTestFixture` (`AddDevice`, `Press`, `Set`) [16] | Button handling, scene flow, recorded pose sequences fed into the touch detector | Yes in batchmode on the PC (graphics optional for logic) |
| Whole app in XR runtime | Meta XR Simulator + Meta XR Operator (set controller pose/input, read state, capture images) [5][18] | App boots under OpenXR, passthrough/room setup works, a scripted lunge registers a touch, UI reachable | Needs a Windows desktop session with GPU; not a headless CI box |
| Repeatable sessions | XR Simulator VRS record/replay with JSON replay config (documented for standalone Windows builds, not Unity Play mode, in the older 65.x docs) [17] | Regression replays of a recorded drill | Scriptable on Windows; current-version details unverified |
| On-device test player | `-testPlatform Android` runs Play-mode tests on a built player [15] | Logic on the real device | Needs headset connected |

What cannot be verified this way: real Touch controller tracking at lunge speed (occlusion, IMU/optical hand-off), motion-to-photon latency, whether haptics feel right or arrive on time, whether the virtual blade lines up with the real grip, passthrough comfort and registration of targets in the room, IOBT quality, thermal throttling over a session, and physical safety. Known issue to watch: XRI simulator UI button regression in recent Unity 6 versions (if XRI is used) [27].

## 4. On-device checks the agent runs itself

All via `metavr` (or raw `adb`, which ships with Unity Android Build Support):

- Install / launch / stop: `metavr app install build.apk`, `metavr app launch <pkg>`, `metavr app foreground` [2][3].
- Logs: `metavr log --tag Unity --level E`, `metavr adb logcat --follow` [3].
- Screenshots: `metavr capture screenshot -o shot.png` (metacam method). Video: MQDH recording, or scrcpy `--record` (scrcpy v5.0.1; Quest 3 works per user issues, flicker/Wi-Fi glitches reported) [2][21]. Passthrough in captures: XR Operator advertises composited real+virtual images [5]; older reports say adb/scrcpy show passthrough as black (verify on device) [22].
- Files: app writes `reps.jsonl` under `Application.persistentDataPath` (`/sdcard/Android/data/<pkg>/files/`), agent pulls with `metavr files pull` [3][28].
- Performance: OVR Metrics Tool via adb (`adb shell am start omms://app`, `... SettingsBroadcastReceiver -a com.oculus.ovrmonitormetricsservice.ENABLE_CSV`), CSVs in `/sdcard/Android/data/com.oculus.ovrmonitormetricsservice/files/CapturedMetrics/` [20]; Perfetto traces via `metavr perf` with the `hz-perfetto-debug` skill [2][4].
- Bench testing without wearing it: `metavr device proximity --disable`, `metavr device boundary --disable`, `metavr device configure-testing setup` [2], then drive controller input through XR Operator [6].

## 5. The human part

Only someone wearing the headset can judge:

1. Blade feel: length, angle and offset relative to the real grip; does the tip go where the hand intends.
2. Tracking under real lunges and fast extensions: tip jumps, lost tracking, jitter.
3. Touch correctness as felt: touches that should have scored but did not, and phantom touches.
4. Haptics: strength, timing relative to visual touch, distinguishable patterns.
5. Passthrough and target placement: targets sit at the right height/distance in the real room, readability, comfort, eye strain.
6. Comfort and safety: fatigue, space, cable/strap, boundary off.

Lightweight protocol:

- Agent prepares each build with a `TESTING.md` entry: build id, what changed, 3-5 checks with pass/fail definitions.
- App writes one JSONL line per rep (timestamp, drill, tip path summary, touch result, latency estimate, flag) and a session header (build id, frame rate). Pressing a spare controller button (e.g. B) marks "this rep felt wrong"; optional voice note via Immersive Debugger AI Assistant later.
- Nikita runs a fixed short session (e.g. 20 reps per drill), then answers the checklist in one text block (1-5 score + one sentence per item) as a Linear comment or a file in the repo.
- Agent pulls rep logs, logcat and OVR Metrics CSV, correlates flagged reps with logged data, and proposes changes. Optionally record the session (MQDH or scrcpy) for the agent to sample frames from.

## Recommended toolchain and loop

Install on the Windows PC: Unity Hub + Unity 6 LTS with Android Build Support (SDK/NDK/OpenJDK); Meta XR All-in-One SDK (Core v207+ for XR Operator); `metavr` CLI + `agentic-tools` Claude Code plugin; CoplayDev unity-mcp package (+ `uv`, Python 3.10+); Meta XR Simulator (standalone); Meta Quest Developer Hub; optionally scrcpy. On headset: OVR Metrics Tool.

Loop:

1. Agent edits C# / scenes (Editor MCP or files).
2. Agent runs Edit-mode tests in batchmode (or via MCP `run_tests`); fails stop the loop.
3. Agent runs the app in Meta XR Simulator, drives scripted lunges via XR Operator, checks touches in logs and screenshots.
4. Agent builds the APK (batchmode `-executeMethod`, Development Build), `metavr app install`, launch, smoke-checks logcat and a screenshot, optionally runs an XR Operator scripted input on device with proximity disabled.
5. Agent writes the build's checklist; Nikita wears it and runs the short session.
6. Agent pulls rep logs/metrics, reads Nikita's checklist answers, files fixes.

Setup only Nikita can do: Meta developer account + organisation; Developer Mode in Meta Horizon app; accept USB debugging prompt in the headset (and on new PCs/cables); Unity ID sign-in and Personal licence activation in Unity Hub; approve first MCP connection in the Editor (official Unity MCP and XR Operator prompts); accept the in-headset screen-capture consent for XR Operator; decide on any Unity AI subscription; keep the play space clear when the boundary is disabled.

## Gaps and open questions

- Exact current XR Operator tool names and whether the composited capture includes passthrough on Quest 3 retail: verify on first device run.
- Whether Meta XR Simulator simulates haptics and IOBT body tracking: not documented.
- Tapedeck on retail Quest (`apk-bundled` mode) availability.
- Unity official MCP pricing/limits from Unity's own page (only third-party summaries seen).
- Third-party community MCP servers run code in the Editor with full project access; pin a release tag rather than `#main`.

## Sources

1. Meta, Horizon Debug Bridge / MCP server for Unity: https://developers.meta.com/horizon/documentation/unity/ts-mqdh-mcp/
2. meta-quest/agentic-tools, metavr CLI full reference (xrsim, tapedeck, xroperator, device, capture, mcp install): https://github.com/meta-quest/agentic-tools/blob/main/docs/metavr-cli.md
3. meta-quest/agentic-tools skills `metavr-cli`, `hz-vr-debug`, `hz-quest-verify-first`: https://github.com/meta-quest/agentic-tools/tree/main/skills
4. Meta, AI tooling overview (Unity): https://developers.meta.com/horizon/documentation/unity/ts-ai-tooling-overview/ ; repo (Apache-2.0, 221 stars, pushed 2026-09-24): https://github.com/meta-quest/agentic-tools
5. Meta, Meta XR Operator: https://developers.meta.com/horizon/documentation/unity/meta-xr-operator/
6. Meta, Meta XR Operator on Quest: https://developers.meta.com/horizon/documentation/unity/meta-xr-operator/quest/
7. Unity blog, Unity AI MCP how to get started (2026-05-11): https://unity.com/blog/unity-ai-mcp-how-to-get-started
8. Unity AI Assistant manual, Unity MCP get started: https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.0/manual/unity-mcp-get-started.html
9. Unity AI pricing summaries (third-party, unverified against Unity): https://aihola.com/article/unity-ai-open-beta ; forum: https://discussions.unity.com/t/mcp-versus-built-in-assistant/1719106
10. CoplayDev/unity-mcp (MIT, ~15.1k stars, pushed 2026-10-17, v10.3.0): https://github.com/CoplayDev/unity-mcp
11. CoplayDev unity-mcp camera screenshot command (source): https://github.com/CoplayDev/unity-mcp/blob/main/Server/src/cli/commands/camera.py
12. IvanMurzak/Unity-MCP (Apache-2.0, ~4.5k stars, pushed 2026-10-18, v1.2.0): https://github.com/IvanMurzak/Unity-MCP
13. CoderGamester/mcp-unity (MIT, ~1.9k stars, pushed 2026-10-11, v1.5.0): https://github.com/CoderGamester/mcp-unity
14. Unity 6 Manual, Editor command-line arguments: https://docs.unity3d.com/6000.0/Documentation/Manual/EditorCommandLineArguments.html
15. Unity Test Framework, command-line reference: https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html
16. Input System, Testing (InputTestFixture): https://docs.unity3d.com/Packages/com.unity.inputsystem@1.11/manual/Testing.html
17. Meta, XR Simulator hybrid automation testing (v65, 2024): https://developers.meta.com/horizon/documentation/unity/xrsim-unity-automated-testing-hybrid
18. Meta, XR Simulator intro (standalone, updated 2026-09-04): https://developers.meta.com/horizon/documentation/unity/xrsim-intro/
19. meta-quest/agentic-tools skill `hz-xr-simulator-control`: https://github.com/meta-quest/agentic-tools/tree/main/skills/hz-xr-simulator-control
20. Meta, OVR Metrics Tool: https://developers.meta.com/horizon/documentation/unity/ts-ovrmetricstool/
21. scrcpy (v5.0.1) and Quest 3 user issues: https://github.com/Genymobile/scrcpy , https://github.com/Genymobile/scrcpy/issues/6735 , https://github.com/Genymobile/scrcpy/issues/4386
22. Passthrough black in adb/scrcpy captures (older reports): https://skarredghost.com/2021/09/05/record-videos-oculus-quest-passthrough-ar ; https://communityforums.atmeta.com/discussions/dev-unity/record-videos-of-a-unity-vr-scene-with-passthrough-running-on-a-quest-pro-throug/1061743
23. Meta, Building Blocks overview: https://developers.meta.com/horizon/documentation/unity/bb-overview/
24. Meta, Meta Quest Developer Hub: https://developers.meta.com/horizon/documentation/unity/ts-mqdh/
25. Meta, Immersive Debugger overview: https://developers.meta.com/horizon/documentation/unity/immersivedebugger-overview/
26. Meta, Immersive Debugger AI Assistant: https://developers.meta.com/horizon/documentation/unity/immersivedebugger-ai-assistant/
27. Unity issue tracker, XR Interaction Simulator UI buttons regression: https://issuetracker.unity.com/issues/24518/cannot-select-buttons-using-simulator-in-ar-mobile-template
28. Unity Scripting API, Application.persistentDataPath: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application-persistentDataPath.html

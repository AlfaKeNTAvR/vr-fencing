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

- Scene: `Assets/Scenes/Passthrough.unity`, the only build scene, kept up to date by `PassthroughSceneBuilder` (menu "VR Fencing > Update passthrough scene", or batchmode `-executeMethod VrFencing.EditorTools.PassthroughSceneBuilder.UpdatePassthroughScene`). Meta's camera rig with passthrough as the background, and the Strip with its En Garde Line transform (the Strip frame). The Dummy sits under the En Garde Line at lunge distance, so it moves with the Strip; it reads the Fencer's height and hand from the settings file at start. Until a Strip is placed or restored, the En Garde Line is at the scene origin. A `Practice` object holds the Drill runner, the session recorder (rep log), the arm tracking (IOBT) and the footwork tracking (head); the `Menu` object (`MenuBehaviour`) builds the panels and the off-hand pointer at runtime and is what starts, pauses and cancels Drills. The camera rig's OVRManager asks for the body tracking permission at launch.
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

On the headset, every judged Rep also prints a `[Tracking] Rep judged: ...` line to `adb logcat -s Unity`, and body tracking start, fidelity changes (as the runtime reports them) and missing permission are logged with the same tag. Body tracking runs whenever the app is active and stops while the app is paused (headset asleep), has lost input focus (system menu), or the headset is off the head; it restarts once all have cleared (`BodyTrackingLifecycle.cs`), logging `[Tracking] Body tracking stopped (<reason>).` and `[Tracking] Restarting body tracking (<reason>).` The app asks for the body tracking permission at first launch (Meta's dialog); if it was denied, grant it with `"$ADB" shell pm grant com.alfakentavr.vrfencing com.oculus.permission.BODY_TRACKING` (or in the headset's app permissions) and restart the app.

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
| Trigger | Placement: first press at the En Garde Line (over the front foot, standing en garde), second at the far end. Outside placement it is the menu pointer's select |
| Y (left) / B (right) | Re-place the Strip; press again to cancel and keep the old one |
| X (left) / A (right) | Cycle the off-strip tolerance 0 / 0.2 / 0.4 m, shown in front of you and saved to Settings |

The Strip extends behind the En Garde Line by the en garde stance (0.30 of the Fencer's height) plus the rear Warning Zone, so about 1 m of free floor is needed behind the first point. Saved Strips are listed in `strip-anchors.json` next to the settings file (`/sdcard/Android/data/com.alfakentavr.vrfencing/files/`), newest first, at most 10 rooms; each launch tries all of them and uses the newest anchor the headset finds in the room. Anchor save and restore steps are logged with a `[Strip]` tag (`adb logcat -s Unity`). Anchors need the anchor support and `USE_ANCHOR_API` permission the project already enables (`Assets/Oculus/OculusProjectConfig.asset`, `Assets/Plugins/Android/AndroidManifest.xml`).

## The Blade on the headset

The épée sits on the sword-hand controller (right by default; the Sword hand Setting moves it and mirrors its yaw): a 90 cm blade, a 13.5 cm bell guard, a grip and pommel behind it, and a red point. It uses no buttons. Detection and drawing live in the Blade module (`Assets/VrFencing/Blade/`); the scene's `Blade` object (`BladeBehaviour`) is made by `PassthroughSceneBuilder`.

Every frame the straight blade (guard front to point) is moved into the Strip frame and swept against the Dummy's Target Zone capsules, so a 15 m/s point cannot pass through a zone between frames. When the point enters a zone from outside every zone it is judged once:

| Event | When | Goes to | Feedback |
|---|---|---|---|
| Touch | Moving into the surface at least the minimum speed, approach and blade both within the rule's angles | Drill runner as point-first | Haptic (default Heavy), the zone flashes white, the point flashes green |
| Glance | The point reached the zone but failed the Touch test | Not reported (only the point scores) | Light haptic tick, the point flashes blue |
| Flat contact | The blade body (all but the last 3 cm) crosses a zone while the point is in none | Drill runner as flat | Haptic (default Double tap), at most once per 0.15 s |

The approach is the point's motion over the last 40 ms before it entered (stretched back up to 100 ms until the point has moved 15 mm), and the blade direction is averaged over the same 40 ms. A single 72 Hz frame was too short: at 2 m/s it is only 28 mm, so one noisy frame made fast point-first thrusts read as sideways (PER-22).

| Touch rule | Min speed into the surface | Approach within | Blade within | Point radius, blade radius, point length |
|---|---|---|---|---|
| Loose (default) | 0.02 m/s | 70 degrees | 75 degrees | 4 mm, 6 mm, 3 cm |
| Strict | 0.05 m/s | 40 degrees | 45 degrees | same |

Haptics play on the sword hand through the Meta Haptics SDK (OVRInput vibration does nothing under OpenXR). Strength rises with tip speed from the "Haptic strength when slow" floor (default 40%) to full at "Full haptic strength at" (default 2.0 m/s), on top of each event's pattern (Off, Tick, Crisp, Heavy, Double tap). The blade drawn bends up to 7 cm on fast moves and springs back; detection always uses the straight blade. A held blade stays still: the flex ignores point speeds under 0.35 m/s (tracking jitter, slow aiming), smooths the point over 30 ms and is damped (7 Hz spring, damping ratio 0.6) (PER-48).

The drawn blade never passes through the Dummy or the floor (any capsule, Target Zone or not, and the floor plane; `BladeObstacle`, `BladeContactBend`, `BladeArc`, `DrawnShape` and `DrawnBlade` in the Blade Core, generic obstacles so a blade can join them later). It is always one smooth curve of the blade's full length: it bends in the plane of the push (sideways when the point is off the hand's line, bowing out when pushed straight in), never more sharply than the bow of a 15 cm straight push (about a 14 cm radius), and never folds. The hand is not stopped; the drawn blade is:

| Situation | Drawn blade | Haptic |
|---|---|---|
| The point meets a body (found along its whole path since the last frame, so a fast thrust cannot tunnel) | The point pins on that near-side surface | The event haptic (Touch, glance) as before |
| The hand pushes on, up to 15 cm past the surface straight in (somewhat more when turned) | The blade bends in one smooth curve and leaves the guard along the hand (by more than 10 cm of bow at 15 cm of straight push) | A buzz every 0.06 s, from 25% as the point lands to 100% at 15 cm of push |
| The hand asks for more bend than the blade has (pushed further, front or side, or turned more than 40 degrees off the line to the point) | The drawn hilt stops following: it takes the reachable pose closest to the hand on the way to the blade lined up on the point at 15 cm of push, or holds; a faint ghost hilt shows where the hand is. It follows again once the hand is back within reach | Full-strength buzz |
| The hand turns the blade more than 20 degrees off the line to the point (about 30 cm sideways) | The point slides over the surface, never through it | Buzz continues |
| The hand comes back out or goes around (the straight point is outside every body with a clear path to the pin) | The point lets go and the blade springs back ringing a few decaying cycles (7 Hz, damping 0.12 until nearly still) | Stops |
| The blade body is pushed or swept into a body | The whole drawn blade stops at the body's near side and lags the hand (ghost hilt), until the hand goes back or around; catching up, it rings out the same way | Flat-contact haptic as before, full buzz while it lags |

Each release logs `[Blade] Point released: CameOut` (or `BodyMoved` when the Dummy changes distance), and the drawn blade starting or stopping to lag the hand logs a line both ways.

The floor (Strip frame y = 0, the tracking floor; the room's y = 0 before a Strip is placed) is an obstacle under the same rule (PER-49): the point pins on it, the blade bends, lags past the most bend and slides along it, and the whole drawn blade stays above it. Floor contacts are not Touches, never reach the Drill runner and play only the press buzz.

Every event prints a `[Blade]` line to `adb logcat -s Unity`: event, zone, tip speed, approach and blade angles, distance from the zone center, and the rule. For a flat contact the approach is the motion of the blade point nearest the contact and the blade angle is against the surface normal (about 90 degrees for a true flat); no angle is ever NaN.

## The menus on the headset

World-space panels float about 1.1 m in front of the Fencer, a little below eye level; they follow the head's position and turn with it once the Fencer looks more than 40 degrees away. A laser from the off-hand controller points (tilted down from the controller's forward axis by the "Menu laser tilt" Setting, default 15 degrees; Meta's SDK has no separate aim pose for controllers under OpenXR, and its controller rotation already follows the aim pose's), its trigger presses (hold it on a settings step button to repeat). Everything mirrors with the sword hand setting. Placing the Strip hides the panels, since placement uses the same trigger.

| Screen | Holds |
|---|---|
| Home | The Drill picker, easy to hard; Drills that don't fit the Strip are greyed out with "Needs a N m Strip". Start, Settings, History, Re-place Strip, and the off-strip tolerance (same as X / A) |
| Running (during a Drill) | Small panel low on the off-hand side: Drill and Rep, the cue or why it is paused, Pause/Resume, Cancel, Flag Rep |
| Results (after each Drill) | Success %, average reaction time and tip speed, the hit map per Target Zone, the last-10-sessions trend; Run again, History, Done |
| History | Every Drill with saved runs (shortened runs listed apart), its newest run and its trend |
| Settings | Every Setting with - and +, saved at once; height and hand rebuild the Dummy and the Strip |

A small burger button (three bars) sits low on the off-hand side, just past the Running panel's spot, whenever the Strip is not being placed: point and pull the trigger to fold every panel away (Home, Settings, Results, History and the Running panel, which otherwise sits in view during a Drill) and again to open them. The laser keeps working on it while the panels are folded; the button is lit while they are open. The folded state is remembered between sessions (Unity PlayerPrefs, key `VrFencing.MenuCollapsed`).

Pause from the menu holds the Drill until Resume, even back on the Strip; leaving the Strip pauses it as before. Development builds also list a test Drill after the real ones, "Test: needs a full piste", always greyed out, to see a greyed-out row.

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

All world-space text is TextMeshPro (SDF shader, drawn in both eyes under single-pass instanced stereo). Its essentials live in `Assets/TextMesh Pro/`; `VrFencing.EditorTools.TextMeshProSetup.ImportEssentialsIfMissing` re-imports them in batchmode (run without `-quit`, it exits itself). To check the panels' layout without the headset, `VrFencing.EditorTools.MenuSnapshots.RenderAll` (menu "VR Fencing > Render menu snapshots", batchmode without `-nographics`) renders every screen with sample data to `Builds/menu-snapshots/`.

## The MCP servers (`.mcp.json`)

MCP servers are plug-ins that give Claude extra tools. They are configured for this repo only, in `.mcp.json` at the repo root; Claude Code asks once whether to trust them.

- **UnityMCP** (CoplayDev "MCP for Unity"): lets Claude drive the open Unity Editor: create and edit scenes and GameObjects, edit scripts, read the console, run tests, enter Play mode, build, take screenshots. The Unity package starts a small Python server (through `uvx`, pinned to 10.3.0) and Claude talks to it at `http://localhost:8080/mcp`. It only works while the Editor is open and the server is started (Window > MCP for Unity > Start Server, or turn on "Auto-start on load" under Advanced). Do not press its "Configure" button for Claude Code: `.mcp.json` already does that, and the button rewrites Claude's config.
- **metavr** (Meta VR CLI, run through `npx`, pinned to 1.8.1, nothing installed globally): device and app control on the Quest (install, launch, logcat, screenshots, files, performance traces), plus Meta documentation search. Needs the headset connected and USB debugging allowed.
- **Meta XR Operator** is not in `.mcp.json`: it is an MCP server inside the running app (port 8720) in the Editor's Play mode, in the Meta XR Simulator, or on the headset through the `adb forward` above. It reads poses, scene and screen, and can move the controllers and press buttons for scripted tests.

When Meta's `agentic-tools` Claude Code plugin is installed, it brings its own `metavr` server (unpinned). Keep one of the two: either remove `metavr` from `.mcp.json`, or disable the plugin's copy.

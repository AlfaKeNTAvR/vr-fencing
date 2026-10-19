# Quest 3 tech stack: engine, passthrough, IOBT, haptics, future multiplayer

Research for Linear PER-18. Researched 2026-10-18.

## Answer

- **Recommend Unity 6 (LTS) + OpenXR + Meta XR SDK (All-in-One, current line is v2xx, e.g. Core SDK 207.0, Sep 2026), plus Movement SDK, MR Utility Kit and Haptics SDK.** It is the only stack where every feature the MVP and its later phases need (passthrough, IOBT upper body, room/boundary, PCM haptics with runtime amplitude/frequency control, Multiplayer Building Blocks) is first-party, documented, and has samples. Meta's own setup flow uses OpenXR and calls the Oculus XR plugin deprecated.
- **Unreal** (Meta XR plugin fork) covers the same Meta features, but Movement SDK requires Meta's plugin and is incompatible with Epic's OpenXR plugins, Unreal-on-Quest is heavier to learn and build, and the beginner path (C++/Blueprints, multi-GB toolchain) is steeper. Pick only if you already know Unreal.
- **Godot 4.6/4.7 + OpenXR Vendors plugin** now exposes passthrough, scene, anchors, colocation discovery and Meta body tracking (with fidelity and lower-body options), but haptics are only the generic OpenXR pulse (amplitude/frequency/duration), there are no Meta-grade samples for MR + IOBT + multiplayer together, and multiplayer has no relay/NAT service out of the box. Viable, not the low-risk choice.
- **WebXR** (Quest Browser) has passthrough (`immersive-ar`), planes and `pulse()` haptics, but no body tracking and no PCM haptics, and JS frame budget is tighter. Rule it out for IOBT.
- **Physics/tunnelling is an engine-agnostic design decision, not an engine feature.** At 10 m/s the tip moves 8-14 cm per rendered frame (120-72 Hz) and 20 cm per Unity default physics step (50 Hz). Do tip-touch detection yourself as a swept test from last-frame tip to this-frame tip (sphere/capsule cast or segment math), and later blade-on-blade as segment-vs-segment closest approach over the frame. Do not rely on rigidbody contacts or built-in CCD (Unity's sweep CCD ignores rotation; kinematic bodies only get speculative CCD).
- **Multiplayer kept open:** Unity has Netcode for GameObjects (+ Unity Relay) and Photon Fusion at parity in Meta's Multiplayer Building Blocks, plus Normcore. Note Meta Avatars SDK is frozen (final v40.0.1, Sep 2025); plan to network your own blade + IOBT pose instead.
- **Install first:** Unity Hub + Unity 6 LTS (6000.0.66f2 or newer, MRUK 203 requires it) with Android Build Support; Meta Quest Developer Hub (Windows); Meta developer account/org + Developer Mode on the headset; Meta XR All-in-One SDK. Learn first: C# basics, Unity scene/GameObject/component model, `Update` vs `FixedUpdate`, OpenXR project setup via Meta's Project Setup Tool, building and installing an APK.

## Comparison

| Topic | Unity + Meta XR SDK | Unreal + Meta XR plugin | Godot + OpenXR Vendors | WebXR (Quest Browser) |
|---|---|---|---|---|
| Passthrough | Yes, OVRManager / Building Blocks [1][10] | Yes [7] | Yes, via OpenXR blend mode + vendors plugin (color filters, projected passthrough) [12][13] | Yes, `immersive-ar` session, transparent background [17] |
| Scene / room / boundary | MRUK (v203 needs Unity 6000.0.66f2+), Room Guardian block [10][11] | MRUK for Unreal, Guardian sample [10] | Scene Manager, global mesh, spatial anchors [13] | Plane detection, anchors, hit test [17] |
| IOBT (upper body) | Movement SDK, Body Tracking Fidelity = High; Quest 3 only; falls back to low fidelity under heavy load (passthrough, controllers, high CPU) [4] | Movement SDK, IOBT on Quest 3/3S; needs Meta plugin, incompatible with Epic OpenXR plugins; recommends UE 5.4 [5][6] | Meta body tracking extension incl. fidelity + lower body + height override [12][13] | Not exposed; body tracking is engine-SDK / Spatial SDK only [18] |
| Haptics | Haptics SDK: `.haptic` clips from Haptics Studio, PCM on Touch Plus; runtime `amplitude` 0-1, `frequencyShift` -1..1, looping, priority 0-255 [8][9] | Haptics SDK + raw "Set Haptics by Value" (freq/amp 0-1), PCM sound-wave batches (36 ms default) [14][15] | Generic `trigger_haptic_pulse(action, frequency, amplitude, duration, delay)`; PCM not documented [13][16] | `GamepadHapticActuator.pulse(intensity, ms)` only [19][20] |
| Physics + CCD | PhysX; default fixed step 0.02 s (50 Hz), tunable; CCD speculative or sweep; sweep ignores angular motion; kinematic only speculative; Box/Sphere/Capsule only [21][22][23] | Chaos, per-body "Use CCD" [24] | Godot Physics or Jolt (4.4+ experimental built-in); RigidBody3D `continuous_cd` [25][26] | Whatever JS library you bring |
| Multiplayer paths | NGO (+ Unity Relay), Photon Fusion (both in Meta Multiplayer Building Blocks at parity; voice only on Fusion), Normcore, Shared Spatial Anchors / colocation blocks [27][28][29][30] | Built-in UE replication; Meta colocation/anchors via plugin (Photon Fusion and NGO are Unity-only) [27] | High-level multiplayer over ENet/WebRTC/WebSocket; no built-in relay/NAT, port forwarding needed [31] | WebRTC/WebSocket, roll your own |
| Meta Avatars | Unity SDK, but frozen at final v40.0.1 (Sep 2025) [32] | n/a for this plan | No | No |
| Sideload | Build APK, install via MQDH (Windows/macOS) or adb, Developer Mode required [33][34] | Same | Same (Android export) | No install; open a URL (HTTPS) |

### Controller tracking rate and latency

- No official controller tracking rate or motion-to-photon figure for Quest 3 Touch Plus was found on developers.meta.com. Hand-tracking runs 30 Hz default, 60 Hz in Fast Motion Mode [35]; that does not apply to controllers, which are IMU + optical. Anecdotal forum reports describe Quest 3 controllers losing optical tracking during fast arm moves [36]. Treat this as an empirical question for the prototype: log tip pose per frame at 120 Hz and look for jumps.
- What the app actually sees is one controller pose per rendered frame, predicted by the runtime to display time (OpenXR `xrLocateSpace` at predicted display time [37]). So sampling rate for blade logic = display rate. Quest 3 supports 72 (default), 80, 90, 120 Hz; the app must request higher rates and may be throttled back to 72 Hz on thermal events [38][39].
- Per-frame tip travel at 10 m/s: 72 Hz = 13.9 cm, 90 Hz = 11.1 cm, 120 Hz = 8.3 cm. Unity default physics step 50 Hz = 20 cm (computed from [21][38]).
- Velocity: `OVRInput.GetLocalControllerVelocity` / angular velocity exist, but forum reports of space/quaternion issues in Quest builds make finite differences of poses the safer default [40].

### Tunnelling: what to do in any engine

- Built-in CCD is "a safety net" and not exact [22]; sweep CCD ignores angular motion [21], and a blade pivots around the hand, so the tip has large rotational velocity. Kinematic (hand-driven) bodies only support speculative CCD, which can produce ghost contacts [21][23].
- Do scoring in `Update` (per rendered frame, same rate as pose), not `FixedUpdate`: store last tip position, cast a small sphere from last to current tip against target colliders (or do segment-vs-shape math yourself). For blade-on-blade later, compute closest approach between the two blade segments interpolated over the frame. This is the same in every engine and ports to a networked opponent.
- Optionally raise display rate to 90/120 Hz and set fixed timestep to match, which shrinks per-step travel but does not remove the need for the swept test.

### IOBT quality notes

- Missing in low fidelity (no IOBT): elbow tracking and correct spine during lean [4]. Under heavy load (passthrough + controllers + high CPU, which is this app) IOBT can silently fall back to low fidelity [4]. Unreal docs add: jitter when controllers are in hard-to-track spots (e.g. overhead), dropouts on occlusion, sensitivity to backlight/dim rooms [5].
- Calibration: auto within ~10 s of start, standing recommended; height override available; only use poses when calibration state is valid [4].
- Implication: use IOBT for elbow/arm-extension feedback (e.g. "arm before feet"), not for hit detection. The blade must come from the controller pose only.

## What a beginner needs to install and learn first (Unity path)

Install:
1. Meta developer account, create an organization, enable Developer Mode for the headset in the Meta Horizon phone app [34].
2. Meta Quest Developer Hub (Windows) for device setup, logs, casting, installing builds; it can also create a Unity XR project and install Unity [33][1].
3. Unity Hub + Unity 6 LTS (6000.0.66f2+) with Android Build Support (SDK/NDK/OpenJDK) [11].
4. Meta XR All-in-One SDK (pulls Core, Interaction, MRUK, Movement, Haptics, Platform) via Package Manager; XR Plug-in Management set to OpenXR with the Meta XR feature group; run Project Setup Tool "Fix All" [1].
5. Meta Haptics Studio (desktop) for authoring `.haptic` clips [8].
6. A USB-C data cable (or wireless adb). Optional: Meta XR Simulator (standalone installer since v83) for in-editor testing [2].

Learn, in order:
1. C# basics and Unity's GameObject/component model, prefabs, `Update` vs `FixedUpdate`.
2. Build and run: one empty passthrough scene deployed to Quest via Build and Run / MQDH.
3. Building Blocks: Camera Rig, Passthrough, Controller tracking; attach a blade mesh to the right controller anchor.
4. Swept tip test (Physics.SphereCast / Linecast) against target colliders; haptic clip on hit.
5. Later: MRUK room data for placing targets, Movement SDK body tracking, then Multiplayer Building Blocks (NGO or Fusion).

## Gaps and open questions

- No official Quest 3 controller tracking rate / latency number; measure it.
- Normcore pricing and Unity 6 support not confirmed from a primary page (platforms page lists Quest) [30].
- Whether Godot OpenXR Vendors supports Meta PCM haptics was not found in its docs [13].
- Unity Relay on Android/Quest: no explicit Quest statement in Unity docs [29].

## Sources

1. Meta, Unity project setup (OpenXR, Oculus XR plugin deprecated, All-in-One, Project Setup Tool): https://developers.meta.com/horizon/documentation/unity/unity-project-setup/
2. Meta XR Simulator v83 standalone: https://developers.meta.com/horizon/downloads/package/meta-xr-simulator-windows/83.0
3. Meta XR Core SDK package (207.0, Sep 22 2026): https://developers.meta.com/horizon/downloads/package/meta-xr-core-sdk/
4. Meta, Unity Movement SDK body tracking: https://developers.meta.com/horizon/documentation/unity/move-body-tracking/
5. Meta, Unreal Movement overview: https://developers.meta.com/horizon/documentation/unreal/unreal-movement-overview/
6. Meta, Unreal Movement getting started: https://developers.meta.com/horizon/documentation/unreal/unreal-movement-getting-started/
7. Meta, Unreal engine docs index: https://developers.meta.com/vr/documentation/unreal/unreal-engine/
8. Meta, Unity Haptics SDK: https://developers.meta.com/horizon/documentation/unity/unity-haptics-sdk/
9. Meta, Unity Haptics SDK integrate (HapticClipPlayer API): https://developers.meta.com/horizon/documentation/unity/unity-haptics-sdk-integrate/
10. Meta, Unity MRUK getting started / Unreal MRUK overview: https://developers.meta.com/horizon/documentation/unity/unity-mr-utility-kit-gs/ , https://developers.meta.com/horizon/documentation/unreal/unreal-mr-utility-kit-overview
11. Meta, MRUK UPM 203.0 (min Unity 6000.0.66f2): https://developers.meta.com/horizon/downloads/package/meta-xr-mr-utility-kit-upm/203.0/
12. Godot, OpenXR Vendors plugin 4.0 / XR updates: https://godotengine.org/article/godot-openxr-vendors-plugin-400/ , https://godotengine.org/article/godot-xr-update-oct-2024/
13. Godot OpenXR Vendors docs: https://godotvr.github.io/godot_openxr_vendors/
14. Meta, Unreal Haptics SDK: https://developers.meta.com/horizon/documentation/unreal/unreal-haptics-sdk
15. Meta, Unreal haptics APIs: https://developers.meta.com/vr/documentation/unreal/unreal-haptics-apis/
16. Godot XRNode3D.trigger_haptic_pulse: https://docs.godotengine.org/en/stable/classes/class_xrnode3d.html
17. Meta, WebXR mixed reality: https://developers.meta.com/horizon/documentation/web/webxr-mixed-reality/
18. Meta, Horizon OS immersive features (body tracking via engine SDKs / Spatial SDK): https://developers.meta.com/horizon/essentials/horizon-os-immersive-features
19. Meta, WebXR Touch Pro controller haptics: https://developers.meta.com/horizon/documentation/web/webxr-pro-controller/
20. MDN, GamepadHapticActuator: https://developer.mozilla.org/docs/Web/API/GamepadHapticActuator
21. Unity Manual, Continuous collision detection (sweep ignores angular motion, speculative ghost contacts): https://docs.unity3d.com/2022.1/Documentation/Manual/ContinuousCollisionDetection.html
22. Unity 6 Manual, CCD (safety net, Box/Sphere/Capsule only): https://docs.unity3d.com/6000.0/Documentation/Manual/ContinuousCollisionDetection.html
23. Unity 6 Rigidbody.collisionDetectionMode (only speculative on kinematic): https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody-collisionDetectionMode.html
24. Unreal, Physics bodies reference (Use CCD): https://dev.epicgames.com/documentation/en-us/unreal-engine/physics-bodies-reference-for-unreal-engine
25. Godot RigidBody3D (continuous_cd): https://docs.godotengine.org/en/stable/classes/class_rigidbody3d.html
26. Godot 4.4, Using Jolt Physics: https://docs.godotengine.org/en/4.4/tutorials/physics/using_jolt_physics.html
27. Meta, Multiplayer Building Blocks (NGO and Fusion at parity, colocation, SSA): https://developers.meta.com/horizon/documentation/unity/bb-multiplayer-blocks
28. Meta, Colocated experiences MR Motif: https://developers.meta.com/horizon/documentation/unity/unity-mrmotifs-colocated-experiences/
29. Unity Relay get started / Relay and NGO: https://docs.unity.com/relay/get-started , https://docs.unity.com/ja-jp/relay/relay-and-ngo
30. Normcore platforms: https://normcore.io/documentation/platforms
31. Godot High-level multiplayer: https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html
32. Meta Avatars SDK package (final 40.0.1): https://developers.meta.com/horizon/downloads/package/meta-avatars-sdk/
33. Meta, Meta Quest Developer Hub: https://developers.meta.com/horizon/documentation/unity/ts-mqdh/
34. Sideloading walkthrough (third-party, Developer Mode + MQDH steps): https://bookstack.cores.utah.edu/books/sd2cgapp-resources/page/apk-sideloading-onto-the-quest-2-3
35. UploadVR, hand tracking rates (30 Hz / 60 Hz Fast Motion Mode): https://www.uploadvr.com/playstation-vr2-hand-tracking-compared-to-quest-3-waltz-of-the-wizard/
36. Meta community forum, Quest 3 controller tracking issues (anecdotal): https://communityforums.atmeta.com/other-troubleshooting-14/unresolved-as-of-may-30-2024-quest-3-controller-tracking-issues-131083
37. Khronos, OpenXR specification (xrLocateSpace, predicted display time): https://registry.khronos.org/OpenXR/specs/1.1/html/xrspec.html
38. Meta, Unity set display frequency: https://developers.meta.com/vr/documentation/unity/unity-set-disp-freq/
39. Meta, Unreal Get Available Display Frequencies (Quest 3: 72/80/90/120 Hz): https://developers.meta.com/horizon/documentation/unreal/unreal-blueprints-get-available-display-frequencies/
40. Meta community forum, controller velocity in OVRInput: https://communityforums.atmeta.com/unity-development-123/how-to-get-contoller-veclocity-meta-sdk-all-in-one-153477

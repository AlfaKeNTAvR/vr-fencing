# Editor player two: playing back a recorded Fencer

Research for Linear PER-55 (parent PER-53, blocks PER-58). Researched 2026-10-19. Question: what is the cheapest way for a PC Unity Editor client to join a bout and replay a recorded Fencer (tracked body plus blade), so Nikita can fence a moving opponent alone and agents can test the network path without a headset. Builds on remote-multiplayer.md (NGO + Relay, Quest as host, body through the Movement SDK's `NetworkCharacterRetargeter`, blade as a separate message, judge on the host), ADR 0002 (poses as data, plain C# Core) and dev-loop.md. Items marked **[derived]** are my own inference or arithmetic; **[unverified]** marks claims with no primary source found. Source numbers refer to the list at the end; dates are the page's "updated" date or, for source code, the tag read on 2026-10-19.

## Recommendation

- **Record with our own recorder, not a Meta tool.** Meta ships no body-tracking recorder for real headsets. The Movement SDK v207 can *play back* `.sbn` snapshot files (its retargeting editor uses them for preview), but has no public writer, and an `.sbn` holds already-retargeted, delta-compressed network snapshots [2][3][4]. Meta XR Simulator's Session Capture records only simulator head and controller input to `.vrs`, not a real Quest and not body tracking [6]. So add a small `FencerRecorder` on the Quest that, every rendered frame, writes what the live sender reads: the head, the sword controller (from which `BladePose.GetBlade` makes the blade), and the OVRPlugin body joints, all in the Fencer's Strip frame, with the fidelity and validity flags. The pieces exist: `ArmTrackingBehaviour` already reads OVRPlugin joints into `TrackedPose`s, `BladeBehaviour` already puts the controller into the Strip frame, and `BodyTrackingLifecycle` already decides when tracking runs [11]. One owner must start body tracking: the recorder needs the Full Body joint set (84 joints, legs included) that the multiplayer plan uses, while `ArmTrackingBehaviour` starts Upper Body (70) today [7][11].
- **Format: own binary file, about 10 MB per minute.** A short JSON header (format version, joint set, frame rate, Fencer height, hand, Strip length, app version, UTC start), then fixed-size frames: time (8 B), head pose (28 B), controller pose (28 B), fidelity and validity bits (about 12 B), 84 joint poses x 28 B (2,352 B). About 2.4 KB per frame; at the Quest's 72 Hz that is about 175 KB/s, **10.5 MB per minute**, about 32 MB for a 3-minute bout **[derived]**. Body only every other frame (36 Hz) gives about 5.5 MB per minute **[derived]**. JSON Lines like the rep log would be about 2.5x bigger (about 26 MB per minute) and slower to parse **[derived]**; keep JSON for the header only. Reader and writer live in a plain C# Core assembly (ADR 0002), so they are Edit-mode testable.
- **Transfer: `adb pull`, same as the session logs.** Write to `Application.persistentDataPath/recordings/fencer-<UTC>.fencer` on the headset (`/sdcard/Android/data/com.alfakentavr.vrfencing/files/recordings/`) and pull with `"$ADB" pull ... Recordings/` or `metavr files pull` [10]. `Recordings/` is already gitignored at the repo root [11]. Check in only a trimmed few-second clip (under 2 MB) as a test fixture.
- **Playback feeds the same send path through one seam per stream.** On the PC, a `ReplayFencer` scene with no XR rig runs the normal *sender* side as an NGO client:
  - **Body:** a `ReplaySourceDataProvider` implementing the Movement SDK's `ISourceDataProvider` (`GetSkeletonPose`, `GetSkeletonTPose`, `IsPoseValid`, ...) sits on the same GameObject as the `CharacterRetargeter`, which finds its provider by `GetComponent<ISourceDataProvider>()` [5]. On the Quest that slot holds `MetaSourceDataProvider : OVRBody`. Everything downstream is unchanged: `CharacterRetargeter` -> `NetworkCharacterRetargeter` (owner = Host of that character) -> `MSDKUtility.SerializeSkeletonAndFace` -> NGO [1][5].
  - **Blade and claims:** the replayed controller pose goes through the same `BladePose.GetBlade` and blade message the live headset sends, and the PC's own Touch detector makes the same timestamped claims.
  - The receiving Quest (host and judge) cannot tell a replay from a person, so the avatar, interpolation, Strip-to-Strip rotation and judge run unchanged **[derived]**. Record poses in the recorder's own Strip frame and treat the PC's world origin as that Strip frame, so no extra transform is needed **[derived]**.
  - Join the Quest host the same way the partner will (Sessions / Relay join code). Editor and Quest on the same home Wi-Fi could use a direct Unity Transport IP instead, for lower latency in solo practice **[derived]**.
- **Do not replay at the network-bytes level.** Injecting recorded `.sbn` or NGO bytes into the receiver (`NetworkCharacterHandler.ReceiveData`) would skip the sender, the blade message and the claims, and its snapshots are delta-compressed against acks from the original session [2][3]. It is only worth it later as a compact archive.
- **Headless and tests: yes, in three layers.**
  1. **Edit-mode, no Unity scene, no network (the bulk):** recording round trip; the replay clock (time -> interpolated frame, looping, seek); the 180-degree Strip-to-Strip transform; the replayed blade through the real Touch detector against the Dummy's capsules; the judge rule on claims made from two recordings. All plain C# under ADR 0002, run by the existing `scripts/run-editmode-tests.sh`.
  2. **Play-mode, one process, no transport:** the Movement SDK's own `NetworkCharacterBehaviourLocal` passes the sender's bytes straight to a receiver in the same scene, with `NetworkTime = Time.time` [4]. Replay -> retarget -> serialize -> deserialize -> avatar can be checked without NGO. The native `MetaMovementSDK_Utility` library ships for Win64, Linux, Mac and Android, so it loads in the Editor [2]; whether it runs under `-nographics` is **[unverified]**. Play-mode tests run from the command line with `-runTests -batchmode -testPlatform PlayMode` [9].
  3. **Real NGO between two processes:** a batchmode build or Editor started with NGO's `-mode host` / `-mode client` command-line pattern [8], one side replaying a recording, over localhost, with the Multiplayer Tools Network Simulator adding delay, jitter and loss presets [12]. Multiplayer Play Mode (up to 4 Editor players, Unity 6000.0.50f1+) is the interactive alternative [13]. This layer needs a lot of RAM (two Unity instances), so it is a separate script, not part of every test run **[derived]**.

## 1. What exists in this repo (read 2026-10-19)

- `TrackedPose` (`Settings/Core/HandMirroring.cs`): position and rotation as `System.Numerics`, no UnityEngine. `TrackedPoseJsonConverter` (`Session/Core/JsonLinesFile.cs`) writes it as `{"position":[x,y,z],"rotation":[x,y,z,w]}` [11].
- `ArmTrackingBehaviour` (`Tracking/Unity`): starts IOBT with `OVRPlugin.StartBodyTracking2(BodyJointSet.UpperBody)`, requests High fidelity, reads `OVRPlugin.GetBodyState4(Step.Render, UpperBody, ...)` every `Update`, converts each joint from tracking space (OpenXR flipped z) to the room, and keeps only shoulder, elbow, wrist and head for the rep log, at judging time [11].
- `BodyTrackingLifecycle` (`Tracking/Core`): plain C# rule that stops body tracking while paused, unfocused or off the head (PER-43). A recorder should reuse it [11].
- `BladeBehaviour` (`Blade/Unity`): in `LateUpdate` reads the sword controller anchor, moves it into the Strip frame (`StripFrame.ToStripFrame`) and builds the blade with `BladePose.GetBlade` [11].
- `SessionRecorder` (`Session/Core`): one JSON line per judged Rep, not a continuous recording. No continuous pose recorder exists. Nothing is networked yet [11].

So the current code records three arm joints at one instant per Rep; a replayable Fencer needs a new continuous recorder, but every input it reads is already read somewhere **[derived]**.

## 2. Meta tooling for body recording and playback

| Tool | What it records / plays | Usable for a real-headset Fencer? |
|---|---|---|
| Movement SDK `SequenceFileReader` + `SequencePlaybackManager` + `PlaybackFunctions` (v207) | Plays `.sbn` files: 156-byte start header (data version 0.04, OS, engine, bundle id, Meta XR SDK version, UTC start, snapshot count, start network time), then per snapshot `int size`, `int snapshotsSinceLastSync` and the bytes from `SerializeSkeletonAndFace`, then an 8-byte end header. Timestamps come from inside each snapshot; playback loops, seeks and interpolates with `GetInterpolatedSkeleton` [2][3] | Playback only. No writer in v207: `SerializeStartHeader` / `SerializeEndHeader` exist, but nothing writes snapshots to a file. The only user is the retargeting editor window, which previews bundled sequences from `Packages/com.meta.xr.sdk.movement/Editor/Native/Poses/Sequences` [2][3]. Data is post-retargeting and delta-compressed, so a recording is tied to one retargeting config |
| Movement SDK `BodyPoseRuntimeRecorder` (Body Tracking for Fitness sample) | One static pose in memory, for pose matching [2] | No |
| Meta XR Simulator Session Capture | Simulator head motion and keyboard, mouse or controller input, to `.vrs`; replays for CI [6] | No: simulator only, body tracking not mentioned |
| Meta XR Simulator body tracking | Plays a built-in synthetic upper-body motion for a few seconds [14] | No: not a recording of a person |

Conclusion: build our own recorder. The Movement SDK's `ISourceDataProvider` is the documented-in-code seam that lets our recording drive Meta's retargeter and networking unchanged [5].

## 3. The live send path the replay must feed (Movement SDK v207)

- `CharacterRetargeter` reads `_dataProvider.GetSkeletonPose()` in `Update`, where `_dataProvider = GetComponent<ISourceDataProvider>()`. On a headset this is `MetaSourceDataProvider : OVRBody, ISourceDataProvider`, which supports Upper Body and Full Body joint sets [5].
- `NetworkCharacterRetargeter.Owner` (`None`, `Host`, `Client`) is a plain settable property; the host side retargets, the client side applies received data [5]. Default send interval in the v207 source is `0.083333` s (12 Hz) with `SerializationCompressionType.High`; Meta's networking page says 30 fps and about 6 KB/s optimized (about 70 KB/s raw) [1][5]. Check which rate the bout uses when PER-58 is built.
- `NetworkCharacterHandler.SendData(networkTime)` calls `SerializeSkeletonAndFace(handle, snapshotData, ref bytes)` with `Timestamp = networkTime` and hands the bytes to the behaviour; the receiver queues them in `ReceiveData(NativeArray<byte>)` and applies them in `TryReceiveData(networkTime, renderTime)` [5].
- Other netcodes plug in through `INetworkCharacterSpawner`, `INetworkCharacterBehaviour` and, optionally, `INetworkCharacterRetargeter` [1]. NGO named and unnamed custom messages (`FastBufferWriter`) carry our own blade and claim messages [8].

Because the replay plugs in *above* the retargeter, the PC exercises the sender's retargeting, compression, send timing and the blade message, and the Quest exercises the receiver exactly as with a partner **[derived]**.

## 4. Size per minute [derived]

| Format | Per frame | 72 Hz | 36 Hz body, 72 Hz blade |
|---|---|---|---|
| Own binary, float32, Full Body 84 joints + head + controller | about 2.43 KB | 175 KB/s, 10.5 MB/min | about 5.5 MB/min |
| Own binary, Upper Body 70 joints | about 2.04 KB | 147 KB/s, 8.8 MB/min | about 4.7 MB/min |
| JSON Lines (rep-log style, about 9 characters per number) | about 6 KB | about 26 MB/min | about 13 MB/min |
| `.sbn`-style network snapshots (for comparison) | n/a | about 6 KB/s, 0.36 MB/min [1] | n/a |

Joint counts from Meta's body tracking page (84 Full Body = 70 upper + 14 lower) [7]. The IOBT update rate itself is not published **[unverified]**, so record every rendered frame and let playback interpolate. Writing 175 KB/s through a buffered `FileStream` is small next to the Quest's storage bandwidth **[derived]**. If size ever matters, quantize rotations to smallest-three (as in remote-multiplayer.md) for roughly 4x less.

## 5. Running the PC as player two

- **Scene:** a separate `ReplayFencer` scene (or bootstrap) with no `OVRCameraRig` and no OpenXR start-up, so the Editor runs without Link **[derived]**; whether Meta's OpenXR loader complains when no headset is attached is **[unverified]**.
- **Inputs:** a file picker or command-line argument for the recording, loop on/off, start offset, playback speed (slow-motion is useful for Parry practice) **[derived]**.
- **Pacing:** the replay runs on its own clock, not the opponent's, so it does not react. For solo practice that is a moving target, not a sparring partner **[derived]**. A later step could cut recordings into phrases (advance, lunge, retreat) and pick one on a cue.
- **Left-handed recordings:** record true, unmirrored poses (remote-multiplayer.md section 6); `HandMirroring` stays local-logic only.

## 6. Headless and automated checks

- Edit-mode tests already run in batchmode via `scripts/run-editmode-tests.sh` [10]. Unity's `-batchmode` suppresses dialogs and exits with code 1 on failure; `-nographics` skips the graphics device [15].
- The Test Framework runs Play-mode tests from the command line with `-runTests`, `-batchmode` and `-testPlatform PlayMode` (1.4 docs; the repo pins 1.6.0) [9].
- NGO's documented local-testing options are player builds, Multiplayer Play Mode and ParrelSync; its command-line helper starts host, server or client from a `-mode` argument [8][13]. NGO pages read do not mention `-batchmode` or Edit-mode use; treat NGO as Play-mode only **[unverified]**. That is why the Core layer (recording, replay clock, transform, Touch detector, judge) must not depend on NGO.
- Network Simulator (Multiplayer Tools, needs Unity Transport 2.0+ and NGO 1.1+) adds delay, jitter, loss and fuzz presets to the transport [12], so a scripted two-process run can check the judge's double-window behaviour at, for example, 30 ms and 65 ms one-way.

## Open questions

- Which joint set and send rate the bout uses (Full Body at 30 Hz per Meta's page, or the 12 Hz default in the v207 source). The recorder should capture Full Body either way.
- Whether body tracking can run at Full Body with passthrough without falling back to Low fidelity (remote-multiplayer.md section 3); log fidelity per frame in the recording.
- Whether the PC replay should also act as host (PC Editor hosts, Quest joins) for agent tests without a headset at all; nothing found against it **[derived]**.

## Sources

1. Meta, Networking with Movement SDK for Unity (components, `MSDKUtility` serialization, 70 KB/s raw vs about 6 KB/s at 30 fps, interfaces for other netcodes; updated 2025-12-15): https://developers.meta.com/horizon/documentation/unity/move-networking/
2. Meta, Unity-Movement source at tag v207.0.0 (the version pinned in `unity/Packages/manifest.json`), read 2026-10-19: `Runtime/Native/Scripts/MSDKUtility.cs` (`StartHeader`/`EndHeader`, 156 B and 8 B, version 0.04, `SerializeSkeletonAndFace`, `DeserializeSnapshotTimestamp`, `GetInterpolatedSkeleton`, `SerializeStartHeader`, native library `MetaMovementSDK_Utility`), `Runtime/Native/Plugins/` (Android, Linux, Mac, Win64), `Runtime/Tracking/Scripts/BodyTrackingForFitness/BodyPoseRuntimeRecorder.cs`, `Editor/Native/Scripts/MSDKUtilityEditorWindow.cs`: https://github.com/oculus-samples/Unity-Movement/tree/v207.0.0
3. Same tag, `Runtime/Native/Scripts/Serialization/` (`SequencePlaybackManager.cs`, `SequenceFileReader.cs`, `PlaybackFunctions.cs` with the `.sbn` extension and 8-byte snapshot header, `BandwidthRecorder.cs`): https://github.com/oculus-samples/Unity-Movement/tree/v207.0.0/Runtime/Native/Scripts/Serialization
4. Same tag, `Runtime/Native/Scripts/Networking/NetworkCharacterBehaviourLocal.cs` (in-process loopback, `NetworkTime` = `Time.time`): https://github.com/oculus-samples/Unity-Movement/blob/v207.0.0/Runtime/Native/Scripts/Networking/NetworkCharacterBehaviourLocal.cs
5. Same tag, `Runtime/Native/Scripts/Retargeting/ISourceDataProvider.cs`, `MetaSourceDataProvider.cs`, `CharacterRetargeter.cs`, and `Runtime/Native/Scripts/Networking/NetworkCharacterRetargeter.cs` (`Owner`, 0.083333 s send interval, High compression), `NetworkCharacterHandler.cs` (`SendData`, `ReceiveData`, `TryReceiveData`): https://github.com/oculus-samples/Unity-Movement/tree/v207.0.0/Runtime/Native/Scripts
6. Meta, Capture a Meta XR Simulator Session (records simulator inputs to VRS, replays for CI; updated 2026-09-03): https://developers.meta.com/horizon/documentation/unity/xrsim-session-capture/
7. Meta, Unity Movement body tracking (84 Full Body joints, 70 upper + 14 lower; updated 2026-10-07): https://developers.meta.com/horizon/documentation/unity/move-body-tracking/
8. Unity, Netcode for GameObjects 2.4.4 manual: Testing locally, https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.4/manual/tutorials/testing/testing_locally.html ; Command-line helper (`-mode host/server/client`), https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.4/manual/tutorials/command-line-helper.html ; Custom messages, https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.4/manual/advanced-topics/message-system/custom-messages.html (all read 2026-10-19)
9. Unity Test Framework 1.4, Running tests from the command line (`-runTests`, `-batchmode`, `-testPlatform EditMode/PlayMode`; read 2026-10-19): https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html
10. This repo, `docs/dev-loop.md` (adb pull of session logs, `metavr files pull`, Edit-mode test script), read 2026-10-19.
11. This repo at commit 6790d6a (2026-10-19): `unity/Assets/VrFencing/Settings/Core/HandMirroring.cs`, `Session/Core/JsonLinesFile.cs`, `Session/Core/SessionRecorder.cs`, `Tracking/Unity/ArmTrackingBehaviour.cs`, `Tracking/Core/BodyTrackingLifecycle.cs`, `Blade/Unity/BladeBehaviour.cs`, `.gitignore` (`[Rr]ecordings/`).
12. Unity Multiplayer Tools 2.2, Network Simulator (delay, jitter, loss presets; needs Unity Transport 2.0+ and NGO 1.1+; read 2026-10-19): https://docs.unity3d.com/Packages/com.unity.multiplayer.tools@2.2/manual/network-simulator.html
13. Unity Multiplayer Play Mode 1.6.3 (up to four Editor players, Unity 6000.0.50f1 or later, Windows and macOS; read 2026-10-19): https://docs.unity3d.com/Packages/com.unity.multiplayer.playmode@1.6/manual/index.html
14. Meta, Body tracking in Meta XR Simulator (built-in motion runs for several seconds; updated 2026-04-02): https://developers.meta.com/horizon/documentation/unity/xrsim-body-tracking/
15. Unity 6.0 Manual, Editor command line arguments (`-batchmode`, `-nographics`, `-executeMethod`; built 2026-10-17): https://docs.unity3d.com/6000.0/Documentation/Manual/EditorCommandLineArguments.html

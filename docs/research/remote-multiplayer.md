# Remote multiplayer: two Quests in different states on one shared Strip

Research for Linear PER-42. Researched 2026-10-19. Setup: Nikita on Quest 3, partner on Quest 3S, both in Developer Mode with the app sideloaded (no store listing), in different US states on home Wi-Fi behind NAT. Each fences in their own room in passthrough; the other appears as an avatar at the opponent En Garde Line, driven by IOBT upper body plus Generative Legs. Builds on quest3-tech-stack.md (stack, multiplayer paths, Avatars SDK frozen) and epee-rules-for-sim.md (40-50 ms double window, speeds, piste). Items marked **[derived]** are my own inference or arithmetic; **[anecdotal]** marks forum, fan-wiki or vendor-marketing sources; **[unverified]** marks claims with no primary source found.

## Recommendation

- **Connectivity: Unity Gaming Services Multiplayer Services (Relay + Sessions) with Netcode for GameObjects (NGO), Nikita's headset as host.** Free at 2 players (50 average CCU, 150 GiB/month), no Meta app ID, store listing or VPN needed, and it is one of the two netcodes Meta's Movement SDK networking supports [1][2][3]. Tailscale is not needed and is fragile on Quest (no official app; only an old 2024 APK reported working) [5]; keep it only as an optional direct-IP experiment.
- **Body: Movement SDK `CharacterRetargeter` onto our own rigged humanoid, synced with the Movement SDK's built-in networking (`NetworkCharacterRetargeter`, NGO flavour, about 6 KB/s at 30 Hz).** No Meta app ID. Do not use the Meta Avatars SDK: it is end-of-feature, and showing real user avatars needs an app ID, Platform SDK, an uploaded APK in a release channel and both accounts on it [10][11][12][13].
- **Quest 3S supports IOBT** (Meta's Unreal Movement page, updated 2026-09-30, names Quest 3 and 3S) [8]. Nothing found that changes the plan; verify on the 3S once.
- **Blade: send it separately from the body, at 60-72 Hz with velocity**, Hermite-interpolated with a ~50 ms adaptive buffer. The body can stay at the Movement SDK's 30 Hz.
- **Virtual judge: attacker-side Touch detection, host-headset judge.** Each headset runs the existing Touch detector on its own blade against the remote avatar as it sees it, and sends a timestamped claim on a clock synced to the host. The host compares claims within the double window (50 ms default, setting up to 150 ms), waits ~120 ms, then lights. Fair to within the clock error (about 5-15 ms) for "who was first"; the leftover unfairness is that the attacker hits a defender pose about 100-130 ms old cross-country, worth 5-20 cm of beginner body motion. Feels fine up to ~50 ms one-way, borderline at 50-80 ms, poor above that.
- **Shared Strip: each headset streams poses in its own Strip frame; the receiver rotates them 180 degrees about the Strip centre.** The bout uses the shorter of the two Strips; scaled markings already exist in the MVP.
- **Voice: out of app** (Nikita's call). Discord has a native Quest app (June 2026) but running its voice behind our passthrough app is unconfirmed; a phone with earbuds always works.
- **MVP choices mostly help.** ADR 0002 (poses as data, plain C# Core) is exactly what remote Touch detection and the judge need. The one thing to do now: build the Dummy (PER-32) as a rigged humanoid so it can later carry the remote body.

## 1. Connectivity

| Option | Works sideloaded, no Meta app ID? | Free tier / cost | Setup effort |
|---|---|---|---|
| UGS Relay + Sessions (`com.unity.services.multiplayer`) + NGO | Yes: needs a Unity Cloud project and Unity Authentication only; no Meta dependency found [unverified, no doc says either way] [1] | Relay: 50 average monthly CCU free, 3 GiB per CCU up to 150 GiB/month; then $0.16/CCU and $0.09/GiB. Lobby: 10 GiB/month free [1][4] | Low. Meta's Auto Matchmaking block wires NGO + Relay [2] |
| Photon Fusion 2 | Yes: needs a Photon App ID only [unverified] [6] | 20 CCU free dev/non-commercial (60 GB/month); 100 CCU free launch plan (Fusion only); paid 500 CCU $125/month [6][7] | Low. Same Building Blocks; the only path for Meta's Player Voice Chat block [2] |
| Meta Multiplayer Building Blocks | Auto/Custom Matchmaking, Networked Character Retargeter, Networked Grabbable: no app ID listed. Networked Avatar, Player Name Tag, Friends Matchmaking, Colocation (without Colocation Session): need a Meta app ID [2] | Free (rides on NGO or Fusion) | Blocks are a wrapper, not a transport. Grabbable needs Interaction SDK, which we do not use |
| Tailscale (VPN) + direct NGO/UDP | No official Quest or Horizon Store app; feature request open since 2022-10. Sideloaded Android APK reported working only up to v1.68.2 on Quest 3 (2024); 1.70+ "don't seem to work" [5][anecdotal] | Personal plan free: up to 6 users, unlimited devices [14] | Medium and fragile: pinned old APK, background VPN behind an immersive app unverified, battery cost unknown |
| ZeroTier | No Quest evidence either way [unverified] | n/a | Not recommended |

- **Latency of relay vs direct.** Relay traffic goes through a Unity relay server; if no region is set, Relay picks one by measured latency and loss to the host [3]. A relay near the path costs roughly 0-15 ms extra over direct **[derived]** from inter-region tables [18]. Tailscale reports direct NAT traversal "well north of 90%" of the time; otherwise it falls back to its DERP relays [15].
- **Meta app ID, if ever wanted.** Platform features (entitlement check, user names, Avatars) need an app created in the developer dashboard, an APK uploaded once to a release channel (this links package name to app ID), and both accounts on the team or channel; "only test users from the tested app's team pass entitlement checks" [12][13]. Not needed for the recommended path.

## 2. Latency vs épée timing, and the virtual judge

### Numbers

- **Backbone RTT from Boston** (server-to-server, checked 2026-10-19): NYC 8 ms, Washington 12, Chicago 20, Denver 38, Miami 39, Dallas 44, Phoenix 60, SF 64, Seattle 65, LA 66 [16]. Azure East US to West US P50: 67-69 ms (2026-07-30) [18].
- **Home last mile + Wi-Fi** adds roughly 10-30 ms per side, with spikes; 802.11ac indoor RTT spikes up to 228 ms were measured [19][unverified for the 10-30 ms figure].
- **Realistic RTT [derived]:** same region 15-35 ms; East Coast to Midwest 30-60 ms; East to West Coast 90-130 ms (45-65 ms one way).
- **Staleness of the remote pose** = one-way latency + interpolation buffer + about half a send interval [17]. Cross-country with a 50 ms buffer: about 100-130 ms **[derived]**. That is 2-3x the 40-50 ms double window, so whichever side judges naively decides who was first.

### How other games decide hits

- **Favor the shooter (Valve, Bernier 2001):** clients render the world about 100 ms in the past; the server rewinds targets by latency + interpolation and tests the hit as the shooter saw it, accepting that a victim can be hit after reaching cover [20][21].
- **Rewind caps (Riot VALORANT, 2020-07-28):** the server rewinds to what the shooter saw, with "some limits on how far the server is willing to rewind", so a high-ping player cannot hit someone long after they took cover; 20-50 ms differences decided fights in playtests [22].
- **Meta UltimateGloveBall:** NGO; the thrower's client is trusted for its own throw and not snapped, other clients snap to the host; jitter buffer drops late packets [23].
- **Eleven Table Tennis** [anecdotal]: peer-to-peer first, servers as fallback; the hitter's own paddle is authoritative and the game shows a ping-delayed "Pingball" [24].
- **VR melee PvP** (Thrill of the Fight 2, Ironlights): no published hit-authority design found. Ironlights hides latency by making all combat slow motion [25].
- **Rollback (GGPO)** assumes discrete, re-predictable inputs; tracked VR poses cannot be re-predicted, so it does not transfer [26] **[derived]**.

### Recommended judge (simple, beginner-grade)

1. **Each headset detects only its own blade's Touches**, with the existing Touch detector (ADR 0002), against the remote avatar's body as rendered locally. Favor the attacker: a Touch you see land counts. Local haptic fires at once.
2. **Claim message:** `{attacker, target zone, contact time on the shared clock}`.
3. **Shared clock:** NGO's network time (server time + RTT-based offset) or a Cristian-style ping: take many samples, keep the lowest-RTT ones. Error is bounded by half the path asymmetry, always under RTT/2; with filtering, about 5-15 ms on typical paths [27][28][29][derived]. Timestamp with a monotonic clock, not wall-clock time, which can jump [30].
4. **Judge runs on the host headset** (the NGO host). A cloud judge adds a hop and cost without adding fairness, because detection still happens on the attacker's headset. Both-headsets-agree gives the same answer only after both have both claims, and can split verdicts on packet loss. Write the rule as a pure Core function so it is testable and either side could recompute it **[derived]**.
5. **Rule:** first claim wins; a second claim within the double window makes a double. The host waits window + one-way latency + clock margin (about 50 + 50 + 15 = 115-120 ms) before lighting, which matches a real box, which also locks out only after the window. Reject claims staler than about 200 ms (VALORANT-style cap).

**Fairness limits [derived]:**
- Who-was-first: correct to the clock error, about 5-15 ms. Latency only delays the verdict.
- Stale defender: the attacker hits a defender pose one-way + buffer old, so a defender can be touched after retreating by up to body speed x staleness: 5-20 cm at 0.5-1.5 m/s and 100-130 ms. A Parry must happen that much earlier than in person.
- Stale attack for counter-actions: the defender sees the incoming attack late by the same staleness, so a Stop Hit that would land first in person may become a double or lose.
- Feels fine up to about 50 ms one-way (about 100 ms staleness, matching the ~100 ms RTT where VR shooter experience degrades [31]); borderline at 50-80 ms one-way (East to West on Wi-Fi); poor above that. Show RTT in the bout UI.
- **Wider double window (100-150 ms setting):** reasonable for beginners. It absorbs clock error and jitter and partly offsets the defender's late view on Stop Hits (window of about 45 ms + staleness restores the doubles). It does not fix Parries landing late or hits after retreating. Side effect: many more doubles. Keep 50 ms as the rule-faithful default.

## 3. Quest 3S body tracking

- **IOBT:** "Inside-Out Body Tracking (IOBT), which enables full-body tracking, is available on Meta Quest 3 and Meta Quest 3S." Body tracking in general: Quest 2, 3, 3S, Pro (Unreal Movement overview, updated 2026-09-30) [8]. The Unity body-tracking page (2026-10-07) does not name the 3S; it only contrasts Quest 3 with Quest 2 [9]. Earlier "Quest 3 only" wording predates the 3S. So the "Quest 3 only" note in quest3-tech-stack.md is out of date.
- **Generative Legs:** no device table anywhere. They come with the Full Body joint set (84 joints: 70 upper + 14 lower) and do not require IOBT ("a suggested fidelity mode and not a requirement of the API") [9]. So they work wherever body tracking works, including 3S **[derived]**.
- **Hardware:** Quest 3S has 4 IR tracking cameras plus 2 IR flood illuminators and no depth projector; Quest 3 has 4 IR cameras plus a depth projector. IOBT uses the side cameras on both [32][33][third-party]. No documented or measured IOBT quality difference between 3 and 3S was found; the 3S's flood LEDs help low-light tracking [33].
- **Shared caveat:** under heavy load (passthrough + controllers + high CPU, which is this app) IOBT silently falls back to low fidelity: same skeleton, but no elbow tracking and wrong spine on leans [9]. Either headset may send low-fidelity elbows.

## 4. Avatar

| | Meta Avatars SDK | Own rigged humanoid + Movement SDK retargeting |
|---|---|---|
| Status | End-of-feature: 40.0.1 (2025-09-11) is the final release; services keep working [10][11] | Current; Movement SDK v207 |
| Meta app ID / entitlement | Needed for the user's own avatar: app ID, `OvrPlatformInit`, access token, avatar permission in Data Use Checkup. Without it, only 33 preset avatars [34]. Sideloaded builds need a release-channel upload and both users on it [12][anecdotal for avatars] | None found [unverified, no page says Movement needs it] |
| Body source | Own body-tracking context; link to IOBT / Generative Legs not documented [35] | `CharacterRetargeter` + `OVRBody`, Full Body with legs [9] |
| Networking | `RecordStreamData` / `ApplyStreamData`, 4 detail levels, built-in jitter buffer; no byte sizes published [36] | `NetworkCharacterRetargeter`, NGO or Fusion, about 6 KB/s [3] |

Recommendation: retarget onto our own humanoid (the Dummy, PER-32 / PER-41). Setup: Retargeting Configuration Editor auto-maps known joints, then T-pose, Min/Max T-pose, validate, save to a config JSON [9]. Whether the new `CharacterRetargeter` requires a Mecanim Humanoid import is unverified; the older `OVRUnityHumanoidSkeletonRetargeter` does [37]. Import the Dummy as Humanoid anyway; it costs nothing.

## 5. Pose sync

- **Movement SDK has built-in body-pose networking** ("Networking with Movement SDK for Unity", updated 2025-12-15) [3]: `NetworkCharacterRetargeter`, `NetworkCharacterSpawner(NGO)`, `NetworkCharacterBehaviourNGO`, a loopback `NetworkCharacterBehaviourLocal` that shows the bandwidth, and low-level `MSDKUtility.SerializeSkeletonAndFace` / `GetInterpolatedSkeleton`. Supports NGO and Photon Fusion; other netcodes implement three interfaces.
  - Sends bone positions, lengths and rotations from the retargeting config; quantized (29 or 14 bits per rotation) and delta-compressed with acks over unreliable messages [3].
  - Bandwidth: 84 joints x 28 bytes at 30 fps is about 70 KB/s raw; optimized about 6 KB/s at 30 fps [3]. Release notes: v78 about 25% smaller packets, v83 moved to data streams and NetworkVariables [38].
- **Blade: send separately.** One rigid pose with smallest-three quaternion compression is about 10 bytes (29-bit rotation + ~50-bit position) [39]. At 72 Hz with linear velocity that is under 2 KB/s **[derived]**. Total for body + blade: under 10 KB/s each way, trivial for Relay's free bandwidth **[derived]**.
- **Interpolation buffer:** about 3x the send interval for 2-5% loss, plus 1-2 frames: about 150 ms at 30 Hz, about 85 ms at 60 Hz [17]. NGO's server-time buffer defaults to 50 ms [27]; Fusion 2 sizes its delay from measured jitter [40]. Use Hermite interpolation with sent velocity for positions (removes pulsing), slerp for rotations [17].
- **Believable remote blade at 2-3 m/s [derived]:** at 100-130 ms staleness the remote tip trails truth by 20-40 cm; it looks smooth but late, which is what the judge design above accepts. Optional: extrapolate the remote blade forward by up to ~RTT/2 with sent velocity, clamped (for example 50 ms, 15 cm); dead-reckoning error grows as 0.5 x a x t^2 and overshoots on direction reversals, so keep it short or off for the hit test. Use the interpolated (not extrapolated) body for Touch detection.
- **Blade-on-blade:** two views can never agree on a blade contact at this latency. Treat blade contact as local cosmetics (each side bends and buzzes its own view) and do not judge it **[derived]**.

## 6. Shared Strip

- **Frames [derived].** The MVP's Strip frame (HandMirroring.cs) is +x Fencer's right, +y up, +z forward along the Strip. For the bout, put its origin on the floor at the bout centre (midway between the two En Garde Lines, on the Strip's centreline), built from the two placement points projected to the floor, yaw only (gravity-aligned). Each headset sends all poses (body joints, blade) in its own bout frame with height from the floor.
- **Receiver transform [derived]:** rotate 180 degrees about the vertical axis through the bout centre: (x, y, z) -> (-x, y, -z), rotation pre-multiplied by a 180-degree yaw. If the frame origin is instead the Fencer's own En Garde Line, the same rotation becomes (x, y, z) -> (-x, y, s - z) with s the En Garde Line spacing. This is a rotation, not a reflection, so a right-hander stays right-handed. Send true poses; apply HandMirroring only to local logic, never to network data.
- **Different room lengths [derived].** In real fencing both fencers can drift along the whole piste, so each room needs the whole bout Strip, not half. Bout length = the shorter of the two Strips; the longer room uses a segment of that length centred on its Strip (or anchored at its rear, a setting). En Garde Lines and Warning Zones use the MVP's scaled-markings rule on the bout length, with the 4 m spacing shrunk if needed so each side keeps at least about 1 m to retreat. Each Fencer's rear limit is their own room's; leaving it ends the phrase as in t.33.
- **No physical contact risk** since the opponent is virtual; add a halt when the two bodies come closer than about 0.5 m (corps-a-corps) so avatars never pass through each other.
- **Height:** both avatars stand on their own floors; no scaling, so real height difference shows.

## 7. Voice

Out of app, per Nikita. Discord has a native Horizon Store app (Meta blog, 2026-06-30) [41]; a secondary report says it can stay pinned while other VR apps run [42], but UploadVR (2026-07-02) did not confirm background voice and reported crashes of the running game [43]. Telegram has no Quest app. Test Discord on both headsets with the passthrough app in front; the fallback is a phone call with earbuds under the headset. Our app does not use the microphone, so there is no mic contention.

## 8. MVP choices that help or block

- **Helps: ADR 0002 (poses as data, plain C# Core).** The Touch detector already consumes `TrackedPose` data, so the remote avatar's interpolated body can feed it unchanged. The judge rule, clock offset filter, Strip-to-Strip transform and claim message are all pure C# and Edit-mode testable.
- **Helps: Strip module (two-point placement, per-room anchor, scaled markings).** The shared frame is just the Strip frame; expose it as a transform and reuse the scaling rule for the bout length.
- **Helps: ADR 0001 (Meta XR SDK, no XRI).** Movement networking and Multiplayer Building Blocks target this stack. The only block that needs Interaction SDK (Networked Grabbable) is not needed.
- **Helps if done now: build the Dummy (PER-32/PER-41) as a rigged humanoid**, imported as Humanoid, with standard bone names. Then the same mesh is the remote opponent via `CharacterRetargeter`. A static mesh would have to be redone.
- **Watch: Tracking module requests IOBT High fidelity**; under passthrough load it can fall back to low fidelity on either headset. Log the fidelity actually delivered.
- **Watch: clocks.** RepLogEntry uses wall-clock UTC timestamps, fine for logs; Touch claims need a monotonic clock plus the network offset.
- **Watch: HandMirroring** must stay a local-logic transform; network data is always true, unmirrored poses.
- **Nothing blocks.** Unity 6000.0 works with `com.unity.services.multiplayer` and NGO 2.x; no Meta app ID is needed anywhere on the recommended path.

## Open questions

- Partner's state, and both ISPs (fiber/cable vs DSL): decides whether RTT is 30 ms or 120 ms.
- Room lengths available for each Strip.
- Whether to ever want real Meta avatars or friend invites (would need a dashboard app ID and a release-channel upload).
- Confirm on device: IOBT high fidelity on the 3S with passthrough running; Discord voice behind the app.

## Sources

1. Unity Gaming Services pricing (Relay, Lobby, Vivox, Distributed Authority; page footer 2026): https://unity.com/products/gaming-services/pricing
2. Meta, Multiplayer Building Blocks (app ID requirements per block; Lobby/Relay packages replaced by com.unity.services.multiplayer; updated 2026-04-22): https://developers.meta.com/horizon/documentation/unity/bb-multiplayer-blocks
3. Meta, Networking with Movement SDK for Unity (components, compression, 70 KB/s vs 6 KB/s at 30 fps, NGO and Fusion; updated 2025-12-15): https://developers.meta.com/horizon/documentation/unity/move-networking/ . Unity Relay region selection: https://docs.unity.com/en-us/relay/qos
4. Unity, How is the Relay service priced: https://support.unity.com/hc/en-us/articles/4410136449812-How-is-the-Relay-Service-Priced ; Multiplayer Services SDK: https://docs.unity.com/ugs/en-us/manual/mps-sdk/manual
5. Tailscale GitHub issue 5892, "FR: Meta Quest Store release for Tailscale Client" (opened 2022-10-11, last comments 2024-11-17): https://github.com/tailscale/tailscale/issues/5892
6. Photon pricing and licensing (updated 2026-09-21): https://doc.photonengine.com/photon/current/pricing
7. Photon Fusion pricing: https://www.photonengine.com/fusion/pricing
8. Meta, Unreal Movement overview (IOBT on Quest 3 and 3S; updated 2026-09-30): https://developers.meta.com/horizon/documentation/unreal/unreal-movement-overview/
9. Meta, Unity Movement body tracking (84/70 joints, fidelity fallback, retargeting workflow; updated 2026-10-07): https://developers.meta.com/horizon/documentation/unity/move-body-tracking/
10. Meta Avatars SDK package (40.0.1 final, 2025-09-11): https://developers.meta.com/horizon/downloads/package/meta-avatars-sdk/
11. Meta Avatars overview (end-of-feature; 2026-04-08): https://developers.meta.com/horizon/documentation/unity/meta-avatars-overview/
12. Meta, Entitlement check (team test users, release channel, first upload links app ID; 2026-08-14): https://developers.meta.com/horizon/documentation/unity/ps-entitlement-check/
13. Meta, Platform SDK setup (2026-08-14): https://developers.meta.com/horizon/documentation/unity/ps-setup/
14. Tailscale pricing (Personal: up to 6 users): https://tailscale.com/pricing/
15. Tailscale, NAT traversal improvements part 1 (2025-10-15): https://tailscale.com/blog/nat-traversal-improvements-pt-1 ; connection types: https://tailscale.com/docs/reference/connection-types
16. WonderNetwork, pings from Boston (checked 2026-10-19): https://wondernetwork.com/pings/Boston
17. Gaffer On Games, Snapshot Interpolation (2014-11-30): https://gafferongames.com/post/snapshot_interpolation/
18. Microsoft, Azure network round-trip latency statistics (2026-07-30): https://learn.microsoft.com/en-us/azure/networking/azure-network-latency
19. Zhang et al., 802.11ac latency measurements, ICPADS 2018: https://pmlab.cse.usf.edu/assets/pubs/zhang-icpads18.pdf
20. Bernier, Latency Compensating Methods in Client/Server In-game Protocol Design and Optimization (GDC 2001): https://developer.valvesoftware.com/wiki/Latency_Compensating_Methods_in_Client/Server_In-game_Protocol_Design_and_Optimization
21. Valve, Source Multiplayer Networking (100 ms interpolation, lag compensation): https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking
22. Riot Games, Peeking into VALORANT's Netcode (2020-07-28): https://www.riotgames.com/en/news/peeking-valorants-netcode
23. Meta, UltimateGloveBall ball physics and networking: https://github.com/oculus-samples/Unity-UltimateGloveBall/blob/main/Documentation/BallPhysicsAndNetworking.md
24. Eleven Table Tennis multiplayer and Pingball (fan wiki, anecdotal): https://eleven-vr.fandom.com/wiki/Multiplayer , https://eleven-vr.fandom.com/wiki/Pingball
25. UploadVR, Ironlights: https://uploadvr.com/ironlights-melee-vr/
26. Infil, Netcode explainer (rollback): https://words.infil.net/w02-netcode-p5.html
27. Unity NGO 2.4, NetworkTimeSystem (50 ms server buffer default) and NetworkTime/ticks: https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.4/api/Unity.Netcode.NetworkTimeSystem.html , https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.4/manual/advanced-topics/networktime-ticks.html
28. NIST, Lucky packets and the Network Time Protocol: https://www.nist.gov/publications/lucky-packets-and-network-time-protocol-ntp
29. Cristian, Probabilistic clock synchronization (1989): https://web.eecs.umich.edu/~manosk/assets/papers/Cristian.pdf
30. Android, SystemClock (wall clock can jump; elapsedRealtime is monotonic): https://developer.android.com/reference/android/os/SystemClock
31. WPI technical report surveying latency thresholds (VR shooter about 100 ms RTT, avatar motion about 75 ms): https://ftp.cs.wpi.edu/pub/techreports/pdf/20-02.pdf
32. Road to VR, Quest 3S vs Quest 3 vs Quest 2 specs (third-party): https://roadtovr.com/quest-3s-quest-3-quest-2-specs-compared/
33. UploadVR, Quest 3S specs and IOBT side cameras (third-party): https://uploadvr.com/quest-3s-specs , https://uploadvr.com/bonelab-plans-to-add-inside-out-body-tracking-quest-3s
34. Meta, Load avatars (app ID, access token, 33 presets; 2025-07-09): https://developers.meta.com/horizon/documentation/unity/meta-avatars-load-avatars/
35. Meta, OvrAvatarEntity (body tracking context; 2025-07-09): https://developers.meta.com/horizon/documentation/unity/meta-avatars-ovravatarentity/
36. Meta, Avatars networking: https://developers.meta.com/horizon/documentation/unity/meta-avatars-networking
37. Meta, OVRUnityHumanoidSkeletonRetargeter API (v207): https://developers.meta.com/horizon/reference/unity/v207/class_o_v_r_unity_humanoid_skeleton_retargeter
38. Unity-Movement GitHub releases: https://github.com/oculus-samples/Unity-Movement/releases
39. Gaffer On Games, Snapshot Compression (2015-01-04): https://gafferongames.com/post/snapshot_compression/
40. Photon Fusion 2 TimeSyncConfiguration and network project config: https://doc-api.photonengine.com/en/fusion/v2/class_fusion_1_1_time_sync_configuration.html , https://doc.photonengine.com/fusion/current/manual/network-project-config
41. Meta blog, Discord now available on Meta Quest (2026-06-30): https://www.meta.com/blog/discord-launch/
42. vr.org, Discord native Quest app (2026-07-01, secondary): https://vr.org/articles/discord-native-quest-app-launch-2026
43. UploadVR, Discord now on Meta Quest (2026-07-02): https://www.uploadvr.com/discord-now-on-meta-quest-with-streaming/

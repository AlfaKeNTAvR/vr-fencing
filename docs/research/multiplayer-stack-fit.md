# Multiplayer stack fit: Relay + Sessions, NGO and Movement networking on our exact versions

Research for Linear PER-54. Researched 2026-10-19. Follows remote-multiplayer.md (PER-42), which recommended Unity Multiplayer Services (Relay + Sessions) with Netcode for GameObjects (NGO) and the Movement SDK's `NetworkCharacterRetargeter`. This note checks that stack against what is actually in the repo: Unity 6000.0.85f1, Android (Quest 3 and 3S), Unity OpenXR 1.18.0, Meta XR Core / MRUK / Haptics 207.0.1, Movement SDK 207.0.0 (git tag), URP 17.0.4, sideloaded APK, no Meta app ID.

Markers: **[derived]** is my own inference or arithmetic; **[unverified]** is a claim with no primary source found. "Package source" means I read the package's own code: Movement 207.0.0 and Core 207.0.1 from a local Package Cache of this repo, NGO 2.13.3 from the tarball on packages.unity.com. Registry dates come from the `time` field of packages.unity.com (checked 2026-10-19).

## Recommendation

- **Yes, the stack fits.** Nothing in our manifest conflicts with NGO 2.x or Multiplayer Services 2.x. No Meta package depends on NGO or Unity Transport, and every shared dependency (Burst, Collections, Newtonsoft) is already at or above what the new packages ask for [1][2][3].
- **Pin these two packages** in `unity/Packages/manifest.json`:
  - `"com.unity.netcode.gameobjects": "2.13.3"` (2026-09-21, needs Unity 6000.0). Not 3.x: NGO 3.0.0 (2026-09-15) and 3.1.0 (2026-10-04) need Unity 6000.7.0b1, a beta [1].
  - `"com.unity.services.multiplayer": "2.3.3"` (2026-09-15, needs Unity 6000.0). 2.4.1 is newer (2026-10-16) but only three days old and itself reverts a 2.4.0 regression; move to 2.4.x after it settles [2][4] [derived].
  - Leave Unity Transport, Authentication and Core to resolve as dependencies (Transport 2.7.3, Authentication 3.7.0, Core 1.18.0). Keep Movement at 207.0.0: its NGO code switches on by itself once NGO is installed [5].
- **Known conflicts: none blocking.** NGO 2.7+ marks the `RequireOwnership` RPC field obsolete as a warning (not an error), and Movement 207's spawner uses it, so expect CS0618 warnings only [6][7]. Do not install the standalone `com.unity.services.lobby` or `.relay` packages next to Multiplayer Services: 2.4.0 added a warning because the pair gives CS0433 compile errors [4].
- **Humanoid and the Dummy: the Dummy needs a rig first.** Meta's networking guide says the character "must be rig type Humanoid", and the body-tracking guide asks for an Animator with an assigned avatar [8][9]. The runtime retargeter only writes joint transforms (no Animator, no skinned mesh needed), but the setup wizard only works on a model asset with a bone hierarchy [5]. Our Dummy is neither: it is capsules generated at runtime from `DummyLayout`, with a 3-joint sword arm and no skeleton. A Humanoid-import skeleton already ships in our Movement package (`Shared/ThirdParty/Unity/Armature.fbx`, Unity Companion License) and is a ready starting rig [5].
- **Editor as player 2: yes for networking, no for its own body.** The Editor on Windows can create or join a Relay session by join code; Unity's own Sessions tutorial runs both players in the Editor [10]. Movement ships Win64 native libraries, so the Editor can receive and draw the headset's body [5]. Without a headset, the Editor sends no body: Movement only sends once a tracked pose has been applied [5]. For an Editor player 2, script the opponent body and blade through our own messages, or connect a Quest over Link. With no Link, OpenXR start-up fails quietly and Play mode continues as a flat view [11] [derived].
- **Unity Cloud setup** (the project is not linked yet: `cloudProjectId` is empty in `ProjectSettings.asset`):
  1. Unity Dashboard: Projects, Create project (or Unity Hub with "Connect to Unity Cloud") [12].
  2. Editor: Edit > Project Settings > Services > "Use an existing Unity project ID", pick the organization and project, "Link project ID" [12].
  3. Add the two packages above.
  4. In code: `UnityServices.InitializeAsync()`, then `AuthenticationService.Instance.SignInAnonymouslyAsync()` [13]. The host calls `CreateSessionAsync(new SessionOptions { MaxPlayers = 2 }.WithRelayNetwork())` and reads `session.Code`; the partner calls `JoinSessionByCodeAsync(code)` [14][15].
  5. Whether Relay must also be switched on in the Dashboard is [unverified]; if allocation fails with a "not enabled" error, turn it on there.
- **Cost at 2 players: cents per month, but add a payment method.** CCU is far under the 50 free [16]. The free Relay bandwidth, though, scales with *actual* average CCU, 3 GiB per CCU per month [17]. Two people fencing a few hours a month get almost none, so the body stream goes past it: about $0.10 to $0.15 a month at 20 bout-hours [derived, below]. Going past *any* free limit blocks all UGS for the project until payment details are entered, and disables it after 30 days [18]. No card is needed to start with Relay or Lobby [19]. Add one before regular bouts so a bout is never cut off.

## 1. What is installed now

From `unity/Packages/manifest.json` and `packages-lock.json` (2026-10-19):

| Package | Version | Relevant dependencies |
|---|---|---|
| Unity Editor | 6000.0.85f1 | |
| `com.meta.xr.sdk.core` | 207.0.1 | xr.hands 1.7.2; no netcode dependency |
| `com.meta.xr.mrutilitykit` | 207.0.1 | core 207.0.1, newtonsoft 3.0.2 |
| `com.meta.xr.sdk.haptics` | 207.0.1 | core 207.0.1 |
| `com.meta.xr.sdk.movement` | 207.0.0 (git `v207.0.0`) | burst 1.8.18, textmeshpro 3.0.6, ugui; `package.json` needs Unity 6000.0.66f2 [5] |
| `com.unity.xr.openxr` | 1.18.0 | inputsystem, xr.management, core-utils |
| `com.unity.render-pipelines.universal` | 17.0.4 | |
| `com.unity.nuget.newtonsoft-json` | 3.2.2 | |
| Resolved transitively | burst 1.8.30, collections 2.7.1, mathematics 1.3.2 | |

Android Player settings already force the INTERNET permission (`ForceInternetPermission: 1`), min SDK 32, target 34, IL2CPP. No project manifest removes it.

## 2. Package versions and compatibility

Registry facts (packages.unity.com, checked 2026-10-19) [1][2][3]:

| Package | Candidate | Released | Unity min | Dependencies |
|---|---|---|---|---|
| `com.unity.netcode.gameobjects` | 2.13.3 | 2026-09-21 | 6000.0 | transport 2.6.0, mono-cecil 1.11.4 |
| same | 3.0.0 / 3.1.0 | 2026-09-15 / 2026-10-04 | **6000.7.0b1** | `com.unity.netcode` 7.x, transport 6.7.0 |
| `com.unity.services.multiplayer` | 2.3.3 | 2026-09-15 | 6000.0 | transport 2.7.3, collections 2.6.7, authentication 3.7.0, core 1.18.0, qos 1.4.1, wire 1.5.1, deployment 1.7.2, newtonsoft 3.2.2 |
| same | 2.4.1 | 2026-10-16 | 6000.0 | transport 2.7.4, collections 2.6.8, authentication 3.8.0, qos 1.5.1 |
| `com.unity.transport` | 2.7.4 (latest 2.x) | 2026-07-13 | 2022.3 | burst 1.8.24, collections 2.2.1 |

Fit checks:

- **Unity version.** NGO 2.13.3 and Multiplayer Services 2.x both need 6000.0 with no patch minimum; 6000.0.85f1 qualifies. NGO 3.x is out until we move to 6000.7 [1][2].
- **Shared dependencies.** Burst 1.8.30 and Collections 2.7.1 are already resolved, above what Transport (Burst 1.8.24) and Multiplayer Services (Collections 2.6.7 or 2.6.8) ask. Newtonsoft 3.2.2 matches. Unity resolves to the highest requested version, so nothing downgrades [derived].
- **Meta XR Core 207.** Its Multiplayer Building Blocks compile against NGO "1.7.1" or later and Multiplayer Services "1.1.0" or later (`versionDefines` in `Meta.XR.MultiplayerBlocks.NGO.asmdef`) [20]. Its Auto and Custom Matchmaking blocks call the older `RelayService` and Lobby APIs directly and set `UnityTransport.SetHostRelayData` themselves; they do not use Sessions [20]. Auto Matchmaking joins any open lobby, so for two named people, Sessions with a join code is the better fit [derived].
- **Movement 207 NGO code.** `Meta.XR.Movement.Networking.NGO.asmdef` defines `UNITY_NGO_MODULE_DEFINED` when `com.unity.netcode.gameobjects` is present (any version), and all NGO code sits behind it [5]. It uses:
  - the universal `[Rpc(SendTo...)]` attribute, added in NGO 1.8.0 [6];
  - a custom `NetworkVariableBase` subclass overriding `WriteDelta`, `WriteField`, `ReadField`, `ReadDelta(reader, keepDirtyDelta)`, `ResetDirty` and `IsDirty`. All six still exist with the same signatures in NGO 2.13.3 [7] [derived, from comparing source];
  - `RequireOwnership = false` on the spawn RPC. In NGO 2.13.3 that field is `[Obsolete("RequireOwnership is deprecated...")]` with no error flag, so this gives a compiler warning, not a build break [7].
- **Platform SDK.** The Movement NGO spawner reads the user's Meta ID only if `com.meta.xr.sdk.platform` is installed (`META_PLATFORM_SDK_DEFINED`); otherwise it spawns with ID 0 [5]. We do not have the Platform SDK, so no app ID or entitlement check is involved.
- **OpenXR 1.18.0.** No dependency overlap with the netcode packages beyond the Input System [derived]. Found no published conflict [unverified that none exists].
- **Unity Transport and Relay protocols.** Sessions supports UDP, DTLS and secure WebSocket Relay protocols; the default can differ by platform [21]. Since 2.1.0, set the protocol through `SessionOptions` / `NetworkOptions`; the old `RelayProtocol` argument on `WithRelayNetwork` is obsolete [4].

Multiplayer Services 2.x changelog points that matter here [4]: 2.2.1 (2026-04-13) uses NGO's timeouts when connecting the NetworkManager during session creation; 2.3.1 (2026-08-25) fixed a CS0619 error, but only on Unity 6000.4+; 2.4.0 (2026-10-07) made cancelled operations throw `OperationCanceledException`, and 2.4.1 (2026-10-16) reverted another 2.4.0 change. Nothing Android-specific is listed.

## 3. CharacterRetargeter, Humanoid and our Dummy

What Meta's docs say:
- Networking guide (updated 2025-12-15): the "Add Networked Retargeted Character" wizard lets you select a model, "Check its validity (must be rig type Humanoid)", then save a prefab and retargeting config [8].
- Body-tracking guide (updated 2026-10-07): "Import the custom character into the Unity Project as an asset"; the tool "will automatically find a set of known joints"; "Ensure your character has an animator component with an assigned avatar" [9].

What the 207.0.0 package code does [5]:
- `CharacterRetargeter` (runtime) never touches `Animator`, `HumanBodyBones` or the Humanoid avatar. Only the optional locomotion processor uses an Animator. It writes joint transforms from a JSON retargeting config. So at runtime a plain bone hierarchy is enough, and meshes parented to bones follow without skinning [derived].
- The editor wizard (`MSDKUtilityEditor`) checks a rig by structure, not by Mecanim type: it needs a transform with at least 3 children, each at least 2 levels deep (hips with spine and two legs). Joint auto-mapping is done natively by known joint types. It also needs an asset path (a model or prefab in the project) to store the config next to; otherwise it throws "No asset path found for asset."

Our Dummy (`unity/Assets/VrFencing/Dummy/`):
- `DummyLayout` (Core) describes the Dummy as capsules in the Dummy frame, scaled to the Fencer's height. `DummyBehaviour` builds renderers for those capsules at runtime with a translucent material.
- Only the sword arm is a joint chain (Shoulder, Elbow, Wrist transforms) so arm Target Zones move with `SetArmPose`. There is no skeleton, no model asset and no skinned mesh.

Conclusion [derived]: `CharacterRetargeter` cannot drive the current Dummy. It needs a model asset with a humanoid bone hierarchy first. The documented, lowest-risk path is an FBX imported as Humanoid. Two ways in:
- **Start from `Armature.fbx`**, which ships in Movement 207 (`Shared/ThirdParty/Unity/`, `animationType: 3`, i.e. Humanoid, Unity Companion License), or from the `Realistic.fbx` sample (also Humanoid). Draw it with the Dummy's translucent material.
- **Make our own** Blender rig sized like `DummyLayout`.

Either way, the Target Zone capsules would be parented to bones (chest to spine, arm zones to the arm bones, thigh and foot zones to the legs) instead of being fixed to the stance. That decision (one Dummy model for both the solo Dummy and the remote opponent, or two) belongs to PER-59.

## 4. Editor on Windows as player 2

- **Relay and Sessions in the Editor:** supported. Unity's "Build a session with Netcode for GameObjects" tutorial creates a session in Play mode, then joins it from a second Multiplayer Play Mode player by entering the join code [10]. Relay rides on Unity Transport over UDP and is not tied to a platform; Unity Authentication covers "mobile and PC" [21][22].
- **Anonymous sign-in on one machine:** the session token is stored per Authentication profile ("Players can use profiles to sign in to multiple accounts on a single device") [23]. Headset and Editor are different devices, so the default profile is fine. Running the Editor *and* a Windows build on the same PC needs a different profile in one of them (`InitializationOptions.SetProfile`) [23] [derived].
- **XR on the Editor side:** our Standalone XR settings have the OpenXR loader with "Initialize XR on Startup" on (`Assets/XR/XRGeneralSettingsPerBuildTarget.asset`). When no loader starts (no Link, no XR Simulator), XR Management sets `activeLoader` to null without throwing, so Play mode runs as a flat view [11] [derived, from xr.management source]. OVRManager will log errors; turn XR off for a dedicated "flat player 2" run.
- **Body from the Editor:** Movement's `NetworkCharacterHandler.SendData` returns early until the retargeter has an applied pose. With no body tracking in the Editor, nothing is sent and the headset sees no body for player 2 [5]. Movement ships Win64 natives (`MetaMovementSDK_Utility.dll`), so *receiving* and drawing the headset's body in the Editor works [5] [derived]. For testing, player 2 can drive a scripted body and blade through our own messages (ADR 0002 poses as data), or use a Quest over Link as the Editor's body source.

## 5. Unity Cloud project and cost

Setup (Unity docs, 2026) [12][13][14][15]:
1. Create the project in the Unity Dashboard (Home > Projects > Create project) or from Unity Hub with "Connect to Unity Cloud".
2. Link in the Editor: Edit > Project Settings > Services > Use an existing Unity project ID > choose organization and project > Link project ID. This fills `cloudProjectId` in `ProjectSettings.asset`, currently empty [derived].
3. Install the packages; Multiplayer Services brings Authentication.
4. Initialize and sign in anonymously before any session call: "you must authenticate your players after initializing Multiplayer Services" [13].
5. Host creates a session with `MaxPlayers = 2` and `.WithRelayNetwork()`; the partner joins with the code. Sessions integrates with NGO "out of the box" [21].
6. Unity says any game using UGS products that rely on Unity Authentication "must integrate the notification API" for Digital Services Act notices [24]. For a private two-person sideload this is a formality; flagging it, not acting on it.

Pricing (unity.com pricing page and Unity support, checked 2026-10-19) [16][17][18][19]:
- Relay: first 50 average monthly CCU free; bandwidth "3 GiB per CCU for free up to a max of 150 GiB / month"; overage $0.16 per CCU and $0.09 per GiB (US + EU).
- The bandwidth allowance scales with actual CCU: "if you have a lower CCU your free tier allowance of bandwidth will also be reduced". Bandwidth is "the total data transmitted from the Relay servers to users" (Unity support, updated 2026-09-24) [17].
- Lobby (which Sessions uses): 10 GiB per month free per region group. Authentication: free [16].
- No payment method is needed to start with Relay or Lobby (Unity Billing FAQ, updated 2024-10-07) [19]. "If you exceed the free tier limit for any service, your access to all UGS services and APIs is blocked ... If you haven't entered payment details within 30 days, the services are disabled" (Unity pricing and billing docs) [18].

Cost estimate [derived]:
- Traffic: body about 6 KB/s (Movement's figure at 30 Hz) plus blade under 2 KB/s, so about 8 KB/s received per player. Relay sends each player what the other sends: about 16 KB/s egress for a 2-player bout, about 58 MB per bout-hour, before headers.
- Free allowance: one average CCU is one player connected all month (about 720 h), so 3 GiB / 720 h is about 4.3 MiB per player-hour. A 2-player bout-hour earns about 9 MiB free.
- Overage: about 0.045 GiB per bout-hour, so about $0.004 per bout-hour. At 20 bout-hours a month, about 0.9 to 1.3 GiB over (allowing 0 to 50% header overhead), so $0.08 to $0.12 a month, plus $0 for CCU.
- Consequence: the money is negligible, but it crosses a free limit, and by [18] that blocks all UGS until a card is on file. Whether Unity enforces the block for overages this small is [unverified]. Plan to add a payment method when the project is created.
- Alternative not pursued: Distributed Authority has a flat free tier (6,000 connectivity hours and 100 GiB egress a month) [16], so 2 players would cost $0. But Movement's NGO code assumes a server (`IsServer`, `SendTo.Server`) and Distributed Authority has none; whether Movement networking works under it is [unverified]. Not worth it for cents.

## 6. What this changes in earlier research

- **remote-multiplayer.md "Free at 2 players (50 average CCU, 150 GiB/month)": partly wrong.** The 150 GiB is the cap at 50 CCU. At our CCU the free bandwidth is tiny, and the body stream goes past it. Cost stays under about $1 a month, but plan on a payment method to avoid an all-services block.
- **remote-multiplayer.md "Whether the new `CharacterRetargeter` requires a Mecanim Humanoid import is unverified": now answered.** Meta's docs require rig type Humanoid and an Animator with an avatar. The runtime code does not need either, but the supported setup path does.
- **remote-multiplayer.md "build the Dummy as a rigged humanoid ... the same mesh is the remote opponent": still the right direction, but the current Dummy (PER-32) was built as runtime capsules, not a rig.** It needs a humanoid model asset first. `Armature.fbx` in the Movement package is a ready one.
- **remote-multiplayer.md "Nothing blocks. Unity 6000.0 works with `com.unity.services.multiplayer` and NGO 2.x": confirmed, now with exact pins.** It holds for NGO 2.x only: NGO 3.x needs Unity 6000.7 (beta).
- **remote-multiplayer.md "Meta's Auto Matchmaking block wires NGO + Relay": true, but the block uses the older direct Relay and Lobby calls and random lobby matching.** Use Sessions with a join code instead.

## Open questions

- Is a Dashboard toggle still needed to enable Relay for a new project? [unverified]
- Does Unity enforce the all-services block for a bandwidth overage of a few cents? [unverified] Avoided by adding a card.
- One rig for both the solo Dummy and the remote opponent? Decide in PER-59.
- First real test: a Quest 3 host and the Editor as client over Relay. Log NGO RTT and the bytes actually sent, to replace the 8 KB/s estimate.

## Sources

1. Unity package registry, `com.unity.netcode.gameobjects` versions, dates and Unity minimums (checked 2026-10-19): https://packages.unity.com/com.unity.netcode.gameobjects
2. Unity package registry, `com.unity.services.multiplayer` (checked 2026-10-19): https://packages.unity.com/com.unity.services.multiplayer
3. Unity package registry, `com.unity.transport`, `com.unity.services.authentication`, `com.unity.services.core`, `com.unity.collections` (checked 2026-10-19): https://packages.unity.com/com.unity.transport , https://packages.unity.com/com.unity.services.authentication , https://packages.unity.com/com.unity.services.core , https://packages.unity.com/com.unity.collections
4. Multiplayer Services SDK changelog, 2.0.0 (2025-11-13) to 2.4.1 (2026-10-16): https://docs.unity3d.com/Packages/com.unity.services.multiplayer@2.4/changelog/CHANGELOG.html
5. Meta Movement SDK 207.0.0 package source (git tag `v207.0.0` of oculus-samples/Unity-Movement, as resolved in this repo): `package.json`; `Runtime/Native/Scripts/Networking/NGO/*` (asmdef, `NetworkCharacterBehaviourNGO`, `NetworkCharacterSpawnerNGO`, `NetworkCharacterDataStreamNGO`); `Runtime/Native/Scripts/Networking/NetworkCharacterHandler.cs`; `Runtime/Native/Scripts/Retargeting/CharacterRetargeter*.cs`; `Editor/Native/Scripts/MSDKUtilityEditor.cs`; `Runtime/Native/Plugins/Win64/`; `Shared/ThirdParty/Unity/Armature.fbx(.meta)` and `LICENSE.md`. Repo: https://github.com/oculus-samples/Unity-Movement/tree/v207.0.0
6. NGO changelog (Rpc attribute in 1.8.0, 2023-12-12; RequireOwnership deprecated in 2.7.0, 2025-10-27; 2.8.0 to 2.13.3 entries): https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.13/changelog/CHANGELOG.html
7. NGO 2.13.3 package source (`Runtime/Messaging/RpcAttributes.cs`, `Runtime/NetworkVariable/NetworkVariableBase.cs`), tarball from packages.unity.com: https://download.packages.unity.com/com.unity.netcode.gameobjects/-/com.unity.netcode.gameobjects-2.13.3.tgz ; API page: https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.13/api/Unity.Netcode.RpcAttribute.html
8. Meta, Networking with Movement SDK for Unity (updated 2025-12-15): https://developers.meta.com/horizon/documentation/unity/move-networking/
9. Meta, Unity Movement body tracking (updated 2026-10-07): https://developers.meta.com/horizon/documentation/unity/move-body-tracking/
10. Unity, Build a session with Netcode for GameObjects (Multiplayer Play Mode, join by code; 2026): https://docs.unity.com/en-us/mps-sdk/tutorials/build-your-first-session
11. Unity XR Plug-in Management: automatic start in Play mode (manual 4.5): https://docs.unity3d.com/Packages/com.unity.xr.management@4.5/manual/EndUser.html ; behaviour when no loader starts from package source `Runtime/XRManagerSettings.cs` as resolved in this repo.
12. Unity, Get started with Unity Gaming Services (create and link a project; "Last updated 2 months ago", 2026): https://docs.unity.com/en-us/services/getting-started
13. Unity, Initialize UGS and authenticate the player (2026): https://docs.unity.com/en-us/mps-sdk/init-ugs
14. Unity, Create a session (2026): https://docs.unity.com/en-us/mps-sdk/create-session
15. Unity, Network connection management (Sessions with NGO, Relay protocols, `JoinSessionByCodeAsync`; 2026): https://docs.unity.com/en-us/mps-sdk/manage-session-network-connection
16. Unity Gaming Services pricing (Relay, Lobby, Authentication, Distributed Authority; checked 2026-10-19): https://unity.com/products/gaming-services/pricing
17. Unity support, How is the Relay service priced (updated 2026-09-24): https://support.unity.com/hc/en-us/articles/4410136449812-How-is-the-Relay-Service-Priced
18. Unity, Pricing and billing ("Last updated 5 months ago", 2026): https://docs.unity.com/services/pricing-and-billing
19. Unity support, Billing FAQ (updated 2024-10-07): https://support.unity.com/hc/en-us/articles/6821475035412-Billing-FAQ
20. Meta XR Core SDK 207.0.1 package source: `Scripts/BuildingBlocks/MultiplayerBlocks/NGO/Meta.XR.MultiplayerBlocks.NGO.asmdef`, `AutoMatchmaking/AutoMatchmakingNGO.cs`, `CustomMatchmaking/CustomMatchmakingNGO.cs`. Meta, Multiplayer Building Blocks (updated 2026-04-22): https://developers.meta.com/horizon/documentation/unity/bb-multiplayer-blocks
21. Unity, Network connection management (NGO and Netcode for Entities "out of the box"; UDP, DTLS, WSS): https://docs.unity.com/en-us/mps-sdk/manage-session-network-connection
22. Unity, Relay integrations (UTP over UDP, NGO, Authentication for "mobile and PC"): https://docs.unity.com/en-us/relay/integration ; Relay limitations (150 players per host, single host-chosen region): https://docs.unity.com/en-us/relay/limitations
23. Unity, Authentication profile management: https://docs.unity.com/en-us/authentication/profile-management
24. Unity, Multiplayer Services get started (DSA notification requirement): https://docs.unity.com/en-us/mps-sdk/get-started

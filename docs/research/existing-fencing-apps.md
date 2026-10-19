# Existing fencing apps and open-source sims: what can we reuse?

Ticket: PER-16. Researched 2026-10-18.

## Answer

- **No open-source épée trainer worth forking exists.** Every fencing VR repo on GitHub is a student project or weekend prototype (0 stars, mostly unlicensed, mostly dead). None implements a passthrough MR épée drill app.
- **Commercial Quest fencing apps exist but are thin**: UFence ($24.99, MR passthrough, lessons/drills, 4.3 stars from 3 ratings), Fencing VR: En Garde Arena ($4.99, foil/épée/sabre vs AI, 1.0 stars from 6 ratings), Brazil Fencing Club ($5.99, foil only, 3.0 stars), Fencer by Boxglass (demo, 1.8 stars from 79 ratings; full product is a €2,000 gym kit with a real sword strapped to the controller). None is closed-source reusable; they only confirm the niche is open and give feature ideas.
- **The non-fencing VR sword games teach the key design lesson**: controllers have no force feedback, so blade-on-blade contact is the hard problem. Broken Edge and Ironlights both sidestep it (blades break or shatter on contact) and reviewers still call blade hits inconsistent/weightless. For our Floating Blade phase, plan on a physics-follow blade (spring/damper joint to the controller) and expect tuning work.
- **What is actually reusable is code fragments and platform SDKs, not apps**: the épée tip-touch detector (impulse + angle threshold) and PID physics-follow blade in `lohQ/FencingVR-Redo` (no license, so ideas only), the MIT-licensed `libertylocked/FencingVR` parry/touch triggers (Unity 5, ideas only), Unity XR Interaction Toolkit + Meta OpenXR + Meta Haptics SDK for the base, and Unity ML-Agents (Apache-2.0) if a learned virtual opponent is ever wanted.
- **Recommendation**: start from Unity's own MR template (XRI + Meta OpenXR + AR Foundation), write the blade, Touch detection, and Drill logic ourselves (each is small), and borrow the physics-follow idea from Evan Fletcher's sword-mechanics write-up and FencingVR-Redo.

## Commercial apps on Quest (fencing-specific)

| App | Platform / price | Weapons | What it does | Signal from users/reviews |
|---|---|---|---|---|
| **UFence** (dev "Sword Fencing", publisher Hugo Veryzer) | Quest 3/3S/Pro, $24.99, store release listed 2026-06-05, offline, standing or sitting | Not stated on store page | MR passthrough "fence in your own space" or immersive arenas; guided lessons, free practice, challenge routines for timing, distance, footwork, blade control; "responsive physics" [1][2][3] | 4.3 stars from 3 ratings; no review text visible [1][3]. Closest to our concept, which makes it the main competitor to try before building. Homepage has no feature detail [4]. |
| **Fencing VR: En Garde Arena** (Abstron Pvt Ltd) | Quest 2/3/3S/Pro, $4.99, Early Access, released 2025-09-26 | Foil, épée, sabre | AI opponents of varying skill, "realistic fencing physics" (parries, lunges, touches), online multiplayer (foil only), roomscale or stationary [5][6] | 1.0 stars from 6 ratings, all 1-star [6]. Only Quest app that claims épée vs AI; low ratings suggest the physics/AI do not deliver. |
| **Brazil Fencing Club** (CounterParry) | Quest store $5.99; Steam (PCVR) $5.99, released 2023-12-03 | Foil only | Nine AI foil opponents; claims beat attacks, disengages, parry-ripostes [7][8] | 3.0 stars from 7 Quest ratings; too few Steam reviews for a score [7][8]. |
| **Fencer** (Boxglass, Russia) | Quest; full product is a €2,000 gym kit (headset + real sword + controller mount + license); free App Lab demo since 2021-07-16 | Real or virtual épée or sabre | Gamified reaction/attention exercises plus attack and defense drills following FIE rules; controller mounted on a real blade for real weight [9][10] | Demo: 1.8 stars from 79 ratings [10]. Road to VR notes it cannot teach parrying because there is no opposing blade [9]. Developer itch page says the project closed and relaunched elsewhere [11]. |
| **VR Fencing Trainer** (javirola + yutingzhangrita, itch.io) | Android/Quest 3 sideload, free, in development | Not stated | Precision target striking, footwork via distance control, timed reaction targets, defending against attacks, speedruns; AI-assisted code [12] | No ratings. Source appears to be `Shannju/Fencing-VR` (see below). |

Also seen: Mark Zuckerberg teased an unreleased MR fencing demo for Quest Pro in 2022, no further details [13].

Shortcomings across all of them: tiny user bases, few reviews, no public evidence any of them handles épée-specific rules (point-only, whole-body target, double touches) or blade-on-blade contact convincingly.

## Commercial VR sword games (not fencing, but same physics problem)

- **Broken Edge** (Trebuchet / Fast Travel Games, Quest 2 + SteamVR, 2022): 1v1 dueling. Explicitly "isn't a physics-based sword game"; you win by cutting the opponent's blade near the hilt. Road to VR: blades cross unintentionally, hits feel inconsistent, sword feels weightless (immersion 5/10), controller tracking degrades at the edge of the tracking volume. Score 6/10 [14].
- **Ironlights** (Quest + PCVR, 2020): avoids weapon-on-weapon clashes entirely: weapons shatter on one hit and are "reloaded" by swinging behind your back; all combat in slow motion. UploadVR: "good in theory but frustrating in practice", 3/5 [15]. Quest Store DB shows 4.4 from 2.3K ratings [16].
- **Swordsman VR** (Quest, PCVR, PSVR): physics-based melee with a "sharper skill curve in pursuit of more realistic sword fighting"; reviewed 7/10 [17][18].
- **Blade & Sorcery: Nomad** (Quest, Unity): has an official mod SDK, but Nomad mods cannot run custom scripts, so its sword physics cannot be reused or meaningfully extended [19][20].

Lesson for us: the industry answer to "virtual blade meets virtual blade with no force feedback" is either (a) abstract it away (shatter/break), or (b) a physics-follow blade that lags and deflects. For an épée trainer, (b) is the only option that teaches real parries; it is also the part reviewers complain about most. Keep it out of the MVP (MVP is hit/miss vs Targets) and treat it as its own risk spike before the Floating Blade phase.

## Open-source projects

All licenses checked from the repo's LICENSE file via the GitHub API on 2026-10-18.

| Repo | Engine | License | Status | Mechanics | Verdict |
|---|---|---|---|---|---|
| [lohQ/FencingVR-Redo](https://github.com/lohQ/FencingVR-Redo) [21] | Unity 2021.2.7, XRI 2.0, ML-Agents 2.0.1 | **None** (all rights reserved) | Dead since 2022-04 | Two physically-simulated épée agents trained by self-play RL; wrist/elbow IK hand controller; PID physics-follow épée (`PositionPID`, `TorqueRotation`); footwork from recorded animation; **épée Touch detector**: tip collider vs target area, collision impulse above a threshold and hit angle within a threshold ([`NewHitDetector.cs`](https://github.com/lohQ/FencingVR-Redo/blob/HEAD/Assets/Scripts/Env%20Codes/NewHitDetector.cs)); energy/fatigue idea from Fletcher [21] | Best idea mine. Its Touch rule (tip + impulse + angle) is exactly what our MVP needs. No license, so rewrite, do not copy. Interesting reference for a future learned opponent. |
| [libertylocked/FencingVR](https://github.com/libertylocked/FencingVR) [22] | Unity 5.3.5 | MIT | Dead since 2016-06 | Proof of concept: opponent body/sword controllers, trigger-based parry (player weapon touches opponent blade -> opponent staggers) and touch on player head, scoreboard | Legally reusable but tiny and on an ancient Unity. Ideas only. |
| [Shannju/Fencing-VR](https://github.com/Shannju/Fencing-VR) [23] | Unity 6000.0.60, XRI 3.0.10, OpenXR, Oculus XR | **None** | Active until 2026-04 | Appears to be the itch.io VR Fencing Trainer source (`JaviFolder`): trigger-based target hits (`SwordHitZone`), visual blade bend from controller acceleration (`VRSwordBend`, spring-damper on a shader parameter), salute detector, target spawner | No license. Blade-bend visual is a nice cheap trick to copy as an idea. |
| [Mathew-Caldwell/Fencing-VR](https://github.com/Mathew-Caldwell/Fencing-VR) [24] | Unity 6000.0.54, XRI 3.4.1, Meta OpenXR 2.5, AR Foundation 6.4 | None | Created 2026-10-18, one commit | "To practice distancing when fencing"; only Unity VR template scripts so far | Not useful yet, but its package set is the same modern MR stack we would pick. Worth re-checking later. |
| [ShadyFox2004/fencingVr](https://github.com/ShadyFox2004/fencingVr) [25] | three.js / WebXR | GPL-3.0 | Dead since 2022-02 | Minimal web page | Nothing to reuse. |
| [Multiplayer-Sword-Fighting/Brutal-Sword](https://github.com/Multiplayer-Sword-Fighting/Brutal-Sword) [26] | PlayCanvas (browser WebXR) | MIT | Last push 2025-12 | Browser multiplayer VR sword fighting | Wrong engine; possible reference for remote multiplayer later. |
| [mfitzer/KnightEvasion](https://github.com/mfitzer/KnightEvasion) [27] | Unity (2019) | MIT | Dead | VR sword fighting game, mostly imported assets | Ideas only. |
| [BlueFruitGames/VR_SwordFight](https://github.com/BlueFruitGames/VR_SwordFight) [28] | Unreal C++ | None | Dead since 2022-04 | VR hand component + character skeleton | Wrong engine, no license. |

Non-VR fencing code (Android fencing games, CV-based "AI referee" repos such as sholtodouglas/fencing-AI) exists but does not transfer to an MR trainer [29].

## Reusable libraries and SDKs

| Library | License | Use for us |
|---|---|---|
| Unity XR Interaction Toolkit (+ Examples repo) | Unity Companion License [30] | Grab/attach the blade to the controller, haptic impulses, menus. Default base. |
| Meta Haptics SDK for Unity + Haptics Studio | Meta SDK terms | Designed haptic clips for Touch / miss / parry on Touch Plus (Quest 3). Supports Quest 2/3/3S/Pro; recommends Unity OpenXR (Oculus XR plugin deprecated); PCM high-fidelity haptics only on PCVR [31][32]. |
| oculus-samples/Unity-Discover | MIT [33] | Reference for passthrough, scene API, spatial anchors, colocation. Useful for laying out the Strip in the real room. |
| oculus-samples/Unity-UltimateGloveBall | MIT [33] | Reference for Quest multiplayer (later remote multiplayer phase). |
| Unity ML-Agents | Apache-2.0 [34] | Only if we ever want a learned virtual opponent (FencingVR-Redo shows it is possible but heavy). Scripted Floating Blade is the simpler path. |
| Godot XR Tools | MIT, active [35] | Only relevant if the engine choice flips to Godot. |
| HPTK (Hand Physics Toolkit) | MIT, **archived** [36] | Physics hands; archived, skip. |
| HurricaneVR / Auto Hand | Paid Unity Asset Store assets, not open source [37] | Mature physics-interaction frameworks; could shortcut the physics-follow blade for the Floating Blade phase if we accept a paid dependency. |

Design reference (no code, no license): Evan Fletcher, "Sword Mechanics for VR" (2018): massless wrist follows the controller, sword attached by a ConfigurableJoint spring/damper so it deflects off obstacles; linear spring much stiffer than rotational; physics timestep and collision settings matter a lot for fast thin blades; spring stiffness as a proxy for strength [38].

## Academic work

- Malawski 2022, "Immersive feedback in fencing training using mixed reality" (AGH): AR see-through glasses + single RGB camera blade tracking, coach-recorded weapon action models, virtual trajectory overlay; novices performed bladework more correctly [39]. Idea for later: record a coach's motion as the reference for a Drill.
- Mini-review of VR in combat sports (2025, 13 studies incl. fencing): VR improves skill and reaction, lack of tactile feedback is the named gap [40].
- MIT thesis: 30 novices learning advance-jump-lunge in XR; part-task practice (components first) gave better learning; environment type made no significant difference [41]. Supports splitting Drills into small parts.
- Silesian University biomechatronic fencing simulator (2019): motion data from 6 sabre + 4 épée national-team fencers; movement patterns are stable per individual [42].

## Shortlist of viable starting points (ranked)

1. **Unity 6 MR template stack (XRI 3.x + Meta OpenXR + AR Foundation + Meta Haptics SDK), our own code on top.** All first-party, maintained, licensed for our use; it is the stack `Mathew-Caldwell/Fencing-VR` and the itch.io trainer independently landed on. Blade, Touch detection, Drill menu are each small enough that forking a prototype saves nothing.
2. **Mine `lohQ/FencingVR-Redo` for the épée Touch rule and the PID physics-follow blade** (rewrite, no license). Directly answers "what counts as a Touch" (tip collider + impulse + angle).
3. **Evan Fletcher's spring-joint sword design** as the blueprint for the Floating Blade phase (parries), with HurricaneVR/Auto Hand as a paid fallback if hand-tuning the joint takes too long.
4. **Buy and play UFence (and optionally En Garde Arena)** for an afternoon as competitive research: it is the only shipping MR passthrough fencing trainer and will show what to copy and what to avoid.
5. **Meta samples (Unity-Discover, UltimateGloveBall)** later, for Strip placement with scene anchors and for remote multiplayer.

Not recommended as a base: any fencing GitHub repo (unlicensed or ancient), Blade & Sorcery modding (no scripting on Quest), ML-Agents opponent (heavy; scripted opponent first).

## Sources

1. Meta Horizon Store, Ufence: https://www.meta.com/experiences/9537294272953838/
2. QuestStoreDB, Ufence: https://queststoredb.com/game/ufence-9537294272953838/
3. VRDB, Ufence: https://vrdb.app/game/9537294272953838
4. UFence homepage: https://ufencevr.com
5. Meta Horizon Store, Fencing VR: En Garde Arena: https://www.meta.com/experiences/24542588405433988/
6. VRDB, En Garde Arena: https://vrdb.app/game/24542588405433988
7. Meta Horizon Store, Brazil Fencing Club: https://www.meta.com/experiences/brazil-fencing-club/7806972616003740/
8. Steam, Brazil Fencing Club VR: https://store.steampowered.com/app/1956230
9. Road to VR, VR Fencing Trainer Combines a Real Sword with Virtual Instruction (2021-01-14): https://roadtovr.com/vr-fencing-training-fencer-boxglass/
10. QuestStoreDB, Fencer - Demo: https://queststoredb.com/game/fencer-demo-4007385822625371/
11. Boxglass devlog, Fencer now on App Lab: https://boxglass.itch.io/vog/devlog/275975/fencer-now-on-app-lab
12. itch.io, vr fencing (javirola): https://javirola.itch.io/vr-fencing
13. UploadVR, Zuckerberg Quest Pro mixed reality fencing: https://uploadvr.com/zuckerberg-quest-pro-mixed-reality-fencing/
14. Road to VR, Broken Edge review: https://roadtovr.com/broken-edge-review/
15. UploadVR, Ironlights review: https://uploadvr.com/ironlights-review-vr/
16. QuestStoreDB, Ironlights: https://queststoredb.com/game/ironlights-2449350021843569
17. Keengamer, Swordsman VR review: https://www.keengamer.com/articles/reviews/vr-reviews/swordsman-vr-review-two-years-into-a-solid-vr-sword-fighting-entry
18. Outsider Gaming, Swordsman VR review: https://outsidergaming.com/swordsman-vr-review/
19. UploadVR, Blade and Sorcery Nomad mod support: https://uploadvr.com/blade-and-sorcery-nomad-mod-support-live/
20. Warp Frog modding docs: https://warpfrog.notion.site
21. lohQ/FencingVR-Redo (README, NewHitDetector.cs, ProjectVersion.txt, manifest.json): https://github.com/lohQ/FencingVR-Redo
22. libertylocked/FencingVR (LICENSE: MIT): https://github.com/libertylocked/FencingVR
23. Shannju/Fencing-VR: https://github.com/Shannju/Fencing-VR
24. Mathew-Caldwell/Fencing-VR: https://github.com/Mathew-Caldwell/Fencing-VR
25. ShadyFox2004/fencingVr (LICENSE: GPL-3.0): https://github.com/ShadyFox2004/fencingVr
26. Multiplayer-Sword-Fighting/Brutal-Sword (LICENSE: MIT): https://github.com/Multiplayer-Sword-Fighting/Brutal-Sword
27. mfitzer/KnightEvasion (LICENSE: MIT): https://github.com/mfitzer/KnightEvasion
28. BlueFruitGames/VR_SwordFight: https://github.com/BlueFruitGames/VR_SwordFight
29. sholtodouglas/fencing-AI: https://github.com/sholtodouglas/fencing-AI
30. Unity-Technologies/XR-Interaction-Toolkit-Examples (LICENSE.md: Unity Companion License): https://github.com/Unity-Technologies/XR-Interaction-Toolkit-Examples
31. Meta, Unity Haptics SDK get started: https://developers.meta.com/horizon/documentation/unity/unity-haptics-sdk-get-started/
32. Meta, Unity haptics overview: https://developers.meta.com/horizon/documentation/unity/unity-haptics-overview/
33. oculus-samples (LICENSE: MIT): https://github.com/oculus-samples/Unity-Discover , https://github.com/oculus-samples/Unity-UltimateGloveBall
34. Unity-Technologies/ml-agents (LICENSE.md: Apache-2.0): https://github.com/Unity-Technologies/ml-agents
35. GodotVR/godot-xr-tools (LICENSE: MIT): https://github.com/GodotVR/godot-xr-tools
36. jorgejgnz/HPTK (LICENSE.md: MIT, archived): https://github.com/jorgejgnz/HPTK
37. Unity Asset Store, Hurricane VR: https://assetstore.unity.com/packages/tools/physics/hurricane-vr-physics-interaction-toolkit-177300
38. Evan Fletcher, Sword Mechanics for VR (2018): https://evanfletcher42.com/2018/12/29/sword-mechanics-for-vr/
39. Malawski, Immersive Feedback in Fencing Training using Mixed Reality, Computer Science 23(1), 2022: https://journals.agh.edu.pl/csci/article/view/4570
40. Advancements in virtual reality for performance enhancement in combat sports: a mini-review: https://www.ncbi.nlm.nih.gov/pmc/articles/PMC11920182/
41. MIT DSpace thesis (XR motor learning, advance-jump-lunge): https://dspace.mit.edu/handle/1721.1/152500
42. Biomechatronic simulator for fencing training using virtual reality (Silesian University of Technology): https://research.polsl.pl/en/publications/biomechatronic-simulator-for-fencing-training-using-virtual-reali/

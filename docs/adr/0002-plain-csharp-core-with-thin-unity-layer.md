# Plain C# core with a thin Unity layer, blade poses as data

The app's rules and math (Touch detection, Drill state machines, Strip geometry, scoring, rep log format) live in plain C# assemblies with no Unity scene dependency, and Unity MonoBehaviours only read devices, render and forward data. This is so agents can test nearly everything headless in Edit mode, since only feel needs the headset. The Touch detector consumes blade poses as plain data rather than reading the controller directly, so a networked partner's blade can later feed the same detector (the multiplayer constraint) without rewriting it.

## Modules

- **Strip:** two-point placement, per-room spatial anchor, scaled markings, warning zones, off-strip signal.
- **Blade:** blade pose from the controller, tip sweep, Touch detector, haptics.
- **Dummy:** static silhouette with Target Zones, scaled to the Fencer's height.
- **Drills:** each Drill is data (required strip length, Reps, windows) plus a small state machine.
- **Session:** rep log (JSONL), per-Drill results, session history.
- **UI:** off-hand pointer, Drill picker, results panel, settings.
- **Tracking:** IOBT arm and elbow poses written to the rep log only.
- **Settings:** hand (right by default, left mirrors everything), Rep counts, time windows, Fencer height.

## Consequences

- Each module has a `Core` assembly (plain C#, Edit-mode tested) and a `Unity` assembly (thin MonoBehaviours).
- Build tickets are executed by agents, each on its own branch, and merged to main when tests and the APK build pass (Nikita's call). Feel is checked by Nikita on the headset afterwards.

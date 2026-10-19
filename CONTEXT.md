# VR Fencing Trainer

A passthrough mixed-reality app on Meta Quest 3 for practicing épée alone at home, built so a beginner fencer can keep up with the WPI Fencing Club's program.

## Language

### Fencing

**Fencer**:
The person wearing the headset and practicing.
_Avoid_: Player, user

**Touch**:
A valid hit: the point of the blade landing on a target with enough pressure (750 g on a real épée's point; in the app, pushed in a few millimeters or arriving fast). In épée only the point scores, anywhere on the body.
_Avoid_: Hit, score, strike

**Grip**:
How the épée is held, chosen in Settings: Center, Pistol, French or French posted. It sets where the virtual blade sits on the controller (how far ahead the guard is, and the blade's pitch and yaw), computed from the hand's geometry and fine-tuned per grip by a thrust calibration.
_Avoid_: Mount, blade alignment

**Set**:
The deliberate slight curve of an épée blade toward the palm side (under 1 cm). With gravity sag it makes the point hang a few centimeters below the forte's line, and a straight push bends the blade further that way.
_Avoid_: Bend (reserved for the blade bowing under pressure)

**Strip**:
The rectangular practice lane the Fencer advances and retreats along, laid out in the real room around the two En Garde Lines. Behind each line it runs on for an en garde stance and a Warning Zone, which may reach past a wall. One Strip is saved per room; a room without one offers a new Strip.
_Avoid_: Piste (reserve for the regulation 14 m competition lane), track, lane

**En Garde Line**:
A starting line on the Strip. The Fencer's is set by the first point when placing the Strip, the Dummy's (the opponent's side) by the second. Unqualified, it means the Fencer's.
_Avoid_: Start line, spawn point

**Warning Zone**:
The marked end section of the Strip (2 m on a piste, scaled down on short Strips) that tells the Fencer they are close to the end.
_Avoid_: Danger zone, edge zone

### Practice

**Drill**:
One named exercise the Fencer picks from the menu (e.g. lunge to a target), run as a fixed number of Reps.
_Avoid_: Exercise, level, lesson

**Rep**:
A single attempt within a Drill, judged as success or failure with timing.
_Avoid_: Trial, round

**Session**:
One practice sitting, from app launch to close. It can hold several Drill runs; the results history and its last-10-sessions trend count Sessions.
_Avoid_: Workout, practice (as a noun for this)

**Fencer Recording**:
A capture of the Fencer's tracked body, head and sword-hand controller, frame by frame in the Strip frame, made on the headset and replayed in the PC Editor as a moving opponent.
_Avoid_: Replay file, motion capture, take

**Glance**:
The point reaching a Target Zone without counting as a Touch: arriving sideways, with the blade not pointing in, too slowly, or too lightly (a brush that never presses the point in). Felt as a light tick, never scored.
_Avoid_: Miss (a Rep with no contact), near-touch

**Flat Contact**:
The side of the blade, not the point, crossing a Target Zone. Never a Touch.
_Avoid_: Slap, hit

**Footwork Call**:
A cue that names a footwork action for the Fencer to do at once: advance, retreat, lunge or check (a short move forward that comes straight back). Judged from the head's movement alone, so only roughly.
_Avoid_: Command, footwork cue

**Start Mark**:
The mark on the floor at the center of the En Garde Line where the Fencer's front foot goes between Reps. In Drills where the Fencer moves, the next Rep waits until the Fencer is back on it and still.
_Avoid_: Home position, start position, spawn point

**Target**:
A virtual object the Fencer aims Touches at.
_Avoid_: goal

**Dummy**:
A static, translucent body silhouette standing opposite the Fencer, carrying the Target Zones. In Drills it stands at the Drill's Dummy Distance; bouts start it on the Dummy's En Garde Line. It never moves or attacks; the Armed Dummy does.
_Avoid_: Opponent (reserved for a full virtual fencer), mannequin

**Target Zone**:
A named part of the Dummy that a Drill asks the Fencer to Touch: hand, wrist, forearm, upper arm, thigh, foot or body.
_Avoid_: Hitbox, area

**Dummy Distance**:
How far the Dummy stands from the Fencer along the Strip, front foot to front foot, named by the action the Fencer needs to Touch its body: extension, lunge or step-lunge distance. Scales with the Fencer's reach as measured by Calibrate grip, or with height until it is measured.
_Avoid_: Range, measure

**Armed Dummy**:
The Dummy holding a blade in its moving sword arm: it makes scripted attacks for the Fencer to Parry and holds its blade in line for the Fencer to Beat. Its feet stay planted except in lunge Drills.
_Avoid_: Floating blade, bot, enemy sword, opponent (reserved for a full virtual fencer)

**Parry**:
A Fencer's blade contact that pushes the Armed Dummy's attacking point off the Fencer's body. Named by the line it closes: 6, 4, 8 or 7.
_Avoid_: Block

**Riposte**:
The Fencer's Touch on the Armed Dummy right after a successful Parry.
_Avoid_: Counter, counter-attack

**Beat**:
A sharp hit on the Armed Dummy's blade that knocks it out of line before the Fencer attacks.
_Avoid_: Tap, knock

**Stop Hit**:
A Touch on the Armed Dummy's hand or arm while its attack is still on the way, landing first instead of parrying. If both Touches land within the double window, it counts as a double, not a Stop Hit.
_Avoid_: Counter-attack (the broader family), counter

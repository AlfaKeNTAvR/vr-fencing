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
The rectangular practice lane the Fencer advances and retreats along, laid out in the real room with a configurable length.
_Avoid_: Piste (reserve for the regulation 14 m competition lane), track, lane

**En Garde Line**:
The Fencer's starting line on the Strip, set by the first point when placing it.
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

**Glance**:
The point reaching a Target Zone without counting as a Touch: arriving sideways, with the blade not pointing in, too slowly, or too lightly (a brush that never presses the point in). Felt as a light tick, never scored.
_Avoid_: Miss (a Rep with no contact), near-touch

**Flat Contact**:
The side of the blade, not the point, crossing a Target Zone. Never a Touch.
_Avoid_: Slap, hit

**Footwork Call**:
A cue that names a footwork action for the Fencer to do at once: advance, retreat, lunge or check (a short move forward that comes straight back). Judged from the head's movement alone, so only roughly.
_Avoid_: Command, footwork cue

**Target**:
A virtual object the Fencer aims Touches at.
_Avoid_: goal

**Dummy**:
A static, translucent body silhouette standing at the opponent's en garde line, carrying the Target Zones. It never moves or attacks; the Armed Dummy does.
_Avoid_: Opponent (reserved for a full virtual fencer), mannequin

**Target Zone**:
A named part of the Dummy that a Drill asks the Fencer to Touch: hand, wrist, forearm, upper arm, thigh, foot or body.
_Avoid_: Hitbox, area

**Dummy Distance**:
How far the Dummy stands from the Fencer along the Strip, front foot to front foot, named by the action the Fencer needs to Touch its body: extension, lunge or step-lunge distance. Scales with the Fencer's height.
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

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
A static, translucent body silhouette standing opposite the Fencer, carrying the Target Zones. In Drills it stands at the Drill's Dummy Distance; bouts start it on the Dummy's En Garde Line. It never attacks (the Armed Dummy does). It stands in en garde, breathing and shifting its weight on planted feet, unless a Drill makes it keep its Dummy Distance, stepping along the Strip (advancing and retreating on planted feet) as the Fencer advances and retreats, a reaction delay late. Its head and sword arm stay level whatever its legs do.
_Avoid_: Opponent (reserved for a full virtual fencer), mannequin

**Target Zone**:
A named part of the Dummy that a Drill asks the Fencer to Touch: hand, wrist, forearm, upper arm, thigh, foot or body.
_Avoid_: Hitbox, area

**Dummy Distance**:
How far the Dummy stands from the Fencer along the Strip, front foot to front foot, named by the action the Fencer needs to Touch its body: extension, lunge or step-lunge distance. Scales with the Fencer's reach as measured by Calibrate grip, or with height until it is measured.
_Avoid_: Range, measure

**Armed Dummy**:
The Dummy holding a blade in its moving sword arm: it makes scripted attacks for the Fencer to Parry and holds its blade in line for the Fencer to Beat. Its feet stay planted except in lunge Drills, where it lunges on its legs: the front foot kicks out and lands, the back foot stays (dragging a little at full length) and the hips drop. For a thrust at the hand or forearm it stands 0.6 to 0.75 m further back, as real fencers attack the arm from further away.
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

### Partner

**Partner**:
The second Fencer in a two-headset session: a fixed person in another room (or the PC Editor standing in for them), joined over the internet. In the app, also the side that joins the Host's session.
_Avoid_: Player 2, guest, friend

**Host**:
The Fencer whose app creates the session (Nikita's headset): it shows the Join Code, picks the mode, and runs the session for both. The Host leaving ends it for both.
_Avoid_: Server, owner, room creator

**Join Code**:
The 6-character code (from 20 letters and digits that cannot be confused, such as FGR78N) the Host's Partner page shows; the Partner types it on a large keypad to join. Read out over voice.
_Avoid_: Room code, invite, password

**Partner page**:
The menu page next to Drills that connects two Fencers: Host, Join (the keypad), the Bout / Paired Practice switch (Host only) and Leave. While connected, solo Drills are off and the Dummy is hidden.
_Avoid_: Lobby, multiplayer menu

**Opponent**:
The Partner as shown in this Fencer's room: the Dummy's rigged body driven by the Partner's tracked body (its legs built on the Partner's own footwork, not guessed), holding a blade drawn from the Partner's hand and grip settings, standing on the far side of the shared Strip.
_Avoid_: Avatar, remote player, enemy

**Bout**:
Two Fencers fencing each other freely on a shared Strip, with a virtual judge lighting Touches and doubles (no score or timer in v1).
_Avoid_: Match, game

**Light**:
The virtual judge's call that a Touch counted in a Bout, shown on the Scoring Box. The first Touch of a Phrase lights; in a Bout nothing counts again until the next "Allez!".
_Avoid_: Point, score, hit

**Double**:
Both Fencers' Touches within the double window of each other (50 ms, up to 150 ms if the Host sets it): both lamps light.
_Avoid_: Trade, simultaneous hit

**Scoring Box**:
The two lamps floating above the shared Strip's centre in a Bout. In each headset the Fencer's own lamp is green and on their left, the Opponent's red; the round-trip time shows under it.
_Avoid_: Scoreboard, lights panel

**Phrase**:
One exchange of a Bout, from the referee's "En garde! Prets? Allez!" (called once both Fencers have stood behind their En Garde Lines for a second) until the Halt that ends it. Touches count only inside it.
_Avoid_: Round, rally, point

**Halt**:
The referee's "Halte!" that ends a Phrase, with its reason shown: a Light, a Fencer leaving their own Strip, or Corps-a-corps. Nothing counts until the next Phrase. Paired Practice has no Halts.
_Avoid_: Stop, pause, break

**Corps-a-corps**:
The two Fencers' bodies coming within about half a metre of each other. It halts a Bout; in Paired Practice the Opponent only fades.
_Avoid_: Collision, clash, body contact

**Paired Practice**:
Two connected Fencers practicing actions together with Touch detection, lights and haptics, but no roles, calls, Halts, lock-out or competition; each has a resettable tally (Touches, doubles, glances) saved with the Session log.
_Avoid_: Free play, training mode

**Rejoin**:
The Partner joining the Host's session again with the same Join Code after a drop or a sleep, within 5 minutes; the session carries on, Paired Practice tally included. After 5 minutes, or when the Host leaves or sleeps, the session is over and the Host hosts again for a new code.
_Avoid_: Reconnect (Netcode's term for the transport), resume

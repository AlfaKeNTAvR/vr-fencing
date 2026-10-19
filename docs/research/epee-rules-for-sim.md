# Épée rules and physical facts the sim must model

Research for Linear PER-19. Researched 2026-10-18. Primary sources: FIE Technical Rules and FIE Material Rules, both August 2026 English editions [1][2]. Biomechanics from the Chen et al. 2017 scoping review [5] and Bottoms et al. 2013 [6]. Items marked **[derived]** are my own calculations or engineering inference from the cited numbers, not rule text.

Tags: **[TOUCH]** affects touch detection, **[BLADE]** affects blade modelling, **[HAPTIC]** affects haptics.

## Answer

- A valid épée touch is point only (t.90.1), anywhere on the body including head, feet, clothing and equipment (t.91). Model the tip as a press sensor, not a collider: it fires only when the point is pushed with more than 750 g (7.36 N) through more than 1 mm of travel (m.19.2, m.19.4).
- Hits on the opponent's guard, on the floor, or point-against-point do not count (t.93, t.94.1). The guard and blade are "earth", so tip contact with them must be ignored.
- There is no right-of-way at épée: first light wins. If the second touch lands within 40 ms (guaranteed double) to 50 ms (guaranteed single), both score (Annexe B, épée timing; t.92). The scoring box ignores contacts shorter than 2 ms and must accept 2-10 ms contacts.
- Weapon envelope: blade ≤ 90 cm, whole weapon ≤ 110 cm, mass < 770 g, guard ≤ 13.5 cm diameter and 3-5.5 cm deep, blade may be off guard-centre by ≤ 3.5 cm, handle ≤ 20 cm (m.4, m.14-m.17). Flex: 4.5-7 cm tip deflection under 200 g, which is an effective tip stiffness of about 28-44 N/m **[derived]**.
- Pistol (orthopaedic) grips fix the hand in one position with the extended thumb tip ≤ 2 cm from the guard (m.4.6). A French grip lets the hand slide back along a handle up to 20 cm long, so blade offset from the controller differs by up to about 15 cm between grips.
- Speeds: weapon tip peak velocity in a lunge is about 2-3.3 m/s in most studies (one épée study reports 12.8 m/s, an outlier). Lunge length is 1.0-1.4 m. Piste: 14 m × 1.5 m, on-guard lines 4 m apart.

## Facts

### Valid touch

- **[TOUCH] Point only.** "The épée is a thrusting weapon only. Attacks with this weapon are therefore only with the point." FIE t.90.1 [1].
- **[TOUCH] Whole-body target.** "At épée the target includes the whole of the fencer's body including his clothing and equipment. Thus any hit which arrives is counted, whatever part of the body (trunk, limbs or head), the clothing or the equipment it touches." FIE t.91 [1]. Sim consequence: for the MVP, the whole target volume is valid. Later, the opponent's own guard and blade must be excluded (next item).
- **[TOUCH] Hits that are annulled.** Points meeting point-to-point, and hits on uninsulated ground (t.93). A hit on the guard of the fencer being hit, or on the conductive piste, that registers is annulled (t.94.1). Guard, blade and piste are earthed so they never light (Annexe B épée (d); m.18.1) [1][2]. The point's guiding flange must be recessed so that sliding the depressed tip across the guard cannot register a hit (m.19.1.c) [2].
- **[TOUCH] Tip force threshold.** Pressure needed to close the circuit "must be more than 750 g, that is to say that this weight must be lifted by the spring of the point". The test weight is 750 g ± 3 g (747-753 g). FIE m.19.2, m.19.3 [2]. 750 gf = 7.36 N.
- **[TOUCH] Tip travel.** The "lighting stroke" (travel before the hit registers) must be greater than 1 mm. Further travel after that must be less than 0.5 mm. Total travel must be greater than 1.5 mm. Gauge tolerance is ±0.05 mm. FIE m.19.4.a-b [2].
- **[TOUCH] Shim tests at the piste.** The referee inserts a 1.5 mm gauge between barrel and tip to check total travel, and a 0.5 mm gauge to check residual travel: with the 0.5 mm gauge in, pressing the point must not register. FIE t.68.3 [1].
- **[TOUCH] Point geometry.** Crown diameter 8 mm ± 0.05 mm, base ≥ 7.7 mm, flat front face, edge radius or chamfer 0.5 mm. FIE m.19.1.a-b [2]. A sim tip sphere/disc of about 4 mm radius matches the real contact patch.
- **[TOUCH] Touch-model consequence [derived].** A real touch needs the tip pressed roughly along its own axis hard enough to compress a spring by about 1 mm. A glancing, sideways or very light contact does not light. Model this as "tip penetration depth along the blade axis ≥ a threshold, with the approach angle within some cone of the surface normal". There is no real force sensing in VR, so the depth and angle thresholds stand in for 750 g / 1 mm. At 3 m/s the 1 mm stroke takes about 0.3 ms, far below one frame, so detection must be a swept test per frame (see quest3-tech-stack.md).
- **[TOUCH] No dragging.** Pushing or dragging the point on the piste is forbidden during the bout (t.90.2) [1].

### Timing: double touch and lockout

- **[TOUCH] [HAPTIC] Double-touch window and lockout.** The apparatus "must register only the first hit which is made. If the interval of time between two hits is less than 40 ms (1/25th of a second), the apparatus must register a double hit ... When the interval is greater than 50 ms (1/20 of a second) the apparatus must register only one hit". Between 40 and 50 ms is apparatus tolerance. FIE Material Rules, Annexe B, B. Épée (b) Timing [2]. Sim: after the first touch, keep the opponent's touch open for a configurable 40-50 ms, then lock out.
- **[TOUCH] Contact duration.** At normal resistance the apparatus "must register hits ... made with a duration of contact of 2-10 ms", and "must not register signals of less than 2 ms duration". FIE Annexe B, B. Épée (c) Sensitivity [2]. Sim: at 72-120 Hz (8-14 ms per frame) any single frame of valid tip penetration already exceeds 2 ms, so a one-frame hit is realistic **[derived]**.
- **Double hit = both score.** "When both competitors are hit, and the apparatus registers both these hits as valid, there is a double hit, that is to say a hit is scored against each competitor." FIE t.92 [1]. Exception: in pools at 4-4, a double hit is not counted (t.38.1.b) [1]. Out of bounds: if one fencer leaves the piste with both feet, only the fencer still on the piste can score, "at épée only ... even in the case of a double hit" (t.33.4) [1].

### No right-of-way

- **No priority at épée.** The épée chapter (t.90-t.95) has only point-only, target, double-hit and annulment rules, with no priority article. t.33.4 contrasts épée with "For foil and sabre the convention must be applied" [1]. USA Fencing's rulebook follows FIE article numbers (it "incorporates the most recent FIE rules changes") [3]. Sim consequence: the scorer is purely temporal (first touch, plus the double window). No referee logic is needed.

### Blade

- **[BLADE] Length and mass.** Blade ≤ 90 cm, including the button, measured from the front of the guard (m.16.3, m.3.1). Tip to back of guard ≤ 95.5 cm (m.17.2). Whole weapon ≤ 110 cm (m.15). Ready-to-use weight < 770 g (m.14) [2]. No official blade-only mass exists in the rules. Model the mass distribution yourself, heavy at the guard.
- **[BLADE] Section.** Triangular section, no cutting edges. Each of the three sides ≤ 24 mm wide. Mounted with the groove upward. Any curve must be uniform, in the vertical plane, near the centre, and < 1 cm. FIE m.16.1-2, m.16.4 [2].
- **[BLADE] Flex.** "The blade should have a flexibility equivalent to a bend of 4.5 cm minimum and 7 cm maximum". Test: blade clamped horizontally 70 cm from the button tip, 200 g (± 1 g) hung 3 cm from the tip, bend measured at the tip. FIE m.16.5 [2].
- **[BLADE] Flex numbers [derived].** 200 g = 1.96 N, so effective lateral tip stiffness ≈ 1.96 N / 0.045-0.07 m ≈ 28-44 N/m over the outer 70 cm. Treating it as a uniform cantilever (L ≈ 0.67 m), effective EI ≈ F·L³/(3δ) ≈ 2.8-4.4 N·m². The real blade tapers, so the base is stiffer and the tip softer. A 750 g axial hit load (7.36 N) on a blade this flexible produces the visible bow seen on real hits.
- **[BLADE] [HAPTIC] How flex affects point control [derived].** The tip lags and overshoots wrist motion (a whip). Small, fast wrist rotations produce larger tip arcs than a rigid stick would. When the tip lands at an angle, the blade bends instead of skidding, which helps a touch stay on. For the MVP a rigid blade is acceptable for touch detection. Visual flex (a spring-damper bend driven by tip acceleration and by contact) and a short haptic "give" on contact add realism. Do not let visual flex move the logical tip used for detection unless it is modelled consistently.

### Guard and grips

- **[BLADE] Guard.** Circular edge. Must pass through a cylinder 13.5 cm in diameter and 15 cm long. Depth (front to back of the bell) 3-5.5 cm. FIE m.17.1 [2]. The blade may be mounted off-centre: centre of guard to blade entry ≤ 3.5 cm (m.17.3) [2]. The convex face must be smooth and unable to catch the opponent's point (m.5.1). Inside padding < 2 cm thick (m.5.2.a) [2].
- **[BLADE] Handle length.** Foil and épée handles ≤ 20 cm from the front of the guard to the end of the pommel (B-E), and ≤ 18 cm to the grip/pommel joint (B-D). FIE m.4.1 [2].
- **[BLADE] Orthopaedic (pistol) grips.** If a handle "has a special shape (orthopaedic) which fixes the position of the hand", it must fix exactly one hand position, and "the extremity of the thumb when completely extended must not be more than 2 cm from the inner surface of the guard". FIE m.4.6 [2]. Hybrids that would allow a pistol shape held at the pommel are therefore illegal [2][9].
- **[BLADE] French vs pistol in practice.** French grip: a straight handle with a pommel. It can be held near the pommel ("posting") for extra reach, and gives less leverage close in. Pistol grip: the hand is locked against the guard, with more strength and leverage for blade work and parries, and less reach [9][10] (secondary sources).
- **[BLADE] Sim consequence [derived].** The Quest Touch controller is the "grip". The blade base (front of guard) sits about 2-5 cm ahead of the index knuckle for a pistol grip. For a French grip it sits about 5-15 cm ahead, varying with how far back the hand posts. Blade direction is roughly along the forearm, with the hand angle set by the grip. Expose the guard-to-hand offset (along-axis distance and angle) as a per-user calibration, and default to a French grip, which is what most beginners start on [10].

### Parry mechanics

- **Rule definition.** "The parry is the defensive action made with the weapon to prevent an offensive action arriving." FIE Technical Rules, definitions of offensive and defensive actions (t.7-t.9 section) [1]. The FIE rules do not define forte or foible.
- **[BLADE] [HAPTIC] Forte vs foible (secondary).** The blade is commonly divided into thirds: foible (outer third, near the tip), middle, and forte (third nearest the guard). A parry meets the attacker's foible with the defender's forte, which gives the defender control of the opponent's blade [7][8]. At épée, any deflection that stops the attack landing counts as a parry, since there is no right-of-way to award [7].
- **[BLADE] Why forte beats foible [derived].** The force on a blade at distance r from the hand creates a torque F·r about the wrist. Contact at the forte (r ≈ 10-30 cm) needs little wrist torque to hold. Contact at the foible (r ≈ 60-90 cm) needs 3-9× more torque to resist the same force. The outer blade is also the most flexible part, so it deflects more. Model blade-on-blade contact by contact point along each blade. The deflection each blade receives should scale with the opponent's lever arm and shrink with your own. Haptic intensity on a parry should depend on where on your blade the contact happened (forte: firm short click; foible: weaker, longer buzz with more displacement).

### Distances and speeds (biomechanics)

- **Weapon velocity.** Peak sword velocity 3.21 ± 0.22 m/s expert vs 2.63 ± 0.29 m/s novice (Mulloy et al. 2015). Foil 2.55 ± 0.42 vs 1.88 ± 0.48 m/s, elite vs medium (Gutiérrez-Dávila et al. 2013). Pooled trend across studies: foil 2.91 m/s vs épée 2.49 m/s peak weapon velocity. All as summarised in Chen et al. 2017 [5].
- **Outlier.** Bottoms et al. 2013 report a mean sword velocity at touch of 12.8 ± 3.3 m/s for 14 club épée fencers (motion capture at 250 Hz). The paper does not state where on the sword this was measured [6], and Chen et al. flag it as inconsistent with other studies [5]. **[TOUCH] [derived]** Design swept detection for up to about 10-15 m/s at the tip anyway, because wrist flicks with a 90 cm lever can exceed whole-arm thrust speed. At 3 m/s and 72-120 Hz, the tip moves 2.5-4 cm per frame. At 12.8 m/s it moves 11-18 cm per frame.
- **Lunge length.** 1.17 ± 0.17 m elite vs 1.02 ± 0.10 m novice (Gholipour et al. 2008). 1.40 ± 0.15 m vs 1.13 ± 0.13 m, elite vs medium (Gutiérrez-Dávila et al. 2013). 1.12 vs 0.83 leg lengths, expert vs novice (Mulloy et al. 2015). All via [5].
- **Body speed.** Lunge velocity 3.35 ± 0.70 m/s in national-team fencers (Turner et al. 2016). Peak centre-of-mass velocity, pooled: foil 1.92 m/s vs épée 1.72 m/s [5].
- **Timing.** Reaction time 333 ± 128 ms elite vs 613 ± 62 ms novice (Williams and Walmsley 2000, foil). Mean high-intensity movement duration about 0.7 s (Wylde 2013) [5]. Useful for an opponent blade's reaction delay and for target-presentation timing **[derived]**.

### Piste

- **Dimensions.** The combat area is 1.50 m wide (t.18.1) and 14 m long, with ±4 cm total tolerance (t.18.2) [1].
- **Lines.** A broken centre line. On-guard lines 2 m either side of the centre (so fencers start 4 m apart, line to line). Rear limits 7 m from the centre. The last 2 m before each rear limit is marked as a warning zone, ideally in a different colour. FIE t.19 [1]. Each fencer has 5 m to retreat from their on-guard line (t.18.2) [1].
- **Runback.** 1.5-2 m extensions at each end, so the conductive strip is 17-18 m long (m.57.4) [2].
- **Sim consequence [derived].** A full piste will not fit most rooms. For a solo MR drill, draw the on-guard lines and a short 1.5 m wide lane segment in passthrough. Use the 4 m on-guard spacing and a 1.0-1.4 m lunge to place targets at lunge distance (about 1.5-2.5 m from the front foot, including arm and blade reach).

### Flags summary

- Touch detection: t.90, t.91, t.93, t.94.1, m.19.2-4, t.68.3, Annexe B épée timing and sensitivity, tip velocity figures.
- Blade modelling: m.3, m.4, m.14-m.17, grip types, forte/foible lever arms, flex.
- Haptics: lockout moment (first touch, 40-50 ms window), tip press (spring stroke "give"), parry contact location (forte vs foible), blade flex on contact.

## Sources

1. FIE Technical Rules, August 2026 (English): https://static.fie.org/uploads/40/204126-Technical%20rules%20August%202026%20ang.pdf (articles t.7-t.9, t.18, t.19, t.33, t.38, t.68, t.90-t.95). Listed at https://fie.org/fie/documents/rules
2. FIE Material Rules, August 2026 (English): https://static.fie.org/uploads/40/204157-book%20material%20August%202026%20ang.pdf (articles m.3, m.4, m.5, m.14-m.19, m.57, Annexe B section B Épée). The FIE rules page links an equivalent upload at https://static.fie.org/uploads/40/204156-book%20material%20August%202026%20ang.pdf
3. USA Fencing, Rules & Compliance (rulebook, last updated October 2026; follows FIE article numbering): https://www.usafencing.org/rules-compliance. The 2026 text was not downloaded; older editions with the same t.90-t.92 épée structure are at https://www.fencingarchive.com/wp-content/uploads/2024/04/../../rulebooks/USFA%20Rules%202020-08.pdf
4. Local note: docs/research/quest3-tech-stack.md (frame rates and swept tip detection).
5. Chen T.L.-W. et al., "Biomechanics of fencing sport: A scoping review", PLoS ONE 12(2): e0171578, 2017: https://pmc.ncbi.nlm.nih.gov/articles/PMC5302478/
6. Bottoms L., Greenhalgh A., Sinclair J., "Kinematic determinants of weapon velocity during the fencing lunge in experienced épée fencers", Acta of Bioengineering and Biomechanics 15(4):109-113, 2013: https://bibliotekanauki.pl/articles/307476.pdf (repository: https://uhra.herts.ac.uk/handle/2299/16213)
7. Parry (fencing), Wikipedia (secondary): https://en.wikipedia.org/wiki/Parry_(fencing)
8. Forte (fencing), Wikipedia (secondary): https://en.wikipedia.org/wiki/Forte_(fencing)
9. Grip (sport fencing), Wikipedia (secondary): https://en.wikipedia.org/wiki/Grip_(sport_fencing)
10. Academy of Fencing Masters, "Épée fencing grips: pistol grip vs French grip" (secondary, coaching): https://academyoffencingmasters.com/blog/epee-fencing-grips-pistol-grip-vs-french-grip/

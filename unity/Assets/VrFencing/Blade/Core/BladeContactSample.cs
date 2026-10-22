using System;
using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Blade
{
    /// <summary>Where a blade is in sliding off the other (<see cref="BladeContactSolver"/>), for the blade-contact log.</summary>
    public enum SlideOffPhase
    {
        /// <summary>Not sliding off.</summary>
        None,

        /// <summary>Pressed past every give, turning aside in the grip toward its point.</summary>
        Sliding,

        /// <summary>Slid off, swinging back to the hand.</summary>
        Returning,
    }

    /// <summary>Discrete things that happened in a frame of the blade-contact log (PER-136); one bit each.</summary>
    [Flags]
    public enum BladeLogEvents
    {
        None = 0,

        /// <summary>The blades met (<see cref="BladeClashDetector"/>).</summary>
        Clash = 1 << 0,

        /// <summary>A Beat Drill's Beat knocked the Dummy's blade out of line (<see cref="BeatJudge"/>).</summary>
        Beat = 1 << 1,

        /// <summary>A Parry was judged (<see cref="ParryJudge"/>).</summary>
        Parry = 1 << 2,

        /// <summary>The Fencer's point scored on the Dummy.</summary>
        Touch = 1 << 3,

        /// <summary>The Fencer's point reached a zone without scoring.</summary>
        Glance = 1 << 4,

        /// <summary>The Fencer's blade body crossed a zone.</summary>
        FlatContact = 1 << 5,

        /// <summary>A blade started sliding off the other.</summary>
        SlideOffStart = 1 << 6,

        /// <summary>A slide-off (and its swing back) ended.</summary>
        SlideOffEnd = 1 << 7,

        /// <summary>The Dummy's point landed on the Fencer.</summary>
        HitOnFencer = 1 << 8,

        /// <summary>The Fencer pressed the "felt wrong" mark button.</summary>
        Mark = 1 << 9,
    }

    /// <summary>One blade in a frame of the blade-contact log. Strip frame, meters, degrees.</summary>
    public struct BladeTrace
    {
        /// <summary>The guard, where the hand holds the blade.</summary>
        public Vector3 Guard;

        /// <summary>The point as the hand holds the blade: the chord's tip, before the contact gives.</summary>
        public Vector3 RealPoint;

        /// <summary>The point as drawn.</summary>
        public Vector3 DrawnPoint;

        /// <summary>How far the contact turned the blade in the grip.</summary>
        public float GripTurnDegrees;

        /// <summary>How far the contact bowed the blade.</summary>
        public float BowMeters;

        public SlideOffPhase SlideOff;

        /// <summary>
        /// How far the other drawn blade keeps out of this blade's bell guard (negative: through it); NaN when there is
        /// no other blade.
        /// </summary>
        public float BellGapMeters;

        /// <summary>
        /// How far the other drawn blade keeps out of this blade's grip behind the bell (negative: through it); NaN when
        /// there is no other blade. Solid in the contact solver since the PER-122 fix, as the bell is.
        /// </summary>
        public float HandleGapMeters;
    }

    /// <summary>
    /// Everything the blade-contact log keeps for one rendered frame (PER-136): both blades, their contact, the Dummy's
    /// hand, the sword hand's speed and the frame's events. A plain record filled in place each frame (no allocation).
    /// Strip frame (+x the Fencer's right, +y up, +z toward the Dummy), meters, degrees, newtons, seconds.
    /// </summary>
    public struct BladeContactSample
    {
        /// <summary>Seconds since the recording started.</summary>
        public double TimeSeconds;

        /// <summary>This frame's step.</summary>
        public float DeltaSeconds;

        public BladeTrace Fencer;

        public BladeTrace Dummy;

        /// <summary>True while the Dummy holds a blade; the contact and Dummy fields are meaningful only then.</summary>
        public bool IsDummyArmed;

        /// <summary>The hands' blades press into each other (<see cref="BladeContactSolver.IsInContact"/>).</summary>
        public bool IsInContact;

        /// <summary>The blades touch, nearly touch or one is behind the other (<see cref="BladeContactSolver.IsEngaged"/>).</summary>
        public bool IsEngaged;

        /// <summary>Where the blades meet; meaningful while <see cref="IsInContact"/>.</summary>
        public Vector3 ContactPoint;

        /// <summary>Where on the Fencer's blade they meet, 0 its guard to 1 its point.</summary>
        public float ContactOnFencer;

        /// <summary>Where on the Dummy's blade they meet, 0 its guard to 1 its point.</summary>
        public float ContactOnDummy;

        /// <summary>How far the hands' blades press into each other (<see cref="BladeContactSolver.PressMeters"/>).</summary>
        public float OverlapMeters;

        /// <summary>The press force the give stands for (<see cref="BladeContactSolver.PressNewtons"/>).</summary>
        public float PressNewtons;

        /// <summary>Which way from the Dummy's blade the Fencer's is (unit); zero before both were seen.</summary>
        public Vector3 Side;

        /// <summary>Which give stages are at their limit (<see cref="BladeContactSolver.SaturatedStages"/>).</summary>
        public int SaturatedStages;

        /// <summary>The hands' blades' closest approach, centerline to centerline (<see cref="BladeContactSolver.ClosestMeters"/>).</summary>
        public float ClosestMeters;

        /// <summary>
        /// How far apart the two drawn blades are at their closest, surface to surface (negative: drawn through each
        /// other); NaN unless armed.
        /// </summary>
        public float DrawnGapMeters;

        /// <summary>What the Dummy is armed for: unarmed, drill, free_guard or free_attacks.</summary>
        public string DummyMode;

        /// <summary>What the Dummy's arm is doing: en_garde, blade_in_line, or its attack's phase.</summary>
        public string DummyAction;

        public OpponentHand OpponentHand;

        /// <summary>Where the Dummy's hand gives way to the press, at its point (<see cref="ArmedDummyBlade.YieldTipOffset"/>).</summary>
        public Vector3 DummyHandGive;

        public DeflectionPhase DummyDeflectionPhase;

        /// <summary>The Dummy's point off its line from its hand (<see cref="BladeDeflection.TipOffset"/>).</summary>
        public Vector3 DummyTipOffset;

        /// <summary>The sword-hand controller's velocity, m/s; NaN when it is not tracked.</summary>
        public Vector3 SwordHandVelocity;

        /// <summary>This frame's events (the log adds the slide-off edges and pending marks).</summary>
        public BladeLogEvents Events;
    }
}

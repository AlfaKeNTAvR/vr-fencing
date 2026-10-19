using System;

namespace VrFencing.Settings
{
    /// <summary>A blade pitch and yaw on the sword-hand controller, for the right hand (the left mirrors the yaw), degrees.</summary>
    public readonly struct GripAngles
    {
        public GripAngles(float pitchDegrees, float yawDegrees)
        {
            PitchDegrees = pitchDegrees;
            YawDegrees = yawDegrees;
        }

        /// <summary>Up positive, from the controller's forward axis.</summary>
        public float PitchDegrees { get; }

        /// <summary>Right positive, for the right hand.</summary>
        public float YawDegrees { get; }
    }

    /// <summary>
    /// Where each grip puts the épée on the controller, computed from geometry in PER-51 (adopted in PER-52), in the
    /// OVRCameraRig controller anchor's frame (+z forward, +y the thumb's side, +x right) for a right hand.
    ///
    /// The Touch Plus handle rises about 60 degrees above the anchor's forward axis (Meta's legacy-to-grip offset, and the
    /// controller mesh), and a neutral-wrist power grasp holds a handle at 105 to 115 degrees to the forearm. So the
    /// forearm line, which every grip continues with the blade, is about 10 degrees below the anchor's forward axis. A
    /// French grip is canted a few degrees down and inward (toward the body's center line, left for a right hand); a
    /// pistol grip a little less. The guard sits on the blade line ahead of the thumb web (the anchor's origin), at the
    /// FIE's 2 cm thumb rule plus the shell and pad for a pistol grip, further for a French grip and further again posted.
    /// </summary>
    public static class GripGeometry
    {
        /// <summary>The PER-22 feel-test angles (pitch +10, yaw -10), kept to compare against.</summary>
        public static readonly GripAngles FeelTestAngles = new GripAngles(10.0f, -10.0f);

        /// <summary>How far the front of the guard sits ahead of the controller's origin, along the blade's line.</summary>
        public static float GetGuardForwardMeters(BladeMount mount)
        {
            switch (mount)
            {
                case BladeMount.Center: return 0.0f;
                case BladeMount.Pistol: return 0.055f;
                case BladeMount.French: return 0.075f;
                case BladeMount.FrenchPosted: return 0.13f;
                default: throw new ArgumentOutOfRangeException(nameof(mount), mount, "Not a grip.");
            }
        }

        /// <summary>The grip's computed pitch and yaw (PER-51 offsets table).</summary>
        public static GripAngles GetComputedAngles(BladeMount mount)
        {
            switch (mount)
            {
                case BladeMount.Center: return new GripAngles(-10.0f, 0.0f);
                case BladeMount.Pistol: return new GripAngles(-12.0f, -3.0f);
                case BladeMount.French: return new GripAngles(-13.0f, -4.0f);
                case BladeMount.FrenchPosted: return new GripAngles(-13.0f, -4.0f);
                default: throw new ArgumentOutOfRangeException(nameof(mount), mount, "Not a grip.");
            }
        }

        /// <summary>The angles in use for the chosen grip: each saved angle, else the computed one.</summary>
        public static GripAngles GetActiveAngles(BladeSettings blade)
        {
            if (blade == null)
            {
                throw new ArgumentNullException(nameof(blade));
            }

            var computed = GetComputedAngles(blade.Mount);
            var saved = blade.SavedAngles.For(blade.Mount);
            return new GripAngles(saved.PitchDegrees ?? computed.PitchDegrees, saved.YawDegrees ?? computed.YawDegrees);
        }
    }
}

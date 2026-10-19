namespace VrFencing.Dummy
{
    /// <summary>
    /// A named part of the Dummy that a Drill asks the Fencer to Touch (Drill decision, PER-21).
    /// Shared by the Dummy (which lays the zones out), the Blade (which reports which zone a contact hit)
    /// and the Drills (which cue a zone and judge the Touch).
    /// </summary>
    public enum TargetZone
    {
        Hand,
        Wrist,
        Forearm,
        UpperArm,
        Thigh,
        Foot,
        Body,
    }
}

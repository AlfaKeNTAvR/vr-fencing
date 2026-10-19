using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Strip
{
    /// <summary>Which press two-point placement is waiting for.</summary>
    public enum PlacementStep
    {
        /// <summary>The first press sets the Fencer's En Garde Line.</summary>
        EnGardeLine,

        /// <summary>The second press sets the Dummy's En Garde Line, and with it the Strip's direction and the gap between the lines.</summary>
        DummyEnGardeLine,

        /// <summary>Both points are set; <see cref="StripPlacement.Result"/> holds the Strip.</summary>
        Done,
    }

    /// <summary>What a placement press did.</summary>
    public enum PlacementPressOutcome
    {
        EnGardeLineSet,

        /// <summary>The Strip is placed.</summary>
        Placed,

        /// <summary>
        /// The Dummy's En Garde Line was closer than <see cref="StripGeometry.MinimumPlacedLengthMeters"/> to the
        /// Fencer's, too close to give a direction; still waiting for it.
        /// </summary>
        TooClose,

        /// <summary>Placement was already done; the press changed nothing.</summary>
        Ignored,
    }

    /// <summary>A placed Strip: where the Fencer's En Garde Line is in the room, and how far the Dummy's En Garde Line is from it.</summary>
    public readonly struct PlacedStrip
    {
        public PlacedStrip(TrackedPose enGardeLine, float placedLengthMeters)
        {
            EnGardeLine = enGardeLine;
            PlacedLengthMeters = placedLengthMeters;
        }

        /// <summary>The Strip frame's pose in the room: the Fencer's En Garde Line, facing the Dummy's.</summary>
        public TrackedPose EnGardeLine { get; }

        /// <summary>The gap between the Fencer's and the Dummy's En Garde Lines.</summary>
        public float PlacedLengthMeters { get; }
    }

    /// <summary>
    /// Two-point placement as a small state machine (PER-90): the first press marks the Fencer's En Garde Line, the
    /// second the Dummy's, and the Strip is built around the two (<see cref="StripGeometry"/>). The Unity layer feeds it
    /// the off-hand controller's position on each trigger press; points are dropped to the floor.
    /// </summary>
    public sealed class StripPlacement
    {
        private Vector3 _enGardePoint;

        public PlacementStep Step { get; private set; } = PlacementStep.EnGardeLine;

        /// <summary>The first point dropped to the floor, once it is set.</summary>
        public Vector3? EnGardePointOnFloor =>
            Step == PlacementStep.EnGardeLine ? (Vector3?)null : new Vector3(_enGardePoint.X, 0.0f, _enGardePoint.Z);

        /// <summary>The placed Strip once <see cref="Step"/> is <see cref="PlacementStep.Done"/>, otherwise null.</summary>
        public PlacedStrip? Result { get; private set; }

        public PlacementPressOutcome Press(Vector3 controllerPositionInRoom)
        {
            switch (Step)
            {
                case PlacementStep.EnGardeLine:
                    _enGardePoint = controllerPositionInRoom;
                    Step = PlacementStep.DummyEnGardeLine;
                    return PlacementPressOutcome.EnGardeLineSet;

                case PlacementStep.DummyEnGardeLine:
                    var preview = Preview(controllerPositionInRoom);
                    if (!preview.HasValue)
                    {
                        return PlacementPressOutcome.TooClose;
                    }

                    Result = preview;
                    Step = PlacementStep.Done;
                    return PlacementPressOutcome.Placed;

                default:
                    return PlacementPressOutcome.Ignored;
            }
        }

        /// <summary>
        /// The Strip the second press would place with the controller at <paramref name="controllerPositionInRoom"/>,
        /// for a live preview; null before the Fencer's En Garde Line is set or while the two lines are too close.
        /// </summary>
        public PlacedStrip? Preview(Vector3 controllerPositionInRoom)
        {
            if (Step != PlacementStep.DummyEnGardeLine)
            {
                return null;
            }

            var horizontal = new Vector2(controllerPositionInRoom.X - _enGardePoint.X, controllerPositionInRoom.Z - _enGardePoint.Z);
            if (horizontal.Length() < StripGeometry.MinimumPlacedLengthMeters)
            {
                return null;
            }

            var (enGardeLine, placedLength) = StripFrame.FromPlacementPoints(_enGardePoint, controllerPositionInRoom);
            return new PlacedStrip(enGardeLine, placedLength);
        }
    }
}

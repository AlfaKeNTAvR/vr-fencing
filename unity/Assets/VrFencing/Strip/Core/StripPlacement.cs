using System.Numerics;
using VrFencing.Settings;

namespace VrFencing.Strip
{
    /// <summary>Which press two-point placement is waiting for.</summary>
    public enum PlacementStep
    {
        /// <summary>The first press sets the En Garde Line.</summary>
        EnGardeLine,

        /// <summary>The second press sets the far end, and with it direction and length.</summary>
        FarEnd,

        /// <summary>Both points are set; <see cref="StripPlacement.Result"/> holds the Strip.</summary>
        Done,
    }

    /// <summary>What a placement press did.</summary>
    public enum PlacementPressOutcome
    {
        EnGardeLineSet,

        /// <summary>The Strip is placed.</summary>
        Placed,

        /// <summary>The far end was closer than <see cref="StripGeometry.MinimumPlacedLengthMeters"/> to the En Garde Line; still waiting for it.</summary>
        TooShort,

        /// <summary>Placement was already done; the press changed nothing.</summary>
        Ignored,
    }

    /// <summary>A placed Strip: where its En Garde Line is in the room, and how far the far end is from it.</summary>
    public readonly struct PlacedStrip
    {
        public PlacedStrip(TrackedPose enGardeLine, float placedLengthMeters)
        {
            EnGardeLine = enGardeLine;
            PlacedLengthMeters = placedLengthMeters;
        }

        /// <summary>The Strip frame's pose in the room.</summary>
        public TrackedPose EnGardeLine { get; }

        public float PlacedLengthMeters { get; }
    }

    /// <summary>
    /// Two-point placement as a small state machine. The Unity layer feeds it the off-hand controller's position on
    /// each trigger press; points are dropped to the floor.
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
                    Step = PlacementStep.FarEnd;
                    return PlacementPressOutcome.EnGardeLineSet;

                case PlacementStep.FarEnd:
                    var preview = Preview(controllerPositionInRoom);
                    if (!preview.HasValue)
                    {
                        return PlacementPressOutcome.TooShort;
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
        /// for a live preview; null before the En Garde Line is set or while the far end is too close.
        /// </summary>
        public PlacedStrip? Preview(Vector3 controllerPositionInRoom)
        {
            if (Step != PlacementStep.FarEnd)
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

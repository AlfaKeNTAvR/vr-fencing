// The footwork feel test's script (PER-111): it runs the Dummy through every move in order, again and again, comes
// home after the moves that travel, and never moves any part further in a frame than the Blade's sweep.
using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VrFencing.Dummy;
using VrFencing.Settings;

namespace VrFencing.Tests.Dummy
{
    public class FootworkShowcaseTests
    {
        private const float FrameSeconds = 1.0f / 72.0f;
        private const float Home = 3.5f;

        [TestCase(1.5f)]
        [TestCase(2.1f)]
        public void ItRunsEveryMoveInOrderAndComesHome(float height)
        {
            var footwork = new DummyFootwork(height, Hand.Right, Home, new Random(1));
            var showcase = new FootworkShowcase(footwork, height);
            var moves = new List<ShowcaseMove>();
            var stepsSeen = new HashSet<FootworkStep>();
            showcase.MoveStarted += moves.Add;

            for (var frame = 0; frame < 72 * 60 && moves.Count < 11; frame++)
            {
                showcase.Step(FrameSeconds);
                stepsSeen.Add(footwork.CurrentStep);
            }

            Assert.That(moves.GetRange(0, 11), Is.EqualTo(new[]
            {
                ShowcaseMove.Stand, ShowcaseMove.Advance, ShowcaseMove.Stand, ShowcaseMove.Retreat, ShowcaseMove.Stand,
                ShowcaseMove.Lunge, ShowcaseMove.Stand, ShowcaseMove.Fleche, ShowcaseMove.Stand, ShowcaseMove.Retreat,
                ShowcaseMove.Stand,
            }));
            Assert.That(stepsSeen, Is.EquivalentTo((FootworkStep[])Enum.GetValues(typeof(FootworkStep))));
            Assert.That(footwork.DistanceMeters, Is.EqualTo(Home).Within(FootworkTuning.MinStepMeters), "Home again.");
        }

        [Test]
        public void NothingJumpsFurtherInAFrameThanTheBladeSweeps()
        {
            const float height = 2.1f;
            var footwork = new DummyFootwork(height, Hand.Left, Home, new Random(3));
            var showcase = new FootworkShowcase(footwork, height);
            var layout = DummyLayout.Create(height, Hand.Left);
            var before = layout.GetBodyOnStrip(footwork.DistanceMeters);

            for (var frame = 0; frame < 72 * 40; frame++)
            {
                showcase.Step(FrameSeconds);
                layout.PoseLegs(footwork.Legs);
                var now = layout.GetBodyOnStrip(footwork.DistanceMeters);
                for (var part = 0; part < now.Count; part++)
                {
                    Assert.That(Vector3.Distance(now[part].Start, before[part].Start), Is.LessThan(0.05f), $"Part {part}, frame {frame}, {showcase.CurrentMove}");
                    Assert.That(Vector3.Distance(now[part].End, before[part].End), Is.LessThan(0.05f), $"Part {part}, frame {frame}, {showcase.CurrentMove}");
                }

                before = now;
            }
        }
    }
}

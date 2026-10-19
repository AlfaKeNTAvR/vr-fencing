// Which footwork answers what in Drills 4 and 6 (PER-39), and how footwork calls keep the Fencer on the Strip.
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;

namespace VrFencing.Tests.Drills
{
    public class FootworkJudgingTests
    {
        private static readonly FootworkAction[] AllCalls =
        {
            FootworkAction.Advance, FootworkAction.Retreat, FootworkAction.Lunge, FootworkAction.Check,
        };

        [TestCase(DummyDistance.Extension, FootworkAction.None, true)]
        [TestCase(DummyDistance.Extension, FootworkAction.Lunge, false)]
        [TestCase(DummyDistance.Extension, FootworkAction.Advance, false)]
        [TestCase(DummyDistance.Lunge, FootworkAction.Lunge, true)]
        [TestCase(DummyDistance.Lunge, FootworkAction.None, false)]
        [TestCase(DummyDistance.Lunge, FootworkAction.Advance, false)]
        [TestCase(DummyDistance.Lunge, FootworkAction.StepLunge, false)]
        [TestCase(DummyDistance.StepLunge, FootworkAction.StepLunge, true)]
        [TestCase(DummyDistance.StepLunge, FootworkAction.Lunge, true)]
        [TestCase(DummyDistance.StepLunge, FootworkAction.Advance, false)]
        public void FootworkMustSuitTheDummyDistance(DummyDistance distance, FootworkAction footwork, bool suits)
        {
            Assert.That(FootworkJudging.SuitsDistance(distance, footwork), Is.EqualTo(suits));
        }

        [TestCase(FootworkAction.Advance, FootworkAction.Advance, true)]
        [TestCase(FootworkAction.Advance, FootworkAction.Check, false)]
        [TestCase(FootworkAction.Retreat, FootworkAction.Retreat, true)]
        [TestCase(FootworkAction.Lunge, FootworkAction.Lunge, true)]
        [TestCase(FootworkAction.Lunge, FootworkAction.StepLunge, true)]
        [TestCase(FootworkAction.Lunge, FootworkAction.Advance, false)]
        [TestCase(FootworkAction.Check, FootworkAction.Check, true)]
        [TestCase(FootworkAction.Check, FootworkAction.Advance, false)]
        public void FootworkMustAnswerTheCall(FootworkAction call, FootworkAction footwork, bool answers)
        {
            Assert.That(FootworkJudging.AnswersCall(call, footwork), Is.EqualTo(answers));
        }

        [Test]
        public void EveryCallIsOpenNearTheEnGardeLine()
        {
            var allowed = FootworkJudging.FilterCallsByDrift(AllCalls, new FootworkAction?[] { FootworkAction.Advance, FootworkAction.Lunge, null });

            Assert.That(allowed, Is.EqualTo(AllCalls));
        }

        [Test]
        public void NoMoreAdvancesOnceTwoStepsAhead()
        {
            var judged = new FootworkAction?[] { FootworkAction.Advance, FootworkAction.Check, FootworkAction.Advance };

            Assert.That(FootworkJudging.FilterCallsByDrift(AllCalls, judged), Is.EqualTo(new[] { FootworkAction.Retreat, FootworkAction.Lunge, FootworkAction.Check }));
        }

        [Test]
        public void NoMoreRetreatsOnceAStepBehind()
        {
            var judged = new FootworkAction?[] { FootworkAction.Advance, FootworkAction.Retreat, FootworkAction.Retreat };

            Assert.That(FootworkJudging.FilterCallsByDrift(AllCalls, judged), Has.No.Member(FootworkAction.Retreat));
            Assert.That(FootworkJudging.FilterCallsByDrift(AllCalls, judged), Has.Member(FootworkAction.Advance));
        }

        [Test]
        public void AnOnlyCallIsKeptEvenWhenItWouldDrift()
        {
            var onlyAdvance = new[] { FootworkAction.Advance };

            var allowed = FootworkJudging.FilterCallsByDrift(onlyAdvance, Enumerable.Repeat<FootworkAction?>(FootworkAction.Advance, 5));

            Assert.That(allowed, Is.EqualTo(onlyAdvance));
        }
    }
}

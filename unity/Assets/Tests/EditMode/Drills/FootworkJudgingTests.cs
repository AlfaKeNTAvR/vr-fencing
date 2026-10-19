// Which footwork answers what in Drills 4 and 6 (PER-39).
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;

namespace VrFencing.Tests.Drills
{
    public class FootworkJudgingTests
    {
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
    }
}

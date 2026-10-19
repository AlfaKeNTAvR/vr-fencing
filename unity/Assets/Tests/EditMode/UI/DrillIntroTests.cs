// The task line before a Drill's first Rep (PER-92): up for about 3 s, then the run starts.
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Settings;
using VrFencing.UI;

namespace VrFencing.Tests.UI
{
    public class DrillIntroTests
    {
        [Test]
        public void TheTaskLineStaysUpForThreeSecondsThenTheRunStarts()
        {
            var drill = DrillCatalog.CreatePointControlDrill(new TrainerSettings());
            var intro = new DrillIntro(drill, startedAtSeconds: 10.0);

            Assert.That(DrillIntro.Seconds, Is.EqualTo(3.0).Within(1e-9));
            Assert.That(intro.IsOver(10.0), Is.False);
            Assert.That(intro.IsOver(12.9), Is.False);
            Assert.That(intro.IsOver(13.0), Is.True);
        }

        [Test]
        public void TheRunningPanelShowsTheDrillAndWhatToDo()
        {
            var drill = DrillCatalog.CreateReactionDrill(new TrainerSettings());
            var intro = new DrillIntro(drill, startedAtSeconds: 0.0);

            Assert.That(intro.Status.Title, Is.EqualTo(drill.Name));
            Assert.That(intro.Status.Instruction, Is.EqualTo(DrillCatalog.ReactionTaskLine));
        }
    }
}

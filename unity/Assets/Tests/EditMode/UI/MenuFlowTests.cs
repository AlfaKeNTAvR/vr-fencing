// Which screen the menu shows, paging of long lists, the Running panel's text, and the history list (PER-36).
using System;
using System.Linq;
using NUnit.Framework;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Session;
using VrFencing.Tests.Drills;
using VrFencing.Tests.Session;
using VrFencing.UI;

namespace VrFencing.Tests.UI
{
    public class MenuFlowTests
    {
        [Test]
        public void HomeShowsUntilTheFencerOpensAnotherPage()
        {
            var navigator = new MenuNavigator();

            Assert.That(navigator.GetVisibleScreen(isPlacingStrip: false, isDrillRunning: false), Is.EqualTo(MenuScreen.Home));

            navigator.Open(MenuScreen.Settings);
            Assert.That(navigator.GetVisibleScreen(false, false), Is.EqualTo(MenuScreen.Settings));
        }

        [Test]
        public void SettingsOpensOnTheGroupsAndBackStepsOutOneLevel()
        {
            var navigator = new MenuNavigator();

            navigator.Open(MenuScreen.Settings);
            Assert.That(navigator.SettingsGroup, Is.Null, "Settings opens on the list of groups.");

            navigator.OpenSettingsGroup(SettingGroup.Drills);
            Assert.That(navigator.Page, Is.EqualTo(MenuScreen.Settings));
            Assert.That(navigator.SettingsGroup, Is.EqualTo(SettingGroup.Drills));

            navigator.GoBackFromSettings();
            Assert.That(navigator.Page, Is.EqualTo(MenuScreen.Settings));
            Assert.That(navigator.SettingsGroup, Is.Null, "Back from a group returns to the list of groups.");

            navigator.GoBackFromSettings();
            Assert.That(navigator.Page, Is.EqualTo(MenuScreen.Home), "Back from the list returns Home.");
        }

        [Test]
        public void LeavingSettingsAndComingBackOpensTheGroupList()
        {
            var navigator = new MenuNavigator();
            navigator.OpenSettingsGroup(SettingGroup.Haptics);

            navigator.Open(MenuScreen.Home);
            navigator.Open(MenuScreen.Settings);

            Assert.That(navigator.SettingsGroup, Is.Null);
        }

        [Test]
        public void FoldingTheMenuKeepsTheOpenSettingsGroup()
        {
            var navigator = new MenuNavigator();
            navigator.OpenSettingsGroup(SettingGroup.Blade);

            navigator.ToggleCollapsed();
            navigator.ToggleCollapsed();

            Assert.That(navigator.GetShownPanel(false, false), Is.EqualTo(MenuScreen.Settings));
            Assert.That(navigator.SettingsGroup, Is.EqualTo(SettingGroup.Blade));
        }

        [Test]
        public void TheGripPageIsOpenedFromAndReturnsToTheBladeGroup()
        {
            var navigator = new MenuNavigator();
            navigator.OpenSettingsGroup(SettingGroup.Blade);
            navigator.Open(MenuScreen.Grip);
            navigator.SetCalibrating(true);
            navigator.SetCalibrating(false);
            Assert.That(navigator.Page, Is.EqualTo(MenuScreen.Grip), "The calibration ends back on the grip page.");

            // The grip page's Back.
            navigator.OpenSettingsGroup(SettingGroup.Blade);

            Assert.That(navigator.Page, Is.EqualTo(MenuScreen.Settings));
            Assert.That(navigator.SettingsGroup, Is.EqualTo(SettingGroup.Blade));
        }

        [Test]
        public void ARunningDrillShowsTheRunningPanelAndPlacingTheStripHidesEverything()
        {
            var navigator = new MenuNavigator();

            Assert.That(navigator.GetVisibleScreen(isPlacingStrip: false, isDrillRunning: true), Is.EqualTo(MenuScreen.Running));
            Assert.That(navigator.GetVisibleScreen(isPlacingStrip: true, isDrillRunning: true), Is.EqualTo(MenuScreen.Hidden));
            Assert.That(navigator.GetVisibleScreen(isPlacingStrip: true, isDrillRunning: false), Is.EqualTo(MenuScreen.Hidden));
        }

        [Test]
        public void AFinishedDrillOpensItsResultsUnlessNoRepWasJudged()
        {
            var navigator = new MenuNavigator();

            navigator.OnDrillFinished(hasResults: true);
            Assert.That(navigator.Page, Is.EqualTo(MenuScreen.Results));

            navigator.OnDrillFinished(hasResults: false);
            Assert.That(navigator.Page, Is.EqualTo(MenuScreen.Home));
        }

        [Test]
        public void RunningAndHiddenCannotBeOpenedByHand()
        {
            var navigator = new MenuNavigator();

            Assert.Throws<ArgumentException>(() => navigator.Open(MenuScreen.Running));
            Assert.Throws<ArgumentException>(() => navigator.Open(MenuScreen.Hidden));
        }

        [Test]
        public void PagesSplitAListAndClampToThePagesThatExist()
        {
            var lastPage = PageWindow.For(totalItems: 13, pageSize: 7, requestedPage: 5);
            Assert.That(lastPage.PageIndex, Is.EqualTo(1));
            Assert.That(lastPage.PageCount, Is.EqualTo(2));
            Assert.That(lastPage.FirstItem, Is.EqualTo(7));
            Assert.That(lastPage.ItemCount, Is.EqualTo(6));
            Assert.That(lastPage.HasNext, Is.False);
            Assert.That(lastPage.HasPrevious, Is.True);

            var empty = PageWindow.For(0, 5, -3);
            Assert.That(empty.PageIndex, Is.EqualTo(0));
            Assert.That(empty.PageCount, Is.EqualTo(1));
            Assert.That(empty.ItemCount, Is.EqualTo(0));
        }

        [Test]
        public void RunningPanelNamesTheRepAndTheZoneToTouch()
        {
            var clock = new FakeClock();
            var runner = new DrillRunner(clock, new Random(1));
            runner.Start(TestDrills.Create(zones: new[] { TargetZone.UpperArm }), 4.0f, false);

            var status = RunStatusText.Format(runner, isFencerOnStrip: true);

            Assert.That(status.Title, Is.EqualTo("Lunge to target: Rep 1 of 3"));
            Assert.That(status.Instruction, Is.EqualTo("Touch the upper arm"));
        }

        [Test]
        public void RunningPanelTellsTheFencersPauseFromLeavingTheStrip()
        {
            var runner = new DrillRunner(new FakeClock(), new Random(1));
            runner.Start(TestDrills.Create(), 4.0f, false);

            runner.ReportLeftStrip();
            Assert.That(RunStatusText.Format(runner, isFencerOnStrip: false).Instruction, Is.EqualTo(RunStatusText.OffStripText));

            runner.ReportBackOnStrip();
            runner.PauseByFencer();
            Assert.That(RunStatusText.Format(runner, isFencerOnStrip: true).Instruction, Is.EqualTo(RunStatusText.PausedText));
        }

        [Test]
        public void TheMenuButtonFoldsThePanelsAwayAndKeepsThePage()
        {
            var navigator = new MenuNavigator();
            navigator.Open(MenuScreen.Settings);

            navigator.ToggleCollapsed();

            Assert.That(navigator.IsCollapsed, Is.True);
            Assert.That(navigator.GetShownPanel(isPlacingStrip: false, isDrillRunning: false), Is.EqualTo(MenuScreen.Hidden));
            Assert.That(navigator.GetShownPanel(isPlacingStrip: false, isDrillRunning: true), Is.EqualTo(MenuScreen.Hidden), "The Running panel folds away too.");
            Assert.That(navigator.IsPointerShown(isPlacingStrip: false, isDrillRunning: false), Is.False, "No laser with nothing to point at.");

            navigator.ToggleCollapsed();

            Assert.That(navigator.GetShownPanel(false, false), Is.EqualTo(MenuScreen.Settings));
            Assert.That(navigator.GetShownPanel(false, true), Is.EqualTo(MenuScreen.Running));
            Assert.That(navigator.IsPointerShown(false, false), Is.True);
        }

        [Test]
        public void PlacingTheStripHidesThePointer()
        {
            var navigator = new MenuNavigator();

            Assert.That(navigator.IsPointerShown(isPlacingStrip: true, isDrillRunning: false), Is.False);
            Assert.That(navigator.GetShownPanel(isPlacingStrip: true, isDrillRunning: false), Is.EqualTo(MenuScreen.Hidden));
        }

        [Test]
        public void NewStripHereIsAskedOverThePageEvenWhenFolded()
        {
            var navigator = new MenuNavigator();
            navigator.Open(MenuScreen.Settings);
            navigator.SetCollapsed(true);

            Assert.That(navigator.GetShownPanel(false, false, isAskingForNewStrip: true), Is.EqualTo(MenuScreen.NewStripPrompt));
            Assert.That(navigator.IsPointerShown(false, false, isAskingForNewStrip: true), Is.True, "The laser answers it.");
            Assert.That(navigator.GetShownPanel(isPlacingStrip: true, isDrillRunning: false, isAskingForNewStrip: true), Is.EqualTo(MenuScreen.Hidden));

            navigator.SetCollapsed(false);
            Assert.That(navigator.GetShownPanel(false, false, isAskingForNewStrip: false), Is.EqualTo(MenuScreen.Settings), "Answered: back to the page.");
        }

        [Test]
        public void AFinishedDrillShowsItsResultsEvenWhenFolded()
        {
            var navigator = new MenuNavigator();
            navigator.SetCollapsed(true);

            navigator.OnDrillFinished(hasResults: true);

            Assert.That(navigator.IsCollapsed, Is.False);
            Assert.That(navigator.GetShownPanel(false, false), Is.EqualTo(MenuScreen.Results));

            navigator.ToggleCollapsed();
            Assert.That(navigator.GetShownPanel(false, false), Is.EqualTo(MenuScreen.Hidden), "The Menu button folds the results again.");
        }

        [Test]
        public void ADrillCancelledWithoutResultsKeepsTheMenuFolded()
        {
            var navigator = new MenuNavigator();
            navigator.SetCollapsed(true);

            navigator.OnDrillFinished(hasResults: false);

            Assert.That(navigator.IsCollapsed, Is.True);
        }

        [Test]
        public void RunningPanelIsEmptyWhenNoDrillRuns()
        {
            var status = RunStatusText.Format(new DrillRunner(new FakeClock(), new Random(1)), true);

            Assert.That(status.Title, Is.Empty);
            Assert.That(status.Instruction, Is.Empty);
        }

        [Test]
        public void TheDummyLabelNamesANamedZoneAndStaysEmptyForALitOne()
        {
            Assert.That(RunStatusText.FormatDummyLabel(new RepCue(0, TargetZone.UpperArm, CueType.NamedTargetZone)), Is.EqualTo("Upper arm"));
            Assert.That(RunStatusText.FormatDummyLabel(new RepCue(0, TargetZone.Wrist, CueType.LitTargetZone)), Is.Empty);
        }

        [Test]
        public void HistoryListsDrillsInPickerOrderWithShortenedRunsApart()
        {
            var noon = SessionTestData.Noon;
            var history = new SessionHistory(new[]
            {
                SessionTestData.Run("s1", noon, drillId: "unknown-drill"),
                SessionTestData.Run("s1", noon, drillId: "lunge-to-target"),
                SessionTestData.Run("s2", noon.AddDays(1), drillId: "lunge-to-target", isShrunk: true),
                SessionTestData.Run("s3", noon.AddDays(2), drillId: "lunge-to-target"),
            });

            var entries = HistoryList.Build(history, new[] { TestDrills.Create() });

            Assert.That(entries.Select(entry => entry.Label), Is.EqualTo(new[] { "Lunge to target", "Lunge to target (shortened)", "unknown-drill" }));
            Assert.That(entries[0].LastRun.SessionId, Is.EqualTo("s3"), "The newest run is shown.");
        }
    }
}

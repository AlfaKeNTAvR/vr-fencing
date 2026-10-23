// The Unity side of the menus (PER-36): text is TextMeshPro at a readable size, greyed-out Drills cannot be
// pressed, and the panels draw on the URP material.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Settings;
using VrFencing.UI;

namespace VrFencing.Tests.UI
{
    public class MenuPanelTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void DestroyCreatedObjects()
        {
            foreach (var created in _created)
            {
                Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        private Transform CreatePanel()
        {
            var panel = new GameObject("Test Panel");
            _created.Add(panel);
            return panel.transform;
        }

        private static DrillDefinition Drill(string id, int order, float requiredLength)
        {
            return new DrillDefinition(id, "Drill " + id, order, requiredLength, null, repCount: 5, cueSeconds: 0.5f,
                windowSeconds: 1.5f, restSeconds: 2.0f, CueType.NamedTargetZone, SuccessRule.PointFirstTouchOnTargetZone,
                new[] { TargetZone.Body });
        }

        [Test]
        public void BodyTextIsSdfTextAboutTwoCentimetersTall()
        {
            var text = UiKit.CreateText(CreatePanel(), "Probe", Vector2.zero, new Vector2(1.0f, 0.2f), 1, UiKit.BodyFontSize,
                TextAlignmentOptions.Center, Color.white);
            text.text = "H";
            text.ForceMeshUpdate();

            Assert.That(text.font, Is.Not.Null, "TMP Essentials (default font) are missing.");
            Assert.That(text.fontSharedMaterial.shader.name, Does.Contain("Distance Field"));

            // A capital is about 0.7 of the em: 0.28 font size is a 2.8 cm em, so about 2 cm capitals, readable at 1.1 m.
            var capital = text.textInfo.characterInfo[0];
            Assert.That(capital.topLeft.y - capital.bottomLeft.y, Is.InRange(0.015f, 0.025f));
        }

        [Test]
        public void PanelQuadsUseTheUrpMaterial()
        {
            var quad = UiKit.CreateQuad(CreatePanel(), "Quad", Vector2.zero, Vector2.one, 0, Color.red);

            Assert.That(quad.sharedMaterial, Is.Not.Null);
            Assert.That(quad.sharedMaterial.shader.name, Does.StartWith("Universal Render Pipeline/"));
            Assert.That(quad.sharedMaterial.renderQueue, Is.LessThan(2500), "Panels are opaque so text always draws over them.");
        }

        [Test]
        public void AGreyedOutDrillShowsItsLengthAndCannotBeChosen()
        {
            var started = new List<string>();
            var home = new HomeScreen(CreatePanel(), started.Add, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
            var entries = DrillPicker.BuildEntries(new[] { Drill("long", 1, 6.0f), Drill("short", 2, 2.0f) }, 4.0f, false);

            home.Refresh(entries, "Strip: 4.0 m", 0.2f, string.Empty, "Partner", areSoloDrillsAllowed: true, FreePracticeBehavior.KeepDistance);

            var buttons = _created[0].GetComponentsInChildren<UiButton>();
            var longRow = buttons.First(button => button.Label == "Drill long");
            Assert.That(longRow.IsInteractable, Is.False);
            Assert.That(longRow.GetComponentsInChildren<TextMeshPro>().Select(text => text.text), Has.Some.EqualTo("Needs a 6.0 m Strip"));

            longRow.Press();
            Assert.That(home.SelectedDrillId, Is.EqualTo("short"));

            buttons.First(button => button.Label == "Start").Press();
            Assert.That(started, Is.EqualTo(new[] { "short" }));
        }

        [Test]
        public void StartIsOffWhileConnectedToAPartner()
        {
            var started = new List<string>();
            var home = new HomeScreen(CreatePanel(), started.Add, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
            var entries = DrillPicker.BuildEntries(new[] { Drill("short", 1, 2.0f) }, 4.0f, false);

            home.Refresh(entries, "Strip: 4.0 m", 0.2f, string.Empty, "Partner: on", areSoloDrillsAllowed: false, FreePracticeBehavior.KeepDistance);

            var buttons = _created[0].GetComponentsInChildren<UiButton>();
            var start = buttons.First(button => button.Label == "Start");
            Assert.That(start.IsInteractable, Is.False);
            start.Press();
            Assert.That(started, Is.Empty);
            Assert.That(buttons.Any(button => button.Label == "Partner: on"), Is.True);
        }

        [Test]
        public void TheFreePracticeRowNamesTheBehaviorAndCyclesOnPress()
        {
            var cycles = 0;
            var home = new HomeScreen(CreatePanel(), _ => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => cycles++);
            var entries = DrillPicker.BuildEntries(new[] { Drill("short", 1, 2.0f) }, 4.0f, false);

            home.Refresh(entries, "Strip: 4.0 m", 0.2f, string.Empty, "Partner", areSoloDrillsAllowed: true, FreePracticeBehavior.Attacks);

            var row = _created[0].GetComponentsInChildren<UiButton>().First(button => button.name == "Free Practice");
            Assert.That(row.Label, Is.EqualTo("Dummy: attacks"));
            row.Press();
            Assert.That(cycles, Is.EqualTo(1));
        }

        [Test]
        public void TheHomeButtonsStackWithoutOverlapping()
        {
            new HomeScreen(CreatePanel(), _ => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });

            var names = new[] { "Free Practice", "Start", "Partner", "Settings", "History", "Re-place Strip", "Tolerance" };
            var buttons = _created[0].GetComponentsInChildren<UiButton>(includeInactive: true);
            var heights = names.Select(name => buttons.First(button => button.name == name).transform.localPosition.y).ToArray();
            for (var index = 1; index < heights.Length; index++)
            {
                Assert.That(heights[index - 1] - heights[index], Is.GreaterThan(0.052f), $"{names[index]} overlaps the button above it.");
            }
        }

        [Test]
        public void TheKeypadJoinsOnlyAFullCode()
        {
            var joined = new List<string>();
            var screen = new PartnerScreen(CreatePanel(), () => { }, () => { }, joined.Add, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
            var session = new VrFencing.Partner.PartnerSession();
            session.BeginEnteringCode();
            screen.SetVisible(true);
            screen.Refresh(session, "1.0");

            var buttons = _created[0].GetComponentsInChildren<UiButton>(includeInactive: true);
            var join = buttons.First(button => button.name == "Join Code");
            foreach (var key in "FGR78")
            {
                buttons.First(button => button.name == $"Key {key}").Press();
            }

            join.Press();
            Assert.That(joined, Is.Empty, "Five characters are not a code.");
            Assert.That(join.IsInteractable, Is.False);

            buttons.First(button => button.name == "Key N").Press();
            join.Press();
            Assert.That(joined, Is.EqualTo(new[] { "FGR78N" }));
        }

        [Test]
        public void OnlyTheHostCanSwitchTheMode()
        {
            var screen = new PartnerScreen(CreatePanel(), () => { }, () => { }, _ => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
            var guest = new VrFencing.Partner.PartnerSession();
            guest.BeginJoin("FGR78N");
            guest.OnJoined(VrFencing.Partner.PartnerMode.Bout);
            screen.SetVisible(true);
            screen.Refresh(guest, "1.0");

            var buttons = _created[0].GetComponentsInChildren<UiButton>();
            Assert.That(buttons.First(button => button.name == "Mode").IsInteractable, Is.False);
            Assert.That(buttons.First(button => button.name == "Leave").IsInteractable, Is.True);
            Assert.That(buttons.First(button => button.name == "Host").IsInteractable, Is.False);
        }

        [Test]
        public void MirrorMeStartsFromThePageAndItsDelayButtonCyclesWhileMirroring()
        {
            var session = new VrFencing.Partner.PartnerSession();
            var screen = new PartnerScreen(CreatePanel(), () => { }, () => { }, _ => { }, () => { }, () => { }, () => { },
                () => session.BeginMirror(), () => session.CycleMirrorDelay(), () => session.CycleMirrorNoise(), () => { }, () => { });
            screen.SetVisible(true);
            screen.Refresh(session, "1.0");

            var buttons = _created[0].GetComponentsInChildren<UiButton>(includeInactive: true);
            var mirror = buttons.First(button => button.name == "Mirror Me");
            var delay = buttons.First(button => button.name == "Mirror Delay");
            Assert.That(mirror.IsInteractable, Is.True);
            Assert.That(delay.gameObject.activeSelf, Is.False);

            mirror.Press();
            screen.Refresh(session, "1.0");
            Assert.That(mirror.IsInteractable, Is.False);
            Assert.That(delay.gameObject.activeSelf, Is.True);
            Assert.That(buttons.First(button => button.name == "Leave").IsInteractable, Is.True);
            Assert.That(buttons.First(button => button.name == "Mode").gameObject.activeSelf, Is.False);

            delay.Press();
            screen.Refresh(session, "1.0");
            Assert.That(session.MirrorDelaySeconds, Is.EqualTo(0.2));

            // PER-145: the link-noise button beside it cycles jitter and loss while mirroring.
            var noise = buttons.First(button => button.name == "Mirror Noise");
            Assert.That(noise.gameObject.activeSelf, Is.True);
            noise.Press();
            screen.Refresh(session, "1.0");
            Assert.That(session.MirrorNoise, Is.EqualTo(VrFencing.Partner.MirrorNoise.Jitter));
            Assert.That(Mathf.Abs(noise.transform.localPosition.x - delay.transform.localPosition.x), Is.GreaterThan(0.3f), "The two mirror buttons do not overlap.");

            // Host, Join, Mirror me and Leave sit in one row without overlapping.
            var row = new[] { "Host", "Join", "Mirror Me", "Leave" }.Select(name => buttons.First(button => button.name == name).transform.localPosition.x).ToArray();
            for (var index = 1; index < row.Length; index++)
            {
                Assert.That(row[index] - row[index - 1], Is.GreaterThan(0.165f), $"{index} overlaps its neighbour.");
            }
        }

        [Test]
        public void SettingsStepButtonsChangeTheirGroupsSetting()
        {
            var changes = new List<(SettingId, int)>();
            var screen = new SettingsScreen(CreatePanel(), (id, direction) => changes.Add((id, direction)), _ => { }, () => { }, () => { });
            screen.Refresh(new VrFencing.Settings.TrainerSettings(), SettingGroup.Drills, string.Empty);

            var buttons = _created[0].GetComponentsInChildren<UiButton>();
            buttons.First(button => button.name == "Setting 1 Up").Press();
            buttons.First(button => button.name == "Setting 0 Down").Press();

            var drills = SettingGroups.GetSettings(SettingGroup.Drills);
            Assert.That(changes, Is.EqualTo(new[] { (drills[1], 1), (drills[0], -1) }));
        }

        [Test]
        public void SettingsOpensOnTheGroupButtonsWithNoSettingRows()
        {
            var opened = new List<SettingGroup>();
            var screen = new SettingsScreen(CreatePanel(), (_, _) => { }, opened.Add, () => { }, () => { });
            screen.Refresh(new VrFencing.Settings.TrainerSettings(), null, string.Empty);

            var shown = _created[0].GetComponentsInChildren<UiButton>().Select(button => button.Label).ToArray();
            Assert.That(shown, Is.EquivalentTo(new[] { "You", "Drills", "Strip", "Touch", "Blade", "Haptics", "Armed Dummy", "Back" }),
                "Only the groups and Back: no Setting steps and no Calibrate grip.");

            _created[0].GetComponentsInChildren<UiButton>().First(button => button.Label == "Strip").Press();
            Assert.That(opened, Is.EqualTo(new[] { SettingGroup.Strip }));
        }

        [Test]
        public void CalibrateGripShowsOnlyInTheBladeGroup()
        {
            var gripOpened = 0;
            var screen = new SettingsScreen(CreatePanel(), (_, _) => { }, _ => { }, () => gripOpened++, () => { });

            screen.Refresh(new VrFencing.Settings.TrainerSettings(), SettingGroup.Touch, string.Empty);
            Assert.That(_created[0].GetComponentsInChildren<UiButton>().Select(button => button.Label), Has.None.EqualTo("Calibrate grip"));

            screen.Refresh(new VrFencing.Settings.TrainerSettings(), SettingGroup.Blade, string.Empty);
            _created[0].GetComponentsInChildren<UiButton>().First(button => button.Label == "Calibrate grip").Press();
            Assert.That(gripOpened, Is.EqualTo(1));
        }

        [Test]
        public void ANamedZoneCueIsLabelledAboveTheDummyFacingTheFencerUntilJudged()
        {
            var dummyObject = new GameObject("Test Dummy");
            _created.Add(dummyObject);
            var dummy = dummyObject.AddComponent<DummyBehaviour>();
            dummy.Configure(1.65f, VrFencing.Settings.Hand.Right);
            var clock = new VrFencing.Tests.Drills.FakeClock();
            var runner = new DrillRunner(clock, new System.Random(3));
            using var display = new DummyCueDisplay(runner, dummy);

            runner.Start(DrillCatalog.CreatePointControlDrill(new VrFencing.Settings.TrainerSettings()), 4.0f, shrinkDrillsToFit: false);

            var label = dummyObject.GetComponentsInChildren<TextMeshPro>(includeInactive: true).Single();
            Assert.That(label.gameObject.activeSelf, Is.True);
            Assert.That(label.text, Is.EqualTo(TargetZoneNames.Get(runner.CurrentTargetZone.Value)));
            Assert.That(label.transform.position.y, Is.GreaterThan(1.65f), "The label sits above the head, clear of every zone.");
            Assert.That(Vector3.Dot(label.transform.forward, Vector3.forward), Is.EqualTo(1.0f).Within(1e-4f),
                "TextMeshPro reads from its -z side, so the label must face away from the Fencer (+z along the Strip).");

            runner.ReportContact(new BladeContact(runner.CurrentTargetZone.Value, ContactKind.PointFirst, 1.0f, 0.0f));

            Assert.That(label.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void TheTaskLineStandsAboveTheDummyUntilTheFirstCueReplacesIt()
        {
            var dummyObject = new GameObject("Test Dummy");
            _created.Add(dummyObject);
            var dummy = dummyObject.AddComponent<DummyBehaviour>();
            dummy.Configure(1.65f, VrFencing.Settings.Hand.Right);
            var runner = new DrillRunner(new VrFencing.Tests.Drills.FakeClock(), new System.Random(3));
            using var display = new DummyCueDisplay(runner, dummy);
            var drill = DrillCatalog.CreatePointControlDrill(new VrFencing.Settings.TrainerSettings());

            display.ShowTaskLine(drill.TaskLine);

            var label = dummyObject.GetComponentsInChildren<TextMeshPro>(includeInactive: true).Single();
            Assert.That(label.gameObject.activeSelf, Is.True);
            Assert.That(label.text, Is.EqualTo(drill.TaskLine));

            runner.Start(drill, 4.0f, shrinkDrillsToFit: false);

            Assert.That(label.text, Is.EqualTo(TargetZoneNames.Get(runner.CurrentTargetZone.Value)), "The named zone's label, as before PER-92.");
        }

        [Test]
        public void ALitZoneCueShowsNoLabel()
        {
            var dummyObject = new GameObject("Test Dummy");
            _created.Add(dummyObject);
            var dummy = dummyObject.AddComponent<DummyBehaviour>();
            dummy.Configure(1.65f, VrFencing.Settings.Hand.Right);
            var runner = new DrillRunner(new VrFencing.Tests.Drills.FakeClock(), new System.Random(3));
            using var display = new DummyCueDisplay(runner, dummy);

            runner.Start(DrillCatalog.CreateReactionDrill(new VrFencing.Settings.TrainerSettings()), 4.0f, shrinkDrillsToFit: false);

            var label = dummyObject.GetComponentsInChildren<TextMeshPro>(includeInactive: true).Single();
            Assert.That(label.gameObject.activeSelf, Is.False);
        }
    }
}

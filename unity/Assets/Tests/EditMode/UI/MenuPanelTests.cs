// The Unity side of the menus (PER-36): text is TextMeshPro at a readable size, greyed-out Drills cannot be
// pressed, and the panels draw on the URP material.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using VrFencing.Drills;
using VrFencing.Dummy;
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
            var home = new HomeScreen(CreatePanel(), started.Add, () => { }, () => { }, () => { }, () => { });
            var entries = DrillPicker.BuildEntries(new[] { Drill("long", 1, 6.0f), Drill("short", 2, 2.0f) }, 4.0f, false);

            home.Refresh(entries, "Strip: 4.0 m", 0.2f, string.Empty);

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
        public void SettingsStepButtonsChangeTheirSetting()
        {
            var changes = new List<(SettingId, int)>();
            var screen = new SettingsScreen(CreatePanel(), (id, direction) => changes.Add((id, direction)), () => { });
            screen.Refresh(new VrFencing.Settings.TrainerSettings(), string.Empty);

            var buttons = _created[0].GetComponentsInChildren<UiButton>();
            buttons.First(button => button.name == "Setting 1 Up").Press();
            buttons.First(button => button.name == "Setting 0 Down").Press();

            Assert.That(changes, Is.EqualTo(new[] { (SettingsPage.All[1], 1), (SettingsPage.All[0], -1) }));
        }
    }
}

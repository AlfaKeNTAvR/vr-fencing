// Renders every menu screen, the Running panel and the Strip status text to PNGs in Builds/menu-snapshots, with
// sample data, from a camera 1.1 m away (where the menu floats). A desktop check of layout and text fit when the
// headset is not available. Batchmode needs graphics for this: run it without -nographics.
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrFencing.Drills;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Strip;
using VrFencing.UI;

namespace VrFencing.EditorTools
{
    public static class MenuSnapshots
    {
        private const string OutputFolder = "../Builds/menu-snapshots";
        private const int ImageWidth = 1600;
        private const int ImageHeight = 1200;
        private const float ViewDistanceMeters = 1.1f;

        // A mid grey-brown, roughly a room seen through passthrough.
        private static readonly Color RoomColor = new Color(0.42f, 0.4f, 0.36f, 1.0f);

        [MenuItem("VR Fencing/Render menu snapshots")]
        public static void RenderAll()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(OutputFolder);
            var camera = CreateCamera();

            var mainPanel = new GameObject("Menu Panel").transform;
            UiSurface.Create(mainPanel, "Background", new Vector2(0.78f, 0.58f));
            var settings = new TrainerSettings();
            var drills = DrillCatalog.CreateDefault(includeTestDrills: true).BuildAll(settings).Concat(SampleDrills()).ToArray();

            var home = new HomeScreen(mainPanel, _ => { }, () => { }, () => { }, () => { }, () => { }, () => { });
            home.Refresh(DrillPicker.BuildEntries(drills, 3.4f, settings.ShrinkDrillsToFit), "Strip: 3.4 m", 0.2f, "Drill cancelled before any Rep was judged",
                "Partner", areSoloDrillsAllowed: true);

            // The Partner page (PER-80): the waiting Host's code, then the Partner's keypad.
            var partner = new PartnerScreen(mainPanel, () => { }, () => { }, _ => { }, () => { }, () => { }, () => { }, () => { });
            var hosting = new PartnerSession();
            hosting.BeginHost();
            hosting.OnHostReady("FGR78N");
            var typing = new PartnerSession();
            typing.BeginEnteringCode();

            var settingsScreen = new SettingsScreen(mainPanel, (_, _) => { }, _ => { }, () => { }, () => { });

            var gripSettings = settings.Clone();
            gripSettings.Blade.Mount = BladeMount.French;
            gripSettings = SettingsPage.SaveGripAngles(gripSettings, new GripAngles(-11.0f, -2.0f));
            var grip = new GripScreen(mainPanel, () => { }, () => { }, () => { });
            grip.Refresh(gripSettings, GripText.FormatResult(new GripAngles(-11.0f, -2.0f), 3.0f));

            var record = SampleRecord("lunge", DateTimeOffset.UtcNow, wasCancelled: true);
            var history = new SessionHistory(Enumerable.Range(0, 12).Select(day => SampleRecord("lunge", DateTimeOffset.UtcNow.AddDays(day - 12), false))
                .Append(record).Append(SampleRecord("reaction", DateTimeOffset.UtcNow, false)).ToArray());
            var trend = ResultsText.FormatTrend(history.GetTrend("lunge", false), TimeSpan.Zero);
            var results = new ResultsScreen(mainPanel, () => { }, () => { }, () => { });
            results.Refresh("Lunge to target", record, trend, canRunAgain: true);

            var historyScreen = new HistoryScreen(mainPanel, () => { });
            historyScreen.Refresh(history, HistoryList.Build(history, drills), TimeSpan.Zero);

            var screens = new (string Name, Action<bool> SetVisible)[]
            {
                ("home", home.SetVisible), ("settings", settingsScreen.SetVisible), ("results", results.SetVisible), ("history", historyScreen.SetVisible),
                ("grip", grip.SetVisible), ("partner", partner.SetVisible),
            };
            foreach (var screen in screens)
            {
                foreach (var other in screens)
                {
                    other.SetVisible(other.Name == screen.Name);
                }

                if (screen.Name == "settings")
                {
                    // The group list, then every group's page (PER-70).
                    settingsScreen.Refresh(settings, null, string.Empty);
                    Render(camera, "settings");
                    foreach (var group in SettingGroups.All)
                    {
                        settingsScreen.Refresh(settings, group, string.Empty);
                        Render(camera, "settings-" + SettingGroups.GetLabel(group).ToLowerInvariant());
                    }

                    continue;
                }

                if (screen.Name == "partner")
                {
                    partner.Refresh(hosting, "1.0");
                    Render(camera, "partner-host");
                    partner.Refresh(typing, "1.0");
                    Render(camera, "partner-keypad");
                    continue;
                }

                Render(camera, screen.Name);
            }

            mainPanel.gameObject.SetActive(false);
            var runningRoot = new GameObject("Running Panel").transform;
            var running = new RunningPanel(runningRoot, () => { }, () => { }, () => { });
            running.Refresh(new RunStatus("Lunge to target: Rep 3 of 10", RunStatusText.OffStripText), isPausedByFencer: false, 0.0);
            Render(camera, "running");
            runningRoot.gameObject.SetActive(false);

            var status = new StripStatusText();
            status.SetStandingMessage("Off the Strip: step back on");
            status.Tick(camera.transform, 0.0, 0.0f);
            Render(camera, "strip-status");

            LogTextMetrics();
            Debug.Log($"[MenuSnapshots] Wrote snapshots to {Path.GetFullPath(OutputFolder)}.");
        }

        private static Camera CreateCamera()
        {
            var camera = new GameObject("Snapshot Camera").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0.0f, 0.0f, -ViewDistanceMeters), Quaternion.identity);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = RoomColor;
            camera.fieldOfView = 40.0f;
            camera.nearClipPlane = 0.05f;
            camera.targetTexture = new RenderTexture(ImageWidth, ImageHeight, 24);
            return camera;
        }

        private static void Render(Camera camera, string name)
        {
            foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            {
                text.ForceMeshUpdate();
            }

            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = camera.targetTexture;
            var image = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, ImageWidth, ImageHeight), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(OutputFolder, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }

        private static void LogTextMetrics()
        {
            var probe = UiKit.CreateText(null, "Probe", Vector2.zero, new Vector2(1.0f, 0.2f), 0, 1.0f, TextAlignmentOptions.Center, Color.white);
            probe.text = "H";
            probe.ForceMeshUpdate();
            var character = probe.textInfo.characterInfo[0];
            Debug.Log($"[MenuSnapshots] Font size 1: line height {probe.textInfo.lineInfo[0].lineHeight:0.0000} m, " +
                $"capital H {character.topLeft.y - character.bottomLeft.y:0.0000} m, text bounds {probe.textBounds.size.y:0.0000} m.");
            UnityEngine.Object.DestroyImmediate(probe.gameObject);
        }

        private static DrillDefinition[] SampleDrills()
        {
            DrillDefinition Drill(string id, string name, int order, float length, float? shrunk) => new DrillDefinition(
                id, name, order, length, shrunk, 10, 0.5f, 1.5f, 2.0f, CueType.NamedTargetZone, SuccessRule.PointFirstTouchOnTargetZone,
                new[] { TargetZone.Body });

            return new[]
            {
                Drill("point", "Point control", 1, 2.0f, null),
                Drill("lunge", "Lunge to target", 2, 3.0f, 2.0f),
                Drill("reaction", "Reaction", 3, 3.0f, null),
                Drill("compound", "Compound sequence", 4, 6.0f, 4.0f),
            };
        }

        private static DrillRunRecord SampleRecord(string drillId, DateTimeOffset at, bool wasCancelled)
        {
            var seed = Math.Abs(at.DayOfYear * 7 % 10);
            var zoneHits = new[]
            {
                new ZoneHitCount(TargetZone.Hand, 2, 1), new ZoneHitCount(TargetZone.Wrist, 1, 1), new ZoneHitCount(TargetZone.Forearm, 1, 0),
                new ZoneHitCount(TargetZone.UpperArm, 1, 1), new ZoneHitCount(TargetZone.Thigh, 1, 0), new ZoneHitCount(TargetZone.Foot, 1, 0),
                new ZoneHitCount(TargetZone.Body, 3, Math.Min(3, seed % 4)),
            };
            var touches = zoneHits.Sum(hits => hits.Touches);
            var results = new DrillResults(drillId, 10, touches, 0.43, 2.7, isShrunk: false, zoneHits);
            return new DrillRunRecord("session-" + at.DayOfYear, at, at.AddMinutes(4), wasCancelled, results);
        }
    }
}

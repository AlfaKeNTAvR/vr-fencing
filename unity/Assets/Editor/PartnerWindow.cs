// The PC Editor as the second Fencer (PER-80, decided in PER-58): "VR Fencing > Partner session" hosts or joins a
// session with the same join code as the headset, in Play mode. It uses the scene's Partner object, or makes one in a
// scene without it (an empty scene or the Fencer replay scene). The command line does the same without the window:
// -partnerJoin ABC123 or -partnerHost.
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrFencing.Partner;

namespace VrFencing.EditorTools
{
    public sealed class PartnerWindow : EditorWindow
    {
        private string _code = string.Empty;
        private string _versionOverride = string.Empty;

        [MenuItem("VR Fencing/Partner session")]
        public static void Open()
        {
            GetWindow<PartnerWindow>("Partner").Show();
        }

        private void OnInspectorUpdate()
        {
            // The session changes from network callbacks; redraw a few times a second to show it.
            Repaint();
        }

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Press Play first (any scene). The window then hosts or joins with the headset's join code.", MessageType.Info);
                return;
            }

            var partner = PartnerSessionBehaviour.FindOrCreate();
            var session = partner.State;
            var view = partner.GetComponent<OpponentViewBehaviour>();
            if (view != null && view.BodyModel == null)
            {
                view.BodyModel = PassthroughSceneBuilder.LoadOpponentBodyModel();
            }

            EditorGUILayout.LabelField("State", $"{session.Phase} ({session.Role})");
            EditorGUILayout.LabelField(PartnerPageText.FormatHeadline(session), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(PartnerPageText.FormatStatus(session), EditorStyles.wordWrappedLabel);
            if (session.Phase == PartnerPhase.WaitingForPartner || session.Phase == PartnerPhase.Connected)
            {
                EditorGUILayout.LabelField(PartnerPageText.FormatMode(session));
            }

            DrawOpponent(partner);

            if (!string.IsNullOrEmpty(session.Message))
            {
                EditorGUILayout.HelpBox(session.Message, MessageType.Warning);
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(session.IsInSession))
            {
                _versionOverride = EditorGUILayout.TextField(
                    new GUIContent("Pretend version", $"Empty sends the real version ({Application.version}); another value tries a refused join."),
                    _versionOverride);
                partner.VersionOverride = _versionOverride;
            }

            EditorGUILayout.LabelField("App version sent", partner.AppVersion);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!session.CanJoin))
            {
                _code = EditorGUILayout.TextField("Join code", _code);
                if (GUILayout.Button("Join"))
                {
                    partner.Join(_code);
                }

                if (GUILayout.Button("Host"))
                {
                    partner.Host();
                }
            }

            using (new EditorGUI.DisabledScope(!session.CanSwitchMode))
            {
                if (GUILayout.Button("Switch Bout / Paired Practice"))
                {
                    partner.SwitchMode();
                }
            }

            using (new EditorGUI.DisabledScope(!session.CanLeave))
            {
                if (GUILayout.Button("Leave"))
                {
                    partner.Leave();
                }
            }
        }

        /// <summary>What goes out (the Fencer replay, if the scene has one) and what comes in from the headset (PER-81).</summary>
        private static void DrawOpponent(PartnerSessionBehaviour partner)
        {
            var link = partner.GetComponent<OpponentLinkBehaviour>();
            if (link == null)
            {
                return;
            }

            EditorGUILayout.LabelField(
                "Sending",
                link.IsSendingReplay ? "the Fencer replay in this scene" : "nothing to send (open a Fencer replay to stand in for a Fencer)");
            var partnerInfo = link.Link.Partner;
            var bout = link.Link.Bout;
            EditorGUILayout.LabelField(
                "Opponent",
                partnerInfo.HasValue
                    ? $"{partnerInfo.Value.HeightMeters:0.00} m, {partnerInfo.Value.Hand}-handed; bout Strip {bout.LengthMeters:0.00} m, En Garde gap {bout.EnGardeGapMeters:0.00} m"
                    : "nothing received yet");
            if (link.Link.RejectedMessages > 0)
            {
                EditorGUILayout.HelpBox($"{link.Link.RejectedMessages} unreadable partner messages dropped (another build?).", MessageType.Warning);
            }

            DrawBout(partner);
        }

        /// <summary>The judge (PER-82): the ping, the rules in force, and the last light, from this app's side.</summary>
        private static void DrawBout(PartnerSessionBehaviour partner)
        {
            var bout = partner.GetComponent<BoutBehaviour>();
            var link = bout != null ? bout.Link : null;
            if (link == null)
            {
                return;
            }

            EditorGUILayout.LabelField("Bout", $"judging as the {link.Me}; {ScoringBox.FormatPing(link.Clock.RoundTripSeconds)}");
            EditorGUILayout.LabelField(
                "Rules",
                $"Touch pressure {link.Rules.Pressure}, double window {link.Rules.DoubleWindowSeconds * 1000.0:0} ms{(link.HasHostRules ? string.Empty : " (the host's not received yet)")}");
            var light = link.Box.LastLight;
            EditorGUILayout.LabelField("Last light", light.HasValue ? $"{light.Value.Sequence}: {ScoringBox.Describe(light.Value, link.Me)}" : "none yet");

            // The bout flow (PER-83): the phase, the call or halt showing, and the prompt or Paired Practice tally.
            if (!link.HasHostFlow)
            {
                EditorGUILayout.LabelField("Flow", "the host's not received yet");
                return;
            }

            var flow = link.FlowState;
            var showing = string.Join(" ", new[] { link.Announcer.Word, link.Announcer.Reason }.Where(text => !string.IsNullOrEmpty(text)));
            EditorGUILayout.LabelField("Flow", $"{PartnerPageText.FormatModeName(flow.Mode)}, {flow.Phase}{(showing.Length > 0 ? $": {showing}" : string.Empty)}");
            var info = BoutCall.FormatInfo(flow, link.Me, link.Tally, false);
            if (info.Length > 0)
            {
                EditorGUILayout.HelpBox(info, MessageType.None);
            }

            if (flow.Mode == PartnerMode.PairedPractice && GUILayout.Button("Reset tally"))
            {
                bout.ResetTally();
            }
        }
    }
}

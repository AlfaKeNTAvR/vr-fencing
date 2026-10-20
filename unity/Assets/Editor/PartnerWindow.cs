// The PC Editor as the second Fencer (PER-80, decided in PER-58): "VR Fencing > Partner session" hosts or joins a
// session with the same join code as the headset, in Play mode. It uses the scene's Partner object, or makes one in a
// scene without it (an empty scene or the Fencer replay scene). The command line does the same without the window:
// -partnerJoin ABC123 or -partnerHost.
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

            EditorGUILayout.LabelField("State", $"{session.Phase} ({session.Role})");
            EditorGUILayout.LabelField(PartnerPageText.FormatHeadline(session), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(PartnerPageText.FormatStatus(session), EditorStyles.wordWrappedLabel);
            if (session.Phase == PartnerPhase.WaitingForPartner || session.Phase == PartnerPhase.Connected)
            {
                EditorGUILayout.LabelField(PartnerPageText.FormatMode(session));
            }

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
    }
}

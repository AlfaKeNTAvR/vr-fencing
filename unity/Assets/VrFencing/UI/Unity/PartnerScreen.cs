using System;
using TMPro;
using UnityEngine;
using VrFencing.Partner;

namespace VrFencing.UI
{
    /// <summary>
    /// The Partner page (PER-80, decided in PER-58), next to Drills: Host shows the session's code in large letters to
    /// read out, Join opens a large keypad to type the Host's code, the Host switches Bout / Paired Practice, Leave
    /// ends the session. What it says comes from <see cref="PartnerPageText"/>; what is offered from
    /// <see cref="PartnerSession"/>.
    /// </summary>
    internal sealed class PartnerScreen
    {
        // The join code is read across a room: about 4.5 cm capitals.
        private const float CodeFontSize = 0.64f;
        // The version and the voice credits: three small lines at the foot of the page.
        private const float CreditsFontSize = 0.12f;
        // 20 keys in 3 rows of 7: 9 cm by 7.5 cm, easy to hit with the laser at arm's length.
        private const float KeySpacingMeters = 0.1f;
        private const float FirstKeyRowY = 0.07f;
        private const float KeyRowStepMeters = 0.088f;
        private static readonly Vector2 KeySize = new Vector2(0.09f, 0.075f);

        private readonly GameObject _pageRoot;
        private readonly GameObject _keypadRoot;
        private readonly TextMeshPro _headline;
        private readonly TextMeshPro _status;
        private readonly TextMeshPro _message;
        private readonly TextMeshPro _version;
        private readonly UiButton _host;
        private readonly UiButton _join;
        private readonly UiButton _leave;
        private readonly UiButton _back;
        private readonly UiButton _mode;
        private readonly UiButton _resetTally;

        private readonly CodeKeypad _keypad = new CodeKeypad();
        private readonly TextMeshPro _typed;
        private readonly TextMeshPro _keypadMessage;
        private readonly UiButton _joinTyped;
        private readonly Action<string> _joinCode;
        private bool _isVisible;
        private bool _isKeypadOpen;

        public PartnerScreen(
            Transform panel,
            Action host,
            Action openKeypad,
            Action<string> joinCode,
            Action cancelKeypad,
            Action switchMode,
            Action resetTally,
            Action leave,
            Action back)
        {
            _joinCode = joinCode;

            _pageRoot = new GameObject("Partner");
            _pageRoot.transform.SetParent(panel, false);
            var page = _pageRoot.transform;

            UiKit.CreateText(page, "Title", new Vector2(-0.13f, 0.235f), new Vector2(0.46f, 0.06f), 1, UiKit.TitleFontSize,
                TextAlignmentOptions.MidlineLeft, UiKit.TextColor).text = "Partner";
            _back = UiButton.Create(page, "Back", new Vector2(0.28f, 0.235f), new Vector2(0.16f, 0.05f), UiKit.SmallFontSize, back);
            _back.SetLabel("Back");

            _headline = UiKit.CreateText(page, "Headline", new Vector2(0.0f, 0.13f), new Vector2(0.72f, 0.1f), 1, CodeFontSize,
                TextAlignmentOptions.Center, UiKit.TextColor);
            _status = UiKit.CreateText(page, "Status", new Vector2(0.0f, 0.05f), new Vector2(0.72f, 0.06f), 1, UiKit.SmallFontSize,
                TextAlignmentOptions.Center, UiKit.DimTextColor);
            _mode = UiButton.Create(page, "Mode", new Vector2(-0.1f, -0.035f), new Vector2(0.46f, 0.065f), UiKit.BodyFontSize, switchMode);

            // Paired Practice only (PER-83): either Fencer zeroes both tallies; the old one is saved to the Session log.
            _resetTally = UiButton.Create(page, "Reset Tally", new Vector2(0.255f, -0.035f), new Vector2(0.2f, 0.065f), UiKit.SmallFontSize, resetTally);
            _resetTally.SetLabel("Reset tally");

            var buttonSize = new Vector2(0.21f, 0.07f);
            _host = UiButton.Create(page, "Host", new Vector2(-0.25f, -0.135f), buttonSize, UiKit.BodyFontSize, host);
            _host.SetLabel("Host");
            _join = UiButton.Create(page, "Join", new Vector2(0.0f, -0.135f), buttonSize, UiKit.BodyFontSize, openKeypad);
            _join.SetLabel("Join");
            _leave = UiButton.Create(page, "Leave", new Vector2(0.25f, -0.135f), buttonSize, UiKit.BodyFontSize, leave);
            _leave.SetLabel("Leave");

            _message = UiKit.CreateText(page, "Message", new Vector2(0.0f, -0.2f), new Vector2(0.72f, 0.045f), 1, UiKit.SmallFontSize,
                TextAlignmentOptions.Center, UiKit.NoteTextColor);

            // The app version, then the voice's credits (PER-75: the attribution the clips' licences ask for).
            _version = UiKit.CreateText(page, "Version", new Vector2(0.0f, -0.255f), new Vector2(0.74f, 0.06f), 1, CreditsFontSize,
                TextAlignmentOptions.Center, UiKit.DimTextColor);

            _keypadRoot = new GameObject("Keypad");
            _keypadRoot.transform.SetParent(panel, false);
            var keypad = _keypadRoot.transform;

            UiKit.CreateText(keypad, "Title", new Vector2(-0.1f, 0.235f), new Vector2(0.52f, 0.06f), 1, UiKit.BodyFontSize,
                TextAlignmentOptions.MidlineLeft, UiKit.TextColor).text = "Type the host's code";
            _typed = UiKit.CreateText(keypad, "Typed", new Vector2(0.0f, 0.16f), new Vector2(0.6f, 0.08f), 1, 0.5f,
                TextAlignmentOptions.Center, UiKit.TextColor);

            var rows = CodeKeypad.GetRows();
            for (var row = 0; row < rows.Length; row++)
            {
                var keys = rows[row];
                var firstKeyX = -0.5f * KeySpacingMeters * (keys.Length - 1);
                for (var column = 0; column < keys.Length; column++)
                {
                    var key = keys[column];
                    var center = new Vector2(firstKeyX + (column * KeySpacingMeters), FirstKeyRowY - (row * KeyRowStepMeters));
                    UiButton.Create(keypad, $"Key {key}", center, KeySize, UiKit.TitleFontSize, () => PressKey(key)).SetLabel(key.ToString());
                }
            }

            var actionSize = new Vector2(0.16f, 0.06f);
            UiButton.Create(keypad, "Cancel", new Vector2(-0.27f, -0.21f), actionSize, UiKit.SmallFontSize, () =>
            {
                _keypad.Clear();
                cancelKeypad();
            }).SetLabel("Cancel");
            UiButton.Create(keypad, "Delete", new Vector2(-0.09f, -0.21f), actionSize, UiKit.SmallFontSize, () =>
            {
                _keypad.Backspace();
                RedrawKeypad(string.Empty);
            }).SetLabel("Delete");
            UiButton.Create(keypad, "Clear", new Vector2(0.09f, -0.21f), actionSize, UiKit.SmallFontSize, () =>
            {
                _keypad.Clear();
                RedrawKeypad(string.Empty);
            }).SetLabel("Clear");
            _joinTyped = UiButton.Create(keypad, "Join Code", new Vector2(0.27f, -0.21f), actionSize, UiKit.SmallFontSize, JoinTyped);
            _joinTyped.SetLabel("Join");
            _keypadMessage = UiKit.CreateText(keypad, "Message", new Vector2(0.0f, -0.262f), new Vector2(0.72f, 0.035f), 1, UiKit.TinyFontSize,
                TextAlignmentOptions.Center, UiKit.NoteTextColor);
        }

        /// <summary>What is typed on the keypad so far (for tests and snapshots).</summary>
        public string TypedCode => _keypad.Typed;

        public void SetVisible(bool isVisible)
        {
            if (!isVisible)
            {
                _pageRoot.SetActive(false);
                _keypadRoot.SetActive(false);
            }

            _isVisible = isVisible;
        }

        public void Refresh(PartnerSession session, string appVersion)
        {
            var isKeypad = session.Phase == PartnerPhase.EnteringCode;
            _pageRoot.SetActive(_isVisible && !isKeypad);
            _keypadRoot.SetActive(_isVisible && isKeypad);
            if (isKeypad)
            {
                // PER-84: after losing the Host, the keypad opens with its code typed, to rejoin with one press.
                if (!_isKeypadOpen)
                {
                    _isKeypadOpen = true;
                    foreach (var key in session.RejoinCode)
                    {
                        _keypad.Press(key);
                    }
                }

                RedrawKeypad(session.Message);
                return;
            }

            _isKeypadOpen = false;
            _keypad.Clear();
            UiKit.SetText(_headline, PartnerPageText.FormatHeadline(session));
            UiKit.SetText(_status, PartnerPageText.FormatStatus(session));
            UiKit.SetText(_message, session.Message);
            UiKit.SetText(_version, $"App version {appVersion}. {BoutCall.Credits}");
            _host.SetInteractable(session.CanHost);
            _join.SetInteractable(session.CanJoin);
            _leave.SetInteractable(session.CanLeave);
            _back.SetInteractable(!session.IsBusy);
            var isModeShown = session.Phase == PartnerPhase.WaitingForPartner || session.Phase == PartnerPhase.Connected;
            _mode.SetVisible(isModeShown);
            _mode.SetLabel(PartnerPageText.FormatMode(session));
            _mode.SetInteractable(session.CanSwitchMode);
            _resetTally.SetVisible(session.Phase == PartnerPhase.Connected && session.Mode == PartnerMode.PairedPractice);
        }

        private void PressKey(char key)
        {
            _keypad.Press(key);
            RedrawKeypad(string.Empty);
        }

        private void JoinTyped()
        {
            if (_keypad.IsComplete)
            {
                _joinCode(_keypad.Typed);
            }
        }

        private void RedrawKeypad(string message)
        {
            UiKit.SetText(_typed, _keypad.FormatDisplay());
            UiKit.SetText(_keypadMessage, message);
            _joinTyped.SetInteractable(_keypad.IsComplete);
        }
    }
}

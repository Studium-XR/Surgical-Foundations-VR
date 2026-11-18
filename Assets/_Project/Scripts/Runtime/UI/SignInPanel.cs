using System.Threading;
using SurgicalFoundations.Audio;
using SurgicalFoundations.Backend;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SurgicalFoundations.UI
{
    /// <summary>
    /// Screen 01 (FR-01). Shows a live QR code and user code for the phone sign-in, or a keypad for the LMS session
    /// code, and moves on to the lobby as soon as the headset is signed in. A returning learner skips it entirely.
    /// </summary>
    public class SignInPanel : MonoBehaviour
    {
        [SerializeField] UITheme theme;
        [Header("Phone sign-in")]
        [SerializeField] GameObject deviceCards;
        [SerializeField] RawImage qrImage;
        [SerializeField] GameObject qrPlaceholder;
        [SerializeField] TMP_Text codeLabel;
        [SerializeField] TMP_Text urlLabel;
        [SerializeField] TMP_Text hintLabel;
        [Header("LMS session code")]
        [SerializeField] GameObject launchCodeCard;
        [SerializeField] TMP_Text launchCodeLabel;
        [SerializeField] TMP_Text launchMessage;
        [Header("Footer")]
        [SerializeField] GameObject mainButtons;
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] Image statusDot;

        const int LaunchCodeLength = 6;
        CancellationTokenSource lifetime;
        string launchCode = "";
        string qrFor;
        bool advanced;
        bool submitting;

        static BackendServices Services => BackendServices.Instance;

        void OnEnable()
        {
            advanced = false;
            lifetime = new CancellationTokenSource();
            ShowDeviceSignIn();
            if (Services == null) { SetStatus("Sign-in is unavailable in this build. You can still train as a guest.", false); return; }
            Services.Auth.Changed += Refresh;
            // A learner who signed in before (even offline now) goes straight to the lobby.
            if (Services.Auth.HasAccount || Services.Auth.IsGuest) { Advance(); return; }
            Services.Auth.StartDeviceSignIn(lifetime.Token);
            Refresh();
        }

        void OnDisable()
        {
            if (Services != null)
            {
                Services.Auth.Changed -= Refresh;
                Services.Auth.CancelDeviceSignIn();
            }
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
        }

        void Refresh()
        {
            if (Services == null || !isActiveAndEnabled) return;
            var auth = Services.Auth;
            if (auth.State == AuthState.SignedIn) { SetStatus($"Signed in as {auth.DisplayName}", true); Advance(); return; }

            var code = auth.PendingCode;
            if (auth.State == AuthState.WaitingForPhone && code != null)
            {
                if (codeLabel != null) codeLabel.text = LargeMono(code.userCode);
                if (urlLabel != null) urlLabel.text = UrlText(code.verificationUri);
                if (hintLabel != null) hintLabel.text = $"Code refreshes every {Mathf.Max(1, code.expiresIn / 60)} minutes";
                LoadQr(code.userCode);
                SetStatus("Waiting for you to approve on your phone…", true);
            }
            else if (auth.State == AuthState.RequestingCode)
            {
                if (codeLabel != null) codeLabel.text = LargeMono("····-····");
                SetStatus("Connecting…", true);
            }
            else if (!string.IsNullOrEmpty(auth.LastError))
            {
                SetStatus(auth.LastError + " You can still train as a guest; guest results aren't saved.", false);
            }
        }

        async void LoadQr(string userCode)
        {
            if (qrImage == null || qrFor == userCode) return;
            qrFor = userCode;
            var texture = await Services.Api.GetTextureAsync("/auth/device/qr?code=" + userCode, lifetime?.Token ?? default);
            if (texture == null || qrFor != userCode || qrImage == null) return;
            texture.filterMode = FilterMode.Point; // crisp modules at any viewing distance
            if (qrImage.texture is Texture2D old && old != null) Destroy(old);
            qrImage.texture = texture;
            qrImage.enabled = true;
            if (qrPlaceholder != null) qrPlaceholder.SetActive(false);
        }

        // ───────────── Buttons (wired by UIScreensBuilder) ─────────────

        public void ShowLaunchCodeEntry()
        {
            launchCode = "";
            UpdateLaunchCode(null);
            SetMode(launchCode: true);
        }

        public void ShowDeviceSignIn() => SetMode(launchCode: false);

        public void KeypadPress(string character)
        {
            if (submitting || launchCode.Length >= LaunchCodeLength) return;
            launchCode += character;
            UpdateLaunchCode(null);
        }

        public void KeypadBackspace()
        {
            if (submitting || launchCode.Length == 0) return;
            launchCode = launchCode.Substring(0, launchCode.Length - 1);
            UpdateLaunchCode(null);
        }

        public async void SubmitLaunchCode()
        {
            if (Services == null || submitting) return;
            if (launchCode.Length != LaunchCodeLength) { UpdateLaunchCode("Enter all six characters shown in your learning platform."); return; }
            submitting = true;
            UpdateLaunchCode("Checking…");
            var (ok, message) = await Services.Auth.SignInWithLaunchCodeAsync(launchCode, lifetime?.Token ?? default);
            submitting = false;
            if (ok) return; // Refresh() advances on the state change
            AudioManager.Instance?.Play(SoundId.UI_Denied);
            UpdateLaunchCode(message);
        }

        public void ContinueAsGuest()
        {
            Services?.Auth.ContinueAsGuest();
            Advance();
        }

        // ───────────── helpers ─────────────

        void SetMode(bool launchCode)
        {
            if (deviceCards != null) deviceCards.SetActive(!launchCode);
            if (launchCodeCard != null) launchCodeCard.SetActive(launchCode);
            if (mainButtons != null) mainButtons.SetActive(!launchCode);
        }

        void UpdateLaunchCode(string message)
        {
            if (launchCodeLabel != null)
            {
                var shown = launchCode.PadRight(LaunchCodeLength, '_');
                launchCodeLabel.text = LargeMono(shown.Substring(0, 3) + "-" + shown.Substring(3));
            }
            if (launchMessage != null) launchMessage.text = message ?? "Shown in your learning platform when you open this activity.";
        }

        void SetStatus(string text, bool ok)
        {
            if (statusLabel != null) statusLabel.text = text;
            if (statusDot != null && theme != null) statusDot.color = ok ? theme.accent : theme.warning;
        }

        void Advance()
        {
            if (advanced) return;
            var sequence = GetComponentInParent<StepSequence>();
            if (sequence == null) return;
            advanced = true;
            // The step after this panel, whichever step is showing: on first launch with a saved account this runs from
            // OnEnable before the sequence has shown anything, and Next() would have shown the sign-in step again.
            sequence.ShowAfter(gameObject);
        }

        // Same monospacing the builder gives these labels; setting .text replaces the builder's tags.
        static string Mono(string s) => $"<mspace=0.62em>{s}</mspace>";
        static string LargeMono(string s) => $"<mspace=0.78em>{s}</mspace>";

        /// <summary>
        /// The learner has to type this, so it must never be cut off. Long hosts get the path on a second line instead of
        /// shrinking the text below readable size (the label shrinks to fit each line as well).
        /// </summary>
        static string UrlText(string url)
        {
            var s = StripScheme(url);
            var slash = s.IndexOf('/');
            if (s.Length > 32 && slash > 0) s = s.Substring(0, slash) + "\n" + s.Substring(slash);
            return Mono(s);
        }

        static string StripScheme(string url) =>
            string.IsNullOrEmpty(url) ? "" : url.Replace("https://", "").Replace("http://", "");
    }
}

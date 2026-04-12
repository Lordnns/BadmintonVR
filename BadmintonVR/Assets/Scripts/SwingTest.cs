// ============================================================
//  SwingTest.cs  —  DELETE ME WHEN DONE
//
//  Drop on any GameObject that has a SwingCoordinator.
//  Bind two XRI actions (e.g. right trigger, right grip).
//  Set swingName to the reference you want to test against.
//  Hit play, press launch button, swing, press land button.
//  Score + trim info shows in console + feedback label.
//  Press launch again to replay.
// ============================================================

using UnityEngine;
using UnityEngine.InputSystem;

namespace BadmintonPoseTracking
{
    [RequireComponent(typeof(SwingCoordinator))]
    public sealed class SwingTest : MonoBehaviour
    {
        [Header("Swing to test against")]
        public string swingName = "smash_overhead";

        [Header("XRI Input Actions")]
        [Tooltip("Press to simulate launcher fire + start recording.\n" +
                 "e.g. XRI RightHand / Activate Value")]
        public InputActionReference launchAction;

        [Tooltip("Press to simulate shuttlecock landing + stop recording.\n" +
                 "e.g. XRI RightHand / Select Value")]
        public InputActionReference landAction;

        [Tooltip("Press to toggle replay on/off.\n" +
                 "e.g. XRI RightHand / Primary Button")]
        public InputActionReference replayAction;

        [Header("Replay")]
        [Tooltip("Toggle in Inspector to show/hide replay.")]
        public bool showReplay;

        private SwingCoordinator _coord;
        private bool _replayVisible;

        private void Awake()
        {
            _coord = GetComponent<SwingCoordinator>();
        }

        private void OnEnable()
        {
            if (launchAction != null)
            {
                launchAction.action.Enable();
                launchAction.action.performed += OnLaunch;
            }

            if (landAction != null)
            {
                landAction.action.Enable();
                landAction.action.performed += OnLand;
            }

            if (replayAction != null)
            {
                replayAction.action.Enable();
                replayAction.action.performed += OnReplayToggle;
            }

            _coord.OnSwingScored += LogScore;
        }

        private void OnDisable()
        {
            if (launchAction != null)
                launchAction.action.performed -= OnLaunch;

            if (landAction != null)
                landAction.action.performed -= OnLand;

            if (replayAction != null)
                replayAction.action.performed -= OnReplayToggle;

            _coord.OnSwingScored -= LogScore;
        }

        private void OnLaunch(InputAction.CallbackContext ctx)
        {
            if (_coord.IsRecording)
            {
                Debug.Log("[SwingTest] Already recording — ignoring launch press.");
                return;
            }

            Debug.Log($"[SwingTest] ▶ Launch! Recording against '{swingName}'...");
            _coord.OnLaunch(swingName);
        }

        private void OnLand(InputAction.CallbackContext ctx)
        {
            if (!_coord.IsGameplayRecording)
            {
                Debug.Log("[SwingTest] Not recording — ignoring land press.");
                return;
            }

            Debug.Log("[SwingTest] ■ Landed! Stopping + scoring...");
            _coord.OnShuttlecockLanded();
        }

        private void OnReplayToggle(InputAction.CallbackContext ctx)
        {
            showReplay = !showReplay;
            Debug.Log($"[SwingTest] Replay toggled → {(showReplay ? "ON" : "OFF")}");
        }

        private void Update()
        {
            if (showReplay == _replayVisible) return;
            _replayVisible = showReplay;

            if (!_coord.HasReplay)
            {
                Debug.Log("[SwingTest] No replay available yet — do a swing first.");
                showReplay = false;
                _replayVisible = false;
                return;
            }

            if (_replayVisible)
            {
                Debug.Log("[SwingTest] Showing replay...");
                _coord.ShowReplay();
            }
            else
            {
                Debug.Log("[SwingTest] Hiding replay.");
                _coord.HideReplay();
            }
        }

        private void LogScore(SwingScore score)
        {
            string trim = score.OriginalFrameCount > 0
                ? $"  Trimmed: {score.OriginalFrameCount} → {score.TrimmedFrameCount} frames"
                : "  (no trim needed)";

            Debug.Log($"[SwingTest] ══════════════════════════════════════\n" +
                      $"  Score:  {score.Score:F1} / 100\n" +
                      $"  Cost:   {score.NormalisedCost:F3}\n" +
                      $"  Weak:   [{string.Join(", ", score.WeakJoints)}]\n" +
                      $"  {trim}\n" +
                      $"══════════════════════════════════════");
        }
    }
}
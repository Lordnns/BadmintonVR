// ============================================================
//  SwingCoordinator.cs
//
//  Orchestrates recording, trimming, scoring, and replay.
//
//  ── DEV MODE (recording reference swings) ───────────────────
//
//    coordinator.StartDevRecording("smash_overhead");
//    coordinator.StopDevRecording();
//    // → saves to StreamingAssets/Swings/smash_overhead.json
//
//  ── GAMEPLAY MODE (launcher-driven) ─────────────────────────
//
//    Shuttlecock launcher fires:
//      coordinator.OnLaunch("smash_overhead");
//      // → recording starts immediately
//
//    Shuttlecock lands (collision / trigger / timeout):
//      coordinator.OnShuttlecockLanded();
//      // → stops recording
//      // → trims capture to the active swing window
//      // → scores trimmed capture against the reference JSON
//      // → fires OnSwingScored
//      // → holds both captures for optional replay
//
//    Show the replay (two skeletons — player vs reference):
//      coordinator.ShowReplay();
//      // → playerVisualizer plays the trimmed capture (green)
//      // → referenceVisualizer plays the reference JSON (blue)
//
//    Hide the replay:
//      coordinator.HideReplay();
//
//  ── INSPECTOR SETUP ─────────────────────────────────────────
//    PoseRecorder              : auto-found or drag in
//    playerVisualizer          : SwingReplayVisualizer (green)
//    referenceVisualizer       : SwingReplayVisualizer (blue)
//    feedbackLabel             : TMP_Text (optional)
// ============================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.Events;

namespace BadmintonPoseTracking
{
    [RequireComponent(typeof(PoseRecorder))]
    public sealed class SwingCoordinator : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────

        [Header("Components")]
        public PoseRecorder recorder;

        [Header("Replay Visualizers  (optional)")]
        [Tooltip("Skeleton that plays back the player's trimmed swing.\n" +
                 "Create a GameObject with SwingReplayVisualizer, set autoPlayOnStart=false.")]
        public SwingReplayVisualizer playerVisualizer;

        [Tooltip("Skeleton that plays back the reference swing from JSON.\n" +
                 "Create a second GameObject with SwingReplayVisualizer, set autoPlayOnStart=false.")]
        public SwingReplayVisualizer referenceVisualizer;

        [Header("Replay Colors")]
        public Color playerJointColor    = new Color(0.2f, 1f, 0.4f, 0.85f);    // green
        public Color playerBoneColor     = new Color(0.2f, 0.8f, 0.4f, 0.4f);
        public Color referenceJointColor = new Color(0.2f, 0.8f, 1f, 0.85f);    // blue
        public Color referenceBoneColor  = new Color(0.2f, 0.6f, 1f, 0.4f);

        [Header("Dev Recording")]
        [Tooltip("Name that will be saved to disk when dev-recording.")]
        public string devSwingName = "swing_01";

        [Header("Matching")]
        [Tooltip("Sakoe-Chiba band. 0.20 = ±20% timing tolerance.")]
        [Range(0.05f, 0.50f)]
        public float dtwBandFraction = 0.20f;

        [Tooltip("How forgiving the scorer is.  Higher = easier.  Degrees scale.")]
        [Range(5f, 90f)]
        public float matchSensitivity = 45f;

        [Tooltip("Automatically trim idle frames from the player capture.\n" +
                 "Essential for launcher mode — the recording spans launch→land\n" +
                 "but only the actual swing matters for scoring.")]
        public bool autoTrimCapture = true;

        [Tooltip("Score threshold for a 'pass'.  0–100.")]
        [Range(40f, 90f)]
        public float passThreshold = 60f;

        [Header("Launcher Settings")]
        [Tooltip("Max recording duration (seconds).  Safety cap so a missed\n" +
                 "OnShuttlecockLanded() call doesn't record forever.")]
        [Range(2f, 15f)]
        public float maxRecordingDuration = 8f;

        [Header("UI")]
        public TMP_Text feedbackLabel;
        public float    feedbackDuration = 3f;

        [Header("Input Actions  (optional — also callable from script)")]
        public InputActionReference devStartAction;
        public InputActionReference devStopAction;

        // ── C# Events ─────────────────────────────────────────────────────

        /// <summary>Fired after a gameplay capture is trimmed and scored.</summary>
        public event Action<SwingScore> OnSwingScored;

        /// <summary>Fired when a dev recording is saved.</summary>
        public event Action<string> OnSwingSaved;

        /// <summary>
        /// Fired when a replay is ready.  Carries (playerCapture, referenceCapture).
        /// Listen to this if you want custom replay handling instead of the built-in visualizers.
        /// </summary>
        public event Action<PoseCapture, PoseCapture> OnReplayReady;

        // ── Unity Events (Inspector-bindable) ─────────────────────────────

        [Header("Unity Events")]
        public UnityEvent<float>  onSwingScored;   // float = 0–100
        public UnityEvent<string> onSwingSaved;

        // ── Private state ──────────────────────────────────────────────────

        private string _activeSwingName;
        private enum Mode { Idle, DevRecording, GameplayRecording }
        private Mode _mode = Mode.Idle;

        private SwingDatabase _database;
        private SwingMatcher  _matcher;
        private Coroutine     _feedbackCoroutine;
        private Coroutine     _timeoutCoroutine;

        // Last scored round — available for replay
        private PoseCapture _lastPlayerCapture;
        private PoseCapture _lastReferenceCapture;
        private SwingScore  _lastScore;

        // ── Unity lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            if (recorder == null) recorder = GetComponent<PoseRecorder>();

            _database = new SwingDatabase();
            _matcher  = new SwingMatcher
            {
                BandFraction = dtwBandFraction,
                Sensitivity  = matchSensitivity,
                AutoTrim     = autoTrimCapture
            };

            _database.LoadAll();
        }

        private void OnEnable()
        {
            Bind(devStartAction, StartDevRecording);
            Bind(devStopAction,  StopDevRecording);
        }

        private void OnDisable()
        {
            Unbind(devStartAction, StartDevRecording);
            Unbind(devStopAction,  StopDevRecording);

            if (recorder.IsRecording) recorder.StopRecording();
            CancelTimeout();
        }

        private void OnValidate()
        {
            if (_matcher != null)
            {
                _matcher.BandFraction = dtwBandFraction;
                _matcher.Sensitivity  = matchSensitivity;
                _matcher.AutoTrim     = autoTrimCapture;
            }
        }

        // =================================================================
        //  DEV API
        // =================================================================

        public void StartDevRecording()           => StartDevRecording(null);

        public void StartDevRecording(string swingName)
        {
            if (_mode != Mode.Idle)
            {
                Debug.LogWarning("[SwingCoordinator] Already recording.");
                return;
            }

            if (!string.IsNullOrEmpty(swingName)) devSwingName = swingName;

            _mode = Mode.DevRecording;
            recorder.StartRecording(estimatedFrames: EstimatedFrames());
            ShowFeedback($"● Recording reference: {devSwingName}", Color.red);
            Debug.Log($"[SwingCoordinator] Dev recording started for '{devSwingName}'.");
        }

        public void StopDevRecording()
        {
            if (_mode != Mode.DevRecording)
            {
                Debug.LogWarning("[SwingCoordinator] Not in dev recording mode.");
                return;
            }

            PoseCapture capture = recorder.StopRecording();
            _mode = Mode.Idle;

            if (capture == null || capture.FrameCount == 0)
            {
                ShowFeedback("Dev recording failed — no frames captured.", Color.red);
                return;
            }

            _database.Save(devSwingName, capture);

            string msg = $"Saved '{devSwingName}'  {capture.FrameCount} frames  " +
                         $"{capture.DurationSeconds:F2}s";
            ShowFeedback(msg, Color.green);

            OnSwingSaved?.Invoke(devSwingName);
            onSwingSaved?.Invoke(devSwingName);
            Debug.Log($"[SwingCoordinator] Dev recording complete: {msg}");
        }

        // =================================================================
        //  GAMEPLAY API — launcher-driven
        // =================================================================

        /// <summary>
        /// Call when the shuttlecock launcher fires.
        /// Starts recording immediately.  The recording runs until
        /// OnShuttlecockLanded() is called (or the safety timeout fires).
        /// </summary>
        /// <param name="referenceSwingName">
        /// Which reference to compare against (e.g. "smash_overhead").
        /// </param>
        public void OnLaunch(string referenceSwingName)
        {
            if (_mode != Mode.Idle)
            {
                Debug.LogWarning("[SwingCoordinator] Already recording — ignoring launch.");
                return;
            }

            _activeSwingName = referenceSwingName;

            // Verify the reference exists before we start recording
            if (!_database.Exists(referenceSwingName))
            {
                Debug.LogError($"[SwingCoordinator] Reference '{referenceSwingName}' not found. " +
                               "Record it in dev mode first.");
                ShowFeedback($"No reference for '{referenceSwingName}'.", Color.red);
                return;
            }

            _mode = Mode.GameplayRecording;
            recorder.StartRecording(estimatedFrames: EstimatedFrames());

            // Safety timeout — auto-stop if OnShuttlecockLanded is never called
            CancelTimeout();
            _timeoutCoroutine = StartCoroutine(RecordingTimeout());

            Debug.Log($"[SwingCoordinator] Launch! Recording started. " +
                      $"Will compare against '{_activeSwingName}'.");
        }

        /// <summary>
        /// Call when the shuttlecock hits the ground / goes out.
        /// Stops recording → trims → scores → fires events → holds replay data.
        /// </summary>
        public void OnShuttlecockLanded()
        {
            if (_mode != Mode.GameplayRecording)
            {
                Debug.LogWarning("[SwingCoordinator] Not recording — ignoring land event.");
                return;
            }

            CancelTimeout();
            ProcessGameplayCapture();
        }

        // ── Legacy convenience (still works if you prefer manual start/stop) ──

        public void StartGameplayRecording(string refName) => OnLaunch(refName);
        public void StopGameplayRecording()                => OnShuttlecockLanded();

        // =================================================================
        //  REPLAY
        // =================================================================

        /// <summary>
        /// Plays the named reference swing on the reference visualizer only.
        /// Call this before a launch to preview the target move to the player.
        /// </summary>
        public void ShowReferencePreview(string swingName)
        {
            if (referenceVisualizer == null) return;

            PoseCapture reference = _database.Load(swingName);
            if (reference == null)
            {
                Debug.LogWarning($"[SwingCoordinator] ShowReferencePreview: '{swingName}' not found.");
                return;
            }

            referenceVisualizer.SetColors(referenceJointColor, referenceBoneColor);
            referenceVisualizer.DisplayName = swingName;
            referenceVisualizer.PlayCapture(reference);
            Debug.Log($"[SwingCoordinator] Showing reference preview: '{swingName}'");
        }

        /// <summary>Stops the reference visualizer skeleton.</summary>
        public void HideReferencePreview()
        {
            if (referenceVisualizer != null) referenceVisualizer.Stop();
        }

        /// <summary>
        /// Plays only the player's last trimmed capture (no reference skeleton).
        /// Useful after scoring to let the player review their own form alone.
        /// </summary>
        public void ShowPlayerReplay()
        {
            if (_lastPlayerCapture == null)
            {
                Debug.LogWarning("[SwingCoordinator] No player capture to replay yet.");
                return;
            }

            if (playerVisualizer != null)
            {
                playerVisualizer.SetColors(playerJointColor, playerBoneColor);
                playerVisualizer.DisplayName = _activeSwingName;
                playerVisualizer.PlayCapture(_lastPlayerCapture);
            }
        }

        /// <summary>Stops the player replay skeleton.</summary>
        public void HidePlayerReplay()
        {
            if (playerVisualizer != null) playerVisualizer.Stop();
        }

        /// <summary>
        /// Plays back the last scored swing overlaid with the reference.
        /// Requires playerVisualizer and referenceVisualizer to be assigned.
        /// </summary>
        public void ShowReplay()
        {
            if (_lastPlayerCapture == null || _lastReferenceCapture == null)
            {
                Debug.LogWarning("[SwingCoordinator] No scored swing to replay.");
                return;
            }

            if (playerVisualizer != null)
            {
                playerVisualizer.SetColors(playerJointColor, playerBoneColor);
                playerVisualizer.DisplayName = $"{_activeSwingName} (you)";
                playerVisualizer.PlayCapture(_lastPlayerCapture);
            }

            if (referenceVisualizer != null)
            {
                referenceVisualizer.SetColors(referenceJointColor, referenceBoneColor);
                referenceVisualizer.DisplayName = $"{_activeSwingName} (reference)";
                referenceVisualizer.PlayCapture(_lastReferenceCapture);
            }

            Debug.Log("[SwingCoordinator] Replay started " +
                      $"(player: {_lastPlayerCapture.FrameCount}f, " +
                      $"ref: {_lastReferenceCapture.FrameCount}f).");
        }

        /// <summary>Stop both replay skeletons.</summary>
        public void HideReplay()
        {
            if (playerVisualizer    != null) playerVisualizer.Stop();
            if (referenceVisualizer != null) referenceVisualizer.Stop();
        }

        /// <summary>True if there is a scored swing available for replay.</summary>
        public bool HasReplay => _lastPlayerCapture != null && _lastReferenceCapture != null;

        /// <summary>Last scored result (null before the first gameplay round).</summary>
        public SwingScore LastScore => _lastScore;

        // =================================================================
        //  INTERNAL — scoring pipeline
        // =================================================================

        private void ProcessGameplayCapture()
        {
            PoseCapture rawCapture = recorder.StopRecording();
            _mode = Mode.Idle;

            if (rawCapture == null || rawCapture.FrameCount == 0)
            {
                ShowFeedback("No swing detected.", Color.gray);
                return;
            }

            // Load reference (O(1) if cached)
            PoseCapture reference = _database.Load(_activeSwingName);
            if (reference == null)
            {
                Debug.LogError($"[SwingCoordinator] Reference '{_activeSwingName}' disappeared!");
                return;
            }

            // ── Trim + Score ───────────────────────────────────────────────
            //
            //    We trim here so we can hold the trimmed capture for replay.
            //    Then we tell the matcher to skip its own trim pass.

            PoseCapture trimmed = autoTrimCapture
                ? SwingTrimmer.Trim(rawCapture, reference.DurationSeconds,
                                    reference.CaptureRateFps)
                : rawCapture;

            // Temporarily disable matcher's own trim — we already did it
            bool savedAutoTrim = _matcher.AutoTrim;
            _matcher.AutoTrim = false;
            SwingScore score = _matcher.Compare(trimmed, reference);
            _matcher.AutoTrim = savedAutoTrim;

            // Populate trim metadata
            if (trimmed != rawCapture)
            {
                score.OriginalFrameCount = rawCapture.FrameCount;
                score.TrimmedFrameCount  = trimmed.FrameCount;
            }

            // ── Hold for replay ────────────────────────────────────────────

            _lastPlayerCapture    = trimmed;
            _lastReferenceCapture = reference;
            _lastScore            = score;

            // ── Fire events ────────────────────────────────────────────────
            
            OnSwingScored?.Invoke(score);
            onSwingScored?.Invoke(score.Score);
            OnReplayReady?.Invoke(trimmed, reference);

            // ── Feedback ───────────────────────────────────────────────────

            bool   pass  = score.Passes(passThreshold);
            Color  color = pass ? Color.green : Color.yellow;
            string weak  = score.WeakJoints.Length > 0
                ? $"\nFix: {string.Join(", ", score.WeakJoints)}"
                : string.Empty;
            string trim  = score.OriginalFrameCount > 0
                ? $"  (trimmed {score.OriginalFrameCount}→{score.TrimmedFrameCount}f)"
                : string.Empty;

            ShowFeedback($"{score.Score:F0} / 100{weak}", color);
            Debug.Log($"[SwingCoordinator] {score}{trim}");

            // rawCapture falls out of scope — GC eligible
        }

        // =================================================================
        //  PROPERTIES
        // =================================================================

        public bool IsDevRecording      => _mode == Mode.DevRecording;
        public bool IsGameplayRecording => _mode == Mode.GameplayRecording;
        public bool IsRecording         => _mode != Mode.Idle;
        public SwingDatabase Database   => _database;

        // =================================================================
        //  UI
        // =================================================================

        private void ShowFeedback(string message, Color color)
        {
            if (feedbackLabel == null) return;
            feedbackLabel.text  = message;
            feedbackLabel.color = color;

            if (_feedbackCoroutine != null) StopCoroutine(_feedbackCoroutine);
            _feedbackCoroutine = StartCoroutine(FeedbackFade());
        }

        private IEnumerator FeedbackFade()
        {
            yield return new WaitForSeconds(feedbackDuration - 0.4f);
            float t = 0f;
            Color c = feedbackLabel.color;
            while (t < 0.4f)
            {
                t += Time.deltaTime;
                c.a = Mathf.Lerp(1f, 0f, t / 0.4f);
                feedbackLabel.color = c;
                yield return null;
            }
            feedbackLabel.text = string.Empty;
        }

        // =================================================================
        //  HELPERS
        // =================================================================

        private int EstimatedFrames()
        {
            return Mathf.CeilToInt(recorder.captureRateFps * maxRecordingDuration);
        }

        private IEnumerator RecordingTimeout()
        {
            yield return new WaitForSeconds(maxRecordingDuration);
            if (_mode == Mode.GameplayRecording)
            {
                Debug.LogWarning("[SwingCoordinator] Recording timeout — auto-stopping.");
                ProcessGameplayCapture();
            }
        }

        private void CancelTimeout()
        {
            if (_timeoutCoroutine != null)
            {
                StopCoroutine(_timeoutCoroutine);
                _timeoutCoroutine = null;
            }
        }

        private static void Bind(InputActionReference r, Action cb)
        {
            if (r == null) return;
            r.action.Enable();
            r.action.performed += _ => cb();
        }

        private static void Unbind(InputActionReference r, Action cb)
        {
            if (r == null) return;
            r.action.performed -= _ => cb();
        }
    }
}
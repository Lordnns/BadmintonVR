// ============================================================
//  SwingCoordinator.cs
//
//  Orchestrates the two recording modes using PoseRecorder,
//  SwingDatabase, and SwingMatcher.
//
//  ── DEV MODE (recording reference swings) ───────────────────
//
//    Call via Inspector button or script:
//      coordinator.StartDevRecording("smash_overhead");
//      coordinator.StopDevRecording();
//      // → saves to StreamingAssets/Swings/smash_overhead.json
//
//  ── GAMEPLAY MODE (comparing player swings) ─────────────────
//
//    Fire and forget — the result comes back via OnSwingScored:
//      coordinator.StartGameplayRecording("smash_overhead");
//      coordinator.StopGameplayRecording();
//      // → SwingScore fires on OnSwingScored, capture is discarded
//
//  Both modes use exactly the same PoseRecorder underneath.
//  The only difference is what happens with the PoseCapture
//  after StopRecording() returns.
//
//  ── INSPECTOR BINDINGS ──────────────────────────────────────
//    - PoseRecorder     : drag in
//    - FeedbackLabel    : TMP_Text for on-screen feedback (optional)
//    - devSwingName     : name to save when dev-recording
//    - targetSwingName  : reference to compare against in gameplay
//    - OnSwingScored    : UnityEvent<float> or use the C# event
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

        [Header("Dev Recording")]
        [Tooltip("Name that will be saved to disk when dev-recording.")]
        public string devSwingName = "swing_01";

        [Header("Gameplay — Swing Slots")]
        [Tooltip("JSON name for the first swing type (e.g. smash).")]
        public string swing1Name = "smash";

        [Tooltip("JSON name for the second swing type (e.g. net_shot).")]
        public string swing2Name = "net_shot";

        [Header("Matching")]
        [Tooltip("Sakoe-Chiba band. 0.20 = ±20% timing tolerance.")]
        [Range(0.05f, 0.50f)]
        public float dtwBandFraction = 0.20f;

        [Tooltip("How forgiving the scorer is.  Higher = easier.  Degrees scale.")]
        [Range(15f, 60f)]
        public float matchSensitivity = 30f;

        [Tooltip("Score threshold for a 'pass'.  0–100.")]
        [Range(40f, 90f)]
        public float passThreshold = 60f;

        [Header("UI")]
        public TMP_Text feedbackLabel;
        public float    feedbackDuration = 3f;

        [Header("Input Actions  (optional — also callable from script)")]
        public InputActionReference devStartAction;
        public InputActionReference devStopAction;
        public InputActionReference gameplayStartAction;
        public InputActionReference gameplayStopAction;

        // ── C# Events ─────────────────────────────────────────────────────

        /// <summary>Fired after a gameplay recording is scored.  Carries the full SwingScore.</summary>
        public event Action<SwingScore> OnSwingScored;

        /// <summary>Fired when a dev recording is successfully saved.</summary>
        public event Action<string>     OnSwingSaved;

        // ── Unity Events (Inspector-bindable) ─────────────────────────────

        [Header("Unity Events")]
        public UnityEvent<float>  onSwingScored;   // float = 0–100 score
        public UnityEvent<string> onSwingSaved;    // string = swing name

        // ── Private state ──────────────────────────────────────────────────

        private string    _activeSwingName;
        private enum Mode { Idle, DevRecording, GameplayRecording }
        private Mode _mode = Mode.Idle;

        private SwingDatabase _database;
        private SwingMatcher  _matcher;
        private Coroutine     _feedbackCoroutine;

        // ── Unity lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            if (recorder == null) recorder = GetComponent<PoseRecorder>();

            _database = new SwingDatabase();
            _matcher  = new SwingMatcher
            {
                BandFraction = dtwBandFraction,
                Sensitivity  = matchSensitivity
            };

            // Pre-load all reference swings from disk into memory
            _database.LoadAll();
        }

        private void OnEnable()
        {
            Bind(devStartAction,      StartDevRecording);
            Bind(devStopAction,       StopDevRecording);
            Bind(gameplayStartAction, StartGameplayRecording);
            Bind(gameplayStopAction,  StopGameplayRecording);
        }

        private void OnDisable()
        {
            Unbind(devStartAction,      StartDevRecording);
            Unbind(devStopAction,       StopDevRecording);
            Unbind(gameplayStartAction, StartGameplayRecording);
            Unbind(gameplayStopAction,  StopGameplayRecording);

            if (recorder.IsRecording) recorder.StopRecording();
        }

        private void OnValidate()
        {
            // Keep matcher in sync when values are tweaked in Inspector at runtime
            if (_matcher != null)
            {
                _matcher.BandFraction = dtwBandFraction;
                _matcher.Sensitivity  = matchSensitivity;
            }
        }

        // ── DEV API ───────────────────────────────────────────────────────

        /// <summary>
        /// Start recording a reference swing using the current devSwingName.
        /// Parameterless so it can be bound to a Button or InputActionReference directly.
        /// </summary>
        public void StartDevRecording()
        {
            StartDevRecording(null);
        }

        /// <summary>
        /// Start recording a reference swing with an explicit name.
        /// The capture will be saved to disk as &lt;swingName&gt;.json when stopped.
        /// </summary>
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

        /// <summary>Stop dev recording and save the result to disk.</summary>
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

        // ── GAMEPLAY API ──────────────────────────────────────────────────

        /// <summary>
        /// Start recording the player's swing using the current targetSwingName.
        /// Parameterless so it can be bound to a Button or InputActionReference directly.
        /// </summary>
        public void StartGameplayRecording()
        {
            StartGameplayRecording(null);
        }

        /// <summary>
        /// Start recording the player's swing for comparison against an explicit reference.
        /// </summary>
        public void StartGameplayRecording(string referenceSwingName)
        {
            if (_mode != Mode.Idle)
            {
                Debug.LogWarning("[SwingCoordinator] Already recording.");
                return;
            }

            if (!string.IsNullOrEmpty(referenceSwingName))
                _activeSwingName = referenceSwingName;

            _mode = Mode.GameplayRecording;
            recorder.StartRecording(estimatedFrames: EstimatedFrames());
            Debug.Log($"[SwingCoordinator] Gameplay recording started. Will compare against '{_activeSwingName}'.");
        }

        /// <summary>
        /// Stop gameplay recording, compare against the reference, fire OnSwingScored.
        /// The player's capture is NOT saved — it is discarded after scoring.
        /// </summary>
        public void StopGameplayRecording()
        {
            if (_mode != Mode.GameplayRecording)
            {
                Debug.LogWarning("[SwingCoordinator] Not in gameplay recording mode.");
                return;
            }

            PoseCapture playerCapture = recorder.StopRecording();
            _mode = Mode.Idle;

            if (playerCapture == null || playerCapture.FrameCount == 0)
            {
                ShowFeedback("No swing detected.", Color.gray);
                return;
            }

            // Load reference (O(1) if already cached)
            PoseCapture reference = _database.Load(_activeSwingName);
            if (reference == null)
            {
                Debug.LogError($"[SwingCoordinator] Reference '{_activeSwingName}' not found. " +
                               "Record it in dev mode first.");
                ShowFeedback($"No reference for '{_activeSwingName}'.", Color.red);
                return;
            }

            // DTW comparison — playerCapture is used here, then falls out of scope
            SwingScore score = _matcher.Compare(playerCapture, reference);

            // Fire events
            OnSwingScored?.Invoke(score);
            onSwingScored?.Invoke(score.Score);

            // Show feedback
            bool   pass    = score.Passes(passThreshold);
            Color  color   = pass ? Color.green : Color.yellow;
            string weak    = score.WeakJoints.Length > 0
                ? $"\nFix: {string.Join(", ", score.WeakJoints)}"
                : string.Empty;
            ShowFeedback($"{score.Score:F0} / 100{weak}", color);

            Debug.Log($"[SwingCoordinator] {score}");

            // playerCapture is now eligible for GC — no references held
        }

        // ── Swing slot triggers ───────────────────────────────────────────
        //    Call these from game logic to start recording against a preset slot.

        /// <summary>Start gameplay recording against swing1Name (e.g. "smash").</summary>
        public void StartSwing1() => StartGameplayRecording(swing1Name);

        /// <summary>Start gameplay recording against swing2Name (e.g. "net_shot").</summary>
        public void StartSwing2() => StartGameplayRecording(swing2Name);

        // ── Convenience properties ─────────────────────────────────────────

        public bool IsDevRecording      => _mode == Mode.DevRecording;
        public bool IsGameplayRecording => _mode == Mode.GameplayRecording;
        public bool IsRecording         => _mode != Mode.Idle;

        /// <summary>All reference swing names currently loaded into memory.</summary>
        public SwingDatabase Database => _database;

        // ── UI ────────────────────────────────────────────────────────────

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

        // ── Helpers ───────────────────────────────────────────────────────

        private int EstimatedFrames()
        {
            // Pre-size the frame list to avoid List<> internal resizing.
            // Assume max 5-second swing at the configured capture rate.
            return Mathf.CeilToInt(recorder.captureRateFps * 5f);
        }

        private static void Bind(InputActionReference r, System.Action cb)
        {
            if (r == null) return;
            r.action.Enable();
            r.action.performed += _ => cb();
        }

        private static void Unbind(InputActionReference r, System.Action cb)
        {
            if (r == null) return;
            r.action.performed -= _ => cb();
        }
    }
}
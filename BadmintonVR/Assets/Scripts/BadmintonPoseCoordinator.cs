// ============================================================
//  BadmintonPoseCoordinator.cs
//
//  Orchestrates recorder → databank → UI.
//
//  SEQUENCE CAPTURE FLOW
//  ─────────────────────
//  1. Press captureReferenceAction  →  3-second countdown shown
//  2. Countdown ends                →  recording starts automatically
//  3. Perform your movement
//  4. Press captureReferenceAction again  →  recording stops, sequence saved
//     OR wait for captureMaxDuration seconds for auto-stop
//
//  LIVE MATCHING
//  ─────────────
//  Every frame is added to a rolling buffer (liveBufferSeconds deep).
//  Every matchEveryNFrames frames, the last N frames (where N = the
//  reference sequence length) are compared against each saved sequence.
// ============================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace BadmintonPoseTracking
{
    [RequireComponent(typeof(BodyPoseRecorder))]
    [RequireComponent(typeof(PoseDatabank))]
    public class BadmintonPoseCoordinator : MonoBehaviour
    {
        // ──────────────────────────────────────────────────────────────────
        //  Inspector
        // ──────────────────────────────────────────────────────────────────

        [Header("Components (auto-found if left empty)")]
        public BodyPoseRecorder recorder;
        public PoseDatabank     databank;

        [Header("Input Actions  (XRI / New Input System)")]
        [Tooltip("e.g. XRI RightHand / Primary Button — starts recording.")]
        public InputActionReference startAction;

        [Tooltip("e.g. XRI RightHand / Secondary Button — stops recording.")]
        public InputActionReference stopAction;

        [Tooltip("Press once to start capture countdown, press again to stop recording.")]
        public InputActionReference captureReferenceAction;

        [Header("Live Matching")]
        [Tooltip("Match every N captured frames to reduce CPU load.  " +
                 "At 30 fps:  1 = every frame,  5 = 6 matches/sec.")]
        [Range(1, 30)]
        public int matchEveryNFrames = 5;

        [Tooltip("Only show feedback when the match score exceeds this value.")]
        [Range(0f, 1f)]
        public float feedbackMinScore = 0.40f;

        [Tooltip("How many seconds of live frames to keep in the rolling buffer. " +
                 "Should be >= your longest reference sequence.")]
        [Range(1f, 15f)]
        public float liveBufferSeconds = 5f;

        [Header("UI Feedback")]
        [Tooltip("World-space or overlay TextMeshPro label for live feedback.")]
        public TMP_Text feedbackLabel;

        [Tooltip("How long feedback text stays on screen before fading (seconds).")]
        public float feedbackDuration = 2.5f;

        [Header("Reference Capture Settings")]
        [Tooltip("Category tag written into new reference sequences.")]
        public string newReferenceCategory = "Uncategorised";

        [Tooltip("Minimum similarity threshold stored in a newly captured sequence.")]
        [Range(0.5f, 0.99f)]
        public float newReferenceThreshold = 0.75f;

        [Tooltip("Countdown in seconds shown before recording starts.")]
        [Range(1f, 5f)]
        public float captureCountdownSeconds = 3f;

        [Tooltip("Auto-stop capture after this many seconds. 0 = manual stop only.")]
        [Range(0f, 30f)]
        public float captureMaxDuration = 0f;

        // ──────────────────────────────────────────────────────────────────
        //  Private state
        // ──────────────────────────────────────────────────────────────────

        private int              _framesSinceLast;
        private Coroutine        _feedbackTimer;

        // Rolling buffer for live sequence matching
        private readonly List<PoseFrame> _liveBuffer = new List<PoseFrame>();

        // Capture state machine
        private enum CaptureState { Idle, Countdown, Recording }
        private CaptureState _captureState = CaptureState.Idle;
        private Coroutine    _captureCoroutine;

        // ──────────────────────────────────────────────────────────────────
        //  Unity lifecycle
        // ──────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (recorder == null) recorder = GetComponent<BodyPoseRecorder>();
            if (databank == null) databank = GetComponent<PoseDatabank>();
        }

        private void OnEnable()
        {
            BindAction(startAction,            StartSession);
            BindAction(stopAction,             StopSession);
            BindAction(captureReferenceAction, OnCaptureButtonPressed);

            recorder.onFrameCaptured    += OnFrameCaptured;
            recorder.onRecordingStarted += () => ShowFeedback("● Recording…", Color.red);
            recorder.onRecordingComplete += OnRecordingComplete;
        }

        private void OnDisable()
        {
            UnbindAction(startAction,            StartSession);
            UnbindAction(stopAction,             StopSession);
            UnbindAction(captureReferenceAction, OnCaptureButtonPressed);

            if (recorder != null)
            {
                recorder.onFrameCaptured     -= OnFrameCaptured;
                recorder.onRecordingComplete -= OnRecordingComplete;
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  Session control  (start/stop buttons — full session recording)
        // ──────────────────────────────────────────────────────────────────

        public void StartSession()
        {
            recorder.StartRecording("Player");
        }

        public void StopSession()
        {
            var session = recorder.StopRecording();
            if (session == null) return;

            string path = PoseSessionSerializer.Save(session);
            ShowFeedback($"Session saved\n{session.FrameCount} frames  " +
                         $"{session.DurationSeconds:F1}s", Color.green);
            Debug.Log($"[Coordinator] Session saved → {path}");
        }

        // ──────────────────────────────────────────────────────────────────
        //  Live frame handler
        // ──────────────────────────────────────────────────────────────────

        private void OnFrameCaptured(PoseFrame frame)
        {
            // ── Rolling buffer ─────────────────────────────────────────────
            _liveBuffer.Add(frame);

            // Trim the buffer to liveBufferSeconds
            int maxFrames = Mathf.CeilToInt(liveBufferSeconds * recorder.captureRateFps);
            while (_liveBuffer.Count > maxFrames)
                _liveBuffer.RemoveAt(0);

            // ── Throttled matching ─────────────────────────────────────────
            _framesSinceLast++;
            if (_framesSinceLast < matchEveryNFrames) return;
            _framesSinceLast = 0;

            // Prefer sequence matching if sequences exist
            if (databank.Sequences.Count > 0)
            {
                MatchSequences();
            }
            else if (databank.Poses.Count > 0)
            {
                // Fallback to legacy single-frame matching
                var result = databank.FindBestMatch(frame);
                if (result == null || result.similarityScore < feedbackMinScore) return;

                Color  color   = result.isMatch ? Color.green : Color.yellow;
                string message = result.isMatch
                    ? $"✓ {result.referenceName}\n{result.similarityScore:P0}"
                    : $"○ {result.referenceName}  {result.similarityScore:P0}\n" +
                      result.feedbackMessage;

                ShowFeedback(message, color);
            }
        }

        private void MatchSequences()
        {
            PoseMatchResult best = null;

            foreach (var seq in databank.Sequences)
            {
                // We need at least as many live frames as the reference has
                if (_liveBuffer.Count < seq.FrameCount) continue;

                // Take the most recent seq.FrameCount frames from the buffer
                int      start  = _liveBuffer.Count - seq.FrameCount;
                var      window = _liveBuffer.GetRange(start, seq.FrameCount);
                var      result = databank.FindBestSequenceMatch(window);

                if (result != null &&
                    (best == null || result.similarityScore > best.similarityScore))
                    best = result;
            }

            if (best == null || best.similarityScore < feedbackMinScore) return;

            Color  color   = best.isMatch ? Color.green : Color.yellow;
            string message = best.isMatch
                ? $"✓ {best.referenceName}\n{best.similarityScore:P0}"
                : $"○ {best.referenceName}  {best.similarityScore:P0}\n" +
                  best.feedbackMessage;

            ShowFeedback(message, color);
        }

        private void OnRecordingComplete(PoseRecording session)
        {
            Debug.Log($"[Coordinator] Recording complete — {session.FrameCount} frames.");

            // If FinishCaptureCo already handled this (captureReferenceAction flow), skip.
            // Otherwise save whatever was recorded as a sequence automatically.
            if (_captureState != CaptureState.Idle) return;  // FinishCaptureCo will handle it

            if (session.frames.Count == 0) return;

            // Don't save internal snapshots
            if (session.playerName.StartsWith("_")) return;

            string name = $"{newReferenceCategory}_{System.DateTime.Now:HHmmss}";

            var refSequence = new ReferencePoseSequence
            {
                poseName       = name,
                category       = newReferenceCategory,
                frames         = new List<PoseFrame>(session.frames),
                captureRateFps = recorder.captureRateFps,
                matchThreshold = newReferenceThreshold,
                feedbackHint   = $"Work on your {newReferenceCategory} form."
            };

            databank.AddSequence(refSequence);
            databank.SaveToDisk();

            ShowFeedback($"Sequence saved!\n'{name}'\n" +
                         $"{session.FrameCount} frames  {session.DurationSeconds:F1}s",
                Color.green);

            Debug.Log($"[Coordinator] Auto-saved sequence → '{name}'  {session.FrameCount} frames");
        }

        // ──────────────────────────────────────────────────────────────────
        //  Reference sequence capture
        //
        //  First press  → start countdown then auto-start recording
        //  Second press → stop recording and save sequence
        //  captureMaxDuration > 0 → auto-stop after that many seconds
        // ──────────────────────────────────────────────────────────────────

        private void OnCaptureButtonPressed()
        {
            switch (_captureState)
            {
                case CaptureState.Idle:
                    _captureCoroutine = StartCoroutine(CaptureCountdownCo());
                    break;

                case CaptureState.Countdown:
                    // Cancel countdown
                    if (_captureCoroutine != null) StopCoroutine(_captureCoroutine);
                    _captureState = CaptureState.Idle;
                    ShowFeedback("Capture cancelled.", Color.gray);
                    break;

                case CaptureState.Recording:
                    // Manual stop
                    if (_captureCoroutine != null) StopCoroutine(_captureCoroutine);
                    StartCoroutine(FinishCaptureCo());
                    break;
            }
        }

        private IEnumerator CaptureCountdownCo()
        {
            _captureState = CaptureState.Countdown;

            // Show countdown
            float remaining = captureCountdownSeconds;
            while (remaining > 0f)
            {
                ShowFeedback($"Get ready…  {Mathf.CeilToInt(remaining)}", Color.cyan);
                yield return new WaitForSeconds(1f);
                remaining -= 1f;
            }

            // Start recording
            _captureState = CaptureState.Recording;
            recorder.StartRecording("_ref_sequence");
            ShowFeedback("● Recording — perform your movement!\n" +
                         "Press capture button to stop.", Color.red);

            // Auto-stop if configured
            if (captureMaxDuration > 0f)
            {
                yield return new WaitForSeconds(captureMaxDuration);
                if (_captureState == CaptureState.Recording)
                    StartCoroutine(FinishCaptureCo());
            }
            // Otherwise wait for manual press (handled in OnCaptureButtonPressed)
        }

        private IEnumerator FinishCaptureCo()
        {
            _captureState = CaptureState.Idle;

            var session = recorder.StopRecording();
            if (session == null || session.frames.Count == 0)
            {
                ShowFeedback("Capture failed — no frames.", Color.red);
                yield break;
            }

            string name = $"{newReferenceCategory}_{System.DateTime.Now:HHmmss}";

            var refSequence = new ReferencePoseSequence
            {
                poseName       = name,
                category       = newReferenceCategory,
                frames         = new List<PoseFrame>(session.frames), // copy all frames
                captureRateFps = recorder.captureRateFps,
                matchThreshold = newReferenceThreshold,
                feedbackHint   = $"Work on your {newReferenceCategory} form."
            };

            databank.AddSequence(refSequence);
            databank.SaveToDisk();

            ShowFeedback($"Sequence saved!\n'{name}'\n" +
                         $"{session.FrameCount} frames  {session.DurationSeconds:F1}s",
                         Color.green);

            Debug.Log($"[Coordinator] Sequence saved → '{name}'  " +
                      $"{session.FrameCount} frames  {session.DurationSeconds:F1}s");

            yield break;
        }

        // ──────────────────────────────────────────────────────────────────
        //  UI feedback
        // ──────────────────────────────────────────────────────────────────

        private void ShowFeedback(string message, Color color)
        {
            Debug.Log($"[Coordinator] {message}");

            if (feedbackLabel == null) return;

            feedbackLabel.text  = message;
            feedbackLabel.color = color;

            if (_feedbackTimer != null) StopCoroutine(_feedbackTimer);
            _feedbackTimer = StartCoroutine(FeedbackFade());
        }

        private IEnumerator FeedbackFade()
        {
            yield return new WaitForSeconds(feedbackDuration - 0.5f);

            float elapsed = 0f;
            Color c = feedbackLabel.color;
            while (elapsed < 0.5f)
            {
                elapsed += Time.deltaTime;
                c.a = Mathf.Lerp(1f, 0f, elapsed / 0.5f);
                feedbackLabel.color = c;
                yield return null;
            }
            feedbackLabel.text = string.Empty;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Input action helpers
        // ──────────────────────────────────────────────────────────────────

        private static void BindAction(InputActionReference actionRef,
                                        System.Action callback)
        {
            if (actionRef == null) return;
            actionRef.action.Enable();
            actionRef.action.performed += _ => callback();
        }

        private static void UnbindAction(InputActionReference actionRef,
                                          System.Action callback)
        {
            if (actionRef == null) return;
            actionRef.action.performed -= _ => callback();
        }
    }
}
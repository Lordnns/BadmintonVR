// ============================================================
//  BadmintonPoseCoordinator.cs
//
//  Thin orchestration layer — wires recorder → databank → UI.
//
//  Drop this on a Manager GameObject alongside BodyPoseRecorder
//  and PoseDatabank.
//
//  Controller bindings (default):
//    Right A button  →  Start recording
//    Right B button  →  Stop  recording
// ============================================================

using System.Collections;
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

        [Tooltip("Hold this button to capture the current pose as a new reference.")]
        public InputActionReference captureReferenceAction;

        [Header("Live Matching")]
        [Tooltip("Match every N captured frames to reduce CPU load.  " +
                 "At 30 fps:  1 = every frame,  5 = 6 matches/sec.")]
        [Range(1, 30)]
        public int matchEveryNFrames = 5;

        [Tooltip("Only show feedback when the match score exceeds this value.")]
        [Range(0f, 1f)]
        public float feedbackMinScore = 0.40f;

        [Header("UI Feedback")]
        [Tooltip("World-space or overlay TextMeshPro label for live feedback.")]
        public TMP_Text feedbackLabel;

        [Tooltip("How long feedback text stays on screen before fading (seconds).")]
        public float feedbackDuration = 2.5f;

        [Header("Reference Capture Settings")]
        [Tooltip("Category tag written into new reference poses.")]
        public string newReferenceCategory = "Uncategorised";

        [Tooltip("Minimum similarity threshold stored in a newly captured reference.")]
        [Range(0.5f, 0.99f)]
        public float newReferenceThreshold = 0.75f;

        // ──────────────────────────────────────────────────────────────────
        //  Private state
        // ──────────────────────────────────────────────────────────────────

        private int      _framesSinceLast;
        private Coroutine _feedbackTimer;

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
            BindAction(captureReferenceAction, BeginReferenceCapture);

            recorder.onFrameCaptured    += OnFrameCaptured;
            recorder.onRecordingStarted += () => ShowFeedback("● Recording…", Color.red);
            recorder.onRecordingComplete+= OnRecordingComplete;
        }

        private void OnDisable()
        {
            UnbindAction(startAction,            StartSession);
            UnbindAction(stopAction,             StopSession);
            UnbindAction(captureReferenceAction, BeginReferenceCapture);

            if (recorder != null)
            {
                recorder.onFrameCaptured     -= OnFrameCaptured;
                recorder.onRecordingComplete -= OnRecordingComplete;
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  Session control
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
            _framesSinceLast++;
            if (_framesSinceLast < matchEveryNFrames) return;
            _framesSinceLast = 0;

            var result = databank.FindBestMatch(frame);
            if (result == null || result.similarityScore < feedbackMinScore) return;

            Color  color   = result.isMatch ? Color.green : Color.yellow;
            string message = result.isMatch
                ? $"✓ {result.referenceName}\n{result.similarityScore:P0}"
                : $"○ {result.referenceName}  {result.similarityScore:P0}\n" +
                  result.feedbackMessage;

            ShowFeedback(message, color);
        }

        private void OnRecordingComplete(PoseRecording session)
        {
            Debug.Log($"[Coordinator] Recording complete — {session.FrameCount} frames.");
        }

        // ──────────────────────────────────────────────────────────────────
        //  Reference pose capture
        //
        //  Press + hold the captureReferenceAction button while standing in
        //  the pose you want to save.  After 1 second, a snapshot is taken
        //  and written to the databank JSON.
        // ──────────────────────────────────────────────────────────────────

        private void BeginReferenceCapture()
        {
            StartCoroutine(ReferenceCaptureCo());
        }

        private IEnumerator ReferenceCaptureCo()
        {
            ShowFeedback("Hold still…", Color.cyan);
            yield return new WaitForSeconds(1f);   // give the player time to settle

            // Take a single-frame snapshot without starting a full session
            recorder.StartRecording("_ref_snapshot");

            // Wait one capture interval so at least one frame is recorded
            yield return new WaitForSeconds(1f / recorder.captureRateFps + 0.05f);

            var session = recorder.StopRecording();
            if (session == null || session.frames.Count == 0)
            {
                ShowFeedback("Capture failed — no frames.", Color.red);
                yield break;
            }

            // Use the middle frame for stability
            var keyFrame = session.frames[session.frames.Count / 2];

            // Generate a name from the timestamp
            string name = $"{newReferenceCategory}_{System.DateTime.Now:HHmmss}";

            var refPose = new ReferencePose
            {
                poseName       = name,
                category       = newReferenceCategory,
                keyFrame       = keyFrame,
                matchThreshold = newReferenceThreshold,
                feedbackHint   = $"Work on your {newReferenceCategory} form."
            };

            databank.AddReference(refPose);
            databank.SaveToDisk();

            ShowFeedback($"Reference saved!\n'{name}'", Color.green);
            Debug.Log($"[Coordinator] New reference → '{name}'");
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

            // Fade alpha over the last 0.5 s
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
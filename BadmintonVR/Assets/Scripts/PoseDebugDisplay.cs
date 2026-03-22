// ============================================================
//  PoseDebugDisplay.cs
//
//  Drop on your PoseManager GameObject alongside BodyPoseRecorder.
//  Works in both PCVR (Quest Link) and standalone Quest builds.
//
//  Shows on screen:
//    • Body tracking state (active / waiting / no skeleton)
//    • Per-joint tracked/untracked status
//    • Controller velocity (swing speed)
//    • FPS and frame count
//    • One-button test: start/stop recording from keyboard or controller
//
//  SETUP:
//    1. Add this component to your PoseManager GO
//    2. Assign bodyPoseRecorder in the Inspector
//    3. Hit Play — press SPACE (PCVR) or Right Trigger (Quest) to start
// ============================================================
 
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
 
namespace BadmintonPoseTracking
{
    public class PoseDebugDisplay : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────
 
        [Header("References")]
        public BodyPoseRecorder bodyPoseRecorder;
 
        [Header("Display")]
        [Tooltip("Scale of the on-screen GUI. Increase for headset readability.")]
        [Range(1f, 4f)]
        public float guiScale = 1.5f;
 
        [Tooltip("Show individual joint status list.")]
        public bool showJointList = true;
 
        [Tooltip("Show controller velocity (useful for swing speed debug).")]
        public bool showControllerVelocity = true;
 
        [Header("Keyboard Shortcut (PCVR / Editor)")]
        [Tooltip("Press this key to toggle recording on/off.")]
        public Key toggleRecordingKey = Key.Space;
 
        [Header("Controller Shortcut (Quest)")]
        public InputActionReference toggleRecordingAction;
 
        // ── Private ────────────────────────────────────────────────────────
 
        private PoseFrame      _lastFrame;
        private float          _lastFrameTime;
        private int            _frameCount;
        private float          _fps;
        private float          _fpsTimer;
        private bool           _recording;
 
        // Joint display order — most relevant for badminton at top
        private static readonly TrackedJoint[] DisplayOrder =
        {
            TrackedJoint.Head,
            TrackedJoint.Neck,
            TrackedJoint.Chest,
            TrackedJoint.SpineUpper,
            TrackedJoint.SpineMiddle,
            TrackedJoint.SpineLower,
            TrackedJoint.Hips,
            TrackedJoint.LeftShoulder,
            TrackedJoint.LeftUpperArm,
            TrackedJoint.LeftForearm,
            TrackedJoint.LeftWrist,
            TrackedJoint.RightShoulder,
            TrackedJoint.RightUpperArm,
            TrackedJoint.RightForearm,
            TrackedJoint.RightWrist,
            TrackedJoint.LeftUpperLeg,
            TrackedJoint.LeftLowerLeg,
            TrackedJoint.LeftAnkle,
            TrackedJoint.RightUpperLeg,
            TrackedJoint.RightLowerLeg,
            TrackedJoint.RightAnkle,
            TrackedJoint.LeftController,
            TrackedJoint.RightController,
        };
 
        // ── Lifecycle ──────────────────────────────────────────────────────
 
        private void Awake()
        {
            if (bodyPoseRecorder == null)
                bodyPoseRecorder = GetComponent<BodyPoseRecorder>();
        }
 
        private void OnEnable()
        {
            if (bodyPoseRecorder != null)
                bodyPoseRecorder.onFrameCaptured += OnFrameCaptured;
 
            if (toggleRecordingAction != null)
            {
                toggleRecordingAction.action.Enable();
                toggleRecordingAction.action.performed += _ => ToggleRecording();
            }
        }
 
        private void OnDisable()
        {
            if (bodyPoseRecorder != null)
                bodyPoseRecorder.onFrameCaptured -= OnFrameCaptured;
 
            if (toggleRecordingAction != null)
                toggleRecordingAction.action.performed -= _ => ToggleRecording();
        }
 
        private void Update()
        {
            // FPS counter
            _fpsTimer += Time.deltaTime;
            _frameCount++;
            if (_fpsTimer >= 0.5f)
            {
                _fps        = _frameCount / _fpsTimer;
                _frameCount = 0;
                _fpsTimer   = 0f;
            }
 
            // Keyboard toggle (PCVR / Editor)
            if (Keyboard.current != null &&
                Keyboard.current[toggleRecordingKey].wasPressedThisFrame)
                ToggleRecording();
        }
 
        private void OnFrameCaptured(PoseFrame frame)
        {
            _lastFrame     = frame;
            _lastFrameTime = Time.time;
        }
 
        // ── Toggle recording ───────────────────────────────────────────────
 
        private void ToggleRecording()
        {
            if (bodyPoseRecorder == null) return;
 
            if (_recording)
            {
                bodyPoseRecorder.StopRecording();
                _recording = false;
                Debug.Log("[PoseDebug] Recording stopped.");
            }
            else
            {
                bodyPoseRecorder.StartRecording("DebugPlayer");
                _recording = true;
                Debug.Log("[PoseDebug] Recording started.");
            }
        }
 
        // ── GUI ────────────────────────────────────────────────────────────
 
        private void OnGUI()
        {
            if (bodyPoseRecorder == null) return;
 
            GUI.matrix = Matrix4x4.Scale(Vector3.one * guiScale);
 
            float panelWidth  = 540f;
            float panelHeight = showJointList ? 480f : 200f;
            float x = 10f;
            float y = 10f;
 
            // Background
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(new Rect(x, y, panelWidth, panelHeight),
                            Texture2D.whiteTexture);
            GUI.color = Color.white;
 
            GUILayout.BeginArea(new Rect(x + 8f, y + 8f,
                                         panelWidth - 16f, panelHeight - 16f));
 
            // ── Header ────────────────────────────────────────────────────
            GUIStyle header = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 13,
                fontStyle = FontStyle.Bold
            };
            header.normal.textColor = Color.cyan;
            GUILayout.Label("● POSE TRACKING DEBUG", header);
 
            // ── FPS + frame info ──────────────────────────────────────────
            GUIStyle small = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            small.normal.textColor = Color.white;
            GUILayout.Label($"FPS: {_fps:F1}   Frames captured: " +
                            $"{bodyPoseRecorder.FramesCaptured}", small);
 
            // ── Body tracking state ───────────────────────────────────────
            GUIStyle stateStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 12,
                fontStyle = FontStyle.Bold
            };
 
            bool isTracking = bodyPoseRecorder.IsBodyTracking;
            stateStyle.normal.textColor = isTracking ? Color.green : Color.yellow;
            GUILayout.Label(isTracking
                ? "✓ Body Tracking: ACTIVE"
                : "⚠ Body Tracking: WAITING (normal in Editor)", stateStyle);
 
            // ── Recording state ───────────────────────────────────────────
            stateStyle.normal.textColor = _recording ? Color.red : Color.gray;
            GUILayout.Label(_recording ? "● RECORDING" : "○ Not recording", stateStyle);
 
            // ── Toggle hint ───────────────────────────────────────────────
            small.normal.textColor = Color.gray;
            GUILayout.Label($"[SPACE] or [Right Trigger] to toggle recording", small);
 
            // ── Last frame age ────────────────────────────────────────────
            if (_lastFrame != null)
            {
                float age = Time.time - _lastFrameTime;
                small.normal.textColor = age < 0.2f ? Color.green : Color.yellow;
                GUILayout.Label($"Last frame: {age * 1000f:F0}ms ago  " +
                                $"(idx {_lastFrame.frameIndex})", small);
            }
            else
            {
                small.normal.textColor = Color.gray;
                GUILayout.Label("No frames received yet — start recording.", small);
            }
 
            // ── Controller velocity ───────────────────────────────────────
            if (showControllerVelocity && _lastFrame != null)
            {
                GUILayout.Space(4f);
                GUIStyle velHeader = new GUIStyle(GUI.skin.label)
                    { fontSize = 11, fontStyle = FontStyle.Bold };
                velHeader.normal.textColor = Color.cyan;
                GUILayout.Label("Controller Velocity", velHeader);
 
                float leftSpeed  = _lastFrame.leftControllerVelocity.magnitude;
                float rightSpeed = _lastFrame.rightControllerVelocity.magnitude;
 
                small.normal.textColor = SpeedColor(leftSpeed);
                GUILayout.Label($"  Left:  {leftSpeed:F2} m/s  " +
                                $"{VelocityBar(leftSpeed)}", small);
 
                small.normal.textColor = SpeedColor(rightSpeed);
                GUILayout.Label($"  Right: {rightSpeed:F2} m/s  " +
                                $"{VelocityBar(rightSpeed)}", small);
            }
 
            // ── Joint list ────────────────────────────────────────────────
            if (showJointList)
            {
                GUILayout.Space(4f);
                GUIStyle jointHeader = new GUIStyle(GUI.skin.label)
                    { fontSize = 11, fontStyle = FontStyle.Bold };
                jointHeader.normal.textColor = Color.cyan;
                GUILayout.Label("Joint Status", jointHeader);
 
                GUIStyle jointStyle = new GUIStyle(GUI.skin.label) { fontSize = 10 };
 
                if (_lastFrame == null)
                {
                    jointStyle.normal.textColor = Color.gray;
                    GUILayout.Label("  (no frame data yet)", jointStyle);
                }
                else
                {
                    int trackedCount = 0;
 
                    // Pre-compute tracked state for each joint
                    bool[] tracked = new bool[DisplayOrder.Length];
                    for (int i = 0; i < DisplayOrder.Length; i++)
                    {
                        tracked[i] = _lastFrame.GetJoint(DisplayOrder[i]).isTracked;
                        if (tracked[i]) trackedCount++;
                    }
 
                    // Summary line
                    jointStyle.normal.textColor =
                        trackedCount > 15 ? Color.green :
                        trackedCount > 5  ? Color.yellow : Color.red;
                    GUILayout.Label($"  Tracked: {trackedCount}/{DisplayOrder.Length}",
                                    jointStyle);
 
                    // Three-column layout
                    float colWidth = (panelWidth - 16f) / 3f;
                    int rows = Mathf.CeilToInt(DisplayOrder.Length / 3f);
 
                    for (int row = 0; row < rows; row++)
                    {
                        GUILayout.BeginHorizontal();
 
                        for (int col = 0; col < 3; col++)
                        {
                            int idx = row + col * rows;
                            if (idx < DisplayOrder.Length)
                            {
                                string icon  = tracked[idx] ? "✓" : "✗";
                                string label = DisplayOrder[idx].ToString();
                                jointStyle.normal.textColor =
                                    tracked[idx] ? Color.green : Color.red;
                                GUILayout.Label($" {icon} {label}", jointStyle,
                                                GUILayout.Width(colWidth));
                            }
                        }
 
                        GUILayout.EndHorizontal();
                    }
                }
            }
 
            GUILayout.EndArea();
        }
 
        // ── Helpers ────────────────────────────────────────────────────────
 
        private static Color SpeedColor(float speed)
        {
            if (speed > 5f)  return Color.red;
            if (speed > 2f)  return Color.yellow;
            if (speed > 0.5f) return Color.green;
            return Color.gray;
        }
 
        private static string VelocityBar(float speed)
        {
            int bars = Mathf.Clamp(Mathf.RoundToInt(speed), 0, 10);
            return new string('█', bars) + new string('░', 10 - bars);
        }
    }
}
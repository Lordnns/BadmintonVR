// ============================================================
//  PoseRecorder.cs
//
//  Single responsibility: capture body + controller frames
//  into a PoseCapture buffer.  No matching, no saving, no modes.
//  Start() → record → Stop() → PoseCapture.
//
//  Used identically for dev reference recording and gameplay
//  recording.  The caller decides what to do with the result.
//
//  Optimisations:
//  ─────────────────────────────────────────────────────────
//  • Static int[] bone lookup (array index vs dict hash per bone)
//  • Cached WaitForSecondsRealtime (one alloc for lifetime)
//  • Reused List<InputDevice> buffer (no per-event alloc)
//  • Pre-allocated PoseCapture.frames capacity on Start()
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Unity.XR.CoreUtils;

namespace BadmintonPoseTracking
{
    // ── Lightweight capture result ─────────────────────────────────────────
    //    Returned by StopRecording().  Intentionally simple.

    public sealed class PoseCapture
    {
        public readonly PoseFrame[] Frames;
        public readonly int         FrameCount;
        public readonly float       CaptureRateFps;
        public readonly float       DurationSeconds;

        public PoseCapture(List<PoseFrame> frames, float fps, float duration)
        {
            Frames          = frames.ToArray();   // one alloc, then immutable
            FrameCount      = Frames.Length;
            CaptureRateFps  = fps;
            DurationSeconds = duration;
        }
    }

    // ── Recorder ───────────────────────────────────────────────────────────

    public sealed class PoseRecorder : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────

        [Header("Capture")]
        [Range(10, 72)]
        public float captureRateFps = 30f;

        [Tooltip("Auto-stop after N seconds. 0 = unlimited.")]
        public float maxDurationSeconds = 0f;

        [Header("OpenXR / XRI")]
        public XROrigin  xrOrigin;
        public Transform hmdTransform;
        public Transform leftController;
        public Transform rightController;

        [Header("Meta XR — Body Tracking")]
        public OVRSkeleton bodySkeleton;

        // ── Events ─────────────────────────────────────────────────────────

        public event Action<PoseFrame>   OnFrameCaptured;
        public event Action<PoseCapture> OnCaptureComplete;

        // ── Public state ───────────────────────────────────────────────────

        public bool IsRecording    { get; private set; }
        public int  FramesCaptured { get; private set; }

        public bool IsBodyTracking => bodySkeleton != null &&
                                      bodySkeleton.IsDataHighConfidence;

        // ── Private ────────────────────────────────────────────────────────

        private List<PoseFrame>        _frames;
        private Coroutine              _loop;
        private InputDevice            _leftDev;
        private InputDevice            _rightDev;
        private WaitForSecondsRealtime _wait;           // cached, mutated per-start
        private float                  _startTime;

        // Reused device-query buffer — avoids alloc on every connect/disconnect
        private readonly List<InputDevice> _devBuf = new List<InputDevice>(4);

        // ── Static bone lookup ─────────────────────────────────────────────
        //
        //    Replaces Dictionary<BoneId,TrackedJoint>.TryGetValue(…)
        //    (hash + bucket walk) with a direct array read.
        //    Built once at class load time, shared across all instances.

        private static readonly int[] _boneToJoint;   // value -1 = "ignore this bone"

        // Joints confirmed tracked by OVRBody (Body mode, Quest 3):
        //   Head, Neck, Chest, SpineUpper, SpineMiddle, SpineLower, Hips
        //   LeftShoulder, LeftUpperArm, LeftForearm, LeftWrist
        //   RightShoulder, RightUpperArm, RightForearm, RightWrist
        //   Legs NOT tracked in Body mode — excluded.
        private static readonly (OVRSkeleton.BoneId bone, TrackedJoint joint)[] _boneMap =
        {
            // Full spine — available in Body mode
            (OVRSkeleton.BoneId.Body_Hips,            TrackedJoint.Hips),
            (OVRSkeleton.BoneId.Body_SpineLower,       TrackedJoint.SpineLower),
            (OVRSkeleton.BoneId.Body_SpineMiddle,      TrackedJoint.SpineMiddle),
            (OVRSkeleton.BoneId.Body_SpineUpper,       TrackedJoint.SpineUpper),
            (OVRSkeleton.BoneId.Body_Chest,            TrackedJoint.Chest),
            (OVRSkeleton.BoneId.Body_Neck,             TrackedJoint.Neck),

            // Left arm
            (OVRSkeleton.BoneId.Body_LeftShoulder,     TrackedJoint.LeftShoulder),
            (OVRSkeleton.BoneId.Body_LeftArmUpper,     TrackedJoint.LeftUpperArm),
            (OVRSkeleton.BoneId.Body_LeftArmLower,     TrackedJoint.LeftForearm),
            (OVRSkeleton.BoneId.Body_LeftHandWrist,    TrackedJoint.LeftWrist),

            // Right arm — the racket arm
            (OVRSkeleton.BoneId.Body_RightShoulder,    TrackedJoint.RightShoulder),
            (OVRSkeleton.BoneId.Body_RightScapula,     TrackedJoint.RightScapula),
            (OVRSkeleton.BoneId.Body_RightArmUpper,    TrackedJoint.RightUpperArm),
            (OVRSkeleton.BoneId.Body_RightArmLower,    TrackedJoint.RightForearm),
            (OVRSkeleton.BoneId.Body_RightHandWrist,   TrackedJoint.RightWrist),

            // Head comes from hmdTransform — no body tracking cost.
            // Legs excluded: not tracked in Body mode.
        };

        static PoseRecorder()
        {
            int maxId = 0;
            foreach (var (bone, _) in _boneMap)
                if ((int)bone > maxId) maxId = (int)bone;

            _boneToJoint = new int[maxId + 1];
            for (int i = 0; i < _boneToJoint.Length; i++) _boneToJoint[i] = -1;
            foreach (var (bone, joint) in _boneMap)
                _boneToJoint[(int)bone] = (int)joint;
        }

        // ── Unity lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            if (xrOrigin == null)
                xrOrigin = FindFirstObjectByType<XROrigin>();

            if (hmdTransform == null)
                hmdTransform = xrOrigin != null
                    ? xrOrigin.Camera?.transform
                    : Camera.main?.transform;

            if (bodySkeleton == null)
                Debug.LogWarning("[PoseRecorder] OVRSkeleton not assigned.");

            // Pre-allocate the wait object — mutate .waitTime before each start
            _wait = new WaitForSecondsRealtime(1f / captureRateFps);
        }

        private void OnEnable()
        {
            InputDevices.deviceConnected    += OnDeviceChanged;
            InputDevices.deviceDisconnected += OnDeviceChanged;
            RefreshDevices();
        }

        private void OnDisable()
        {
            InputDevices.deviceConnected    -= OnDeviceChanged;
            InputDevices.deviceDisconnected -= OnDeviceChanged;
            if (IsRecording) StopRecording();
        }

        // ── Public API ─────────────────────────────────────────────────────

        /// <summary>
        /// Begin capturing.  estimatedFrames tunes the initial List capacity;
        /// provide your expected capture length in frames to avoid resizing.
        /// </summary>
        public void StartRecording(int estimatedFrames = 120)
        {
            if (IsRecording)
            {
                Debug.LogWarning("[PoseRecorder] Already recording.");
                return;
            }

            _frames         = new List<PoseFrame>(estimatedFrames);
            FramesCaptured  = 0;
            IsRecording     = true;
            _startTime      = Time.time;
            _wait.waitTime  = 1f / captureRateFps;
            _loop           = StartCoroutine(CaptureLoop());

            Debug.Log($"[PoseRecorder] Started  {captureRateFps} fps  " +
                      $"bodyTracking={IsBodyTracking}");
        }

        /// <summary>
        /// Stop capturing and return the result.
        /// The internal buffer is cleared — hold the returned PoseCapture.
        /// </summary>
        public PoseCapture StopRecording()
        {
            if (!IsRecording) return null;

            if (_loop != null) { StopCoroutine(_loop); _loop = null; }
            IsRecording = false;

            float duration = Time.time - _startTime;
            var   capture  = new PoseCapture(_frames, captureRateFps, duration);
            _frames = null;   // release the list — capture holds the array

            Debug.Log($"[PoseRecorder] Stopped  {capture.FrameCount} frames  " +
                      $"{capture.DurationSeconds:F2}s");

            OnCaptureComplete?.Invoke(capture);
            return capture;
        }

        // ── Capture loop ───────────────────────────────────────────────────

        private IEnumerator CaptureLoop()
        {
            float interval = 1f / captureRateFps;

            while (IsRecording)
            {
                float t0 = Time.unscaledTime;

                CaptureFrame();

                if (maxDurationSeconds > 0f &&
                    Time.time - _startTime >= maxDurationSeconds)
                {
                    StopRecording();
                    yield break;
                }

                float remaining = interval - (Time.unscaledTime - t0);
                if (remaining > 0f)
                {
                    _wait.waitTime = remaining;
                    yield return _wait;
                }
                else
                {
                    yield return null;   // missed the window — resume next frame
                }
            }
        }

        // ── Frame capture ──────────────────────────────────────────────────

        private void CaptureFrame()
        {
            var frame = new PoseFrame
            {
                timestamp  = Time.time,
                frameIndex = FramesCaptured
            };

            // OVRSkeleton gives positions in tracking space (relative to the floor/guardian
            // origin).  HMD and controller Transforms are in Unity world space.
            // If XROrigin has any offset from the world origin these will disagree by
            // exactly that offset — the ~10m shift we saw.
            //
            // Fix: convert world-space positions to tracking space using InverseTransformPoint
            // on the XROrigin transform, which is the root of the tracking hierarchy.

            if (hmdTransform != null)
            {
                Vector3    pos = xrOrigin != null
                    ? xrOrigin.transform.InverseTransformPoint(hmdTransform.position)
                    : hmdTransform.position;
                Quaternion rot = xrOrigin != null
                    ? Quaternion.Inverse(xrOrigin.transform.rotation) * hmdTransform.rotation
                    : hmdTransform.rotation;
                WriteJoint(frame, TrackedJoint.Head, pos, rot, true);
            }

            CaptureBodySkeleton(frame);
            CaptureControllers(frame);

            _frames.Add(frame);
            FramesCaptured++;
            OnFrameCaptured?.Invoke(frame);
        }

        private void CaptureBodySkeleton(PoseFrame frame)
        {
            if (bodySkeleton == null)               return;
            if (!bodySkeleton.IsDataValid)          return;
            if (!bodySkeleton.IsDataHighConfidence) return;

            IList<OVRBone> bones   = bodySkeleton.Bones;
            if (bones == null) return;

            int len = _boneToJoint.Length;

            foreach (OVRBone bone in bones)
            {
                if (bone?.Transform == null) continue;

                int idx = (int)bone.Id;
                if ((uint)idx >= (uint)len) continue;

                int jointIdx = _boneToJoint[idx];
                if (jointIdx < 0) continue;

                WriteJoint(frame, (TrackedJoint)jointIdx,
                           bone.Transform.position,
                           bone.Transform.rotation, true);
            }
        }

        private Vector3 ToTrackingPos(Vector3 worldPos) =>
            xrOrigin != null
                ? xrOrigin.transform.InverseTransformPoint(worldPos)
                : worldPos;

        private Quaternion ToTrackingRot(Quaternion worldRot) =>
            xrOrigin != null
                ? Quaternion.Inverse(xrOrigin.transform.rotation) * worldRot
                : worldRot;

        private void CaptureControllers(PoseFrame frame)
        {
            if (leftController != null)
            {
                Transform t   = leftController;
                Vector3   pos = ToTrackingPos(t.position);
                Quaternion rot = ToTrackingRot(t.rotation);
                WriteJoint(frame, TrackedJoint.LeftController, pos, rot, true);

                if (!frame.GetJoint(TrackedJoint.LeftWrist).isTracked)
                    WriteJoint(frame, TrackedJoint.LeftWrist, pos, rot, true);

                if (_leftDev.isValid)
                {
                    _leftDev.TryGetFeatureValue(CommonUsages.deviceVelocity,        out frame.leftControllerVelocity);
                    _leftDev.TryGetFeatureValue(CommonUsages.deviceAngularVelocity, out frame.leftControllerAngularVelocity);
                    _leftDev.TryGetFeatureValue(CommonUsages.trigger,               out frame.leftTrigger);
                    _leftDev.TryGetFeatureValue(CommonUsages.grip,                  out frame.leftGrip);
                }
            }

            if (rightController != null)
            {
                Transform t    = rightController;
                Vector3   pos  = ToTrackingPos(t.position);
                Quaternion rot = ToTrackingRot(t.rotation);
                WriteJoint(frame, TrackedJoint.RightController, pos, rot, true);

                if (!frame.GetJoint(TrackedJoint.RightWrist).isTracked)
                    WriteJoint(frame, TrackedJoint.RightWrist, pos, rot, true);

                if (_rightDev.isValid)
                {
                    _rightDev.TryGetFeatureValue(CommonUsages.deviceVelocity,        out frame.rightControllerVelocity);
                    _rightDev.TryGetFeatureValue(CommonUsages.deviceAngularVelocity, out frame.rightControllerAngularVelocity);
                    _rightDev.TryGetFeatureValue(CommonUsages.trigger,               out frame.rightTrigger);
                    _rightDev.TryGetFeatureValue(CommonUsages.grip,                  out frame.rightGrip);
                }
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static void WriteJoint(PoseFrame f, TrackedJoint id,
                                        Vector3 pos, Quaternion rot, bool tracked)
        {
            f.joints[(int)id] = new JointPose
                { joint = id, position = pos, rotation = rot, isTracked = tracked };
        }

        private void RefreshDevices()
        {
            _devBuf.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, _devBuf);
            if (_devBuf.Count > 0) _leftDev = _devBuf[0];

            _devBuf.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, _devBuf);
            if (_devBuf.Count > 0) _rightDev = _devBuf[0];
        }

        private void OnDeviceChanged(InputDevice _) => RefreshDevices();
    }
}
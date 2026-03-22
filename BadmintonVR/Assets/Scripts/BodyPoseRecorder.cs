// ============================================================
//  BodyPoseRecorder.cs
//
//  Body joint data   →  Meta XR Core SDK  (OVRSkeleton / OVRBody)
//  HMD + Controllers →  OpenXR + XR Interaction Toolkit  (unchanged)
//
//  The Meta SDK is ONLY used inside this file.
//
//  ── Package requirements ────────────────────────────────────
//  com.unity.xr.meta-openxr
//  com.unity.xr.openxr
//  com.unity.xr.interaction.toolkit
//  Meta XR Core SDK
//
//  ── Scene setup (one-time) ──────────────────────────────────
//  1. Create an empty GameObject called "MetaBodyTracking"
//  2. Add OVRManager  →  Tracking Origin = Floor Level
//                        Uncheck "Use Recommended MSAA Level"
//                        Leave all rendering options at default
//  3. Add OVRBody     →  Body Tracking Fidelity = High
//                        Body Tracking Mode = FullBody
//  4. Add OVRSkeleton →  Skeleton Type = Body
//                        (auto-links to the OVRBody on same GO)
//  5. Drag that GameObject into BodyPoseRecorder.bodySkeleton
//
//  ── AndroidManifest.xml ─────────────────────────────────────
//  <uses-permission android:name="com.oculus.permission.BODY_TRACKING"/>
// ============================================================
 
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
 
using Unity.XR.CoreUtils;
 
namespace BadmintonPoseTracking
{
    public class BodyPoseRecorder : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────
 
        [Header("Recording")]
        [Range(10, 72)]
        [Tooltip("Samples per second. Quest 3 body tracking runs at ~50 Hz internally.")]
        public float captureRateFps = 30f;
 
        [Tooltip("Auto-stop after N seconds. 0 = unlimited.")]
        public float maxRecordingSeconds = 0f;
 
        [Header("OpenXR / XRI References  (unchanged from your existing setup)")]
        [Tooltip("Your existing XROrigin — drag it in.")]
        public XROrigin xrOrigin;
 
        [Tooltip("Main Camera under XROrigin → Camera Offset.")]
        public Transform hmdTransform;
 
        [Tooltip("Left controller GameObject under your XR Origin.")]
        public Transform leftController;
 
        [Tooltip("Right controller GameObject under your XR Origin.")]
        public Transform rightController;
 
        [Header("Meta XR Core SDK — Body Tracking Only")]
        [Tooltip("OVRSkeleton component on your MetaBodyTracking GameObject.")]
        public OVRSkeleton bodySkeleton;
 
        [Header("Events")]
        public Action<PoseFrame>     onFrameCaptured;
        public Action<PoseRecording> onRecordingComplete;
        public Action                onRecordingStarted;
 
        // ── Public state ───────────────────────────────────────────────────
 
        public bool IsRecording     { get; private set; }
        public int  FramesCaptured  { get; private set; }
 
        ///True once OVRSkeleton reports it has valid bone data.
        public bool IsBodyTracking => bodySkeleton != null &&
                                      bodySkeleton.IsDataHighConfidence;
 
        // ── Private ────────────────────────────────────────────────────────
 
        private PoseRecording _session;
        private Coroutine     _captureLoop;
        private InputDevice   _leftDevice;
        private InputDevice   _rightDevice;
 
        // ── OVRSkeleton BoneId → our TrackedJoint mapping ──────────────────
        //    OVRSkeleton.BoneId values for SkeletonType.Body (FullBody mode).
        //    Only the joints we care about are mapped; the rest are silently
        //    skipped. This table is the ONLY place an OVR type is referenced
        //    outside of the capture method itself.
 
        private static readonly Dictionary<OVRSkeleton.BoneId, TrackedJoint> BoneMap =
            new Dictionary<OVRSkeleton.BoneId, TrackedJoint>()
            {
                // Torso
                { OVRSkeleton.BoneId.Body_Hips,              TrackedJoint.Hips        },
                { OVRSkeleton.BoneId.Body_SpineLower,        TrackedJoint.SpineLower  },
                { OVRSkeleton.BoneId.Body_SpineMiddle,       TrackedJoint.SpineMiddle },
                { OVRSkeleton.BoneId.Body_SpineUpper,        TrackedJoint.SpineUpper  },
                { OVRSkeleton.BoneId.Body_Chest,             TrackedJoint.Chest       },
                { OVRSkeleton.BoneId.Body_Neck,              TrackedJoint.Neck        },
                { OVRSkeleton.BoneId.Body_Head,              TrackedJoint.Head        },
 
                // Left arm
                { OVRSkeleton.BoneId.Body_LeftShoulder,     TrackedJoint.LeftShoulder  },
                { OVRSkeleton.BoneId.Body_LeftScapula,      TrackedJoint.LeftScapula   },
                { OVRSkeleton.BoneId.Body_LeftArmUpper,     TrackedJoint.LeftUpperArm  },
                { OVRSkeleton.BoneId.Body_LeftArmLower,     TrackedJoint.LeftForearm   },
                { OVRSkeleton.BoneId.Body_LeftHandWrist,    TrackedJoint.LeftWrist     },
 
                // Right arm
                { OVRSkeleton.BoneId.Body_RightShoulder,    TrackedJoint.RightShoulder },
                { OVRSkeleton.BoneId.Body_RightScapula,     TrackedJoint.RightScapula  },
                { OVRSkeleton.BoneId.Body_RightArmUpper,    TrackedJoint.RightUpperArm },
                { OVRSkeleton.BoneId.Body_RightArmLower,    TrackedJoint.RightForearm  },
                { OVRSkeleton.BoneId.Body_RightHandWrist,   TrackedJoint.RightWrist    },
 
                // Left leg
                { OVRSkeleton.BoneId.FullBody_LeftUpperLeg,     TrackedJoint.LeftUpperLeg },
                { OVRSkeleton.BoneId.FullBody_LeftLowerLeg,     TrackedJoint.LeftLowerLeg },
                { OVRSkeleton.BoneId.FullBody_LeftFootAnkle,    TrackedJoint.LeftAnkle    },
                { OVRSkeleton.BoneId.FullBody_LeftFootBall,     TrackedJoint.LeftFoot     },
 
                // Right leg
                { OVRSkeleton.BoneId.FullBody_RightUpperLeg,    TrackedJoint.RightUpperLeg },
                { OVRSkeleton.BoneId.FullBody_RightLowerLeg,    TrackedJoint.RightLowerLeg },
                { OVRSkeleton.BoneId.FullBody_RightFootAnkle,   TrackedJoint.RightAnkle    },
                { OVRSkeleton.BoneId.FullBody_RightFootBall,    TrackedJoint.RightFoot     },
            };
 
        // ── Unity lifecycle ────────────────────────────────────────────────
 
        private void Awake()
        {
            // Auto-find XROrigin and HMD — pure OpenXR/XRI, no OVR involved
            if (xrOrigin == null)
                xrOrigin = FindFirstObjectByType<XROrigin>();
 
            if (hmdTransform == null)
                hmdTransform = xrOrigin != null
                    ? xrOrigin.Camera?.transform
                    : Camera.main?.transform;
 
            if (bodySkeleton == null)
                Debug.LogWarning("[BodyPoseRecorder] OVRSkeleton not assigned. " +
                                 "Body joints will not be recorded.");
        }
 
        private void OnEnable()
        {
            InputDevices.deviceConnected    += OnDeviceChanged;
            InputDevices.deviceDisconnected += OnDeviceChanged;
            RefreshControllerDevices();
        }
 
        private void OnDisable()
        {
            InputDevices.deviceConnected    -= OnDeviceChanged;
            InputDevices.deviceDisconnected -= OnDeviceChanged;
            if (IsRecording) StopRecording();
        }
 
        // ── Public API ─────────────────────────────────────────────────────
 
        public void StartRecording(string playerName = "Player")
        {
            if (IsRecording)
            {
                Debug.LogWarning("[BodyPoseRecorder] Already recording.");
                return;
            }
 
            _session = new PoseRecording
            {
                sessionId      = Guid.NewGuid().ToString("N"),
                playerName     = playerName,
                captureRateFps = captureRateFps,
                startTime      = Time.time
            };
 
            FramesCaptured = 0;
            IsRecording    = true;
            _captureLoop   = StartCoroutine(CaptureLoop());
 
            onRecordingStarted?.Invoke();
            Debug.Log($"[BodyPoseRecorder] Recording started — {captureRateFps} fps  " +
                      $"bodyTracking={IsBodyTracking}  id={_session.sessionId}");
        }
 
        public PoseRecording StopRecording()
        {
            if (!IsRecording) return null;
 
            if (_captureLoop != null) { StopCoroutine(_captureLoop); _captureLoop = null; }
 
            IsRecording      = false;
            _session.endTime = Time.time;
 
            Debug.Log($"[BodyPoseRecorder] Stopped — {FramesCaptured} frames " +
                      $"in {_session.DurationSeconds:F2}s");
 
            onRecordingComplete?.Invoke(_session);
            return _session;
        }
 
        // ── Capture coroutine ──────────────────────────────────────────────
 
        private IEnumerator CaptureLoop()
        {
            float interval = 1f / captureRateFps;
 
            while (IsRecording)
            {
                float t0 = Time.unscaledTime;
                CaptureFrame();
 
                if (maxRecordingSeconds > 0f &&
                    Time.time - _session.startTime >= maxRecordingSeconds)
                {
                    StopRecording();
                    yield break;
                }
 
                float wait = Mathf.Max(0f, interval - (Time.unscaledTime - t0));
                yield return new WaitForSecondsRealtime(wait);
            }
        }
 
        // ── Frame assembly ─────────────────────────────────────────────────
 
        private void CaptureFrame()
        {
            var frame = new PoseFrame
            {
                timestamp  = Time.time,
                frameIndex = FramesCaptured
            };
 
            // 1 ── HMD (OpenXR / XRI — no OVR) ────────────────────────────
            if (hmdTransform != null)
                WriteJoint(frame, TrackedJoint.Head,
                           hmdTransform.position, hmdTransform.rotation, true);
 
            // 2 ── Body skeleton (Meta XR Core SDK — isolated here) ─────────
            CaptureBodySkeleton(frame);
 
            // 3 ── Controllers (XRI InputDevice — no OVR) ──────────────────
            CaptureControllers(frame);
 
            _session.frames.Add(frame);
            FramesCaptured++;
            onFrameCaptured?.Invoke(frame);
        }
 
        // ── Body skeleton via OVRSkeleton ──────────────────────────────────
        //
        //  OVRSkeleton.Bones is an IList<OVRBone>.
        //  Each OVRBone has:
        //    .Id          — OVRSkeleton.BoneId enum value
        //    .Transform   — world-space Transform
        //  IsDataHighConfidence — true when the body tracking system
        //                         has a confident skeleton estimate.
 
        private void CaptureBodySkeleton(PoseFrame frame)
        {
            if (bodySkeleton == null)           return;
            if (!bodySkeleton.IsDataValid)      return;   // subsystem not ready
            if (!bodySkeleton.IsDataHighConfidence) return; // low confidence — skip
 
            IList<OVRBone> bones = bodySkeleton.Bones;
            if (bones == null) return;
 
            foreach (OVRBone bone in bones)
            {
                if (bone?.Transform == null) continue;
 
                // Only map bones we care about — rest are silently skipped
                if (!BoneMap.TryGetValue(bone.Id, out TrackedJoint jointId)) continue;
 
                // OVRSkeleton already provides world-space transforms
                WriteJoint(frame, jointId,
                           bone.Transform.position,
                           bone.Transform.rotation,
                           tracked: true);
            }
        }
 
        // ── Controllers (pure XRI / InputSystem — zero OVR) ───────────────
 
        private void CaptureControllers(PoseFrame frame)
        {
            // Left
            if (leftController != null)
            {
                Transform t = leftController.transform;
                WriteJoint(frame, TrackedJoint.LeftController,
                           t.position, t.rotation, true);
 
                // Wrist fallback if body tracking didn't provide it
                if (!frame.GetJoint(TrackedJoint.LeftWrist).isTracked)
                    WriteJoint(frame, TrackedJoint.LeftWrist,
                               t.position, t.rotation, true);
 
                if (_leftDevice.isValid)
                {
                    _leftDevice.TryGetFeatureValue(CommonUsages.deviceVelocity,
                        out frame.leftControllerVelocity);
                    _leftDevice.TryGetFeatureValue(CommonUsages.deviceAngularVelocity,
                        out frame.leftControllerAngularVelocity);
                    _leftDevice.TryGetFeatureValue(CommonUsages.trigger,
                        out frame.leftTrigger);
                    _leftDevice.TryGetFeatureValue(CommonUsages.grip,
                        out frame.leftGrip);
                }
            }
 
            // Right
            if (rightController != null)
            {
                Transform t = rightController.transform;
                WriteJoint(frame, TrackedJoint.RightController,
                           t.position, t.rotation, true);
 
                if (!frame.GetJoint(TrackedJoint.RightWrist).isTracked)
                    WriteJoint(frame, TrackedJoint.RightWrist,
                               t.position, t.rotation, true);
 
                if (_rightDevice.isValid)
                {
                    _rightDevice.TryGetFeatureValue(CommonUsages.deviceVelocity,
                        out frame.rightControllerVelocity);
                    _rightDevice.TryGetFeatureValue(CommonUsages.deviceAngularVelocity,
                        out frame.rightControllerAngularVelocity);
                    _rightDevice.TryGetFeatureValue(CommonUsages.trigger,
                        out frame.rightTrigger);
                    _rightDevice.TryGetFeatureValue(CommonUsages.grip,
                        out frame.rightGrip);
                }
            }
        }
 
        // ── Helpers ────────────────────────────────────────────────────────
 
        private static void WriteJoint(PoseFrame frame, TrackedJoint id,
                                        Vector3 pos, Quaternion rot, bool tracked)
        {
            frame.joints[(int)id] = new JointPose
            {
                joint     = id,
                position  = pos,
                rotation  = rot,
                isTracked = tracked
            };
        }
 
        private void RefreshControllerDevices()
        {
            var buf = new List<InputDevice>();
 
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, buf);
            if (buf.Count > 0) _leftDevice = buf[0];
 
            buf.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, buf);
            if (buf.Count > 0) _rightDevice = buf[0];
        }
 
        private void OnDeviceChanged(InputDevice _) => RefreshControllerDevices();
    }
}
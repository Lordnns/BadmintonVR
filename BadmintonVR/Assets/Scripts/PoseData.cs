// ============================================================
//  PoseData.cs
//  Vendor-neutral data structures shared by all other scripts.
//  No SDK dependency — plain Unity types only.
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BadmintonPoseTracking
{
    // ------------------------------------------------------------------ //
    //  TrackedJoint
    //  Every joint we can extract from Quest 3 body tracking + controllers.
    //  Indices are stable — do not reorder, only append.
    // ------------------------------------------------------------------ //
    public enum TrackedJoint
    {
        // Head / neck
        Head        = 0,
        Neck        = 1,

        // Spine
        Chest       = 2,
        SpineUpper  = 3,
        SpineMiddle = 4,
        SpineLower  = 5,
        Hips        = 6,   // pelvis / root

        // Left arm
        LeftShoulder  = 7,
        LeftScapula   = 8,
        LeftUpperArm  = 9,
        LeftForearm   = 10,
        LeftWrist     = 11,

        // Right arm
        RightShoulder  = 12,
        RightScapula   = 13,
        RightUpperArm  = 14,
        RightForearm   = 15,
        RightWrist     = 16,

        // Left leg
        LeftUpperLeg = 17,
        LeftLowerLeg = 18,
        LeftAnkle    = 19,
        LeftFoot     = 20,

        // Right leg
        RightUpperLeg = 21,
        RightLowerLeg = 22,
        RightAnkle    = 23,
        RightFoot     = 24,

        // Physical controller grip points (always available)
        LeftController  = 25,
        RightController = 26,

        COUNT = 27
    }

    // ------------------------------------------------------------------ //
    //  JointPose  —  one joint at one instant
    // ------------------------------------------------------------------ //
    [Serializable]
    public struct JointPose
    {
        public TrackedJoint joint;
        public Vector3      position;    // world-space
        public Quaternion   rotation;    // world-space
        public bool         isTracked;

        public Vector3 Forward => rotation * Vector3.forward;
        public Vector3 Up      => rotation * Vector3.up;
        public Vector3 Right   => rotation * Vector3.right;

        public override string ToString() =>
            $"[{joint}]  pos={position:F3}  euler={rotation.eulerAngles:F1}  tracked={isTracked}";
    }

    // ------------------------------------------------------------------ //
    //  PoseFrame  —  full-body snapshot at a single point in time
    // ------------------------------------------------------------------ //
    [Serializable]
    public class PoseFrame
    {
        public float      timestamp;
        public int        frameIndex;
        public JointPose[] joints;          // length == (int)TrackedJoint.COUNT

        // Controller extras (velocity / input state)
        public Vector3 leftControllerVelocity;
        public Vector3 rightControllerVelocity;
        public Vector3 leftControllerAngularVelocity;
        public Vector3 rightControllerAngularVelocity;
        public float   leftTrigger;
        public float   rightTrigger;
        public float   leftGrip;
        public float   rightGrip;

        public PoseFrame()
        {
            joints = new JointPose[(int)TrackedJoint.COUNT];
        }

        /// <summary>Fast O(1) joint lookup.</summary>
        public JointPose GetJoint(TrackedJoint id) => joints[(int)id];

        /// <summary>True when all listed joints are marked as tracked.</summary>
        public bool AllTracked(params TrackedJoint[] ids)
        {
            foreach (var id in ids)
                if (!joints[(int)id].isTracked) return false;
            return true;
        }

        /// <summary>World-space vector from joint A to joint B.</summary>
        public Vector3 Between(TrackedJoint from, TrackedJoint to)
            => joints[(int)to].position - joints[(int)from].position;

        /// <summary>Angle (degrees) at the middle joint of a three-joint chain.</summary>
        public float JointAngle(TrackedJoint a, TrackedJoint vertex, TrackedJoint b)
        {
            Vector3 va = joints[(int)a].position      - joints[(int)vertex].position;
            Vector3 vb = joints[(int)b].position      - joints[(int)vertex].position;
            return Vector3.Angle(va, vb);
        }
    }

    // ------------------------------------------------------------------ //
    //  PoseRecording  —  an ordered list of frames for one play session
    // ------------------------------------------------------------------ //
    [Serializable]
    public class PoseRecording
    {
        public string          sessionId;
        public string          playerName;
        public float           captureRateFps;
        public float           startTime;
        public float           endTime;
        public List<PoseFrame> frames = new List<PoseFrame>();

        public float DurationSeconds => endTime - startTime;
        public int   FrameCount      => frames.Count;

        /// <summary>
        /// Returns frames inside a time window [tStart, tEnd] relative to
        /// the recording's own start time.
        /// </summary>
        public List<PoseFrame> Slice(float tStart, float tEnd)
        {
            var result = new List<PoseFrame>();
            foreach (var f in frames)
            {
                float rel = f.timestamp - startTime;
                if (rel >= tStart && rel <= tEnd)
                    result.Add(f);
            }
            return result;
        }
    }

    // ------------------------------------------------------------------ //
    //  ReferencePose  —  one ideal badminton pose (single frame)
    //  Kept for backward compatibility with existing saved data.
    // ------------------------------------------------------------------ //
    [Serializable]
    public class ReferencePose
    {
        public string    poseName;        // e.g. "Overhead Smash — Wind-Up"
        public string    category;        // "Smash" | "Drop" | "Clear" | "Serve" | "Footwork"
        public PoseFrame keyFrame;        // the ideal snapshot
        public float     matchThreshold;  // 0..1 — minimum score to count as a match
        public string    feedbackHint;    // shown to the player when near-miss
    }

    // ------------------------------------------------------------------ //
    //  ReferencePoseSequence  —  a full movement recorded across N frames.
    //  This is what gets saved when you record a swing, serve, etc.
    //  The matcher resamples the live window to the reference frame count
    //  before comparing, so timing differences are handled automatically.
    // ------------------------------------------------------------------ //
    [Serializable]
    public class ReferencePoseSequence
    {
        public string          poseName;
        public string          category;
        public List<PoseFrame> frames         = new List<PoseFrame>();
        public float           captureRateFps;
        public float           matchThreshold;
        public string          feedbackHint;

        public int   FrameCount      => frames.Count;
        public float DurationSeconds => captureRateFps > 0
            ? frames.Count / captureRateFps : 0f;
    }

    // ------------------------------------------------------------------ //
    //  PoseMatchResult  —  output of one comparison
    // ------------------------------------------------------------------ //
    [Serializable]
    public class PoseMatchResult
    {
        public string       referenceName;
        public float        similarityScore;    // 0 = no match, 1 = perfect
        public bool         isMatch;
        public List<string> deviatingJoints = new List<string>();
        public string       feedbackMessage;
    }
    
    [Serializable]
    public class ScoringJointEntry
    {
        public TrackedJoint joint;
        [Range(0.1f, 5f)]
        public float weight = 1f;

        public ScoringJointEntry() { }
        public ScoringJointEntry(TrackedJoint j, float w) { joint = j; weight = w; }
    }
}
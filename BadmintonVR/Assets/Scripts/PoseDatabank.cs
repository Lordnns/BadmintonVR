// ============================================================
//  PoseDatabank.cs
//
//  Stores / loads reference poses using Unity's built-in
//  JsonUtility — zero external package dependencies.
//
//  Limitation: JsonUtility can't serialise a top-level List<T>
//  directly, so we wrap it in a tiny container class.
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BadmintonPoseTracking
{
    // ------------------------------------------------------------------ //
    //  JsonUtility wrapper — lets us serialise List<ReferencePose>
    // ------------------------------------------------------------------ //
    [Serializable]
    internal class ReferencePoseBank
    {
        public List<ReferencePose> poses = new List<ReferencePose>();
    }

    [Serializable]
    internal class RecordingWrapper
    {
        public PoseRecording recording;
    }

    // ------------------------------------------------------------------ //
    //  PoseDatabank
    // ------------------------------------------------------------------ //
    public class PoseDatabank : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────

        [Header("Storage")]
        [Tooltip("File name (no extension) saved inside  StreamingAssets/Poses/")]
        public string databankFileName = "badminton_poses";

        [Header("Matching")]
        [Tooltip("Joints that contribute to the similarity score. " +
                 "Leave empty to use built-in badminton defaults.")]
        public ScoringJointEntry[] scoringJoints = Array.Empty<ScoringJointEntry>();

        [Tooltip("A joint is 'perfect' at 0° and scores 0 at tolerance × 2 degrees.")]
        [Range(5f, 50f)]
        public float jointAngleTolerance = 22f;

        // ── Public state ──────────────────────────────────────────────────

        public IReadOnlyList<ReferencePose> Poses => _poses;
        private readonly List<ReferencePose> _poses = new List<ReferencePose>();

        // ── Default joints for badminton ──────────────────────────────────

        private static readonly ScoringJointEntry[] _defaults =
        {
            // Swing arm — highest priority
            new(TrackedJoint.RightShoulder,  2.0f),
            new(TrackedJoint.RightUpperArm,  2.5f),
            new(TrackedJoint.RightForearm,   2.5f),
            new(TrackedJoint.RightWrist,     2.0f),

            // Off-arm (balancing)
            new(TrackedJoint.LeftShoulder,   1.5f),
            new(TrackedJoint.LeftUpperArm,   1.2f),
            new(TrackedJoint.LeftForearm,    1.0f),

            // Torso
            new(TrackedJoint.SpineUpper,     1.5f),
            new(TrackedJoint.Hips,           1.0f),

            // Head
            new(TrackedJoint.Head,           0.8f),

            // Legs — lower weight (body tracking less reliable)
            new(TrackedJoint.LeftUpperLeg,   0.6f),
            new(TrackedJoint.RightUpperLeg,  0.6f),
            new(TrackedJoint.LeftLowerLeg,   0.4f),
            new(TrackedJoint.RightLowerLeg,  0.4f),
        };

        // ── Unity lifecycle ───────────────────────────────────────────────

        private void Awake()
        {
            if (scoringJoints == null || scoringJoints.Length == 0)
                scoringJoints = _defaults;

            LoadFromDisk();
        }

        // ── Persistence ───────────────────────────────────────────────────

        public void LoadFromDisk()
        {
            string path = DatabankPath();
            if (!File.Exists(path))
            {
                Debug.Log($"[PoseDatabank] No file found at {path} — starting empty.");
                return;
            }

            try
            {
                string           json = File.ReadAllText(path);
                ReferencePoseBank bank = JsonUtility.FromJson<ReferencePoseBank>(json);

                _poses.Clear();
                if (bank?.poses != null)
                    _poses.AddRange(bank.poses);

                Debug.Log($"[PoseDatabank] Loaded {_poses.Count} reference pose(s).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PoseDatabank] Load failed: {ex.Message}");
            }
        }

        public void SaveToDisk()
        {
            string path = DatabankPath();

            // StreamingAssets doesn't exist at first run on some platforms — create it
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var bank = new ReferencePoseBank { poses = new List<ReferencePose>(_poses) };
            string json = JsonUtility.ToJson(bank, prettyPrint: true);
            File.WriteAllText(path, json);

            Debug.Log($"[PoseDatabank] Saved {_poses.Count} pose(s) → {path}");
        }

        public void AddReference(ReferencePose pose)
        {
            _poses.Add(pose);
            Debug.Log($"[PoseDatabank] Added '{pose.poseName}'  (total {_poses.Count})");
        }

        public void RemoveReference(string poseName)
        {
            int n = _poses.RemoveAll(p => p.poseName == poseName);
            Debug.Log($"[PoseDatabank] Removed {n} entry/entries named '{poseName}'.");
        }

        // ── Matching ──────────────────────────────────────────────────────

        /// <summary>Returns the best-matching reference, or null if bank is empty.</summary>
        public PoseMatchResult FindBestMatch(PoseFrame live)
        {
            if (_poses.Count == 0) return null;

            PoseMatchResult best = null;
            foreach (var reference in _poses)
            {
                var r = Compare(live, reference);
                if (best == null || r.similarityScore > best.similarityScore)
                    best = r;
            }
            return best;
        }

        /// <summary>Returns all results sorted best → worst.</summary>
        public List<PoseMatchResult> ScoreAll(PoseFrame live)
        {
            var results = new List<PoseMatchResult>(_poses.Count);
            foreach (var r in _poses) results.Add(Compare(live, r));
            results.Sort((a, b) => b.similarityScore.CompareTo(a.similarityScore));
            return results;
        }

        // ── Core comparison ───────────────────────────────────────────────
        //
        //  1.  All joint rotations are expressed RELATIVE to the Hips joint
        //      (player-facing-direction independent).
        //  2.  Angular distance per joint → cosine falloff score (0–1).
        //  3.  Weighted average across all scoring joints.

        private PoseMatchResult Compare(PoseFrame live, ReferencePose reference)
        {
            var result = new PoseMatchResult { referenceName = reference.poseName };

            var liveHips = live.GetJoint(TrackedJoint.Hips);
            var refHips  = reference.keyFrame.GetJoint(TrackedJoint.Hips);

            Quaternion liveHipsInv = liveHips.isTracked
                ? Quaternion.Inverse(liveHips.rotation) : Quaternion.identity;
            Quaternion refHipsInv  = refHips.isTracked
                ? Quaternion.Inverse(refHips.rotation)  : Quaternion.identity;

            float totalScore  = 0f;
            float totalWeight = 0f;

            foreach (var entry in scoringJoints)
            {
                var lj = live.GetJoint(entry.joint);
                var rj = reference.keyFrame.GetJoint(entry.joint);
                if (!lj.isTracked || !rj.isTracked) continue;

                Quaternion liveLocal = liveHipsInv * lj.rotation;
                Quaternion refLocal  = refHipsInv  * rj.rotation;

                float angle  = Quaternion.Angle(liveLocal, refLocal);
                float t      = Mathf.Clamp01(angle / (jointAngleTolerance * 2f));
                float jScore = Mathf.Cos(t * Mathf.PI * 0.5f);

                totalScore  += jScore * entry.weight;
                totalWeight += entry.weight;

                if (angle > jointAngleTolerance)
                    result.deviatingJoints.Add($"{entry.joint} ({angle:F0}°)");
            }

            result.similarityScore = totalWeight > 0f ? totalScore / totalWeight : 0f;
            result.isMatch         = result.similarityScore >= reference.matchThreshold;
            result.feedbackMessage = BuildFeedback(result, reference);
            return result;
        }

        private static string BuildFeedback(PoseMatchResult r, ReferencePose reference)
        {
            if (r.isMatch)
                return $"Great {reference.poseName}! ({r.similarityScore:P0})";

            if (r.deviatingJoints.Count == 0)
                return reference.feedbackHint ?? "Keep adjusting your form.";

            int    count  = Mathf.Min(2, r.deviatingJoints.Count);
            string joints = string.Join(" | ", r.deviatingJoints.GetRange(0, count));
            return !string.IsNullOrEmpty(reference.feedbackHint)
                ? reference.feedbackHint
                : $"Adjust: {joints}";
        }

        // ── Utility ───────────────────────────────────────────────────────

        private string DatabankPath() =>
            Path.Combine(Application.streamingAssetsPath, "Poses", databankFileName + ".json");
    }

    // ------------------------------------------------------------------ //
    //  ScoringJointEntry  —  a joint + weight pair, editable in Inspector
    // ------------------------------------------------------------------ //
    [Serializable]
    public class ScoringJointEntry
    {
        public TrackedJoint joint;
        [Range(0.1f, 5f)]
        public float weight = 1f;

        public ScoringJointEntry() { }
        public ScoringJointEntry(TrackedJoint j, float w) { joint = j; weight = w; }
    }

    // ------------------------------------------------------------------ //
    //  PoseSessionSerializer  —  save / load full recording sessions
    //  Uses JsonUtility with a wrapper, same pattern as the databank.
    // ------------------------------------------------------------------ //
    public static class PoseSessionSerializer
    {
        private static string SessionFolder =>
            Path.Combine(Application.persistentDataPath, "Sessions");

        public static string Save(PoseRecording session)
        {
            Directory.CreateDirectory(SessionFolder);
            string path = Path.Combine(SessionFolder, session.sessionId + ".json");

            // JsonUtility can't serialise a class directly at the root if it
            // contains List<> with complex elements — wrap it just in case.
            var wrapper = new RecordingWrapper { recording = session };
            File.WriteAllText(path, JsonUtility.ToJson(wrapper, prettyPrint: true));

            Debug.Log($"[PoseSessionSerializer] Saved → {path}");
            return path;
        }

        public static PoseRecording Load(string sessionId)
        {
            string path = Path.Combine(SessionFolder, sessionId + ".json");
            if (!File.Exists(path))
            {
                Debug.LogError($"[PoseSessionSerializer] Not found: {path}");
                return null;
            }

            var wrapper = JsonUtility.FromJson<RecordingWrapper>(File.ReadAllText(path));
            return wrapper?.recording;
        }

        public static string[] ListAll() =>
            Directory.Exists(SessionFolder)
                ? Directory.GetFiles(SessionFolder, "*.json")
                : Array.Empty<string>();
    }
}
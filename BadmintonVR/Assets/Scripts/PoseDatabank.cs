// ============================================================
//  PoseDatabank.cs
//
//  Stores / loads reference poses AND full motion sequences.
//  Both are saved to the same JSON file under StreamingAssets.
//
//  Single-frame poses (ReferencePose) are kept for backward
//  compatibility.  New captures save as ReferencePoseSequence.
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BadmintonPoseTracking
{
    // ------------------------------------------------------------------ //
    //  JSON wrapper — holds both legacy single poses and new sequences
    // ------------------------------------------------------------------ //
    [Serializable]
    internal class ReferencePoseBank
    {
        public List<ReferencePose>         poses     = new List<ReferencePose>();
        public List<ReferencePoseSequence> sequences = new List<ReferencePoseSequence>();
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

        public IReadOnlyList<ReferencePose>         Poses     => _poses;
        public IReadOnlyList<ReferencePoseSequence> Sequences => _sequences;

        private readonly List<ReferencePose>         _poses     = new List<ReferencePose>();
        private readonly List<ReferencePoseSequence> _sequences = new List<ReferencePoseSequence>();

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
                string        json = File.ReadAllText(path);
                ReferencePoseBank bank = JsonUtility.FromJson<ReferencePoseBank>(json);

                _poses.Clear();
                _sequences.Clear();

                if (bank?.poses != null)
                    _poses.AddRange(bank.poses);

                if (bank?.sequences != null)
                    _sequences.AddRange(bank.sequences);

                Debug.Log($"[PoseDatabank] Loaded {_poses.Count} pose(s) " +
                          $"and {_sequences.Count} sequence(s).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PoseDatabank] Load failed: {ex.Message}");
            }
        }

        public void SaveToDisk()
        {
            string path = DatabankPath();

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var bank = new ReferencePoseBank
            {
                poses     = new List<ReferencePose>(_poses),
                sequences = new List<ReferencePoseSequence>(_sequences)
            };

            string json = JsonUtility.ToJson(bank, prettyPrint: false);
            File.WriteAllText(path, json);

            Debug.Log($"[PoseDatabank] Saved {_poses.Count} pose(s) and " +
                      $"{_sequences.Count} sequence(s) → {path}");
        }

        // ── Single-frame pose API (legacy) ────────────────────────────────

        public void AddReference(ReferencePose pose)
        {
            _poses.Add(pose);
            Debug.Log($"[PoseDatabank] Added pose '{pose.poseName}'  (total {_poses.Count})");
        }

        public void RemoveReference(string poseName)
        {
            int n = _poses.RemoveAll(p => p.poseName == poseName);
            Debug.Log($"[PoseDatabank] Removed {n} pose(s) named '{poseName}'.");
        }

        // ── Sequence API ──────────────────────────────────────────────────

        public void AddSequence(ReferencePoseSequence seq)
        {
            _sequences.Add(seq);
            Debug.Log($"[PoseDatabank] Added sequence '{seq.poseName}' " +
                      $"({seq.FrameCount} frames, {seq.DurationSeconds:F1}s)");
        }

        public void RemoveSequence(string poseName)
        {
            int n = _sequences.RemoveAll(s => s.poseName == poseName);
            Debug.Log($"[PoseDatabank] Removed {n} sequence(s) named '{poseName}'.");
        }

        // ── Sequence matching ─────────────────────────────────────────────

        /// <summary>
        /// Compares a live window of frames against every saved sequence
        /// and returns the best match, or null if no sequences exist.
        ///
        /// The live window is resampled to the reference frame count before
        /// comparison, so a fast or slow swing still matches correctly.
        /// </summary>
        public PoseMatchResult FindBestSequenceMatch(List<PoseFrame> liveFrames)
        {
            if (_sequences.Count == 0) return null;
            if (liveFrames == null || liveFrames.Count == 0) return null;

            PoseMatchResult best = null;
            foreach (var seq in _sequences)
            {
                var r = CompareSequence(liveFrames, seq);
                if (best == null || r.similarityScore > best.similarityScore)
                    best = r;
            }
            return best;
        }

        /// <summary>
        /// Returns all sequence results sorted best → worst.
        /// </summary>
        public List<PoseMatchResult> ScoreAllSequences(List<PoseFrame> liveFrames)
        {
            var results = new List<PoseMatchResult>(_sequences.Count);
            foreach (var seq in _sequences)
                results.Add(CompareSequence(liveFrames, seq));
            results.Sort((a, b) => b.similarityScore.CompareTo(a.similarityScore));
            return results;
        }

        private PoseMatchResult CompareSequence(List<PoseFrame> live,
                                                 ReferencePoseSequence reference)
        {
            var result = new PoseMatchResult { referenceName = reference.poseName };

            if (reference.frames.Count == 0)
            {
                result.similarityScore = 0f;
                result.feedbackMessage = "Empty reference sequence.";
                return result;
            }

            // Resample live frames to match the reference frame count.
            // This handles timing differences — a fast smash and a slow smash
            // both get stretched/compressed to the same length before comparison.
            List<PoseFrame> resampled = ResampleFrames(live, reference.frames.Count);

            float totalScore = 0f;
            var   allDeviating = new List<string>();

            for (int i = 0; i < reference.frames.Count; i++)
            {
                // Re-use the per-frame comparison logic via a temporary ReferencePose
                var tempRef = new ReferencePose
                {
                    poseName       = reference.poseName,
                    keyFrame       = reference.frames[i],
                    matchThreshold = reference.matchThreshold,
                    feedbackHint   = reference.feedbackHint
                };

                var frameResult = Compare(resampled[i], tempRef);
                totalScore += frameResult.similarityScore;

                // Accumulate deviating joints across frames (deduplicated)
                foreach (var j in frameResult.deviatingJoints)
                    if (!allDeviating.Contains(j))
                        allDeviating.Add(j);
            }

            result.similarityScore  = totalScore / reference.frames.Count;
            result.isMatch          = result.similarityScore >= reference.matchThreshold;
            result.deviatingJoints  = allDeviating;
            result.feedbackMessage  = BuildSequenceFeedback(result, reference);
            return result;
        }

        /// <summary>
        /// Resamples a frame list to exactly targetCount frames using nearest-
        /// neighbour selection.  No interpolation — keeps real captured data.
        /// </summary>
        private static List<PoseFrame> ResampleFrames(List<PoseFrame> source, int targetCount)
        {
            var result = new List<PoseFrame>(targetCount);

            if (source.Count == 0) return result;
            if (targetCount  == 1) { result.Add(source[source.Count / 2]); return result; }

            for (int i = 0; i < targetCount; i++)
            {
                float t   = i / (float)(targetCount - 1);
                int   idx = Mathf.RoundToInt(t * (source.Count - 1));
                result.Add(source[Mathf.Clamp(idx, 0, source.Count - 1)]);
            }
            return result;
        }

        private static string BuildSequenceFeedback(PoseMatchResult r,
                                                      ReferencePoseSequence reference)
        {
            if (r.isMatch)
                return $"Great {reference.poseName}! ({r.similarityScore:P0})";

            if (!string.IsNullOrEmpty(reference.feedbackHint))
                return reference.feedbackHint;

            if (r.deviatingJoints.Count > 0)
            {
                int    count  = Mathf.Min(2, r.deviatingJoints.Count);
                string joints = string.Join(" | ", r.deviatingJoints.GetRange(0, count));
                return $"Adjust: {joints}";
            }

            return "Keep working on your form.";
        }

        // ── Single-frame matching (legacy, kept for compatibility) ─────────

        /// <summary>Returns the best-matching single-frame reference, or null.</summary>
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

        /// <summary>Returns all single-frame results sorted best → worst.</summary>
        public List<PoseMatchResult> ScoreAll(PoseFrame live)
        {
            var results = new List<PoseMatchResult>(_poses.Count);
            foreach (var r in _poses) results.Add(Compare(live, r));
            results.Sort((a, b) => b.similarityScore.CompareTo(a.similarityScore));
            return results;
        }

        // ── Core per-frame comparison ─────────────────────────────────────
        //
        //  1.  All joint rotations expressed RELATIVE to Hips
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
    // ------------------------------------------------------------------ //
    public static class PoseSessionSerializer
    {
        private static string SessionFolder =>
            Path.Combine(Application.persistentDataPath, "Sessions");

        public static string Save(PoseRecording session)
        {
            Directory.CreateDirectory(SessionFolder);
            string path = Path.Combine(SessionFolder, session.sessionId + ".json");

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
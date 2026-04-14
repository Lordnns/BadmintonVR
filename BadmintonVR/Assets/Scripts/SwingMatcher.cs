// ============================================================
//  SwingMatcher.cs
//
//  Compares two PoseCaptures using Dynamic Time Warping (DTW)
//  with a Sakoe-Chiba band constraint.
//
//  WHY DTW?
//  ────────────────────────────────────────────────────────────
//  A reference smash was recorded at normal pace.  The player
//  might swing 20% faster or slower.  Naive frame-by-frame
//  comparison punishes good technique just because the timing
//  differs.  DTW finds the OPTIMAL alignment between the two
//  sequences, stretching/compressing time on each side to
//  minimise the total cost.  The result is a path through the
//  n×m cost matrix; the path cost, normalised by its length,
//  gives a distance that is timing-invariant.
//
//  BAND CONSTRAINT (Sakoe-Chiba)
//  ────────────────────────────────────────────────────────────
//  Unconstrained DTW allows arbitrarily degenerate paths
//  (e.g. one frame mapped to all 90 reference frames).  The
//  band limits warping to ±BandFraction of max(n,m), keeping
//  timing biologically plausible and cutting computation from
//  O(n×m) to O(n × band_width).
//
//  SCORING
//  ────────────────────────────────────────────────────────────
//  Raw DTW cost = sum of frame-pair distances along the path.
//  Normalised cost = raw / path_length.
//  Score [0,100] = 100 × exp(−normalised_cost / Sensitivity)
//  Sensitivity tunes how steeply the score drops with error.
//  Default ~30° angular sensitivity feels natural for badminton.
//
//  PERFORMANCE
//  ────────────────────────────────────────────────────────────
//  • Pre-allocated flat float[] matrix (no per-call alloc).
//  • Parallel float[] and int[] for joint weights/ids.
//    (avoids struct field reads in the innermost loop)
//  • float multiply instead of Mathf.Cos in score conversion.
//  • All math uses structs / value types — zero boxing.
//
//  MAX DIMENSIONS
//  ────────────────────────────────────────────────────────────
//  MaxFrames = 180 supports captures up to 6 s at 30 fps.
//  Increase if you record longer swings.
//  Memory: 180×180×4 bytes = 126 KB — completely negligible.
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BadmintonPoseTracking
{
    // ── Result ────────────────────────────────────────────────────────────

    public sealed class SwingScore
    {
        /// <summary>0 = no match, 100 = perfect.</summary>
        public float Score;

        /// <summary>Normalised DTW cost before the exp conversion (debug use).</summary>
        public float NormalisedCost;

        /// <summary>Average per-joint angular deviation (degrees) over the optimal path.</summary>
        public float AvgJointDeg;

        /// <summary>Joints that deviated most on average across the matched path.</summary>
        public string[] WeakJoints;

        /// <summary>Original player frame count before trimming (-1 if no trim).</summary>
        public int OriginalFrameCount = -1;

        /// <summary>Frame count actually sent into DTW (after trimming).</summary>
        public int TrimmedFrameCount  = -1;

        /// <summary>Convenience: Score >= threshold → passing grade.</summary>
        public bool Passes(float threshold = 60f) => Score >= threshold;

        public override string ToString()
        {
            string trim = OriginalFrameCount > 0
                ? $"  Trim={OriginalFrameCount}→{TrimmedFrameCount}f"
                : string.Empty;
            return $"Score={Score:F1}  NormCost={NormalisedCost:F3}  AvgJointDeg={AvgJointDeg:F1}°" +
                   $"  Weak=[{string.Join(", ", WeakJoints)}]{trim}";
        }
    }

    // ── Matcher ───────────────────────────────────────────────────────────

    public sealed class SwingMatcher
    {
        // ── Tuning knobs (set before comparing) ───────────────────────────

        /// <summary>
        /// Fraction of max(n,m) used as the Sakoe-Chiba band.
        /// 0.15 = tight (must match timing closely).
        /// 0.30 = loose (allows slow/fast swings).
        /// </summary>
        public float BandFraction = 0.20f;

        /// <summary>
        /// Controls how aggressively the score drops with angular error.
        /// Units: degrees.  A normalised cost equal to this value → score ~37.
        /// Lower = stricter grading.  Good range: 20–45.
        /// </summary>
        public float Sensitivity = 30f;

        /// <summary>
        /// When true, the player capture is automatically trimmed to its
        /// active swing window before DTW comparison.  This eliminates
        /// idle padding at the start/end of a capture so that only the
        /// actual movement is scored.
        /// </summary>
        public bool AutoTrim = true;

        // ── Pre-allocated DTW matrix ──────────────────────────────────────

        private const int MaxFrames = 180;
        private readonly float[] _dtw = new float[MaxFrames * MaxFrames];

        // ── Scoring joint arrays (parallel float[] + int[]) ───────────────
        //
        //    We avoid reading .weight and .joint from ScoringJointEntry
        //    structs inside the inner loop — that's two field reads per
        //    joint per frame pair.  Parallel arrays give us tight cache
        //    access and branch-free indexing.

        private float[]        _weights;       // indexed 0..numJoints-1
        private int[]          _jointIds;      // (int)TrackedJoint
        private float          _totalWeight;
        private int            _numJoints;

        // Per-joint cumulative-deviation accumulators (weak-joint detection)
        private float[]        _jointDeviation;
        private int[]          _jointHits;

        // ── Constructor ───────────────────────────────────────────────────

        public SwingMatcher(ScoringJointEntry[] scoringJoints)
        {
            SetScoringJoints(scoringJoints);
        }

        public SwingMatcher() : this(DefaultJoints()) { }

        public void SetScoringJoints(ScoringJointEntry[] joints)
        {
            _numJoints = joints.Length;
            _weights       = new float[_numJoints];
            _jointIds      = new int[_numJoints];
            _jointDeviation = new float[_numJoints];
            _jointHits     = new int[_numJoints];
            _totalWeight   = 0f;

            for (int i = 0; i < _numJoints; i++)
            {
                _weights[i]  = joints[i].weight;
                _jointIds[i] = (int)joints[i].joint;
                _totalWeight += joints[i].weight;
            }
        }

        // ── Main entry point ──────────────────────────────────────────────

        /// <summary>
        /// Compare a player capture against a reference swing.
        /// Returns a SwingScore — the player capture is not modified.
        /// When AutoTrim is true, idle padding is stripped from the
        /// player capture before DTW so only the active movement is scored.
        /// </summary>
        public SwingScore Compare(PoseCapture player, PoseCapture reference)
        {
            PoseCapture trimmed = AutoTrim
                ? SwingTrimmer.Trim(player, reference.DurationSeconds,
                                    reference.CaptureRateFps, _jointIds)
                : player;

            SwingScore score = CompareFrameArrays(trimmed.Frames, trimmed.FrameCount,
                                                   reference.Frames, reference.FrameCount);

            if (AutoTrim && trimmed != player)
            {
                score.OriginalFrameCount = player.FrameCount;
                score.TrimmedFrameCount  = trimmed.FrameCount;
            }

            return score;
        }

        /// <summary>Overload accepting a ReferencePoseSequence directly.</summary>
        public SwingScore Compare(PoseCapture player, ReferencePoseSequence reference)
        {
            float refDuration = reference.DurationSeconds;
            float refFps      = reference.captureRateFps > 0
                ? reference.captureRateFps : 30f;

            PoseCapture trimmed = AutoTrim
                ? SwingTrimmer.Trim(player, refDuration, refFps, _jointIds)
                : player;

            var refFrames = reference.frames;
            int refCount  = refFrames.Count;

            var refArr = new PoseFrame[refCount];
            for (int i = 0; i < refCount; i++) refArr[i] = refFrames[i];

            SwingScore score = CompareFrameArrays(trimmed.Frames, trimmed.FrameCount,
                                                   refArr, refCount);

            if (AutoTrim && trimmed != player)
            {
                score.OriginalFrameCount = player.FrameCount;
                score.TrimmedFrameCount  = trimmed.FrameCount;
            }

            return score;
        }

        // ── Core DTW ──────────────────────────────────────────────────────

        private SwingScore CompareFrameArrays(PoseFrame[] pFrames, int n,
                                               PoseFrame[] rFrames, int m)
        {
            if (n == 0 || m == 0)
                return new SwingScore { Score = 0f, NormalisedCost = float.MaxValue,
                                        WeakJoints = Array.Empty<string>() };

            n = Mathf.Min(n, MaxFrames);
            m = Mathf.Min(m, MaxFrames);

            // Band must be at least |n−m| so the path can reach from (0,0) to (n-1,m-1).
            // Without this, sequences of different lengths produce unreachable endpoints.
            int lengthDiff = Mathf.Abs(n - m);
            int band = Mathf.Max(lengthDiff,
                           Mathf.Max(2, Mathf.RoundToInt(Mathf.Max(n, m) * BandFraction)));

            Debug.Log($"[SwingMatcher] DTW  n={n}  m={m}  band={band}  (diff={lengthDiff})");

            // Reset per-joint deviation accumulators
            for (int i = 0; i < _numJoints; i++)
            {
                _jointDeviation[i] = 0f;
                _jointHits[i]      = 0;
            }

            // ── Fill DTW matrix ───────────────────────────────────────────
            //    Using flat index: [i * MaxFrames + j]
            //    Cells outside the band stay at float.MaxValue.

            const float INF = float.MaxValue * 0.5f;   // avoid overflow on addition

            // Init first cell
            _dtw[0] = FrameDist(pFrames[0], rFrames[0]);

            // First column
            for (int i = 1; i < n; i++)
            {
                int j = 0;
                if (Mathf.Abs(i - j) <= band)
                {
                    float prev = _dtw[(i - 1) * MaxFrames + j];
                    _dtw[i * MaxFrames + j] = prev < INF
                        ? FrameDist(pFrames[i], rFrames[j]) + prev
                        : INF;
                }
                else _dtw[i * MaxFrames + j] = INF;
            }

            // First row
            for (int j = 1; j < m; j++)
            {
                int i = 0;
                if (Mathf.Abs(i - j) <= band)
                {
                    float prev = _dtw[0 * MaxFrames + (j - 1)];
                    _dtw[0 * MaxFrames + j] = prev < INF
                        ? FrameDist(pFrames[i], rFrames[j]) + prev
                        : INF;
                }
                else _dtw[0 * MaxFrames + j] = INF;
            }

            // Fill remaining cells
            for (int i = 1; i < n; i++)
            {
                int jMin = Mathf.Max(1, i - band);
                int jMax = Mathf.Min(m - 1, i + band);

                for (int j = jMin; j <= jMax; j++)
                {
                    float cost = FrameDist(pFrames[i], rFrames[j]);

                    float a = _dtw[(i - 1) * MaxFrames + j];       // insertion
                    float b = _dtw[i       * MaxFrames + (j - 1)]; // deletion
                    float c = _dtw[(i - 1) * MaxFrames + (j - 1)]; // diagonal (match)

                    // min of three — branchless via sequential Mathf.Min
                    float best = a < b ? a : b;
                    if (c < best) best = c;

                    _dtw[i * MaxFrames + j] = best < INF ? cost + best : INF;
                }

                // Cells outside the band this row
                for (int j = 0;       j < jMin; j++) _dtw[i * MaxFrames + j] = INF;
                for (int j = jMax + 1; j < m;  j++) _dtw[i * MaxFrames + j] = INF;
            }

            // ── Traceback to accumulate per-joint deviations ──────────────
            //
            //    We walk the optimal path backwards from (n-1, m-1).
            //    At each cell we call FrameDistDetailed() to update
            //    the joint deviation accumulators — then build WeakJoints.

            int pi = n - 1, pj = m - 1;
            int pathLength = 0;

            while (pi > 0 || pj > 0)
            {
                FrameDistDetailed(pFrames[pi], rFrames[pj]);
                pathLength++;

                if (pi == 0) { pj--; continue; }
                if (pj == 0) { pi--; continue; }

                float d = _dtw[(pi - 1) * MaxFrames + (pj - 1)];
                float ins = _dtw[(pi - 1) * MaxFrames + pj];
                float del = _dtw[pi       * MaxFrames + (pj - 1)];

                if (d <= ins && d <= del) { pi--; pj--; }
                else if (ins <= del)      { pi--; }
                else                      { pj--; }
            }
            // Include the origin cell
            FrameDistDetailed(pFrames[0], rFrames[0]);
            pathLength++;

            // ── Compute final score ───────────────────────────────────────
            //
            //    TWO independent signals, combined via geometric mean:
            //
            //    1. dtwScore  — path-alignment quality (timing-invariant).
            //       Can be artificially inflated when BodyFrame normalization
            //       is imperfect or when the DTW path degenerates (many-to-one
            //       mappings through accidental low-cost cells).
            //
            //    2. jointScore — average per-joint angular deviation across the
            //       optimal path (from the traceback above).  Directly measures
            //       whether each joint ended up in the right place, regardless
            //       of how DTW chose to align the sequences.
            //
            //    Geometric mean: sqrt(a * b).  If EITHER metric says "wrong
            //    swing," the combined score reflects it.  A wrong swing that
            //    somehow fools the DTW path (dtwScore=97%) but shows 80°
            //    joint deviations (jointScore≈2%) yields sqrt(97*2) ≈ 14%.

            float rawCost  = _dtw[(n - 1) * MaxFrames + (m - 1)];
            float normCost = rawCost / Mathf.Max(1, pathLength);
            float dtwScore = Mathf.Exp(-normCost / Sensitivity) * 100f;

            // Average per-joint deviation (degrees) over the traceback path
            float totalDev   = 0f;
            int   trackedJointCount = 0;
            for (int k = 0; k < _numJoints; k++)
            {
                if (_jointHits[k] > 0)
                {
                    totalDev += _jointDeviation[k] / _jointHits[k];
                    trackedJointCount++;
                }
            }
            float avgJointDeg = trackedJointCount > 0 ? totalDev / trackedJointCount : 0f;
            float jointScore  = Mathf.Exp(-avgJointDeg / Sensitivity) * 100f;

            // Geometric mean — both must be high for a good final score
            float score = Mathf.Sqrt(dtwScore * jointScore);

            Debug.Log($"[SwingMatcher] dtwScore={dtwScore:F1}  jointScore={jointScore:F1}  " +
                      $"avgDev={avgJointDeg:F1}°  normCost={normCost:F3}  " +
                      $"→ final={score:F1}");

            // ── Identify weak joints ──────────────────────────────────────

            const int MaxWeakJoints = 3;
            int weakCount = 0;
            string[] weak = new string[MaxWeakJoints];

            // Find the top-N joints by average deviation (simple selection sort)
            bool[] used = new bool[_numJoints];
            for (int w = 0; w < MaxWeakJoints; w++)
            {
                float maxDev = 0f;
                int   maxIdx = -1;
                for (int k = 0; k < _numJoints; k++)
                {
                    if (used[k]) continue;
                    float avg = _jointHits[k] > 0
                        ? _jointDeviation[k] / _jointHits[k] : 0f;
                    if (avg > maxDev) { maxDev = avg; maxIdx = k; }
                }
                if (maxIdx < 0 || maxDev < 10f) break;  // < 10° average = not noteworthy
                used[maxIdx] = true;
                weak[weakCount++] = $"{(TrackedJoint)_jointIds[maxIdx]} ({maxDev:F0}°)";
            }

            var finalWeak = new string[weakCount];
            Array.Copy(weak, finalWeak, weakCount);

            return new SwingScore
            {
                Score           = score,
                NormalisedCost  = normCost,
                AvgJointDeg     = avgJointDeg,
                WeakJoints      = finalWeak
            };
        }

        // ── Per-frame distance ─────────────────────────────────────────────
        //
        //    All joint rotations are expressed relative to the body (pelvis)
        //    frame so the score is orientation-independent — the player does
        //    not need to face the same direction as the reference.

        // ── Reference frame ───────────────────────────────────────────────
        //
        //    PRIMARY: Hips.rotation (OVRBody Body mode tracks the pelvis).
        //    The pelvis is the most stable anchor — it does NOT co-move with
        //    the arm.  The old shoulder-line approach was corrupted because
        //    raising the racket arm shifts the shoulder joint position, which
        //    rotated the derived "shoulder frame" in the same direction as the
        //    arm swing.  FrameDist then cancelled out the very differences we
        //    were trying to measure (normCost ≈ 0.4 even when WeakJoints
        //    showed 80–105° deviations).
        //
        //    FALLBACK 1: shoulder line (still better than nothing if hips lost)
        //    FALLBACK 2: HMD yaw projection (always available via HMD)

        private static Quaternion BodyFrame(in PoseFrame f)
        {
            // ── Primary: pelvis rotation ───────────────────────────────────
            var hips = f.GetJoint(TrackedJoint.Hips);
            if (hips.isTracked)
                return hips.rotation;

            // ── Fallback 1: synthetic torso frame from shoulder positions ──
            var ls = f.GetJoint(TrackedJoint.LeftShoulder);
            var rs = f.GetJoint(TrackedJoint.RightShoulder);
            if (ls.isTracked && rs.isTracked)
            {
                Vector3 right = rs.position - ls.position;
                if (right.sqrMagnitude > 0.0001f)
                {
                    right.Normalize();
                    Vector3 forward = Vector3.Cross(right, Vector3.up);
                    if (forward.sqrMagnitude > 0.001f)
                    {
                        forward.Normalize();
                        return Quaternion.LookRotation(forward, Vector3.Cross(forward, right));
                    }
                }
            }

            // ── Fallback 2: HMD yaw (always available) ────────────────────
            var head = f.GetJoint(TrackedJoint.Head);
            if (head.isTracked)
            {
                Vector3 fwd = head.rotation * Vector3.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.001f)
                    return Quaternion.LookRotation(fwd.normalized, Vector3.up);
            }

            return Quaternion.identity;
        }

        private float FrameDist(in PoseFrame a, in PoseFrame b)
        {
            Quaternion aInv = Quaternion.Inverse(BodyFrame(a));
            Quaternion bInv = Quaternion.Inverse(BodyFrame(b));

            float totalCost   = 0f;
            float totalWeight = 0f;
            float tol2x       = 90f;   // 45° × 2 — saturate at 90°

            for (int k = 0; k < _numJoints; k++)
            {
                var aj = a.joints[_jointIds[k]];
                var bj = b.joints[_jointIds[k]];
                if (!aj.isTracked || !bj.isTracked)
                {
                    // No data = player isn't doing the movement. Assign max cost (t = 1.0).
                    // Previously this was a silent skip (cost = 0), which caused near-perfect
                    // scores whenever body tracking dropped confidence.
                    totalCost   += _weights[k];
                    totalWeight += _weights[k];
                    continue;
                }

                float angle = Quaternion.Angle(aInv * aj.rotation, bInv * bj.rotation);
                float t     = angle < tol2x ? angle / tol2x : 1f;
                totalCost   += t * _weights[k];
                totalWeight += _weights[k];
            }

            return totalWeight > 0f ? (totalCost / totalWeight) * 90f : 0f;
        }

        /// <summary>
        /// Same as FrameDist but also accumulates per-joint deviations
        /// into _jointDeviation / _jointHits.  Called only during traceback.
        /// </summary>
        private void FrameDistDetailed(in PoseFrame a, in PoseFrame b)
        {
            Quaternion aInv = Quaternion.Inverse(BodyFrame(a));
            Quaternion bInv = Quaternion.Inverse(BodyFrame(b));

            for (int k = 0; k < _numJoints; k++)
            {
                var aj = a.joints[_jointIds[k]];
                var bj = b.joints[_jointIds[k]];
                if (!aj.isTracked || !bj.isTracked)
                {
                    // Flag as maximally wrong so these joints surface in WeakJoints reporting.
                    _jointDeviation[k] += 180f;
                    _jointHits[k]++;
                    continue;
                }

                float angle = Quaternion.Angle(aInv * aj.rotation, bInv * bj.rotation);
                _jointDeviation[k] += angle;
                _jointHits[k]++;
            }
        }

        // ── Default scoring joints ─────────────────────────────────────────

        private static ScoringJointEntry[] DefaultJoints() => new ScoringJointEntry[]
        {
            // Body tracking mode gives us: shoulders + arms + head only.
            // Reference frame is derived from the shoulder line — see ShoulderFrame().

            // Both shoulders — also anchor the reference frame, so always include them
            new(TrackedJoint.RightShoulder,  2.0f),
            new(TrackedJoint.LeftShoulder,   1.2f),

            // Right arm — the racket arm, heaviest weights
            new(TrackedJoint.RightScapula,   1.5f),
            new(TrackedJoint.RightUpperArm,  2.5f),
            new(TrackedJoint.RightForearm,   2.5f),
            new(TrackedJoint.RightWrist,     2.0f),

            // Left arm — counter-balance signal
            new(TrackedJoint.LeftUpperArm,   0.8f),

            // Head from HMD — gaze direction, lightweight
            new(TrackedJoint.Head,           0.5f),
        };
    }
}